using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class RtsInformationOverlayRenderer : IDisposable
{
    private const int MaxVertices = 262_144;
    private const int VertexStride = 24;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers =
        new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private readonly RuntimeUiPalette? _runtimePalette;

    private int _vertexCount;
    private bool _disposed;

    public RtsInformationOverlayRenderer(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog? runtimeAssets = null)
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
        IGraphicsCommandContext context,
        RtsCamera camera,
        PresentationSnapshot snapshot,
        in AxisAlignedBounds worldBounds,
        in RtsInformationLayerView view,
        uint dpi,
        float uiScale = 1.0f)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (context.Width <= 0 ||
            context.Height <= 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        _vertexCount = 0;
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                dpi,
                uiScale);
        float scale =
            layout.Scale;

        if (snapshot.PlayerExperience is
            PlayerExperienceSnapshot experience)
        {
            EmitSelectionStatus(
                experience.Selection,
                layout,
                context.Width,
                context.Height);
        }

        if (view.MinimapEnabled)
        {
            RtsMinimapModel minimap =
                RtsMinimapModelBuilder.Build(
                    snapshot,
                    worldBounds,
                    snapshot.PlayerExperience?.Player ??
                        new PlayerId(1),
                    view.SelectedEntities);

            EmitMinimap(
                minimap,
                camera.Target,
                layout,
                context.Width,
                context.Height);
        }

        if (view.IsDragSelecting)
        {
            EmitSelectionRectangle(
                view.DragStart,
                view.DragCurrent,
                ResolveColor(
                    RtsUiIcon.CursorDragSelect),
                scale,
                context.Width,
                context.Height);
        }

        if (view.HasPointer)
        {
            EmitIcon(
                RtsUiIconCatalog.ResolveCursor(
                    view.Cursor),
                view.PointerPosition.X +
                    10.0f * scale,
                view.PointerPosition.Y +
                    10.0f * scale,
                14.0f * scale,
                context.Width,
                context.Height);
        }

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        IGraphicsBuffer vertexBuffer =
            GetFrameVertexBuffer(
                context.FrameIndex);
        vertexBuffer.SetData<OverlayVertex>(
            _vertices.AsSpan(
                0,
                _vertexCount));

        context.SetPipeline(
            _pipeline);
        context.SetVertexBuffer(
            vertexBuffer,
            VertexStride);
        context.Draw(
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

    private void EmitSelectionStatus(
        in PlayerSelectionSummary selection,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        if (selection.Count <= 0)
        {
            return;
        }

        float x =
            layout.SelectionInspector.X +
            6.0f * scale;
        float y =
            layout.SelectionInspector.Y +
            6.0f * scale;
        float iconSize =
            14.0f * scale;
        float barWidth =
            110.0f * scale;
        float barHeight =
            6.0f * scale;

        EmitQuad(
            x - 6.0f * scale,
            y - 6.0f * scale,
            158.0f * scale,
            62.0f * scale,
            new Vector4(
                0.07f,
                0.08f,
                0.08f,
                1.0f),
            width,
            height);

        if (selection.HasHealth)
        {
            EmitIcon(
                RtsUiIcon.StatusHealth,
                x,
                y,
                iconSize,
                width,
                height);
            EmitSegmentedBar(
                x + 22.0f * scale,
                y + 4.0f * scale,
                barWidth,
                barHeight,
                selection.HealthFraction,
                ResolveColor(
                    RtsUiIcon.StatusHealth),
                width,
                height);
            y +=
                18.0f * scale;
        }

        if (selection.HasSupply)
        {
            EmitIcon(
                RtsUiIconCatalog.ResolveSupply(
                    selection.SupplyStatus),
                x,
                y,
                iconSize,
                width,
                height);

            EmitIcon(
                RtsUiIcon.StatusFuel,
                x + 22.0f * scale,
                y,
                iconSize,
                width,
                height);
            EmitSegmentedBar(
                x + 40.0f * scale,
                y + 4.0f * scale,
                40.0f * scale,
                barHeight,
                selection.FuelFraction,
                ResolveColor(
                    RtsUiIcon.StatusFuel),
                width,
                height);

            EmitIcon(
                RtsUiIcon.StatusAmmunition,
                x + 86.0f * scale,
                y,
                iconSize,
                width,
                height);
            EmitSegmentedBar(
                x + 104.0f * scale,
                y + 4.0f * scale,
                40.0f * scale,
                barHeight,
                selection.AmmunitionFraction,
                ResolveColor(
                    RtsUiIcon.StatusAmmunition),
                width,
                height);
            y +=
                18.0f * scale;
        }

        if (selection.HasPower)
        {
            EmitIcon(
                RtsUiIcon.StatusPower,
                x,
                y,
                iconSize,
                width,
                height);
        }
    }

    private void EmitMinimap(
        RtsMinimapModel model,
        Vector3 cameraTarget,
        in GameplayHudLayout layout,
        int width,
        int height)
    {
        float scale =
            layout.Scale;
        float size =
            MathF.Min(
                layout.Minimap.Width,
                layout.Minimap.Height);

        if (size <= 0.0f)
        {
            return;
        }

        float left =
            layout.Minimap.X;
        float top =
            layout.Minimap.Y;

        EmitQuad(
            left - 3.0f,
            top - 3.0f,
            size + 6.0f,
            size + 6.0f,
            new Vector4(
                0.62f,
                0.66f,
                0.62f,
                1.0f),
            width,
            height);
        EmitQuad(
            left,
            top,
            size,
            size,
            new Vector4(
                0.13f,
                0.15f,
                0.14f,
                1.0f),
            width,
            height);

        float cellSize =
            model.IntelligenceCellSizeMeters;

        for (int index = 0;
             index < model.FogCells.Count;
             index++)
        {
            RtsFogCell fog =
                model.FogCells[index];

            if (fog.Presentation.Pattern ==
                RtsFogPattern.Clear)
            {
                continue;
            }

            Vector3 worldMin =
                new(
                    fog.Cell.X *
                        cellSize,
                    0.0f,
                    fog.Cell.Z *
                        cellSize);
            Vector3 worldMax =
                worldMin +
                new Vector3(
                    cellSize,
                    0.0f,
                    cellSize);
            Vector2 first =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    worldMin,
                    model.WorldBounds);
            Vector2 second =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    worldMax,
                    model.WorldBounds);

            float cellLeft =
                left +
                first.X *
                size;
            float cellRight =
                left +
                second.X *
                size;
            float cellTop =
                top +
                (1.0f -
                 second.Y) *
                size;
            float cellBottom =
                top +
                (1.0f -
                 first.Y) *
                size;

            Vector4 fogColor =
                fog.Presentation.Pattern ==
                    RtsFogPattern.Solid
                    ? new Vector4(
                        0.015f,
                        0.02f,
                        0.02f,
                        1.0f)
                    : new Vector4(
                        0.09f,
                        0.10f,
                        0.10f,
                        1.0f);

            EmitQuad(
                cellLeft,
                cellTop,
                Math.Max(
                    1.0f,
                    cellRight -
                    cellLeft),
                Math.Max(
                    1.0f,
                    cellBottom -
                    cellTop),
                fogColor,
                width,
                height);

            if (fog.Presentation.Pattern ==
                RtsFogPattern.Hatch)
            {
                EmitQuad(
                    cellLeft,
                    (cellTop +
                     cellBottom) *
                    0.5f,
                    Math.Max(
                        1.0f,
                        cellRight -
                        cellLeft),
                    1.0f,
                    new Vector4(
                        0.25f,
                        0.27f,
                        0.25f,
                        1.0f),
                    width,
                    height);
            }
        }

        for (int index = 0;
             index < model.Symbols.Count;
             index++)
        {
            RtsMinimapSymbol symbol =
                model.Symbols[index];
            Vector2 normalized =
                RtsMinimapModelBuilder.NormalizeWorldPosition(
                    symbol.WorldPosition,
                    model.WorldBounds);
            float x =
                left +
                normalized.X *
                size;
            float y =
                top +
                (1.0f -
                 normalized.Y) *
                size;

            float symbolSize =
                symbol.Kind is
                    RtsMinimapSymbolKind.Objective or
                    RtsMinimapSymbolKind.AttackNotification
                    ? 9.0f * scale
                    : 6.0f * scale;

            EmitIcon(
                symbol.Icon,
                x -
                symbolSize *
                0.5f,
                y -
                symbolSize *
                0.5f,
                symbolSize,
                width,
                height);
        }

        Vector2 cameraPosition =
            RtsMinimapModelBuilder.NormalizeWorldPosition(
                cameraTarget,
                model.WorldBounds);
        float cameraX =
            left +
            cameraPosition.X *
            size;
        float cameraY =
            top +
            (1.0f -
             cameraPosition.Y) *
            size;
        Vector4 cameraColor =
            new(
                0.95f,
                0.95f,
                0.90f,
                1.0f);

        EmitQuad(
            cameraX - 5.0f * scale,
            cameraY - 1.0f,
            10.0f * scale,
            2.0f,
            cameraColor,
            width,
            height);
        EmitQuad(
            cameraX - 1.0f,
            cameraY - 5.0f * scale,
            2.0f,
            10.0f * scale,
            cameraColor,
            width,
            height);
    }

    private void EmitSelectionRectangle(
        Vector2 first,
        Vector2 second,
        Vector4 color,
        float scale,
        int width,
        int height)
    {
        Vector2 minimum =
            Vector2.Min(
                first,
                second);
        Vector2 maximum =
            Vector2.Max(
                first,
                second);
        float thickness =
            Math.Max(
                1.0f,
                2.0f * scale);

        EmitQuad(
            minimum.X,
            minimum.Y,
            maximum.X -
                minimum.X,
            thickness,
            color,
            width,
            height);
        EmitQuad(
            minimum.X,
            maximum.Y -
                thickness,
            maximum.X -
                minimum.X,
            thickness,
            color,
            width,
            height);
        EmitQuad(
            minimum.X,
            minimum.Y,
            thickness,
            maximum.Y -
                minimum.Y,
            color,
            width,
            height);
        EmitQuad(
            maximum.X -
                thickness,
            minimum.Y,
            thickness,
            maximum.Y -
                minimum.Y,
            color,
            width,
            height);
    }

    private void EmitSegmentedBar(
        float x,
        float y,
        float widthValue,
        float heightValue,
        double fraction,
        Vector4 color,
        int width,
        int height)
    {
        const int SegmentCount = 5;
        float gap = 2.0f;
        float segmentWidth =
            (widthValue -
             gap *
             (SegmentCount - 1)) /
            SegmentCount;
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
            Vector4 segmentColor =
                index < active
                    ? color
                    : new Vector4(
                        0.18f,
                        0.19f,
                        0.18f,
                        1.0f);

            EmitQuad(
                x +
                index *
                    (segmentWidth + gap),
                y,
                segmentWidth,
                heightValue,
                segmentColor,
                width,
                height);
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
            GlyphPattern(
                definition.Glyph);
        Vector4 color =
            ResolveColor(
                icon);
        float pixel =
            Math.Max(
                1.0f,
                size / 5.0f);

        for (int row = 0;
             row < 5;
             row++)
        {
            for (int column = 0;
                 column < 5;
                 column++)
            {
                if (pattern[
                        row *
                        5 +
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
                "RtsInformationOverlayVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "RtsInformationOverlayPixel.hlsl");

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

    private static string GlyphPattern(
        RtsUiGlyph glyph) =>
        glyph switch
        {
            RtsUiGlyph.Ore =>
                "01110" + "11111" + "11111" + "01110" + "00100",
            RtsUiGlyph.Drop =>
                "00100" + "01110" + "01110" + "11111" + "01110",
            RtsUiGlyph.Crystal =>
                "00100" + "01110" + "11111" + "01110" + "00100",
            RtsUiGlyph.Star or
            RtsUiGlyph.Objective =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Ingot =>
                "00000" + "11111" + "10001" + "11111" + "00000",
            RtsUiGlyph.Circuit =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.Ammunition =>
                "10101" + "10101" + "10101" + "11111" + "01110",
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
            RtsUiGlyph.Move =>
                "00100" + "01100" + "11111" + "01100" + "00100",
            RtsUiGlyph.Attack =>
                "10101" + "01110" + "11111" + "01110" + "10101",
            RtsUiGlyph.AttackMove =>
                "00100" + "01100" + "11111" + "01110" + "10101",
            RtsUiGlyph.Stop =>
                "11111" + "10001" + "10001" + "10001" + "11111",
            RtsUiGlyph.Hold =>
                "10001" + "10001" + "10001" + "10001" + "10001",
            RtsUiGlyph.Patrol =>
                "01110" + "10001" + "10111" + "10000" + "01110",
            RtsUiGlyph.Build =>
                "00100" + "01110" + "11111" + "10001" + "11111",
            RtsUiGlyph.Cancel or
            RtsUiGlyph.Invalid or
            RtsUiGlyph.Empty =>
                "10001" + "01010" + "00100" + "01010" + "10001",
            RtsUiGlyph.Cursor =>
                "10000" + "11000" + "10100" + "10010" + "00001",
            RtsUiGlyph.Select or
            RtsUiGlyph.DragSelect =>
                "11011" + "10001" + "00000" + "10001" + "11011",
            RtsUiGlyph.Pan =>
                "00100" + "10101" + "01110" + "10101" + "00100",
            RtsUiGlyph.Check =>
                "00001" + "00010" + "10100" + "01000" + "00000",
            RtsUiGlyph.LowSupply =>
                "00100" + "00100" + "00100" + "01110" + "00100",
            RtsUiGlyph.Critical or
            RtsUiGlyph.Alert =>
                "00100" + "00100" + "00100" + "00000" + "00100",
            RtsUiGlyph.FriendlyUnit =>
                "01110" + "11111" + "11111" + "11111" + "01110",
            RtsUiGlyph.EnemyUnit =>
                "00100" + "01110" + "11111" + "11111" + "10101",
            RtsUiGlyph.Contact =>
                "00100" + "01010" + "10001" + "01010" + "00100",
            RtsUiGlyph.Health =>
                "01010" + "11111" + "11111" + "01110" + "00100",
            _ =>
                "00100" + "01110" + "11111" + "01110" + "00100"
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

    private sealed class RuntimeUiPalette
    {
        private readonly RuntimeAssetCatalog _catalog;
        private readonly Dictionary<string, Vector4> _colors =
            new(StringComparer.Ordinal);

        public RuntimeUiPalette(
            RuntimeAssetCatalog catalog)
        {
            _catalog = catalog;
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
                factor.GetArrayLength() != 4)
            {
                color = Vector4.One;
            }
            else
            {
                float[] values =
                    factor.EnumerateArray()
                        .Select(
                            static value =>
                                value.GetSingle())
                        .ToArray();

                color =
                    new Vector4(
                        values[0],
                        values[1],
                        values[2],
                        values[3]);
            }

            _colors.Add(
                rawAssetId,
                color);
            return true;
        }
    }
}
