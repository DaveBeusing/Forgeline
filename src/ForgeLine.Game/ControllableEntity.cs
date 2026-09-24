namespace ForgeLine.Game;

[Flags]
public enum ControllableEntityCategory : uint
{
    None = 0,
    Unit = 1 << 0,
    Building = 1 << 1,
    Logistics = 1 << 2,
    All = uint.MaxValue
}

public readonly record struct ControllableEntity(
    PlayerId Owner,
    ControllableEntityCategory Category)
{
    public bool IsControllable =>
        Owner.IsSpecified &&
        Category != ControllableEntityCategory.None;
}
