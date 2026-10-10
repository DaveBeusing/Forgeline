using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Graphics;

namespace ForgeLine.Client;

/// <summary>Plays the packaged studio master with the existing presentation clock and GPU owner.</summary>
internal sealed class StudioSplashVideoRenderer : IDisposable
{
    private readonly IGraphicsDevice _graphics;
    private readonly StudioVideoDecoder _decoder;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new();
    private readonly VideoVertex[] _vertices = new VideoVertex[6];
    private readonly StudioSplashAudio _audio = new();
    private IGraphicsTexture? _texture;
    private bool _disposed;

    private StudioSplashVideoRenderer(IGraphicsDevice graphics, string path)
    {
        _graphics = graphics;
        const string vs = "struct V{float2 P:POSITION;float2 UV:TEXCOORD0;float4 C:COLOR0;};struct O{float4 P:SV_Position;float2 UV:TEXCOORD0;float4 C:COLOR0;};O VSMain(V v){O o;o.P=float4(v.P,0,1);o.UV=v.UV;o.C=v.C;return o;}";
        // Video pixels are already display-referred: do not apply an additional sRGB decode.
        const string ps = "Texture2D Image:register(t0);SamplerState LinearClamp:register(s1);struct I{float4 P:SV_Position;float2 UV:TEXCOORD0;float4 C:COLOR0;};float4 PSMain(I i):SV_Target0{return float4(Image.Sample(LinearClamp,i.UV).rgb*i.C.a,1);}";
        var compiler = new DxcShaderCompiler();
        _pipeline = graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            compiler.Compile(vs, GraphicsShaderStage.Vertex, "VSMain", "StudioVideoVertex.hlsl"),
            compiler.Compile(ps, GraphicsShaderStage.Pixel, "PSMain", "StudioVideoPixel.hlsl"))
        {
            VertexElements = [new("POSITION", 0, GraphicsVertexElementFormat.Float2, 0),
                new("TEXCOORD", 0, GraphicsVertexElementFormat.Float2, 8),
                new("COLOR", 0, GraphicsVertexElementFormat.Float4, 16)],
            PixelTextureCount = 1, DepthEnabled = false, CullMode = GraphicsCullMode.None
        });
        _decoder = new StudioVideoDecoder(path);
    }

    internal static StudioSplashVideoRenderer? TryCreate(IGraphicsDevice graphics)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "branding", "studio-intro.mp4");
        if (!File.Exists(path)) return null;
        try { return new StudioSplashVideoRenderer(graphics, path); }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine($"[studio:video:unavailable] {error.Message}");
            return null;
        }
    }

    internal bool HasFrame => _texture is not null;

    // Upload before RenderFrame: texture creation must never occur inside command recording.
    internal void Prepare(float elapsedSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_decoder.Failure is Exception failure) throw new InvalidDataException("Studio video decode failed.", failure);
        // Prime the first frame even when its encoded timestamp is slightly above zero.
        StudioVideoFrame? frame;
        bool available = HasFrame
            ? _decoder.TryRead((long)(Math.Clamp(elapsedSeconds, 0, 10) * TimeSpan.TicksPerSecond), out frame)
            : _decoder.TryReadFirst(out frame);
        if (!available)
        {
            if (!HasFrame && _decoder.WorkerCompleted)
                throw new InvalidDataException("Studio video contains no playable frames.");
            return;
        }
        IGraphicsTexture next = _graphics.CreateTexture(new GraphicsTextureData(
            new(frame!.Width, frame.Height, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(frame.Width, frame.Height, frame.Width * 4, frame.Pixels)]));
        _texture?.Dispose(); // Native retirement waits for the last GPU submission using this texture.
        _texture = next;
    }

    internal void Render(IGraphicsCommandContext context, float opacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_texture is null) return;
        float aspect = (float)_texture.Description.Width / _texture.Description.Height;
        float fit = MathF.Min(context.Width / aspect, context.Height);
        float x = fit * aspect / context.Width, y = fit / context.Height;
        var color = new Vector4(1, 1, 1, Math.Clamp(opacity, 0, 1));
        _vertices[0] = new(new(-x, y), new(0, 0), color);
        _vertices[1] = new(new(x, y), new(1, 0), color);
        _vertices[2] = new(new(x, -y), new(1, 1), color);
        _vertices[3] = _vertices[0]; _vertices[4] = _vertices[2];
        _vertices[5] = new(new(-x, -y), new(0, 1), color);
        if (!_buffers.TryGetValue(context.FrameIndex, out IGraphicsBuffer? buffer))
        {
            buffer = _graphics.CreateBuffer(new(6 * 32, GraphicsBufferMemory.Upload));
            _buffers.Add(context.FrameIndex, buffer);
        }
        buffer.SetData<VideoVertex>(_vertices);
        context.SetPipeline(_pipeline);
        context.SetPixelTexture(0, _texture);
        context.SetVertexBuffer(buffer, 32);
        context.Draw(6);
    }

    internal void UpdateAudio(float elapsed, float opacity)
    {
        if (HasFrame) _audio.Update(elapsed, opacity);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _decoder.Dispose();
        _audio.Dispose();
        _texture?.Dispose();
        foreach (IGraphicsBuffer buffer in _buffers.Values) buffer.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VideoVertex(Vector2 Position, Vector2 Uv, Vector4 Color);
}
