using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;

namespace ForgeLine.Presentation;

internal sealed class PlayerActionRingHudSurface : IGameplayHudSurface
{
    private readonly PlayerActionRingHudRenderer _renderer;

    public PlayerActionRingHudSurface(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _renderer =
            new PlayerActionRingHudRenderer(
                graphics,
                runtimeAssets);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.GlobalOverlay;

    public UiTextDiagnostics TextDiagnostics => _renderer.TextDiagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _renderer.SkinDiagnostics;
    public int LastRenderedVertexCount => _renderer.LastRenderedVertexCount;

    public void Render(
        in GameplayHudRenderContext context) =>
        _renderer.Render(
            context.Graphics,
            context.Snapshot,
            context.ActionPanel,
            context.TacticalTargeting,
            context.ActiveFormation,
            context.Layout);

    public void Dispose() =>
        _renderer.Dispose();
}

internal sealed partial class PlayerActionRingHudRenderer : IDisposable
{
    internal const int MaxVertices = 4_096;
    private const int VertexStride = 24;
    private static readonly Vector4 TextColor =
        GameplayHudVisualStyle.TextPrimary;
    private static readonly Vector4 MutedTextColor =
        GameplayHudVisualStyle.TextSecondary;

    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly GameplayHudSkin _skin;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];


    private int _vertexCount;
    private float _scale = 1.0f;
    private bool _disposed;

    public PlayerActionRingHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _graphics =
            graphics ??
            throw new ArgumentNullException(nameof(graphics));
        _pipeline =
            CreatePipeline(
                graphics);
        _text = new UiTextBatch(graphics);
        try { _skin = new(graphics, runtimeAssets, icons: true, maximumIcons: 8, panels: false); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }

    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext graphics,
        PresentationSnapshot snapshot,
        in PlayerActionPanelView panel,
        in TacticalTargetingView targeting,
        FormationTemplate activeFormation,
        in GameplayHudLayout layout)
    {
        ThrowIfDisposed();
        _text.Begin();
        ArgumentNullException.ThrowIfNull(graphics);
        if (graphics.Width > 0 && graphics.Height > 0) _skin.Begin(graphics);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (graphics.Width <= 0 ||
            graphics.Height <= 0 ||
            layout.SafeArea.IsEmpty ||
            snapshot.PlayerExperience is
                PlayerExperienceSnapshot experience &&
            experience.IsMatchComplete)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _scale =
            layout.Scale;

        PlayerActionSnapshot? actions = panel.SelectionPending ? null : PlayerActionDockHudModel.ResolveActions(snapshot);
        EmitObjectRing(panel, actions, layout, graphics.Width, graphics.Height);
        if (_vertexCount == 0 && _skin.Vertices == 0 && _text.VertexCount == 0)
        { LastRenderedVertexCount = 0; return; }
        _skin.FlushPanels(graphics);
        if (_vertexCount > 0)
        {
            IGraphicsBuffer vertexBuffer =
                GetFrameVertexBuffer(
                    graphics.FrameIndex);
            vertexBuffer.SetData<OverlayVertex>(
                _vertices.AsSpan(
                    0,
                    _vertexCount));

            graphics.SetPipeline(
                _pipeline);
            graphics.SetVertexBuffer(
                vertexBuffer,
                VertexStride);
            graphics.Draw(
                _vertexCount);

        }

        _skin.FlushArtwork(graphics);
        _text.Render(graphics);
        LastRenderedVertexCount = _vertexCount + _text.VertexCount + _skin.Vertices;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IGraphicsBuffer buffer in
                 _vertexBuffers.Values)
        {
            buffer.Dispose();
        }

        _vertexBuffers.Clear();
        _skin.Dispose();
        _text.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private IGraphicsBuffer GetFrameVertexBuffer(
        int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(
                frameIndex,
                out IGraphicsBuffer? buffer))
        {
            return buffer;
        }

        buffer =
            _graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked(
                        (ulong)_vertices.Length *
                        VertexStride),
                    GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(
            frameIndex,
            buffer);
        return buffer;
    }

    private static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            struct VertexInput
            {
                float2 Position : POSITION;
                float4 Color : COLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position = float4(input.Position, 0.0f, 1.0f);
                output.Color = input.Color;
                return output;
            }
            """;

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                return input.Color;
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "PlayerActionRingVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "PlayerActionRingPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float2,
                        0),
                    new GraphicsVertexElement(
                        "COLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        8)
                ],
                CullMode = GraphicsCullMode.None,
                AlphaBlendEnabled = true,
                DepthEnabled = false
            });
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(
        Vector2 Position,
        Vector4 Color);

    private ref struct HudTextBuilder
    {
        private Span<char> _buffer;
        private int _length;

        public HudTextBuilder(
            Span<char> buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        public readonly ReadOnlySpan<char> Written =>
            _buffer[.._length];

        public void Append(
            string value)
        {
            if (value.AsSpan().TryCopyTo(
                    _buffer[_length..]))
            {
                _length +=
                    value.Length;
            }
        }

        public void Append(
            int value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider:
                        CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }

        public void Append(
            uint value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider:
                        CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }

        public void Append(
            double value,
            string format)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    format,
                    CultureInfo.InvariantCulture))
            {
                _length +=
                    written;
            }
        }
    }


}
