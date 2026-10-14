using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public readonly record struct FrontendBackgroundDiagnostics(string State, long ResidentBytes, int Uploads, string? Failure);

/// <summary>Optional precompiled still image; CPU I/O is asynchronous, GPU ownership stays on the renderer thread.</summary>
public sealed class FrontendMenuBackground : IDisposable
{
    public const string AssetName = "branding.forgeline.menu.directorate";
    public const long MaximumPayloadBytes = 4 * 1024 * 1024;
    private readonly IGraphicsDevice _graphics;
    private readonly Func<RuntimeTextureData?> _load;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly Vertex[] _vertices = new Vertex[6];
    private Task<(RuntimeTextureData? Data, string? Error)>? _worker;
    private RuntimeTextureData? _data;
    private IGraphicsTexture? _texture;
    private IGraphicsPipeline? _pipeline;
    private bool _failed, _disposed;
    private int _mip = -1, _uploads;
    private long _residentBytes;
    private string? _failure;
    public bool Ready => _texture is not null;
    public FrontendBackgroundDiagnostics Diagnostics => new(_disposed ? "Disposed" : Ready ? "Ready" : _failed ? "Fallback" : _worker is null ? "Deferred" : "Loading",
        _residentBytes, _uploads, _failure);

    public FrontendMenuBackground(IGraphicsDevice graphics, RuntimeAssetCatalog catalog) : this(graphics, () => Read(catalog)) { }
    internal FrontendMenuBackground(IGraphicsDevice graphics, Func<RuntimeTextureData?> load)
    { _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics)); _load = load ?? throw new ArgumentNullException(nameof(load)); }

    private static RuntimeTextureData? Read(RuntimeAssetCatalog catalog)
    {
        var id = AssetId.Parse(AssetName);
        if (!catalog.TryGet(id, out var record)) return null;
        if (record!.Type != RuntimeAssetType.Texture || record.RuntimeBytes > MaximumPayloadBytes)
            throw new InvalidDataException("Optional menu image exceeds its compiled texture budget.");
        var data = RuntimeTextureData.FromPayload(catalog.Read(id).Payload);
        Validate(data);
        return data;
    }
    internal static void Validate(RuntimeTextureData data)
    {
        if (data.Width > 2048 || data.Height > 1152 || data.Format != RuntimeTextureFormat.Bc7Unorm ||
            data.ColorSpace != RuntimeTextureColorSpace.Linear || data.Usage != RuntimeTextureUsage.DisplayImage || data.ResidentByteCount > MaximumPayloadBytes)
            throw new InvalidDataException("Optional menu image must be a bounded, display-referred BC7 still.");
    }

    // Call before recording and after the first interactive menu has already presented.
    public void Prepare(RenderQuality quality, bool software = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) return;
        if (_worker is null)
        {
            var load = _load; var token = _cancellation.Token;
            _worker = Task.Run(() =>
            {
                try { token.ThrowIfCancellationRequested(); var data = load(); token.ThrowIfCancellationRequested(); return (data, (string?)null); }
                catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or KeyNotFoundException or OperationCanceledException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
                { return ((RuntimeTextureData?)null, error.Message); }
            });
            return;
        }
        if (!_worker.IsCompleted) return;
        if (_data is null)
        {
            var result = _worker.GetAwaiter().GetResult(); _data = result.Data;
            if (_data is null) { Fail(result.Error ?? "Optional menu image is absent; using command interface fallback."); return; }
            try { Validate(_data); } catch (InvalidDataException error) { Fail(error.Message); return; }
        }
        int mip = Math.Min(software || quality == RenderQuality.Low ? 1 : 0, _data.Mips.Count - 1);
        if (_graphics.AvailableMemoryBudgetBytes is < 16777216) mip = Math.Min(2, _data.Mips.Count - 1);
        if (_mip == mip && _texture is not null) return;
        try
        {
            _pipeline ??= CreatePipeline(_graphics);
            var first = _data.Mips[mip];
            var texture = _graphics.CreateTexture(new(new(first.Width, first.Height, GraphicsTextureFormat.Bc7Unorm,
                GraphicsTextureColorSpace.Linear, _data.Mips.Count - mip) { DeferUploadWait = true },
                _data.Mips.Skip(mip).Select(m => new GraphicsTextureMipData(m.Width, m.Height, m.RowPitch, m.Pixels))));
            _texture?.Dispose(); _texture = texture; _mip = mip; _uploads++;
            _residentBytes = _data.Mips.Skip(mip).Sum(m => (long)m.RowPitch * ((m.Height + 3) / 4));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        { _texture?.Dispose(); _texture = null; _residentBytes = 0; _pipeline?.Dispose(); _pipeline = null; Fail(error.Message); }
    }
    private void Fail(string reason) { _failed = true; _failure = reason; Console.Error.WriteLine($"[frontend:image:fallback] {reason}"); }

    public void Render(IGraphicsCommandContext context, FrontendRegion region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_texture is null || region.Width < 1 || region.Height < 1) return;
        // Cover crop is limited to the visual-only region. No pointer target is introduced.
        float sourceAspect = (float)_texture.Description.Width / _texture.Description.Height;
        float targetAspect = region.Width / region.Height;
        float u = targetAspect < sourceAspect ? targetAspect / sourceAspect : 1;
        float v = targetAspect > sourceAspect ? sourceAspect / targetAspect : 1;
        float left = region.X / context.Width * 2 - 1, right = region.Right / context.Width * 2 - 1;
        float top = 1 - region.Y / context.Height * 2, bottom = 1 - region.Bottom / context.Height * 2;
        _vertices[0] = new(new(left, top), new((1-u)/2, (1-v)/2));
        _vertices[1] = new(new(right, top), new((1+u)/2, (1-v)/2));
        _vertices[2] = new(new(right, bottom), new((1+u)/2, (1+v)/2));
        _vertices[3] = _vertices[0]; _vertices[4] = _vertices[2]; _vertices[5] = new(new(left, bottom), new((1-u)/2, (1+v)/2));
        if (!_buffers.TryGetValue(context.FrameIndex, out var buffer))
        { buffer = _graphics.CreateBuffer(new(6 * 16, GraphicsBufferMemory.Upload)); _buffers.Add(context.FrameIndex, buffer); }
        buffer.SetData<Vertex>(_vertices); context.SetPipeline(_pipeline!); context.SetPixelTexture(0, _texture);
        context.SetVertexBuffer(buffer, 16); context.Draw(6);
    }
    private static IGraphicsPipeline CreatePipeline(IGraphicsDevice graphics)
    {
        const string vs = "struct V{float2 P:POSITION;float2 UV:TEXCOORD0;};struct O{float4 P:SV_Position;float2 UV:TEXCOORD0;};O VSMain(V v){O o;o.P=float4(v.P,0,1);o.UV=v.UV;return o;}";
        const string ps = "Texture2D Image:register(t0);SamplerState LinearClamp:register(s1);struct I{float4 P:SV_Position;float2 UV:TEXCOORD0;};float4 PSMain(I i):SV_Target0{float3 c=Image.Sample(LinearClamp,i.UV).rgb;float edge=smoothstep(0,.2,i.UV.y)*smoothstep(0,.12,1-i.UV.y);return float4(c*lerp(.18,.94,edge),1);}";
        var compiler = new DxcShaderCompiler();
        return graphics.CreateGraphicsPipeline(new(compiler.Compile(vs, GraphicsShaderStage.Vertex, "VSMain", "MenuImageVertex.hlsl"),
            compiler.Compile(ps, GraphicsShaderStage.Pixel, "PSMain", "MenuImagePixel.hlsl"))
        { VertexElements = [new("POSITION",0,GraphicsVertexElementFormat.Float2,0), new("TEXCOORD",0,GraphicsVertexElementFormat.Float2,8)],
            PixelTextureCount = 1, DepthEnabled = false, CullMode = GraphicsCullMode.None });
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _cancellation.Cancel(); _cancellation.Dispose();
        _texture?.Dispose(); _texture = null; _residentBytes = 0; _pipeline?.Dispose(); _data = null; _worker = null;
        foreach (var buffer in _buffers.Values) buffer.Dispose();
        // The bounded CPU read may finish after cancellation; it owns no device/window resources.
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vertex(Vector2 Position, Vector2 Uv);
}
