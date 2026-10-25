using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal readonly record struct NavigationPassageProbe(Vector3 Position, Vector3 SteeringTarget, float LookaheadMeters);
internal sealed record NavigationPassageCapture(IReadOnlyList<NavigationTrafficPassage> Passages, int WorkConsumed, bool Complete);

/// <summary>Bounded one-cell lane discovery on the pinned grid; physical clearance remains a separate veto.</summary>
internal static class NavigationPassageDiscovery
{
    private const int MaximumProbeSteps = 32, MaximumChainCells = 512, MaximumPassages = 128;

    internal static NavigationPassageCapture Capture(NavigationWorldSnapshot snapshot,
        ReadOnlySpan<NavigationPassageProbe> probes, int workAllowance)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(workAllowance);
        if (probes.Length > 2048) throw new ArgumentException("Passage probe bound exceeded.", nameof(probes));
        var grid = snapshot.Grid;
        var passages = new SortedDictionary<ulong, NavigationTrafficPassage>();
        var captured = new Dictionary<(int Cell, int Axis), ulong>();
        int consumed = 0;
        bool exhausted = false;
        foreach (var probe in probes)
        {
            if (!Finite(probe.Position) || !Finite(probe.SteeringTarget) ||
                !float.IsFinite(probe.LookaheadMeters) || probe.LookaheadMeters <= 0) return Finish(false);
            Vector3 delta = probe.SteeringTarget - probe.Position;
            delta.Y = 0;
            float distance = MathF.Min(delta.Length(), probe.LookaheadMeters);
            if (!float.IsFinite(distance)) return Finish(false);
            double requiredSteps = Math.Ceiling((double)distance / (grid.Settings.CellSizeMeters * 0.5));
            if (requiredSteps > MaximumProbeSteps) return Finish(false);
            int steps = Math.Max(1, (int)requiredSteps);
            Vector3 direction = delta.LengthSquared() > 0 ? Vector3.Normalize(delta) : Vector3.Zero;
            for (int sample = 0; sample <= steps; sample++)
            {
                if (!Spend(1)) return Finish(false);
                Vector3 position = probe.Position + direction * (distance * sample / steps);
                if (!grid.TryWorldToCell(position, out var cell)) return Finish(false);
                for (int axis = 0; axis < 2; axis++)
                {
                    if (captured.ContainsKey((grid.GetIndex(cell), axis))) continue;
                    if (!Narrow(cell, axis)) { if (exhausted) return Finish(false); continue; }
                    // An isolated cell has no two different lane owners.
                    if (axis == 1 && Narrow(cell, 0)) continue;
                    if (exhausted) return Finish(false);
                    int first = axis == 0 ? cell.Z : cell.X, last = first, count = 1;
                    while (true)
                    {
                        var previous = axis == 0 ? new NavigationCellCoordinate(cell.X, first - 1) : new(first - 1, cell.Z);
                        if (!Narrow(previous, axis)) break;
                        if (count == MaximumChainCells) return Finish(false);
                        count++; first--;
                    }
                    if (exhausted) return Finish(false);
                    while (true)
                    {
                        var next = axis == 0 ? new NavigationCellCoordinate(cell.X, last + 1) : new(last + 1, cell.Z);
                        if (!Narrow(next, axis)) break;
                        if (count == MaximumChainCells) return Finish(false);
                        count++; last++;
                    }
                    if (exhausted) return Finish(false);
                    var firstCell = axis == 0 ? new NavigationCellCoordinate(cell.X, first) : new(first, cell.Z);
                    var lastCell = axis == 0 ? new NavigationCellCoordinate(cell.X, last) : new(last, cell.Z);
                    ulong id = (axis == 0 ? 0x4000000000000000UL : 0x8000000000000000UL) | ((ulong)grid.GetIndex(firstCell) + 1);
                    Vector3 center = (grid.GetCellCenter(firstCell) + grid.GetCellCenter(lastCell)) * 0.5f;
                    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, axis == 0 ? 0 : MathF.PI / 2);
                    passages[id] = new(id, new NavigationPose(center, rotation),
                        new Vector3(grid.Settings.CellSizeMeters * 0.5f, 1, count * grid.Settings.CellSizeMeters * 0.5f));
                    if (passages.Count > MaximumPassages) return Finish(false);
                    for (int coordinate = first; coordinate <= last; coordinate++)
                    {
                        if (!Spend(1)) return Finish(false);
                        var known = axis == 0 ? new NavigationCellCoordinate(cell.X, coordinate) : new(coordinate, cell.Z);
                        captured[(grid.GetIndex(known), axis)] = id;
                    }
                }
            }
        }
        return Finish(true);

        bool Narrow(NavigationCellCoordinate cell, int axis)
        {
            if (!Spend(3)) return false;
            var a = axis == 0 ? new NavigationCellCoordinate(cell.X - 1, cell.Z) : new(cell.X, cell.Z - 1);
            var b = axis == 0 ? new NavigationCellCoordinate(cell.X + 1, cell.Z) : new(cell.X, cell.Z + 1);
            return grid.IsTraversable(cell, snapshot.Capabilities) &&
                !grid.IsTraversable(a, snapshot.Capabilities) && !grid.IsTraversable(b, snapshot.Capabilities);
        }

        bool Spend(int amount)
        {
            if (amount > workAllowance - consumed) { consumed = workAllowance; exhausted = true; return false; }
            consumed += amount;
            return true;
        }

        NavigationPassageCapture Finish(bool complete) => new(Array.AsReadOnly(passages.Values.ToArray()), consumed, complete);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
