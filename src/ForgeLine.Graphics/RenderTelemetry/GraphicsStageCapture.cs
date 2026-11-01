namespace ForgeLine.Graphics;

[Flags]
public enum GraphicsCaptureStages : byte
{
    ShadowDepth = 1, WorldDepth = 2, SceneLinear = 4, ComposedColor = 8, FinalColor = 16,
    All = ShadowDepth | WorldDepth | SceneLinear | ComposedColor | FinalColor
}

public sealed record GraphicsStageFootprint(GraphicsCaptureStages Stage, GraphicsTextureReadbackLayout? Layout,
    ulong RawBytes, string? UnavailableReason);

public sealed record GraphicsStageReadback(GraphicsCaptureStages Stage, GraphicsTextureReadbackLayout? Layout,
    ReadOnlyMemory<byte> RawBytes, string? UnavailableReason);

// Explicit diagnostic frames only. Logical rows exclude native footprint padding;
// RawBytes and Layout are retained independently for byte-exact offline inspection.
public sealed record GraphicsStageCapture(byte[] FinalRgba, GraphicsFrameSubmission Submission,
    ulong CompletedFence, int FrameIndex, ulong PresentedBefore, ulong PresentedAfter,
    GraphicsDeviceTelemetryIdentity DeviceIdentity, IReadOnlyList<GraphicsStageReadback> Stages)
{ public int BufferCount { get; init; } public GraphicsWorldBindingJournal? WorldBindings { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GraphicsBoundInputProvenance? BoundInputProvenance { get; init; }
}

public static class GraphicsReadbackRows
{
    public static int BytesPerTexel(string format) => format switch
    {
        "R16G16B16A16_Float" => 8,
        "R8G8B8A8_UNorm" or "R32_Typeless" or "R32_Float" or "D32_Float" => 4,
        _ => throw new NotSupportedException("Unsupported diagnostic texture format: " + format)
    };
    public static byte[] LogicalBytes(GraphicsTextureReadbackLayout layout, ReadOnlySpan<byte> raw)
    {
        if (layout.Width <= 0 || layout.Height <= 0 || layout.RowPitch <= 0)
            throw new ArgumentException("Readback dimensions and pitch must be positive.", nameof(layout));
        int rowBytes = checked(layout.Width * BytesPerTexel(layout.Format));
        if (layout.RowPitch < rowBytes || layout.Offset > int.MaxValue ||
            checked((long)layout.Offset + (long)(layout.Height - 1) * layout.RowPitch + rowBytes) > raw.Length)
            throw new ArgumentException("Readback footprint escapes retained bytes.", nameof(layout));
        var logical = new byte[checked(rowBytes * layout.Height)];
        for (int y = 0; y < layout.Height; y++)
            raw.Slice(checked((int)layout.Offset + y * layout.RowPitch), rowBytes).CopyTo(logical.AsSpan(y * rowBytes));
        return logical;
    }
}
