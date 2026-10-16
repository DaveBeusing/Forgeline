using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public readonly record struct UiSkinRect(float X, float Y, float Width, float Height);

public readonly record struct UiSkinDiagnostics(long PageBytes, int Textures, int SharedLeases, int Uploads,
    int Draws, int Vertices, long VertexUploadBytes, int DroppedQuads, int MissingAssets, int BufferCount);

/// <summary>Ordered, clipped draws. Flush before external text/geometry, then continue on a separately prepared upload buffer.</summary>
public sealed class UiSkinBatch : IDisposable
{
    public const int MaximumQuads = 4096;
    private const int Stride = 32;
    private readonly UiSkinResources _resources;
    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsBuffer[] _buffers;
    private readonly int _maximumFlushes;
    private int _flushes;
    private readonly Vertex[] _vertices;
    private readonly Command[] _commands;
    private int _count, _uploaded, _commandCount, _draws, _dropped, _missing, _width, _height, _frame;
    private bool _disposed;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private UiSkinRect _clip;
    public UiSkinDiagnostics Diagnostics => new(_resources.PageBytes, _resources.TextureCount, _resources.LeaseCount, _resources.Uploads,
        _draws, _count, _uploaded * (long)Stride, _dropped, _missing, _buffers.Length);

    public UiSkinBatch(IGraphicsDevice graphics, UiSkinResources resources, int maximumFlushes = 1, int maximumQuads = MaximumQuads)
    {
        ArgumentNullException.ThrowIfNull(graphics); ArgumentNullException.ThrowIfNull(resources);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFlushes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFlushes, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumQuads, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumQuads, MaximumQuads);
        _vertices = new Vertex[maximumQuads * 6];
        _commands = new Command[maximumQuads];
        _maximumFlushes = maximumFlushes;
        _buffers = new IGraphicsBuffer[4 * maximumFlushes];
        resources.ValidateDevice(graphics);
        _resources = resources;
        var compiler = new DxcShaderCompiler();
        const string vs = "struct I{float2 P:POSITION;float4 C:COLOR;float2 U:TEXCOORD;};struct O{float4 P:SV_Position;float4 C:COLOR;float2 U:TEXCOORD;};O VSMain(I i){O o;o.P=float4(i.P,0,1);o.C=i.C;o.U=i.U;return o;}";
        // Straight source alpha; native SrcAlpha/InvSrcAlpha blending applies multiplication exactly once.
        const string ps = "Texture2D<float4> Page:register(t0);SamplerState LinearClamp:register(s1);float4 PSMain(float4 p:SV_Position,float4 c:COLOR,float2 u:TEXCOORD):SV_Target0{return Page.Sample(LinearClamp,u)*c;}";
        _pipeline = graphics.CreateGraphicsPipeline(new(compiler.Compile(vs, GraphicsShaderStage.Vertex, "VSMain", "UiSkinVertex.hlsl"),
            compiler.Compile(ps, GraphicsShaderStage.Pixel, "PSMain", "UiSkinPixel.hlsl"))
        { CullMode = GraphicsCullMode.None, DepthEnabled = false, AlphaBlendEnabled = true, PixelTextureCount = 1,
            VertexElements = [new("POSITION",0,GraphicsVertexElementFormat.Float2,0),new("COLOR",0,GraphicsVertexElementFormat.Float4,8),new("TEXCOORD",0,GraphicsVertexElementFormat.Float2,24)] });
        try { for (int i = 0; i < _buffers.Length; i++) _buffers[i] = graphics.CreateBuffer(new((ulong)(_vertices.Length * Stride), GraphicsBufferMemory.Upload)); }
        catch { foreach (var buffer in _buffers) buffer?.Dispose(); _pipeline.Dispose(); throw; }
    }
    public void Begin(int width, int height, int frameIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex); ArgumentOutOfRangeException.ThrowIfGreaterThan(frameIndex, 3);
        _width = width; _height = height; _frame = frameIndex; _count = _uploaded = _commandCount = _draws = _dropped = _missing = _flushes = 0;
        _clip = new(0, 0, width, height);
    }
    public void SetClip(UiSkinRect clip)
    {
        Check(); Validate(clip); _clip = Intersect(clip, new(0, 0, _width, _height));
    }
    public void Draw(string id, UiSkinRect destination, Vector4 tint, bool nineSlice = false, float fill = 1)
    {
        Check(); Validate(destination);
        if (!float.IsFinite(fill) || !Finite(tint) || tint.X < 0 || tint.Y < 0 || tint.Z < 0 || tint.W < 0 ||
            tint.X > 1 || tint.Y > 1 || tint.Z > 1 || tint.W > 1) throw new ArgumentException("Invalid UI tint or fill.");
        UiSkinSprite sprite;
        try { sprite = _resources.Sprite(id); } catch (KeyNotFoundException) { _missing++; throw; }
        if (destination.Width <= 0 || destination.Height <= 0 || fill <= 0) return;
        var oldClip = _clip;
        if (fill < 1) _clip = Intersect(_clip, destination with { Width = destination.Width * fill });
        if (nineSlice && sprite.NineSlice)
        {
            var i = sprite.Insets;
            float scaleX = MathF.Min(1, destination.Width / Math.Max(1, i.Left + i.Right));
            float scaleY = MathF.Min(1, destination.Height / Math.Max(1, i.Top + i.Bottom));
            Span<float> xs = stackalloc float[] { destination.X, destination.X + i.Left * scaleX, destination.X + destination.Width - i.Right * scaleX, destination.X + destination.Width };
            Span<float> ys = stackalloc float[] { destination.Y, destination.Y + i.Top * scaleY, destination.Y + destination.Height - i.Bottom * scaleY, destination.Y + destination.Height };
            Span<float> us = stackalloc float[] { sprite.X, sprite.X + i.Left, sprite.X + sprite.Width - i.Right, sprite.X + sprite.Width };
            Span<float> vs = stackalloc float[] { sprite.Y, sprite.Y + i.Top, sprite.Y + sprite.Height - i.Bottom, sprite.Y + sprite.Height };
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                Quad(sprite.Page, new(xs[x], ys[y], xs[x+1]-xs[x], ys[y+1]-ys[y]), new(us[x], vs[y], us[x+1]-us[x], vs[y+1]-vs[y]), tint);
        }
        else Quad(sprite.Page, destination, new(sprite.X, sprite.Y, sprite.Width, sprite.Height), tint);
        _clip = oldClip;
    }
    /// <summary>Four projected footprint corners, in texture winding. Scissor remains authoritative.</summary>
    public void DrawProjected(string id, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 tint)
    {
        Check();
        if (!Finite(tint) || tint.X < 0 || tint.Y < 0 || tint.Z < 0 || tint.W < 0 ||
            tint.X > 1 || tint.Y > 1 || tint.Z > 1 || tint.W > 1 ||
            !FinitePoint(a) || !FinitePoint(b) || !FinitePoint(c) || !FinitePoint(d))
            throw new ArgumentException("Invalid projected UI quad.");
        var sprite = _resources.Sprite(id);
        if (_count + 6 > _vertices.Length) { _dropped++; return; }
        var page = _resources.Data.Pages[sprite.Page];
        Vector2 uv0 = new((float)sprite.X / page.Width, (float)sprite.Y / page.Height);
        Vector2 uv1 = new((float)(sprite.X + sprite.Width) / page.Width, (float)(sprite.Y + sprite.Height) / page.Height);
        if (_commandCount > 0 && _commands[_commandCount-1].Page == sprite.Page && _commands[_commandCount-1].Clip == _clip)
            _commands[_commandCount-1].Count += 6;
        else _commands[_commandCount++] = new(sprite.Page, _count, 6, _clip);
        a = Screen(a); b = Screen(b); c = Screen(c); d = Screen(d);
        _vertices[_count++] = new(a,tint,uv0); _vertices[_count++] = new(b,tint,new(uv1.X,uv0.Y)); _vertices[_count++] = new(c,tint,uv1);
        _vertices[_count++] = new(a,tint,uv0); _vertices[_count++] = new(c,tint,uv1); _vertices[_count++] = new(d,tint,new(uv0.X,uv1.Y));
    }
    private Vector2 Screen(Vector2 p) => new(p.X / _width * 2 - 1, 1 - p.Y / _height * 2);
    private static bool FinitePoint(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    public void Tile(string id, UiSkinRect destination, Vector4 tint)
    {
        Check(); Validate(destination);
        if (!Finite(tint) || tint.X < 0 || tint.Y < 0 || tint.Z < 0 || tint.W < 0 || tint.X > 1 || tint.Y > 1 || tint.Z > 1 || tint.W > 1) throw new ArgumentException("Invalid UI tint.");
        var s = _resources.Sprite(id);
        var oldClip = _clip; _clip = Intersect(_clip, destination);
        // Bound iteration even for huge destinations; do not loop through invisible tiles.
        int columns = (int)MathF.Min(MaximumQuads + 1, MathF.Ceiling(MathF.Max(0, destination.Width) / s.LogicalWidth));
        int rows = (int)MathF.Min(MaximumQuads + 1, MathF.Ceiling(MathF.Max(0, destination.Height) / s.LogicalHeight));
        int attempts = 0;
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            if (++attempts > MaximumQuads) { _dropped++; _clip = oldClip; return; }
            Quad(s.Page, new(destination.X + x * s.LogicalWidth, destination.Y + y * s.LogicalHeight, s.LogicalWidth, s.LogicalHeight), new(s.X, s.Y, s.Width, s.Height), tint);
        }
        _clip = oldClip;
    }
    public void Overlay(UiSkinState state, UiSkinRect destination, Vector4 tint)
    {
        Check();
        string? id = state switch
        {
            UiSkinState.Normal => null, UiSkinState.Hover => "ui.skin.directorate.overlay.hover", UiSkinState.Pressed => "ui.skin.directorate.overlay.pressed",
            UiSkinState.Focus => "ui.skin.directorate.overlay.focus", UiSkinState.Selected => "ui.skin.directorate.overlay.selected",
            UiSkinState.Disabled => "ui.skin.directorate.overlay.disabled", UiSkinState.Warning => "ui.skin.directorate.overlay.warning",
            UiSkinState.Error => "ui.skin.directorate.overlay.error", UiSkinState.Pending => "ui.skin.directorate.overlay.pending",
            UiSkinState.Accepted => "ui.skin.directorate.overlay.accepted", _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        if (id is not null) Draw(id, destination, tint, true);
    }
    private void Quad(int page, UiSkinRect destination, UiSkinRect source, Vector4 tint)
    {
        if (destination.Width <= 0 || destination.Height <= 0 || source.Width <= 0 || source.Height <= 0) return;
        var clipped = Intersect(destination, _clip);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;
        if (_count + 6 > _vertices.Length) { _dropped++; return; }
        var p = _resources.Data.Pages[page];
        float u0 = (source.X + (clipped.X - destination.X) / destination.Width * source.Width) / p.Width;
        float v0 = (source.Y + (clipped.Y - destination.Y) / destination.Height * source.Height) / p.Height;
        float u1 = u0 + clipped.Width / destination.Width * source.Width / p.Width;
        float v1 = v0 + clipped.Height / destination.Height * source.Height / p.Height;
        Vector2 a = new(clipped.X / _width * 2 - 1, 1 - clipped.Y / _height * 2), b = new((clipped.X + clipped.Width) / _width * 2 - 1, 1 - (clipped.Y + clipped.Height) / _height * 2);
        if (_commandCount > 0 && _commands[_commandCount-1].Page == page && _commands[_commandCount-1].Clip == _clip)
            _commands[_commandCount-1].Count += 6;
        else _commands[_commandCount++] = new(page, _count, 6, _clip);
        _vertices[_count++] = new(a,tint,new(u0,v0)); _vertices[_count++] = new(new(b.X,a.Y),tint,new(u1,v0)); _vertices[_count++] = new(b,tint,new(u1,v1));
        _vertices[_count++] = new(a,tint,new(u0,v0)); _vertices[_count++] = new(b,tint,new(u1,v1)); _vertices[_count++] = new(new(a.X,b.Y),tint,new(u0,v1));
    }
    public void Flush(IGraphicsCommandContext context)
    {
        Check();
        if (context.Width != _width || context.Height != _height || context.FrameIndex != _frame) throw new InvalidOperationException("UI batch and output frame must match.");
        if (_count == _uploaded) return;
        if (_flushes == _maximumFlushes) throw new InvalidOperationException("UI flush capacity exceeded. Prepare additional flush buffers outside frame recording.");
        var buffer = _buffers[_frame * _maximumFlushes + _flushes++];
        buffer.SetData<Vertex>(_vertices.AsSpan(_uploaded, _count - _uploaded));
        context.SetPipeline(_pipeline); context.SetVertexBuffer(buffer, Stride);
        for (int i = 0; i < _commandCount; i++)
        {
            var c = _commands[i];
            context.SetScissor((int)MathF.Floor(c.Clip.X), (int)MathF.Floor(c.Clip.Y), (int)MathF.Ceiling(c.Clip.X+c.Clip.Width), (int)MathF.Ceiling(c.Clip.Y+c.Clip.Height));
            context.SetPixelTexture(0, _resources.Texture(c.Page)); context.Draw(c.Count, c.Start - _uploaded); _draws++;
        }
        context.SetScissor(0, 0, _width, _height); _uploaded = _count; _commandCount = 0;
    }
    private static UiSkinRect Intersect(UiSkinRect a, UiSkinRect b)
    { float x = MathF.Max(a.X,b.X), y = MathF.Max(a.Y,b.Y); return new(x,y,MathF.Max(0,MathF.Min(a.X+a.Width,b.X+b.Width)-x),MathF.Max(0,MathF.Min(a.Y+a.Height,b.Y+b.Height)-y)); }
    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    private static void Validate(UiSkinRect r)
    { if (!float.IsFinite(r.X) || !float.IsFinite(r.Y) || !float.IsFinite(r.Width) || !float.IsFinite(r.Height) || !float.IsFinite(r.X+r.Width) || !float.IsFinite(r.Y+r.Height)) throw new ArgumentException("Non-finite UI region."); }
    private void CheckThread() { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI batches belong to their graphics thread."); }
    private void Check() { ObjectDisposedException.ThrowIf(_disposed,this); CheckThread(); if (_width == 0) throw new InvalidOperationException("Begin the UI batch first."); }
    public void Dispose() { if (_disposed) return; CheckThread(); _disposed = true; foreach (var buffer in _buffers) buffer.Dispose(); _pipeline.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vertex(Vector2 Position, Vector4 Color, Vector2 Uv);
    private struct Command(int page, int start, int count, UiSkinRect clip)
    { public int Page = page, Start = start, Count = count; public UiSkinRect Clip = clip; }
}
