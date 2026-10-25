using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    private sealed record CrossingPlan(IReadOnlyList<NavigationTrafficPassage> Passages, int Work, bool Complete);

    private CrossingPlan DiscoverCrossings(NavigationTrafficFrame frame, NavigationTrafficIntent[] intents,
        NavigationPose[][] paths, NavigationVehicleProfile[] profiles, int allowance)
    {
        int consumed = 0;
        var passages = new SortedDictionary<ulong, NavigationTrafficPassage>();
        foreach (var passage in frame.Passages) passages.Add(passage.Id, passage);
        foreach (var lease in _passageLeases.Values) passages[lease.Passage.Id] = lease.Passage;
        for (int first = 0; first < intents.Length; first++)
        {
            if (!intents[first].CanMove) continue;
            for (int second = first + 1; second < intents.Length; second++)
            {
                if (!intents[second].CanMove) continue;
                for (int step = 1; step <= PredictionSteps; step++)
                {
                    if (consumed == allowance) return new(passages.Values.ToArray(), consumed, false);
                    consumed++;
                    Vector3 a0 = paths[first][step - 1].Position, a1 = paths[first][step].Position;
                    Vector3 b0 = paths[second][step - 1].Position, b1 = paths[second][step].Position;
                    if (!Conflicts(a0, a1, profiles[first], b0, b1, profiles[second])) continue;
                    Vector2 relative = new(a0.X - b0.X, a0.Z - b0.Z);
                    Vector2 velocity = new(a1.X - a0.X - b1.X + b0.X, a1.Z - a0.Z - b1.Z + b0.Z);
                    float fraction = velocity.LengthSquared() > 0 ?
                        Math.Clamp(-Vector2.Dot(relative, velocity) / velocity.LengthSquared(), 0, 1) : 0;
                    Vector3 center = (Vector3.Lerp(a0, a1, fraction) + Vector3.Lerp(b0, b1, fraction)) * 0.5f;
                    var grid = frame.Collision.World.Grid;
                    if (!grid.TryWorldToCell(center, out var cell)) return new(passages.Values.ToArray(), consumed, false);
                    ulong id = 0x2000000000000000UL | ((ulong)grid.GetIndex(cell) + 1);
                    Vector3 pinned = grid.GetCellCenter(cell);
                    float half = MathF.Max(grid.Settings.CellSizeMeters,
                        MathF.Max(profiles[first].HalfExtents.Length(), profiles[second].HalfExtents.Length()));
                    float halfX = MathF.Min(half, MathF.Min(pinned.X - grid.Origin.X,
                        grid.Origin.X + grid.Width * grid.Settings.CellSizeMeters - pinned.X));
                    float halfZ = MathF.Min(half, MathF.Min(pinned.Z - grid.Origin.Y,
                        grid.Origin.Y + grid.Height * grid.Settings.CellSizeMeters - pinned.Z));
                    if (halfX <= 0 || halfZ <= 0) return new(passages.Values.ToArray(), consumed, false);
                    bool covered = false;
                    foreach (var existing in passages.Values)
                    {
                        if (consumed == allowance) return new(passages.Values.ToArray(), consumed, false);
                        consumed++;
                        Vector3 extent = Vector3.Abs(Vector3.Transform(Vector3.UnitX, existing.Pose.Rotation)) * existing.HalfExtents.X +
                            Vector3.Abs(Vector3.Transform(Vector3.UnitY, existing.Pose.Rotation)) * existing.HalfExtents.Y +
                            Vector3.Abs(Vector3.Transform(Vector3.UnitZ, existing.Pose.Rotation)) * existing.HalfExtents.Z;
                        // Overlapping admission envelopes must not acquire opposing owners. The
                        // existing passage remains authoritative until its actual rear-clear release.
                        if (MathF.Abs(pinned.X - existing.Pose.Position.X) <= extent.X + halfX &&
                            MathF.Abs(pinned.Z - existing.Pose.Position.Z) <= extent.Z + halfZ)
                        { covered = true; break; }
                    }
                    if (!covered)
                    {
                        passages[id] = new(id, new NavigationPose(pinned, Quaternion.Identity), new Vector3(halfX, 1, halfZ));
                        if (passages.Count > MaximumTrackedPassages) return new(passages.Values.ToArray(), consumed, false);
                    }
                    break;
                }
            }
        }
        return new(passages.Values.ToArray(), consumed, true);
    }
}
