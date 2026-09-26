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
        bool showDevelopmentMetrics = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);

        _vertexCount = 0;

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

        EmitText(
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

        if (camera is not null &&
            debugDraw is not null &&
            debugDraw.Enabled)
        {
            foreach (DebugLabel label in debugDraw.Labels)
            {
                ScreenProjection projection =
                    camera.WorldToScreen(
                        label.Position,
                        context.Width,
                        context.Height);

                if (!projection.IsVisible)
                {
                    continue;
                }

                EmitText(
                    label.Text.AsSpan(),
                    projection.Position.X,
                    projection.Position.Y,
                    label.Color,
                    context.Width,
                    context.Height);
            }
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

        builder.Append("STEEL ");
        builder.Append(snapshot.Resources.Steel, "F0");
        builder.Append("  FUEL ");
        builder.Append(snapshot.Resources.Fuel, "F0");
        builder.Append("  ELECTRONICS ");
        builder.Append(snapshot.Resources.Electronics, "F0");
        builder.Append("  AMMO ");
        builder.Append(snapshot.Resources.Ammunition, "F0");
        builder.NewLine();

        builder.Append("RAW FE ");
        builder.Append(snapshot.Resources.FerrousOre, "F0");
        builder.Append("  VOL ");
        builder.Append(snapshot.Resources.Volatiles, "F0");
        builder.Append("  SIL ");
        builder.Append(snapshot.Resources.Silicates, "F0");
        builder.NewLine();

        builder.Append("POWER ");
        builder.Append(snapshot.Power.Generation, "F0");
        builder.Append("/");
        builder.Append(snapshot.Power.Demand, "F0");
        builder.Append("  ");
        builder.Append(
            snapshot.Power.IsConstrained
                ? "CONSTRAINED"
                : "STABLE");
        builder.NewLine();

        builder.Append("INTEL EXP ");
        builder.Append(snapshot.Intelligence.ExploredCells);
        builder.Append(" VIS ");
        builder.Append(snapshot.Intelligence.VisibleCells);
        builder.Append(" CONTACTS ");
        builder.Append(snapshot.Intelligence.KnownContacts);
        builder.NewLine();

        if (snapshot.Selection.Count > 0)
        {
            builder.Append("SELECTED ");
            builder.Append(snapshot.Selection.Count);
            builder.Append(" ");
            builder.Append(snapshot.Selection.DisplayName);
            builder.NewLine();

            if (snapshot.Selection.HasHealth)
            {
                builder.Append("HP ");
                builder.Append(
                    snapshot.Selection.HealthFraction * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasSupply)
            {
                builder.Append("FUEL ");
                builder.Append(
                    snapshot.Selection.FuelFraction * 100.0,
                    "F0");
                builder.Append("% AMMO ");
                builder.Append(
                    snapshot.Selection.AmmunitionFraction * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasReadiness)
            {
                builder.Append("READY ");
                builder.Append(
                    snapshot.Selection.Readiness * 100.0,
                    "F0");
                builder.Append("% ");
            }

            if (snapshot.Selection.HasPower)
            {
                builder.Append("POWER ");
                builder.Append(
                    snapshot.Selection.PowerState.ToString());
            }

            builder.NewLine();

            if (snapshot.Selection.Work.Kind != PlayerWorkKind.None)
            {
                builder.Append(
                    snapshot.Selection.Work.Kind.ToString());
                builder.Append(" ");
                builder.Append(
                    snapshot.Selection.Work.Activity);
                builder.Append(" ");
                builder.Append(
                    snapshot.Selection.Work.Progress * 100.0,
                    "F0");
                builder.Append("% ");
                builder.Append(
                    snapshot.Selection.Work.State.ToString());

                if (!string.IsNullOrWhiteSpace(
                        snapshot.Selection.Work.BlockReason))
                {
                    builder.Append(" ");
                    builder.Append(
                        snapshot.Selection.Work.BlockReason);
                }

                builder.NewLine();
            }
        }

        if (snapshot.Alerts != PlayerAlertFlags.None)
        {
            builder.Append("ALERT ");

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertFlags.LowPower))
            {
                builder.Append("LOW POWER ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertFlags.ProductionBlocked))
            {
                builder.Append("PRODUCTION BLOCKED ");
                builder.Append(
                    snapshot.BlockedProductionFacilities);
                builder.Append(" ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertFlags.SupplyCritical))
            {
                builder.Append("SUPPLY CRITICAL ");
                builder.Append(snapshot.CriticalSupplyUnits);
                builder.Append(" ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertFlags.CommandCoreDamaged))
            {
                builder.Append("COMMAND CORE DAMAGED ");
            }

            if (snapshot.Alerts.HasFlag(
                    PlayerAlertFlags.CommandCoreDestroyed))
            {
                builder.Append("COMMAND CORE DESTROYED ");
            }

            builder.NewLine();
        }

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
                y += LineAdvance;
                continue;
            }

            EmitGlyph(
                character,
                x,
                y,
                color,
                width,
                height);
            x += GlyphAdvance;
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
                    x + column * GlyphPixelSize,
                    y + row * GlyphPixelSize,
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
            ((x + GlyphPixelSize) / width) * 2.0f - 1.0f;
        float top = 1.0f - (y / height) * 2.0f;
        float bottom =
            1.0f -
            ((y + GlyphPixelSize) / height) * 2.0f;

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
