using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal static class SelectionInspectorHudModel
{
    public static RtsUiIcon ResolveRoleIcon(
        in PlayerSelectionSummary selection)
    {
        if (selection.CommonUnitId.IsSpecified)
        {
            return RtsUiIconCatalog.ResolveUnitRole(
                selection.CommonUnitId);
        }

        if (selection.CommonBuildingId.IsSpecified)
        {
            return RtsUiIconCatalog.ResolveBuildingRole(
                selection.CommonBuildingId);
        }

        return selection.Kind switch
        {
            PlayerSelectionKind.Unit =>
                RtsUiIcon.MinimapSelectedGroup,
            PlayerSelectionKind.Building =>
                RtsUiIcon.MinimapBuilding,
            PlayerSelectionKind.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerSelectionKind.Mixed =>
                RtsUiIcon.MinimapSelectedGroup,
            _ =>
                RtsUiIcon.CursorSelect
        };
    }

    public static string ResolveKindLabel(
        PlayerSelectionKind kind) =>
        kind switch
        {
            PlayerSelectionKind.Unit =>
                "UNIT",
            PlayerSelectionKind.Building =>
                "BUILDING",
            PlayerSelectionKind.Construction =>
                "CONSTRUCTION",
            PlayerSelectionKind.Mixed =>
                "MIXED",
            _ =>
                "SELECTION"
        };

    public static string ResolveSupplyLabel(
        BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.Supplied =>
                "SUPPLIED",
            BattlefieldSupplyStatus.LowSupply =>
                "LOW SUPPLY",
            BattlefieldSupplyStatus.Critical =>
                "CRITICAL",
            BattlefieldSupplyStatus.Unsupplied =>
                "UNSUPPLIED",
            _ =>
                "SUPPLY"
        };

    public static string ResolvePowerLabel(
        PowerOperationalState state) =>
        state switch
        {
            PowerOperationalState.Powered =>
                "POWERED",
            PowerOperationalState.Brownout =>
                "BROWNOUT",
            _ =>
                "OFFLINE"
        };

    public static string ResolveWorkStateLabel(
        PlayerWorkState state) =>
        state switch
        {
            PlayerWorkState.Idle =>
                "IDLE",
            PlayerWorkState.Running =>
                "RUNNING",
            PlayerWorkState.Blocked =>
                "BLOCKED",
            _ =>
                "NONE"
        };

    public static RtsUiIcon ResolveWorkIcon(
        PlayerWorkKind kind) =>
        kind switch
        {
            PlayerWorkKind.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerWorkKind.UnitProduction =>
                RtsUiIcon.BuildingFactory,
            PlayerWorkKind.Processing =>
                RtsUiIcon.BuildingProcessing,
            _ =>
                RtsUiIcon.CursorSelect
        };
}

internal sealed class SelectionInspectorHudSurface : IGameplayHudSurface
{
    private readonly SelectionInspectorHudRenderer _renderer;

    public SelectionInspectorHudSurface(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _renderer =
            new SelectionInspectorHudRenderer(
                graphics,
                runtimeAssets);
    }

    public GameplayHudRegion Regions =>
        GameplayHudRegion.SelectionInspector;

    public void Render(
        in GameplayHudRenderContext context) =>
        _renderer.Render(
            context.Graphics,
            context.Snapshot,
            context.Layout);

    public void Dispose() =>
        _renderer.Dispose();
}

internal sealed class SelectionInspectorHudRenderer : IDisposable
{
    private const int MaxVertices = 131_072;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 1.05f;
    private const float GlyphAdvance = 6.6f;

    private static readonly Vector4 PanelColor =
        new(
            0.055f,
            0.065f,
            0.065f,
            0.97f);
    private static readonly Vector4 TextColor =
        new(
            0.92f,
            0.95f,
            0.94f,
            1.0f);
    private static readonly Vector4 MutedTextColor =
        new(
            0.66f,
            0.71f,
            0.70f,
            1.0f);

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private readonly RuntimeUiPalette? _runtimePalette;

    private int _vertexCount;
    private float _scale = 1.0f;
    private bool _disposed;

    public SelectionInspectorHudRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets)
    {
        _graphics =
            graphics ??
            throw new ArgumentNullException(nameof(graphics));
        _pipeline =
            CreatePipeline(
                graphics);
        _runtimePalette =
            runtimeAssets is null
                ? null
                : new RuntimeUiPalette(
                    runtimeAssets);
    }

    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext graphics,
        PresentationSnapshot snapshot,
        in GameplayHudLayout layout)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (graphics.Width <= 0 ||
            graphics.Height <= 0 ||
            layout.SelectionInspector.IsEmpty ||
            snapshot.PlayerExperience is not
                PlayerExperienceSnapshot experience ||
            experience.Selection.Count <= 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        _scale =
            layout.Scale;

        EmitInspector(
            experience.Selection,
            layout.SelectionInspector,
            graphics.Width,
            graphics.Height);

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

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

        LastRenderedVertexCount =
            _vertexCount;
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
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitInspector(
        in PlayerSelectionSummary selection,
        in HudRect region,
        int width,
        int height)
    {
        float padding =
            7.0f *
            _scale;
        float iconSize =
            18.0f *
            _scale;
        float x =
            region.X +
            padding;
        float y =
            region.Y +
            padding;

        EmitQuad(
            region.X,
            region.Y,
            region.Width,
            region.Height,
            PanelColor,
            width,
            height);

        RtsUiIcon roleIcon =
            SelectionInspectorHudModel.ResolveRoleIcon(
                selection);
        EmitIcon(
            roleIcon,
            x,
            y,
            iconSize,
            width,
            height);

        Span<char> headerBuffer =
            stackalloc char[96];
        var header =
            new HudTextBuilder(
                headerBuffer);

        if (selection.Count > 1)
        {
            header.Append(
                selection.Count);
            header.Append(" ");
        }

        header.Append(
            selection.DisplayName);

        EmitClippedText(
            header.Written,
            x +
                26.0f *
                _scale,
            y +
                1.0f *
                _scale,
            region.Right -
                6.0f *
                _scale,
            TextColor,
            width,
            height);

        EmitClippedText(
            SelectionInspectorHudModel.ResolveKindLabel(
                selection.Kind),
            x +
                26.0f *
                _scale,
            y +
                12.0f *
                _scale,
            region.Right -
                6.0f *
                _scale,
            MutedTextColor,
            width,
            height);

        if (!selection.HasSingleEntityDetails)
        {
            EmitMultiSelectionSummary(
                selection,
                x,
                y +
                    34.0f *
                    _scale,
                region,
                width,
                height);
            return;
        }

        float rowY =
            y +
            34.0f *
            _scale;

        if (selection.HasHealth)
        {
            EmitFractionRow(
                RtsUiIcon.StatusHealth,
                "HP",
                selection.HealthFraction,
                x,
                rowY,
                region,
                width,
                height);
            rowY +=
                15.0f *
                _scale;
        }

        if (selection.HasSupply)
        {
            EmitSupplyRow(
                selection,
                x,
                rowY,
                region,
                width,
                height);
            rowY +=
                15.0f *
                _scale;
        }

        if (selection.HasReadiness)
        {
            EmitFractionRow(
                RtsUiIcon.MinimapSelectedGroup,
                "READY",
                selection.Readiness,
                x,
                rowY,
                region,
                width,
                height);
            rowY +=
                15.0f *
                _scale;
        }

        if (selection.HasPower)
        {
            EmitStatusRow(
                RtsUiIcon.StatusPower,
                "POWER",
                SelectionInspectorHudModel.ResolvePowerLabel(
                    selection.PowerState),
                x,
                rowY,
                region,
                width,
                height);
            rowY +=
                15.0f *
                _scale;
        }

        if (selection.Work.Kind !=
            PlayerWorkKind.None &&
            rowY <
                region.Bottom -
                8.0f *
                _scale)
        {
            EmitWorkRow(
                selection.Work,
                x,
                rowY,
                region,
                width,
                height);
        }
    }

    private void EmitMultiSelectionSummary(
        in PlayerSelectionSummary selection,
        float x,
        float y,
        in HudRect region,
        int width,
        int height)
    {
        Span<char> buffer =
            stackalloc char[96];
        var text =
            new HudTextBuilder(
                buffer);

        text.Append("COUNT ");
        text.Append(
            selection.Count);

        if (selection.HasCommonIdentity)
        {
            text.Append("  COMMON TYPE");
        }
        else
        {
            text.Append("  AGGREGATE ONLY");
        }

        EmitClippedText(
            text.Written,
            x,
            y,
            region.Right -
                6.0f *
                _scale,
            MutedTextColor,
            width,
            height);
    }

    private void EmitFractionRow(
        RtsUiIcon icon,
        string label,
        double fraction,
        float x,
        float y,
        in HudRect region,
        int width,
        int height)
    {
        float iconSize =
            10.0f *
            _scale;

        EmitIcon(
            icon,
            x,
            y,
            iconSize,
            width,
            height);

        Span<char> buffer =
            stackalloc char[40];
        var text =
            new HudTextBuilder(
                buffer);
        text.Append(label);
        text.Append(" ");
        text.Append(
            Math.Clamp(
                fraction,
                0.0,
                1.0) *
                100.0,
            "F0");
        text.Append("%");

        float textX =
            x +
            15.0f *
            _scale;

        EmitClippedText(
            text.Written,
            textX,
            y +
                1.0f *
                _scale,
            region.Right -
                98.0f *
                _scale,
            TextColor,
            width,
            height);

        float barX =
            MathF.Max(
                textX +
                    58.0f *
                    _scale,
                region.Right -
                    92.0f *
                    _scale);
        float barWidth =
            MathF.Max(
                24.0f *
                    _scale,
                region.Right -
                    barX -
                    7.0f *
                    _scale);

        EmitSegmentedBar(
            barX,
            y +
                4.0f *
                _scale,
            barWidth,
            5.0f *
                _scale,
            fraction,
            ResolveColor(
                icon),
            width,
            height);
    }

    private void EmitSupplyRow(
        in PlayerSelectionSummary selection,
        float x,
        float y,
        in HudRect region,
        int width,
        int height)
    {
        RtsUiIcon supplyIcon =
            RtsUiIconCatalog.ResolveSupply(
                selection.SupplyStatus);
        float iconSize =
            10.0f *
            _scale;

        EmitIcon(
            supplyIcon,
            x,
            y,
            iconSize,
            width,
            height);

        Span<char> buffer =
            stackalloc char[96];
        var text =
            new HudTextBuilder(
                buffer);
        text.Append(
            SelectionInspectorHudModel.ResolveSupplyLabel(
                selection.SupplyStatus));
        text.Append("  F ");
        text.Append(
            Math.Clamp(
                selection.FuelFraction,
                0.0,
                1.0) *
                100.0,
            "F0");
        text.Append("% A ");
        text.Append(
            Math.Clamp(
                selection.AmmunitionFraction,
                0.0,
                1.0) *
                100.0,
            "F0");
        text.Append("%");

        EmitClippedText(
            text.Written,
            x +
                15.0f *
                _scale,
            y +
                1.0f *
                _scale,
            region.Right -
                7.0f *
                _scale,
            ResolveColor(
                supplyIcon),
            width,
            height);
    }

    private void EmitStatusRow(
        RtsUiIcon icon,
        string label,
        string state,
        float x,
        float y,
        in HudRect region,
        int width,
        int height)
    {
        float iconSize =
            10.0f *
            _scale;

        EmitIcon(
            icon,
            x,
            y,
            iconSize,
            width,
            height);

        Span<char> buffer =
            stackalloc char[64];
        var text =
            new HudTextBuilder(
                buffer);
        text.Append(label);
        text.Append(" ");
        text.Append(state);

        EmitClippedText(
            text.Written,
            x +
                15.0f *
                _scale,
            y +
                1.0f *
                _scale,
            region.Right -
                7.0f *
                _scale,
            TextColor,
            width,
            height);
    }

    private void EmitWorkRow(
        in PlayerWorkSummary work,
        float x,
        float y,
        in HudRect region,
        int width,
        int height)
    {
        RtsUiIcon icon =
            SelectionInspectorHudModel.ResolveWorkIcon(
                work.Kind);
        float iconSize =
            10.0f *
            _scale;

        EmitIcon(
            icon,
            x,
            y,
            iconSize,
            width,
            height);

        Span<char> buffer =
            stackalloc char[128];
        var text =
            new HudTextBuilder(
                buffer);
        text.Append(
            work.Activity);
        text.Append(" ");
        text.Append(
            Math.Clamp(
                work.Progress,
                0.0,
                1.0) *
                100.0,
            "F0");
        text.Append("% ");
        text.Append(
            SelectionInspectorHudModel.ResolveWorkStateLabel(
                work.State));

        if (!string.IsNullOrWhiteSpace(
                work.BlockReason))
        {
            text.Append(" ");
            text.Append(
                work.BlockReason);
        }

        EmitClippedText(
            text.Written,
            x +
                15.0f *
                _scale,
            y +
                1.0f *
                _scale,
            region.Right -
                7.0f *
                _scale,
            work.State ==
                PlayerWorkState.Blocked
                ? ResolveColor(
                    RtsUiIcon.StatusAlert)
                : TextColor,
            width,
            height);
    }

    private void EmitSegmentedBar(
        float x,
        float y,
        float barWidth,
        float barHeight,
        double fraction,
        Vector4 color,
        int width,
        int height)
    {
        const int SegmentCount = 5;
        float gap =
            1.5f *
            _scale;
        float segmentWidth =
            MathF.Max(
                1.0f,
                (barWidth -
                 gap *
                 (SegmentCount - 1)) /
                SegmentCount);
        int active =
            (int)Math.Ceiling(
                Math.Clamp(
                    fraction,
                    0.0,
                    1.0) *
                SegmentCount);

        for (int index = 0;
             index < SegmentCount;
             index++)
        {
            EmitQuad(
                x +
                index *
                    (segmentWidth + gap),
                y,
                segmentWidth,
                barHeight,
                index < active
                    ? color
                    : new Vector4(
                        0.17f,
                        0.19f,
                        0.18f,
                        1.0f),
                width,
                height);
        }
    }

    private void EmitClippedText(
        string text,
        float x,
        float y,
        float right,
        Vector4 color,
        int width,
        int height) =>
        EmitClippedText(
            text.AsSpan(),
            x,
            y,
            right,
            color,
            width,
            height);

    private void EmitClippedText(
        ReadOnlySpan<char> text,
        float x,
        float y,
        float right,
        Vector4 color,
        int width,
        int height)
    {
        float available =
            MathF.Max(
                0.0f,
                right -
                x);
        int maximumCharacters =
            Math.Max(
                0,
                (int)MathF.Floor(
                    available /
                    (GlyphAdvance *
                     _scale)));
        ReadOnlySpan<char> visible =
            text[
                ..Math.Min(
                    text.Length,
                    maximumCharacters)];

        for (int index = 0;
             index < visible.Length;
             index++)
        {
            EmitGlyph(
                char.ToUpperInvariant(
                    visible[index]),
                x +
                index *
                    GlyphAdvance *
                    _scale,
                y,
                color,
                width,
                height);
        }
    }

    private void EmitGlyph(
        char character,
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        string pattern =
            TextGlyphPattern(
                character);

        if (pattern.Length == 0)
        {
            return;
        }

        for (int row = 0;
             row < 7;
             row++)
        {
            for (int column = 0;
                 column < 5;
                 column++)
            {
                if (pattern[
                        row * 5 +
                        column] !=
                    '1')
                {
                    continue;
                }

                EmitQuad(
                    x +
                    column *
                        GlyphPixelSize *
                        _scale,
                    y +
                    row *
                        GlyphPixelSize *
                        _scale,
                    GlyphPixelSize *
                        _scale,
                    GlyphPixelSize *
                        _scale,
                    color,
                    width,
                    height);
            }
        }
    }

    private void EmitIcon(
        RtsUiIcon icon,
        float x,
        float y,
        float size,
        int width,
        int height)
    {
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);
        string pattern =
            IconGlyphPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
        float pixel =
            MathF.Max(
                1.0f,
                size /
                5.0f);

        for (int row = 0;
             row < 5;
             row++)
        {
            for (int column = 0;
                 column < 5;
                 column++)
            {
                if (pattern[
                        row * 5 +
                        column] !=
                    '1')
                {
                    continue;
                }

                EmitQuad(
                    x +
                    column *
                        pixel,
                    y +
                    row *
                        pixel,
                    pixel,
                    pixel,
                    color,
                    width,
                    height);
            }
        }
    }

    private Vector4 ResolveColor(
        RtsUiIcon icon)
    {
        RtsUiIconDefinition definition =
            RtsUiIconCatalog.Get(
                icon);

        return _runtimePalette is not null &&
               _runtimePalette.TryResolve(
                   definition.AssetId,
                   out Vector4 runtimeColor)
            ? runtimeColor
            : definition.FallbackTint;
    }

    private void EmitQuad(
        float x,
        float y,
        float quadWidth,
        float quadHeight,
        Vector4 color,
        int width,
        int height)
    {
        if (_vertexCount >
                MaxVertices -
                6 ||
            width <= 0 ||
            height <= 0 ||
            quadWidth <= 0.0f ||
            quadHeight <= 0.0f)
        {
            return;
        }

        float left =
            x /
            width *
            2.0f -
            1.0f;
        float right =
            (x + quadWidth) /
            width *
            2.0f -
            1.0f;
        float top =
            1.0f -
            y /
            height *
            2.0f;
        float bottom =
            1.0f -
            (y + quadHeight) /
            height *
            2.0f;

        Vector2 topLeft =
            new(
                left,
                top);
        Vector2 topRight =
            new(
                right,
                top);
        Vector2 bottomLeft =
            new(
                left,
                bottom);
        Vector2 bottomRight =
            new(
                right,
                bottom);

        _vertices[_vertexCount++] =
            new OverlayVertex(
                topLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomRight,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                topRight,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                topLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomLeft,
                color);
        _vertices[_vertexCount++] =
            new OverlayVertex(
                bottomRight,
                color);
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
                "SelectionInspectorHudVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "SelectionInspectorHudPixel.hlsl");

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
                DepthEnabled = false
            });
    }

    private static string IconGlyphPattern(
        RtsUiGlyph glyph) =>
        glyph switch
        {
            RtsUiGlyph.Infantry =>
                "00100" + "01110" + "00100" + "01110" + "10101",
            RtsUiGlyph.Reconnaissance =>
                "00000" + "01110" + "10101" + "01110" + "00000",
            RtsUiGlyph.Armor =>
                "01110" + "11111" + "11111" + "10101" + "11111",
            RtsUiGlyph.Artillery =>
                "00010" + "00110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Truck =>
                "00000" + "11110" + "10111" + "11111" + "01010",
            RtsUiGlyph.Repair =>
                "10001" + "01010" + "00100" + "01010" + "10001",
            RtsUiGlyph.Command =>
                "00100" + "01110" + "11111" + "00100" + "00100",
            RtsUiGlyph.Extraction =>
                "00100" + "00100" + "10101" + "01110" + "00100",
            RtsUiGlyph.Processing =>
                "01010" + "11111" + "01110" + "11111" + "01010",
            RtsUiGlyph.Factory =>
                "10101" + "11111" + "11111" + "10001" + "11111",
            RtsUiGlyph.Storage or
            RtsUiGlyph.Building =>
                "11111" + "10001" + "10101" + "10001" + "11111",
            RtsUiGlyph.Supply =>
                "00100" + "00100" + "11111" + "00100" + "00100",
            RtsUiGlyph.Power =>
                "00110" + "01100" + "11110" + "00110" + "01100",
            RtsUiGlyph.Build =>
                "00100" + "01110" + "11111" + "10001" + "11111",
            RtsUiGlyph.FriendlyUnit =>
                "01110" + "11111" + "11111" + "11111" + "01110",
            RtsUiGlyph.Select or
            RtsUiGlyph.DragSelect =>
                "11011" + "10001" + "00000" + "10001" + "11011",
            RtsUiGlyph.Health =>
                "01010" + "11111" + "11111" + "01110" + "00100",
            RtsUiGlyph.Drop =>
                "00100" + "01110" + "01110" + "11111" + "01110",
            RtsUiGlyph.Ammunition =>
                "10101" + "10101" + "10101" + "11111" + "01110",
            RtsUiGlyph.Check =>
                "00001" + "00010" + "10100" + "01000" + "00000",
            RtsUiGlyph.LowSupply =>
                "00100" + "00100" + "00100" + "01110" + "00100",
            RtsUiGlyph.Critical or
            RtsUiGlyph.Alert =>
                "00100" + "00100" + "00100" + "00000" + "00100",
            RtsUiGlyph.Empty =>
                "11111" + "10001" + "10001" + "10001" + "11111",
            _ =>
                "00100" + "01110" + "11111" + "01110" + "00100"
        };

    private static string TextGlyphPattern(
        char value) =>
        value switch
        {
            'A' => "01110100011000111111100011000110001",
            'B' => "11110100011000111110100011000111110",
            'C' => "01111100001000010000100001000001111",
            'D' => "11110100011000110001100011000111110",
            'E' => "11111100001000011110100001000011111",
            'F' => "11111100001000011110100001000010000",
            'G' => "01111100001000010111100011000101111",
            'H' => "10001100011000111111100011000110001",
            'I' => "11111001000010000100001000010011111",
            'J' => "00111000100001000010100101001001100",
            'K' => "10001100101010011000101001001010001",
            'L' => "10000100001000010000100001000011111",
            'M' => "10001110111010110101100011000110001",
            'N' => "10001110011010110011100011000110001",
            'O' => "01110100011000110001100011000101110",
            'P' => "11110100011000111110100001000010000",
            'Q' => "01110100011000110001101011001001101",
            'R' => "11110100011000111110101001001010001",
            'S' => "01111100001000001110000010000111110",
            'T' => "11111001000010000100001000010000100",
            'U' => "10001100011000110001100011000101110",
            'V' => "10001100011000110001100010101000100",
            'W' => "10001100011000110101101011101110001",
            'X' => "10001100010101000100010101000110001",
            'Y' => "10001100010101000100001000010000100",
            'Z' => "11111000010001000100010001000011111",
            '0' => "01110100011001110101110011000101110",
            '1' => "00100011000010000100001000010001110",
            '2' => "01110100010000100010001000100011111",
            '3' => "11110000010000101110000010000111110",
            '4' => "00010001100101010010111110001000010",
            '5' => "11111100001000011110000010000111110",
            '6' => "01110100001000011110100011000101110",
            '7' => "11111000010001000100010000100001000",
            '8' => "01110100011000101110100011000101110",
            '9' => "01110100011000101111000010000101110",
            '.' => "00000000000000000000000000011000110",
            ':' => "00000001100011000000001100011000000",
            '/' => "00001000100010001000100001000000000",
            '-' => "00000000000000011111000000000000000",
            '%' => "11001000100010001000100001001100000",
            ' ' => "",
            _ => "11111000010001000100000000010000100"
        };

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

    private sealed class RuntimeUiPalette
    {
        private readonly RuntimeAssetCatalog _catalog;
        private readonly Dictionary<string, Vector4> _colors =
            new(StringComparer.Ordinal);

        public RuntimeUiPalette(
            RuntimeAssetCatalog catalog)
        {
            _catalog =
                catalog;
        }

        public bool TryResolve(
            string rawAssetId,
            out Vector4 color)
        {
            if (_colors.TryGetValue(
                    rawAssetId,
                    out color))
            {
                return true;
            }

            AssetId id =
                AssetId.Parse(
                    rawAssetId);

            if (!_catalog.TryGet(
                    id,
                    out RuntimeAssetRecord? record) ||
                record is null ||
                record.Type !=
                    RuntimeAssetType.Material)
            {
                color = default;
                return false;
            }

            RuntimeAssetContent content =
                _catalog.Read(
                    id);

            using JsonDocument document =
                JsonDocument.Parse(
                    content.Payload);

            if (!document.RootElement.TryGetProperty(
                    "baseColorFactor",
                    out JsonElement factor) ||
                factor.ValueKind !=
                    JsonValueKind.Array ||
                factor.GetArrayLength() !=
                    4)
            {
                color =
                    Vector4.One;
            }
            else
            {
                color =
                    new Vector4(
                        factor[0].GetSingle(),
                        factor[1].GetSingle(),
                        factor[2].GetSingle(),
                        factor[3].GetSingle());
            }

            _colors.Add(
                rawAssetId,
                color);
            return true;
        }
    }
}
