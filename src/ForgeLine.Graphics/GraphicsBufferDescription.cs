namespace ForgeLine.Graphics;

public enum GraphicsBufferMemory
{
    GpuLocal,
    Upload
}

public readonly record struct GraphicsBufferDescription(
    ulong SizeInBytes,
    GraphicsBufferMemory Memory = GraphicsBufferMemory.GpuLocal)
{
    internal void Validate()
    {
        if (SizeInBytes == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SizeInBytes),
                SizeInBytes,
                "Graphics buffers must contain at least one byte.");
        }
    }
}
