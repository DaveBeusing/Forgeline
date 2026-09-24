namespace ForgeLine.Game;

[Flags]
public enum VisualVisibilityMask : uint
{
    None = 0,
    World = 1 << 0,
    Debug = 1 << 1,
    Default = World
}

public readonly record struct VisualIdentity(
    uint VisualId,
    VisualVisibilityMask Visibility = VisualVisibilityMask.Default);
