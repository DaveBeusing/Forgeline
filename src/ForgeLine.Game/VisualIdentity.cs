namespace ForgeLine.Game;

[Flags]
public enum VisualVisibilityFlags : uint
{
    None = 0,
    World = 1 << 0,
    Debug = 1 << 1,
    Default = World
}

public readonly record struct VisualIdentity(
    uint VisualId,
    VisualVisibilityFlags Visibility = VisualVisibilityFlags.Default);
