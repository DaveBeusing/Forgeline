using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

// Copied operational facts only. No enemy identity, position, target or inventory is retained.
public readonly record struct PlayerBattlefieldStatus(
    SuppressionLevel Suppression,
    bool IsArtillery,
    FireMissionStatus? FireMission,
    bool Resupplying);

public static class PlayerBattlefieldStatusCapture
{
    public static PlayerBattlefieldStatus Capture(EntityRegistry entities, EntityId entity, PlayerId player)
    {
        if (!entities.IsAlive(entity) ||
            !entities.TryGetComponent(entity, out ControllableEntity control) || control.Owner != player ||
            !entities.HasComponent<UnitIdentity>(entity) ||
            (entities.TryGetComponent(entity, out HealthState health) && health.IsDepleted)) return default;

        bool artillery = entities.HasComponent<ArtilleryCapability>(entity);
        return new(
            entities.TryGetComponent(entity, out SuppressionState suppression) ? suppression.Level : SuppressionLevel.Normal,
            artillery,
            artillery && entities.TryGetComponent(entity, out FireMissionState mission) ? mission.Status : null,
            entities.HasComponent<ResupplyOrder>(entity));
    }
}
