using ForgeLine.Combat;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct PlayerTacticalProtectionSummary(
    bool IsInfantry, double DamageMultiplier, string CoverName, string ScreenName);

public static class PlayerTacticalProtection
{
    public static PlayerSelectionSummary Attach(EntityRegistry entities, PlayerId player,
        PlayerSelectionSummary selection, TacticalAreaSet areas)
    {
        if (selection.Count != 1 ||
            !entities.TryGetComponent(selection.PrimaryEntity, out ControllableEntity control) || control.Owner != player ||
            !entities.HasComponent<UnitIdentity>(selection.PrimaryEntity) ||
            !entities.TryGetComponent(selection.PrimaryEntity, out WorldTransform pose))
            return selection;
        bool infantry = entities.TryGetComponent(selection.PrimaryEntity, out Targetable target) &&
            target.Class == TargetClass.Infantry;
        var protection = areas.ProtectionAt(pose.Position);
        string screen = areas.VisualScreenAt(pose.Position);
        string Name(string key)
        {
            foreach (var area in areas.Areas) if (area.Key == key) return area.DisplayName;
            return string.Empty;
        }
        return selection with { TacticalProtection = new PlayerTacticalProtectionSummary(infantry,
            infantry ? protection.DamageMultiplier : 1, Name(protection.AreaKey), Name(screen)) };
    }
}
