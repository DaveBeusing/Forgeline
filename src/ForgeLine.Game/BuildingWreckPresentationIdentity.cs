using ForgeLine.Core;

namespace ForgeLine.Game;

public readonly record struct BuildingWreckPresentationIdentity(
    BuildingId BuildingId,
    PlayerId Owner);
