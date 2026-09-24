namespace ForgeLine.Graphics;

public sealed record GraphicsDeviceInfo(
    string AdapterName,
    ulong DedicatedVideoMemoryBytes,
    bool IsSoftwareAdapter,
    string FeatureLevel,
    bool DebugLayerEnabled);
