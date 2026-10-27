using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal readonly record struct BattlefieldStatusMarker(Vector2 Position, HudRect Bounds, string Label, string Symbol, bool Critical);

// Render-owner scratch. Rebuilt from the same completed tick, never published or retained as history.
internal sealed class BattlefieldStatusOverlay
{
    internal const int MaximumMarkers = 64;
    private readonly BattlefieldStatusMarker[] _markers = new BattlefieldStatusMarker[MaximumMarkers];
    public ReadOnlySpan<BattlefieldStatusMarker> Markers => _markers.AsSpan(0, Count);
    public int Count { get; private set; }

    public void Update(PresentationSnapshot snapshot, RtsCamera camera, in RtsInformationLayerView view,
        HudRect clip, float scale, int width, int height)
    {
        Count = 0;
        if (snapshot.PlayerExperience is not { IsMatchComplete: false } player || player.Tick != snapshot.Tick ||
            !player.Player.IsSpecified || snapshot.CombatGroups is not { } units || units.Tick != snapshot.Tick || units.SessionId != snapshot.SessionId ||
            !snapshot.SessionId.IsSpecified || width <= 0 || height <= 0) return;

        bool strategic = camera.Distance >= 600;
        int limit = strategic ? 32 : MaximumMarkers;
        int labels = strategic ? 4 : 8;
        bool showSupply = view.OverlayMode is StrategicOverlayMode.Supply or StrategicOverlayMode.All;
        int inspected = Math.Min(snapshot.Instances.Length, 4096);
        // Selection first, then pinned and empty/critical supply; ordinary details cannot starve warnings.
        for (int priority = 0; priority < 6 && Count < limit; priority++)
        for (int i = 0; i < inspected && Count < limit; i++)
        {
            ref readonly var instance = ref snapshot.Instances[i];
            if (instance.Selectable.Owner != player.Player || (instance.Visibility & RenderVisibilityMask.World) == 0 ||
                !units.TryGet(instance.Entity, out var unit)) continue;
            bool selected = view.SelectedEntities?.Contains(instance.Entity) == true;
            if ((!selected && !showSupply) || Priority(unit, selected) != priority) continue;
            var point = camera.WorldToScreen(instance.Transform.Position + Vector3.UnitY *
                (PresentationBounds.ResolveLocalHalfExtents(instance).Y + 2), width, height);
            if (!point.IsVisible) continue;
            float size = (strategic ? 7 : 9) * scale;
            var bounds = new HudRect(point.Position.X - size, point.Position.Y - size, size * 2, size * 2);
            if (bounds.X < clip.X || bounds.Right > clip.Right || bounds.Y < clip.Y || bounds.Bottom > clip.Bottom) continue;
            string label = labels > 0 ? Label(unit) : string.Empty;
            var occupied = label.Length > 0
                ? new HudRect(bounds.X, bounds.Y, MathF.Min(330 * scale, clip.Right - bounds.X), 38 * scale)
                : bounds;
            bool overlaps = false;
            for (int j = 0; j < Count; j++)
            {
                var other = _markers[j].Bounds;
                if (occupied.X < other.Right && occupied.Right > other.X && occupied.Y < other.Bottom && occupied.Bottom > other.Y)
                { overlaps = true; break; }
            }
            if (overlaps || occupied.Bottom > clip.Bottom) continue;
            if (label.Length > 0) labels--;
            bool critical = unit.BattlefieldStatus.Suppression == SuppressionLevel.Pinned ||
                unit.HasSupply && unit.SupplyStatus is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied ||
                unit.BattlefieldStatus.FireMission == FireMissionStatus.NoAmmo;
            string symbol = unit.BattlefieldStatus.Suppression == SuppressionLevel.Pinned ? "P" :
                critical ? "!" : unit.BattlefieldStatus.Suppression == SuppressionLevel.Suppressed ? "S" :
                unit.HasSupply && unit.SupplyStatus == BattlefieldSupplyStatus.LowSupply ? "L" : ">";
            _markers[Count++] = new(point.Position, occupied, label, symbol, critical);
        }
    }

    private static int Priority(in CombatGroupMemberReadModel unit, bool selected)
    {
        if (selected) return 0;
        if (unit.BattlefieldStatus.Suppression == SuppressionLevel.Pinned) return 1;
        if (unit.BattlefieldStatus.FireMission == FireMissionStatus.NoAmmo ||
            unit.HasSupply && unit.SupplyStatus == BattlefieldSupplyStatus.Unsupplied) return 2;
        if (unit.HasSupply && unit.SupplyStatus == BattlefieldSupplyStatus.Critical) return 3;
        if (unit.BattlefieldStatus.Suppression == SuppressionLevel.Suppressed) return 4;
        return unit.HasSupply && unit.SupplyStatus == BattlefieldSupplyStatus.LowSupply ? 5 : -1;
    }

    private static string Label(in CombatGroupMemberReadModel unit)
    {
        var status = unit.BattlefieldStatus;
        if (status.Suppression == SuppressionLevel.Pinned) return "PINNED";
        if (status.IsArtillery && (status.FireMission == FireMissionStatus.NoAmmo || unit.HasSupply && unit.AmmunitionFraction <= 0))
            return "CANNOT FIRE / AMMO / RESUPPLY";
        if (unit.HasSupply && unit.SupplyStatus is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied)
            return unit.FuelFraction <= 0 ? "FUEL EMPTY / RESUPPLY" : unit.AmmunitionFraction <= 0 ? "AMMO EMPTY / RESUPPLY" : "SUPPLY CRITICAL";
        if (status.Suppression == SuppressionLevel.Suppressed) return "SUPPRESSED";
        if (status.Resupplying) return "RESUPPLY ORDER";
        if (status.IsArtillery) return BattlefieldStatusText.Fire(status, unit.HasSupply, unit.AmmunitionFraction);
        if (unit.HasOrder) return unit.Order switch
        {
            CombatOrderKind.Attack => "ATTACK",
            CombatOrderKind.AttackMove => "ATTACK MOVE",
            CombatOrderKind.HoldPosition => "HOLD",
            CombatOrderKind.Retreat => "RETREAT",
            CombatOrderKind.Stop => "STOP",
            _ => string.Empty
        };
        return unit.HasSupply ? SelectionInspectorHudModel.ResolveSupplyLabel(unit.SupplyStatus) : string.Empty;
    }
}
