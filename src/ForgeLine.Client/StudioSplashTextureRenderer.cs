using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Owns the GPU resources for compiled studio artwork on the render thread.</summary>
internal sealed class StudioSplashTextureRenderer : IDisposable
{
    private const int Stride = 32;
    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _frameBuffers = new();
    private readonly Dictionary<string, IGraphicsTexture> _textures = new(StringComparer.Ordinal);
    private readonly SplashDefinition? _definition;
    private readonly StudioVertex[] _vertices = new StudioVertex[48];
    private bool _disposed;

    internal StudioSplashTextureRenderer(IGraphicsDevice graphics, RuntimeAssetCatalog assets)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        ArgumentNullException.ThrowIfNull(assets);

        _definition = SplashAssetPreflight.Prepare(
            UndefinedBehaviorStudioSplash.Create(),
            id => assets.Contains(AssetId.Parse(id)),
            message => Console.Error.WriteLine($"[studio:assets] {message}"));

        const string vs = "struct V {float2 P:POSITION; float2 UV:TEXCOORD0; float4 C:COLOR0;}; struct O {float4 P:SV_Position;float2 UV:TEXCOORD0;float4 C:COLOR0;}; O VSMain(V v){O o;o.P=float4(v.P,0,1);o.UV=v.UV;o.C=v.C;return o;}";
        const string ps = "Texture2D Image:register(t0); SamplerState LinearClamp:register(s1); struct I{float4 P:SV_Position;float2 UV:TEXCOORD0;float4 C:COLOR0;}; float4 PSMain(I i):SV_Target0{float4 tex=Image.Sample(LinearClamp,i.UV);clip(tex.a-0.01);float a=saturate(tex.a*i.C.a);float3 rgb=lerp(float3(0.035,0.045,0.045),tex.rgb*i.C.rgb,a);return float4(rgb,1);}";
        var compiler = new DxcShaderCompiler();
        _pipeline = graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            compiler.Compile(vs, GraphicsShaderStage.Vertex, "VSMain", "StudioSplashVertex.hlsl"),
            compiler.Compile(ps, GraphicsShaderStage.Pixel, "PSMain", "StudioSplashPixel.hlsl"))
        {
            VertexElements =
            [
                new GraphicsVertexElement("POSITION", 0, GraphicsVertexElementFormat.Float2, 0),
                new GraphicsVertexElement("TEXCOORD", 0, GraphicsVertexElementFormat.Float2, 8),
                new GraphicsVertexElement("COLOR", 0, GraphicsVertexElementFormat.Float4, 16)
            ],
            PixelTextureCount = 1,
            DepthEnabled = false,
            CullMode = GraphicsCullMode.None
        });

        if (_definition is null) return;

        try
        {
            foreach (SplashLayerDefinition layer in _definition.Layers)
            {
                RuntimeAssetContent content = assets.Read(AssetId.Parse(layer.AssetId));
                if (content.Type != RuntimeAssetType.Texture)
                    throw new InvalidDataException($"Splash resource '{layer.AssetId}' is not a texture.");

                RuntimeTextureData image = RuntimeTextureData.FromPayload(content.Payload);
                GraphicsTextureColorSpace colorSpace =
                    image.ColorSpace == RuntimeTextureColorSpace.Srgb
                        ? GraphicsTextureColorSpace.Srgb
                        : GraphicsTextureColorSpace.Linear;
                var textureData = new GraphicsTextureData(
                    new GraphicsTextureDescription(image.Width, image.Height,
                        GraphicsTextureFormat.Rgba8Unorm, colorSpace, image.Mips.Count),
                    image.Mips.Select(mip => new GraphicsTextureMipData(
                        mip.Width, mip.Height, mip.RowPitch, mip.Pixels)));
                _textures.Add(layer.AssetId, graphics.CreateTexture(textureData));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                       ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"[studio:assets:fallback] {exception.Message}");
            foreach (IGraphicsTexture texture in _textures.Values) texture.Dispose();
            _textures.Clear();
        }
    }

    internal bool HasAssets => _definition is not null &&
        _textures.Count == _definition.Layers.Count;

    internal void Render(IGraphicsCommandContext context, float elapsedSeconds, float masterOpacity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasAssets || _definition is null) return;

        int vertexCount = 0;
        float canvasScale = MathF.Min(context.Width / 1920f, context.Height / 1080f);
        float left = (context.Width - 1920f * canvasScale) / 2f;
        float top = (context.Height - 1080f * canvasScale) / 2f;

        foreach (SplashLayerDefinition layer in _definition.Layers)
        {
            if (vertexCount + 6 > _vertices.Length)
                throw new InvalidOperationException("Studio splash exceeds supported layer capacity.");
            SplashLayerState state = SplashTimelineEvaluator.Evaluate(
                layer.Keyframes is SplashKeyframe[] keys ? keys : layer.Keyframes.ToArray(),
                elapsedSeconds);
            float opacity = Math.Clamp(state.Opacity * masterOpacity, 0, 1);

            // Coordinates and sizes are relative to the 1920x1080 design canvas.
            Vector2 center = layer.Anchor + state.Offset;
            Vector2 size = layer.NormalizedSize * Math.Max(0, state.Scale);
            GraphicsTextureDescription image = _textures[layer.AssetId].Description;
            float maxWidth = size.X * 1920f * canvasScale;
            float maxHeight = size.Y * 1080f * canvasScale;
            float fit = MathF.Min(maxWidth / image.Width, maxHeight / image.Height);
            float halfW = image.Width * fit * 0.5f;
            float halfH = image.Height * fit * 0.5f;
            float x = left + center.X * 1920f * canvasScale;
            float y = top + center.Y * 1080f * canvasScale;
            float l = (x - halfW) / context.Width * 2f - 1f;
            float r = (x + halfW) / context.Width * 2f - 1f;
            float t = 1f - (y - halfH) / context.Height * 2f;
            float b = 1f - (y + halfH) / context.Height * 2f;
            var color = new Vector4(1f, 1f, 1f, opacity);
            _vertices[vertexCount++] = new(new(l, t), new(0, 0), color);
            _vertices[vertexCount++] = new(new(r, t), new(1, 0), color);
            _vertices[vertexCount++] = new(new(r, b), new(1, 1), color);
            _vertices[vertexCount++] = new(new(l, t), new(0, 0), color);
            _vertices[vertexCount++] = new(new(r, b), new(1, 1), color);
            _vertices[vertexCount++] = new(new(l, b), new(0, 1), color);
        }

        IGraphicsBuffer buffer = GetBuffer(context.FrameIndex);
        buffer.SetData<StudioVertex>(_vertices.AsSpan(0, vertexCount));
        context.SetPipeline(_pipeline);
        int draw = 0;
        foreach (SplashLayerDefinition layer in _definition.Layers)
        {
            context.SetPixelTexture(0, _textures[layer.AssetId]);
            context.SetVertexBuffer(buffer, Stride, draw * 6 * Stride);
            context.Draw(6);
            draw++;
        }
    }

    private IGraphicsBuffer GetBuffer(int frame)
    {
        if (_frameBuffers.TryGetValue(frame, out IGraphicsBuffer? buffer)) return buffer;
        buffer = _graphics.CreateBuffer(
            new GraphicsBufferDescription((ulong)(_vertices.Length * Stride), GraphicsBufferMemory.Upload));
        _frameBuffers.Add(frame, buffer);
        return buffer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (IGraphicsBuffer buffer in _frameBuffers.Values) buffer.Dispose();
        foreach (IGraphicsTexture texture in _textures.Values) texture.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct StudioVertex(Vector2 Position, Vector2 Uv, Vector4 Color);
}
