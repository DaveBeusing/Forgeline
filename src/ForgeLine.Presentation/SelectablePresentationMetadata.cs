using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct SelectablePresentationMetadata(
    PlayerId Owner,
    ControllableEntityCategory Category)
{
    public static SelectablePresentationMetadata None => default;

    public bool IsSelectable =>
        Owner.IsSpecified &&
        Category != ControllableEntityCategory.None;
}
