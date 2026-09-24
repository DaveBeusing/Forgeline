namespace ForgeLine.Presentation;

public readonly record struct RenderMeshHandle(uint Value)
{
    public static RenderMeshHandle Invalid => default;

    public bool IsValid => Value != 0;
}

public readonly record struct RenderMaterialHandle(uint Value)
{
    public static RenderMaterialHandle Invalid => default;

    public static RenderMaterialHandle Default => new(1);

    public bool IsValid => Value != 0;
}

[Flags]
public enum RenderVisibilityMask : uint
{
    None = 0,
    World = 1 << 0,
    Debug = 1 << 1,
    Default = World
}
