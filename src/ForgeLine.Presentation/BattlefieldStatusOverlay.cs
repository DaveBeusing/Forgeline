using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal readonly record struct BattlefieldStatusMarker(Vector2 Position, HudRect Bounds, string Label, string Symbol, bool Critical);

// Render-owner scratch. Rebuilt from the same completed tick, never published or retained as history.
internal sealed class BattlefieldStatusOverlay
{
    internal const int MaximumMarkers = 64;
    private readonly int[] _next = new int[4096];
    private readonly HashSet<ForgeLine.Core.EntityId> _selected = new(4096);
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
        if (!showSupply && (view.SelectedEntities is null || view.SelectedEntities.Length == 0)) return;
        _selected.Clear();
        if (view.SelectedEntities is { } selectedEntities)
            for (int i = 0; i < Math.Min(selectedEntities.Length, 4096); i++) _selected.Add(selectedEntities[i]);
        Span<int> heads = stackalloc int[6], tails = stackalloc int[6];
        heads.Fill(-1); tails.Fill(-1);
        int inspected = Math.Min(snapshot.Instances.Length, 4096);
        // Stable priority buckets inspect membership once; no selection-by-unit Cartesian scan.
        for (int i = 0; i < inspected; i++)
        {
            ref readonly var instance = ref snapshot.Instances[i];
            if (instance.Selectable.Owner != player.Player || (instance.Visibility & RenderVisibilityMask.World) == 0 ||
                !units.TryGet(instance.Entity, out var unit)) continue;
            bool selected = _selected.Contains(instance.Entity);
            if (!selected && !showSupply) continue;
            int priority = Priority(unit, selected);
            if (priority < 0) continue;
            _next[i] = -1;
            if (tails[priority] < 0) heads[priority] = i;
            else _next[tails[priority]] = i;
            tails[priority] = i;
        }
        var matrix = camera.GetMatrices(width, height).ViewProjection;
        for (int priority = 0; priority < 6 && Count < limit; priority++)
        for (int i = heads[priority]; i >= 0 && Count < limit; i = _next[i])
        {
            ref readonly var instance = ref snapshot.Instances[i];
            if (!units.TryGet(instance.Entity, out var unit)) continue;
            var world = instance.Transform.Position + Vector3.UnitY *
                (PresentationBounds.ResolveLocalHalfExtents(instance).Y + 2);
            var projected = Vector4.Transform(new Vector4(world, 1), matrix);
            if (!float.IsFinite(projected.W) || projected.W <= 0) continue;
            var ndc = new Vector3(projected.X, projected.Y, projected.Z) / projected.W;
            if (!(ndc.X >= -1 && ndc.X <= 1 && ndc.Y >= -1 && ndc.Y <= 1 && ndc.Z >= 0 && ndc.Z <= 1)) continue;
            var position = new Vector2((ndc.X + 1) * .5f * width, (1 - ndc.Y) * .5f * height);
            float size = (strategic ? 7 : 9) * scale;
            var bounds = new HudRect(position.X - size, position.Y - size, size * 2, size * 2);
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
            _markers[Count++] = new(position, occupied, label, symbol, critical);
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
