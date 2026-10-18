using ForgeLine.Core;

namespace ForgeLine.Game;

public readonly record struct UnitWreckPresentationIdentity(
    UnitId UnitId,
    FactionId ContentFaction)
{
    /// <summary>Cosmetic lifecycle correlation only; never an authoritative live entity reference.</summary>
    public EntityId SourceEntity { get; init; }
}
