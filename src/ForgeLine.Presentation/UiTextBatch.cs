using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public enum UiTextAlignment { Left, Center, Right }
public readonly record struct UiTextDiagnostics(int Glyphs, int Vertices, int MissingGlyphs, int DroppedGlyphs, long UploadBytes, bool AtlasReady, string? FailureReason);

internal sealed class UiTextBatch : IDisposable
{
    private const int MaximumGlyphs = 8192;
    private static readonly ConditionalWeakTable<IGraphicsDevice, Atlas> s_atlases = new();
    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline? _pipeline;
    private readonly Atlas? _atlas;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = [];
    private Vertex[] _vertices = new Vertex[384];
    private int _count, _missing, _dropped;
    private bool _disposed;
    public bool Ready => _atlas is not null;
    public string? FailureReason { get; }
    public int VertexCount => _count;
    public UiTextDiagnostics Diagnostics => new(_count / 6, _count, _missing, _dropped, _count * 32L, Ready, FailureReason);
    public UiTextBatch(IGraphicsDevice graphics)
    {
        _graphics = graphics;
        if (!graphics.SupportsUiText) { FailureReason = "Textured UI unavailable on this debug backend."; return; }
        IGraphicsPipeline? pipeline = null;
        try
        {
            var compiler = new DxcShaderCompiler();
            var vertex = compiler.Compile("""
                struct In { float2 Position:POSITION; float4 Color:COLOR; float2 Uv:TEXCOORD; };
                struct Out { float4 Position:SV_Position; float4 Color:COLOR; float2 Uv:TEXCOORD; };
                Out VSMain(In v) { Out o; o.Position=float4(v.Position,0,1); o.Color=v.Color; o.Uv=v.Uv; return o; }
                """, GraphicsShaderStage.Vertex, "VSMain", "UiTextVertex.hlsl");
            var pixel = compiler.Compile("""
                Texture2D<float> Atlas:register(t0); SamplerState LinearSampler:register(s0);
                float4 PSMain(float4 position:SV_Position,float4 color:COLOR,float2 uv:TEXCOORD):SV_Target0 {
                    float field=Atlas.Sample(LinearSampler,uv);
                    float edge=max(fwidth(field)*.5,1.0/255);
                    float coverage=smoothstep(.5-edge,.5+edge,field);
                    return float4(color.rgb,color.a*coverage);
                }
                """, GraphicsShaderStage.Pixel, "PSMain", "UiTextPixel.hlsl");
            pipeline = graphics.CreateGraphicsPipeline(new(vertex, pixel)
            {
                CullMode = GraphicsCullMode.None,
                AlphaBlendEnabled = true,
                PixelTextureCount = 1,
                VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float2, 0, 0), new("COLOR", 0, GraphicsVertexElementFormat.Float4, 8, 0), new("TEXCOORD", 0, GraphicsVertexElementFormat.Float2, 24, 0)]
            });
            _atlas = s_atlases.GetValue(graphics, static device => new Atlas(device));
            _atlas.Users++;
            _pipeline = pipeline;
        }
        catch (Exception error)
        {
            pipeline?.Dispose();
            if (error is not (IOException or NotSupportedException or System.Text.Json.JsonException)) throw;
            FailureReason = "Bundled UI font unavailable. Reinstall the application. " + error.Message;
            Console.Error.WriteLine("[ui:font] " + FailureReason);
        }
    }
    public void Begin() { ObjectDisposedException.ThrowIf(_disposed, this); _count = 0; _missing = 0; _dropped = 0; }
    public bool Add(ReadOnlySpan<char> text, float x, float y, float size, Vector4 color, int width, int height,
        float maximumWidth = float.MaxValue, bool bold = false, UiTextAlignment alignment = UiTextAlignment.Left, bool baseline = false, bool centerInkBounds = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Ready) return false;
        UiFontMetrics.ValidateSize(size);
        if (width <= 0 || height <= 0 || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(maximumWidth) || maximumWidth <= 0) return true;
        var font = UiFontMetrics.Default; float scale = size / font.Description.PixelSize;
        maximumWidth = MathF.Min(maximumWidth, MathF.Max(0, width - x));
        int offset = 0;
        while (offset < text.Length)
        {
            int newline = text[offset..].IndexOf('\n'); int length = newline < 0 ? text.Length - offset : newline;
            ReadOnlySpan<char> line = text.Slice(offset, length).TrimEnd('\r');
            float measured = font.Measure(line, size, bold);
            bool truncate = measured > maximumWidth;
            float ellipsis = 0;
            int prefix = line.Length;
            if (truncate)
            {
                ellipsis = font.Measure("\u2026", size, bold);
                prefix = font.FitPrefix(line, MathF.Max(0, maximumWidth - ellipsis), size, bold);
                measured = font.Measure(line[..prefix], size, bold) + ellipsis;
            }
            var bounds = centerInkBounds ? font.MeasureLineBounds(line[..prefix], size, bold) : default;
            float centeredWidth = truncate ? MathF.Max(measured, bounds.Right + ellipsis) - bounds.X : bounds.Width;
            float cursor = x + (alignment == UiTextAlignment.Center ? (maximumWidth - (centerInkBounds ? centeredWidth : measured)) * .5f - (centerInkBounds ? bounds.X : 0) : alignment == UiTextAlignment.Right ? maximumWidth - measured : 0);
            int previous = 0;
            for (int index = 0; index < prefix;)
            {
                int cp = font.Next(line, ref index); if (cp == '\r') continue; if (cp == '\t') cp = ' ';
                var glyph = font.Glyph(cp, bold, out bool missing); if (missing) _missing++;
                cursor += font.Kern(previous, glyph.Codepoint, bold) * scale;
                Emit(glyph, cursor, y, size, color, width, height, x + maximumWidth, baseline);
                cursor += glyph.Advance * scale; previous = glyph.Codepoint;
            }
            if (truncate && ellipsis <= maximumWidth) Emit(font.Glyph(0x2026, bold, out _), cursor, y, size, color, width, height, x + maximumWidth, baseline);
            offset += length + (newline < 0 ? 0 : 1); y += font.Description.LineHeight * scale;
            if (newline < 0 || y >= height) break;
        }
        return true;
    }
    public bool AddCentered(ReadOnlySpan<char> text, in HudRect bounds, float size, Vector4 color,
        int width, int height, bool bold = false)
    {
        if (bounds.IsEmpty) return Ready;
        var font = UiFontMetrics.Default;
        float blockHeight = font.Description.LineHeight * size / font.Description.PixelSize;
        foreach (char c in text) if (c == '\n') blockHeight += font.Description.LineHeight * size / font.Description.PixelSize;
        return Add(text, bounds.X, bounds.Y + (bounds.Height - blockHeight) * .5f, size, color,
            width, height, bounds.Width, bold, UiTextAlignment.Center, centerInkBounds: true);
    }

    private void Emit(UiFontGlyph glyph, float x, float y, float size, Vector4 color, int width, int height, float right, bool baseline)
    {
        if (glyph.Width == 0 || glyph.Height == 0) return;
        if (_count / 6 >= MaximumGlyphs) { _dropped++; return; }
        if (_count + 6 > _vertices.Length) Array.Resize(ref _vertices, Math.Min(MaximumGlyphs * 6, _vertices.Length * 2));
        var d = UiFontMetrics.Default.Description; float scale = size / d.PixelSize;
        float left = x + glyph.Left * scale, top = y + (baseline ? 0 : d.Ascent * scale) + glyph.Top * scale;
        float r = left + glyph.Width * scale, b = top + glyph.Height * scale;
        float clipLeft = MathF.Max(0, left), clipTop = MathF.Max(0, top), clipRight = MathF.Min(MathF.Min(width, right), r), clipBottom = MathF.Min(height, b);
        if (clipRight <= clipLeft || clipBottom <= clipTop) return;
        float u0 = (glyph.X + (clipLeft - left) / scale) / d.Width, v0 = (glyph.Y + (clipTop - top) / scale) / d.Height;
        float u1 = (glyph.X + (clipRight - left) / scale) / d.Width, v1 = (glyph.Y + (clipBottom - top) / scale) / d.Height;
        Vector2 a = new(clipLeft / width * 2 - 1, 1 - clipTop / height * 2), c = new(clipRight / width * 2 - 1, 1 - clipBottom / height * 2);
        _vertices[_count++] = new(a, color, new(u0, v0)); _vertices[_count++] = new(new(c.X, a.Y), color, new(u1, v0)); _vertices[_count++] = new(c, color, new(u1, v1));
        _vertices[_count++] = new(a, color, new(u0, v0)); _vertices[_count++] = new(c, color, new(u1, v1)); _vertices[_count++] = new(new(a.X, c.Y), color, new(u0, v1));
    }
    public void Render(IGraphicsCommandContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Ready || _count == 0) return;
        ArgumentOutOfRangeException.ThrowIfNegative(context.FrameIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(context.FrameIndex, 3);
        if (!_buffers.TryGetValue(context.FrameIndex, out var buffer) || buffer.Description.SizeInBytes < (ulong)(_vertices.Length * 32))
        {
            var replacement = _graphics.CreateBuffer(new((ulong)_vertices.Length * 32, GraphicsBufferMemory.Upload));
            buffer?.Dispose(); buffer = replacement; _buffers[context.FrameIndex] = buffer;
        }
        buffer.SetData<Vertex>(_vertices.AsSpan(0, _count)); context.SetPipeline(_pipeline!); context.SetPixelTexture(0, _atlas!.Texture);
        context.SetVertexBuffer(buffer, 32); context.Draw(_count);
    }
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var buffer in _buffers.Values) buffer.Dispose(); _buffers.Clear(); _pipeline?.Dispose();
        if (_atlas is not null && --_atlas.Users == 0) { _atlas.Texture.Dispose(); s_atlases.Remove(_graphics); }
        _disposed = true;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vertex(Vector2 Position, Vector4 Color, Vector2 Uv);
    private sealed class Atlas
    {
        public IGraphicsTexture Texture { get; }
        public int Users { get; set; }
        public Atlas(IGraphicsDevice graphics)
        {
            var d = UiFontMetrics.Default.Description;
            using var stream = typeof(UiFontMetrics).Assembly.GetManifestResourceStream("ForgeLine.UiFont.Alpha") ?? throw new InvalidDataException("Bundled font atlas missing.");
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            var alpha = new byte[checked(d.Width * d.Height)]; gzip.ReadExactly(alpha);
            if (gzip.ReadByte() != -1 || !Convert.ToHexString(SHA256.HashData(alpha)).Equals(d.AlphaSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundled font atlas integrity failed.");
            Texture = graphics.CreateTexture(new(new(d.Width, d.Height, GraphicsTextureFormat.R8Unorm, GraphicsTextureColorSpace.Linear, 1), [new(d.Width, d.Height, d.Width, alpha)]));
        }
    }
}
