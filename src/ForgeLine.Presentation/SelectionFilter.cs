using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct SelectionFilter(
    PlayerId Owner,
    ControllableEntityCategory Categories)
{
    public bool Allows(in SelectablePresentationMetadata metadata) =>
        Owner.IsSpecified &&
        metadata.IsSelectable &&
        metadata.Owner == Owner &&
        (metadata.Category & Categories) != 0;
}
