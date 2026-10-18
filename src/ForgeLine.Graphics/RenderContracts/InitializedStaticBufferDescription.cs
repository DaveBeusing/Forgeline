namespace ForgeLine.Graphics.RenderContracts;

public enum StaticGraphicsBufferUsage { Vertex, Index }

/// <summary>Complete synchronous input transfer on the render owner; no CPU memory is retained.</summary>
public readonly record struct InitializedStaticBufferDescription(ulong SizeInBytes, StaticGraphicsBufferUsage Usage)
{
    internal void Validate(int byteCount)
    {
        if (SizeInBytes == 0 || SizeInBytes != (ulong)byteCount)
            throw new ArgumentException("Initialized static buffers require exactly SizeInBytes of input.", nameof(byteCount));
        if (!Enum.IsDefined(Usage)) throw new ArgumentException("Unsupported initialized static buffer usage.");
    }
}

// External bindings remain frozen until an explicitly coordinated ABI revision.
public static class RenderBindingAbi
{
    public const int InstanceStride = 112;
    public const int MeshStride = 48;
    public const int TerrainStride = 48;
    public const int InstanceFrameConstants = 35;
    public const int TerrainFrameConstants = 51;
    public const int TerrainTextureSlots = 13;
    public const int ShadowTextureSlot = 15;
    public const int ShadowFrameConstantSlot = 1;
}
