namespace ForgeLine.Graphics;

public sealed record GraphicsDeviceInfo(
    string AdapterName,
    ulong DedicatedVideoMemoryBytes,
    bool IsSoftwareAdapter,
    string FeatureLevel,
    bool DebugLayerEnabled)
{
    public GraphicsAdvancedCapabilities Advanced { get; init; } = new(false, false, false, false);
}

// Capability reports never enable an optional rendering path.
public readonly record struct GraphicsAdvancedCapabilities(bool MeshShaderQuerySucceeded, bool MeshShadersSupported,
    bool RaytracingQuerySucceeded, bool RaytracingSupported);
