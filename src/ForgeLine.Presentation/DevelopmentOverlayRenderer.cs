using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed class DevelopmentOverlayRenderer : IDisposable
{
    private const int MaxVertices = 196_608;
    private const int VertexStride = 24;
    private const float GlyphPixelSize = 2.0f;
    private const float GlyphAdvance = 12.0f;
    private const float LineAdvance = 16.0f;

    private readonly IGraphicsDevice _graphics;
    private readonly IGraphicsPipeline _pipeline;
    private readonly Dictionary<int, IGraphicsBuffer> _vertexBuffers = new(4);
    private readonly OverlayVertex[] _vertices =
        new OverlayVertex[MaxVertices];
    private int _vertexCount;
    private float _uiScale = 1.0f;
    private bool _disposed;

    public DevelopmentOverlayRenderer(IGraphicsDevice graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
        _pipeline = CreatePipeline(graphics);
    }

    public int LastRenderedVertexCount { get; private set; }

    public void Render(
        IGraphicsCommandContext context,
        in DevelopmentOverlayMetrics metrics,
        RtsCamera? camera = null,
        DebugDraw? debugDraw = null,
        PlayerExperienceSnapshot? playerExperience = null,
        bool showDevelopmentMetrics = true,
        PlayerActionSnapshot? playerActions = null,
        PlayerActionPanelView? actionPanel = null,
        TacticalTargetingView? tacticalTargeting = null,
        FormationTemplate activeFormation = FormationTemplate.Compact,
        PreAlphaUxView preAlphaUx = default,
        float uiScale = 1.0f,
        DebugDraw? gameplayOverlay = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);

        _vertexCount = 0;
        _uiScale =
            float.IsFinite(uiScale)
                ? Math.Clamp(uiScale, 0.75f, 2.0f)
                : 1.0f;

        if (showDevelopmentMetrics)
        {
        Span<char> buffer = stackalloc char[1_024];
        var builder = new OverlayTextBuilder(buffer);

        builder.Append("FORGELINE DEV");
        builder.NewLine();
        builder.Append("FPS ");
        builder.Append(metrics.FramesPerSecond, "F1");
        builder.Append(" FRAME ");
        builder.Append(metrics.FrameMilliseconds, "F2");
        builder.Append("MS CPU ");
        builder.Append(metrics.CpuRenderMilliseconds, "F2");
        builder.Append("MS");
        builder.NewLine();

        builder.Append("SIM TICK ");
        builder.Append(metrics.SimulationTick);
        builder.Append(" ");
        builder.Append(metrics.SimulationTickMilliseconds, "F2");
        builder.Append("MS ENTITIES ");
        builder.Append(metrics.SimulationEntityCount);
        builder.NewLine();

        builder.Append("CHUNKS ");
        builder.Append(metrics.VisibleTerrainChunks);
        builder.Append("/");
        builder.Append(metrics.TotalTerrainChunks);
        builder.Append(" DRAWS ");
        builder.Append(metrics.DrawCalls);
        builder.Append(" INST ");
        builder.Append(metrics.RenderedInstances);
        builder.Append("/");
        builder.Append(metrics.TotalInstances);
        builder.NewLine();

        builder.Append("JOBS ");
        builder.Append(metrics.JobExecutionMilliseconds, "F2");
        builder.Append("MS ALLOC ");
        builder.AppendBytes(metrics.TotalAllocatedBytes);
        builder.Append(" HEAP ");
        builder.AppendBytes(metrics.HeapSizeBytes);
        builder.NewLine();

        builder.Append("GC ");
        builder.Append(metrics.Gen0Collections);
        builder.Append("/");
        builder.Append(metrics.Gen1Collections);
        builder.Append("/");
        builder.Append(metrics.Gen2Collections);
        builder.NewLine();

        builder.Append("OVERLAY GAME ");
        builder.Append(metrics.GameplayOverlayLines);
        builder.Append(" DEBUG ");
        builder.Append(metrics.DebugOverlayLines);
        builder.Append(" DROP ");
        builder.Append(metrics.DebugOverlayDroppedLines);
        builder.Append(" CPU ");
        builder.Append(
            metrics.DebugOverlayCpuMilliseconds,
            "F3");
        builder.Append("MS");

        EmitReadableText(
            builder.Written,
            12.0f,
            12.0f,
            new Vector4(0.98f, 0.78f, 0.18f, 1.0f),
            context.Width,
            context.Height);
        }

        if (playerExperience.HasValue)
        {
            EmitPlayerExperience(
                playerExperience.Value,
                showDevelopmentMetrics ? 112.0f : 12.0f,
                context.Width,
                context.Height);
        }

        EmitPreAlphaUx(
            preAlphaUx,
            context.Width,
            context.Height);

        if (camera is not null &&
            gameplayOverlay is not null &&
            gameplayOverlay.Enabled)
        {
            EmitWorldLabels(
                camera,
                gameplayOverlay,
                context.Width,
                context.Height);
        }

        if (camera is not null &&
            debugDraw is not null &&
            debugDraw.Enabled)
        {
            EmitWorldLabels(
                camera,
                debugDraw,
                context.Width,
                context.Height);
        }

        if (_vertexCount == 0)
        {
            LastRenderedVertexCount = 0;
            return;
        }

        IGraphicsBuffer vertexBuffer = GetFrameVertexBuffer(context.FrameIndex);
        vertexBuffer.SetData<OverlayVertex>(
            _vertices.AsSpan(0, _vertexCount));

        context.SetPipeline(_pipeline);
        context.SetVertexBuffer(vertexBuffer, VertexStride);
        context.Draw(_vertexCount);

        LastRenderedVertexCount = _vertexCount;
    }

    private void EmitWorldLabels(
        RtsCamera camera,
        DebugDraw draw,
        int width,
        int height)
    {
        foreach (DebugLabel label in draw.Labels)
        {
            ScreenProjection projection =
                camera.WorldToScreen(
                    label.Position,
                    width,
                    height);

            if (!projection.IsVisible)
            {
                continue;
            }

            EmitText(
                label.Text.AsSpan(),
                projection.Position.X,
                projection.Position.Y,
                label.Color,
                width,
                height);
        }
    }

    private void EmitPlayerExperience(
        in PlayerExperienceSnapshot snapshot,
        float originY,
        int width,
        int height)
    {
        Span<char> buffer = stackalloc char[2_048];
        var builder = new OverlayTextBuilder(buffer);

        builder.Append("MATCH ");
        builder.Append(
            snapshot.MatchStatus switch
            {
                PlayerMatchStatus.Loading => "LOADING",
                PlayerMatchStatus.Active => "ACTIVE",
                PlayerMatchStatus.Victory => "VICTORY",
                PlayerMatchStatus.Defeat => "DEFEAT",
                PlayerMatchStatus.Draw => "DRAW",
                PlayerMatchStatus.Ended => "ENDED",
                _ => "UNKNOWN"
            });

        int totalSeconds =
            (int)Math.Clamp(
                snapshot.Statistics.DurationSeconds,
                0.0,
                int.MaxValue);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        builder.Append("  TIME ");
        builder.Append(minutes);
        builder.Append(":");
        if (seconds < 10)
        {
            builder.Append("0");
        }

        builder.Append(seconds);
        builder.NewLine();

        builder.Append("INTEL EXP ");
        builder.Append(snapshot.Intelligence.ExploredCells);
        builder.Append(" VIS ");
        builder.Append(snapshot.Intelligence.VisibleCells);
        builder.Append(" CONTACTS ");
        builder.Append(snapshot.Intelligence.KnownContacts);
        builder.NewLine();

        EmitText(
            builder.Written,
            12.0f,
            originY,
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);

        if (!snapshot.IsMatchComplete)
        {
            return;
        }

        string result =
            snapshot.MatchStatus switch
            {
                PlayerMatchStatus.Victory => "VICTORY",
                PlayerMatchStatus.Defeat => "DEFEAT",
                PlayerMatchStatus.Draw => "DRAW",
                _ => "MATCH ENDED"
            };

        EmitText(
            result.AsSpan(),
            MathF.Max(12.0f, width * 0.5f - 48.0f),
            MathF.Max(12.0f, height * 0.32f),
            new Vector4(0.98f, 0.78f, 0.18f, 1.0f),
            width,
            height);
        EmitText(
            "R RESTART  ESC RETURN".AsSpan(),
            MathF.Max(12.0f, width * 0.5f - 126.0f),
            MathF.Max(30.0f, height * 0.32f + 28.0f),
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);

        Span<char> statisticsBuffer =
            stackalloc char[256];
        var statistics =
            new OverlayTextBuilder(
                statisticsBuffer);
        statistics.Append("UNITS PRODUCED ");
        statistics.Append(
            snapshot.Statistics.UnitsProduced);
        statistics.Append("  BUILDINGS ");
        statistics.Append(
            snapshot.Statistics.BuildingsConstructed);
        statistics.Append("  OUTPUT ");
        statistics.Append(
            snapshot.Statistics.ProcessedOutput,
            "F0");

        EmitText(
            statistics.Written,
            MathF.Max(12.0f, width * 0.5f - 156.0f),
            MathF.Max(48.0f, height * 0.32f + 52.0f),
            new Vector4(0.92f, 0.96f, 1.0f, 1.0f),
            width,
            height);
    }

    private void EmitPreAlphaUx(
        in PreAlphaUxView view,
        int width,
        int height)
    {
        if (view.Mode == PreAlphaUxMode.None &&
            !view.ShowOnboarding)
        {
            return;
        }

        Span<char> buffer =
            stackalloc char[4_096];
        var builder =
            new OverlayTextBuilder(buffer);

        if (view.Mode == PreAlphaUxMode.MatchSetup)
        {
            builder.Append("FORGELINE PRE-ALPHA");
            builder.NewLine();
            builder.Append("MATCH SETUP");
            builder.NewLine();
            builder.NewLine();
            builder.Append("MAP ");
            builder.Append(view.MapName);
            builder.NewLine();
            builder.Append("PLAYER ");
            builder.Append(view.PlayerFaction);
            builder.NewLine();
            builder.Append("OPPONENT ");
            builder.Append(view.OpponentDescription);
            builder.NewLine();
            builder.NewLine();
            builder.Append("ENTER START MATCH");
            builder.NewLine();
            builder.Append("ESC EXIT");
            builder.NewLine();
            builder.Append("F12 CONTROLS AND ONBOARDING");
            builder.NewLine();
            builder.NewLine();
            builder.Append("SETTINGS ");
            builder.Append(view.SettingsPath);

            EmitReadableText(
                builder.Written,
                32.0f,
                160.0f,
                new Vector4(
                    0.98f,
                    0.86f,
                    0.32f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.Mode == PreAlphaUxMode.Paused)
        {
            builder.Append("PAUSED");
            builder.NewLine();
            builder.Append("ESC OR SPACE RESUME");
            builder.NewLine();
            builder.Append("F12 CONTROLS");

            EmitReadableText(
                builder.Written,
                32.0f,
                160.0f,
                new Vector4(
                    0.98f,
                    0.86f,
                    0.32f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.Mode == PreAlphaUxMode.Help)
        {
            builder.Append("CONTROLS");
            builder.NewLine();
            builder.Append("CAMERA ");
            builder.Append(view.PanForwardBinding);
            builder.Append("/");
            builder.Append(view.PanLeftBinding);
            builder.Append("/");
            builder.Append(view.PanBackwardBinding);
            builder.Append("/");
            builder.Append(view.PanRightBinding);
            builder.Append(" PAN  ");
            builder.Append(view.RotateLeftBinding);
            builder.Append("/");
            builder.Append(view.RotateRightBinding);
            builder.Append(" ROTATE  ");
            builder.Append(view.PitchUpBinding);
            builder.Append("/");
            builder.Append(view.PitchDownBinding);
            builder.Append(" PITCH");
            builder.NewLine();
            builder.Append("MOUSE WHEEL ZOOM  ");
            builder.Append(view.DragPanBinding);
            builder.Append(" DRAG PAN");
            builder.NewLine();
            builder.Append("LEFT CLICK SELECT  SHIFT LEFT CLICK MULTI SELECT");
            builder.NewLine();
            builder.Append("RIGHT CLICK MOVE  B BUILD  P PROCESS  U UNITS");
            builder.NewLine();
            builder.Append("L LOGISTICS  Y SUPPLY  K COMBAT");
            builder.NewLine();
            builder.Append("F1 PERFORMANCE METRICS  F2 WORLD DEBUG  F3 FORMATION");
            builder.NewLine();
            builder.Append("F4 COMMAND CORE  F5 POWER PLANT  F6 EXTRACTOR");
            builder.NewLine();
            builder.Append("F7 STORAGE DEPOT  F8 SMELTER  F9 ROTATE BUILDING");
            builder.NewLine();
            builder.Append("F10 STRATEGIC OVERLAY  F11 MINIMAP  F12 CLOSE HELP");
            builder.NewLine();
            builder.Append("ESC OR SPACE PAUSE MENU");
            builder.NewLine();
            builder.NewLine();
            builder.Append("QUICK START");
            builder.NewLine();
            builder.Append("1 MOVE CAMERA AND SELECT YOUR UNITS");
            builder.NewLine();
            builder.Append("2 RIGHT CLICK TO MOVE AND SCOUT");
            builder.NewLine();
            builder.Append("3 BUILD POWER AND INDUSTRY WITH B");
            builder.NewLine();
            builder.Append("4 PROCESS MATERIALS WITH P AND PRODUCE UNITS WITH U");
            builder.NewLine();
            builder.Append("5 USE L AND Y TO KEEP THE FRONT SUPPLIED");
            builder.NewLine();
            builder.Append("6 USE K FOR ATTACK ATTACK-MOVE RETREAT AND ARTILLERY");
            builder.NewLine();
            builder.Append("7 DESTROY THE ENEMY COMMAND CORE TO WIN");
            builder.NewLine();

            EmitReadableText(
                builder.Written,
                24.0f,
                140.0f,
                new Vector4(
                    0.93f,
                    0.95f,
                    0.98f,
                    1.0f),
                width,
                height);
            return;
        }

        if (view.ShowOnboarding)
        {
            builder.Append("F12 HELP  ESC PAUSE MENU  GOAL DESTROY ENEMY COMMAND CORE");

            EmitReadableText(
                builder.Written,
                12.0f,
                MathF.Max(
                    12.0f,
                    height - 28.0f * _uiScale),
                new Vector4(
                    0.93f,
                    0.95f,
                    0.98f,
                    1.0f),
                width,
                height);
        }
    }

    private void EmitReadableText(
        ReadOnlySpan<char> text,
        float originX,
        float originY,
        Vector4 color,
        int width,
        int height)
    {
        EmitText(
            text,
            originX + 2.0f,
            originY + 2.0f,
            new Vector4(
                0.02f,
                0.02f,
                0.02f,
                0.95f),
            width,
            height);
        EmitText(
            text,
            originX,
            originY,
            color,
            width,
            height);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (IGraphicsBuffer vertexBuffer in _vertexBuffers.Values)
        {
            vertexBuffer.Dispose();
        }

        _vertexBuffers.Clear();
        _pipeline.Dispose();
        _disposed = true;
    }

    private void EmitText(
        ReadOnlySpan<char> text,
        float originX,
        float originY,
        Vector4 color,
        int width,
        int height)
    {
        float x = originX;
        float y = originY;

        for (int index = 0; index < text.Length; index++)
        {
            char character = char.ToUpperInvariant(text[index]);

            if (character == '\n')
            {
                x = originX;
                y += LineAdvance * _uiScale;
                continue;
            }

            EmitGlyph(
                character,
                x,
                y,
                color,
                width,
                height);
            x += GlyphAdvance * _uiScale;
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
        string pattern = GlyphPattern(character);
        if (pattern.Length == 0)
        {
            return;
        }

        for (int row = 0; row < 7; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                if (pattern[(row * 5) + column] != '1')
                {
                    continue;
                }

                EmitPixel(
                    x + column * GlyphPixelSize * _uiScale,
                    y + row * GlyphPixelSize * _uiScale,
                    color,
                    width,
                    height);
            }
        }
    }

    private void EmitPixel(
        float x,
        float y,
        Vector4 color,
        int width,
        int height)
    {
        if (_vertexCount > MaxVertices - 6 ||
            width <= 0 ||
            height <= 0)
        {
            return;
        }

        float left = (x / width) * 2.0f - 1.0f;
        float right =
            ((x + GlyphPixelSize * _uiScale) / width) * 2.0f - 1.0f;
        float top = 1.0f - (y / height) * 2.0f;
        float bottom =
            1.0f -
            ((y + GlyphPixelSize * _uiScale) / height) * 2.0f;

        Vector2 topLeft = new(left, top);
        Vector2 topRight = new(right, top);
        Vector2 bottomLeft = new(left, bottom);
        Vector2 bottomRight = new(right, bottom);

        _vertices[_vertexCount++] =
            new OverlayVertex(topLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomRight, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(topRight, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(topLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomLeft, color);
        _vertices[_vertexCount++] =
            new OverlayVertex(bottomRight, color);
    }

    private IGraphicsBuffer GetFrameVertexBuffer(int frameIndex)
    {
        if (_vertexBuffers.TryGetValue(frameIndex, out IGraphicsBuffer? buffer))
        {
            return buffer;
        }

        buffer = _graphics.CreateBuffer(
            new GraphicsBufferDescription(
                checked((ulong)_vertices.Length * VertexStride),
                GraphicsBufferMemory.Upload));
        _vertexBuffers.Add(frameIndex, buffer);
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
                output.Position =
                    float4(input.Position, 0.0f, 1.0f);
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

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader = compiler.Compile(
            vertexShaderSource,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "OverlayVertex.hlsl");
        GraphicsShaderBytecode pixelShader = compiler.Compile(
            pixelShaderSource,
            GraphicsShaderStage.Pixel,
            "PSMain",
            "OverlayPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(vertexShader, pixelShader)
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

    private static string GlyphPattern(char value) =>
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
            '_' => "00000000000000000000000000000011111",
            ' ' => "",
            _ => "11111000010001000100000000010000100"
        };

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OverlayVertex(
        Vector2 Position,
        Vector4 Color);

    private ref struct OverlayTextBuilder
    {
        private Span<char> _buffer;
        private int _length;

        public OverlayTextBuilder(Span<char> buffer)
        {
            _buffer = buffer;
            _length = 0;
        }

        public readonly ReadOnlySpan<char> Written =>
            _buffer[.._length];

        public void NewLine() => Append("\n");

        public void Append(string value)
        {
            if (value.AsSpan().TryCopyTo(_buffer[_length..]))
            {
                _length += value.Length;
            }
        }

        public void Append(int value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider: CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void Append(ulong value)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    provider: CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void Append(double value, string format)
        {
            if (value.TryFormat(
                    _buffer[_length..],
                    out int written,
                    format,
                    CultureInfo.InvariantCulture))
            {
                _length += written;
            }
        }

        public void AppendBytes(long bytes)
        {
            double megabytes = Math.Max(bytes, 0) / (1024.0 * 1024.0);
            Append(megabytes, "F1");
            Append("MB");
        }
    }
}
