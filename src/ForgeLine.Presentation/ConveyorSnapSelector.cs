using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>Presentation-only ranking of currently authorized, exact socket candidates.
/// Never constructs or changes a simulation connection.</summary>
internal static class ConveyorSnapSelector
{
    internal const float AcquirePixels = 28;
    internal const float HoldExtraPixels = 6;
    internal const float SwitchAdvantagePixels = 5;

    internal readonly record struct Candidate(
        ConveyorEndpoint Endpoint, EntityId Identity, string SocketKey,
        int SocketOrdinal, float DistanceSquared, bool Conveyor);

    internal static bool TrySelect(IReadOnlyList<Candidate> candidates,
        ConveyorEndpoint? previous, float scale, out ConveyorEndpoint selected) =>
        TrySelect(candidates, previous, scale, out selected, out _);

    internal static bool TrySelect(IReadOnlyList<Candidate> candidates,
        ConveyorEndpoint? previous, float scale, out ConveyorEndpoint selected,
        out bool ambiguous)
    {
        selected = default;
        ambiguous = false;
        float factor = float.IsFinite(scale) ? Math.Clamp(scale, .5f, 4f) : 1f;
        float acquireSquared = MathF.Pow(AcquirePixels * factor, 2);
        float holdSquared = MathF.Pow((AcquirePixels + HoldExtraPixels) * factor, 2);
        Candidate? best = null, retained = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            if (!float.IsFinite(candidate.DistanceSquared)) continue;
            if (candidate.DistanceSquared <= acquireSquared &&
                (best is null || Compare(candidate, best.Value) < 0)) best = candidate;
            if (previous is { } old && candidate.DistanceSquared <= holdSquared &&
                SameSocket(candidate.Endpoint, old)) retained = candidate;
        }
        Candidate? chosen = retained is { } hold &&
            (best is null || MathF.Sqrt(hold.DistanceSquared) <=
             MathF.Sqrt(best.Value.DistanceSquared) + SwitchAdvantagePixels * factor) ? hold : best;
        if (chosen is not { } winner) return false;

        // Overlapping live endpoints are not two independent valid choices.
        // The authoritative graph rejects ambiguous attachment; so must its picker.
        foreach (Candidate candidate in candidates)
            if (candidate.DistanceSquared <= holdSquared &&
                candidate.Endpoint.Input == winner.Endpoint.Input &&
                !SameSocket(candidate.Endpoint, winner.Endpoint) &&
                Vector3.DistanceSquared(candidate.Endpoint.Position, winner.Endpoint.Position) < .0004f)
            {
                ambiguous = true;
                return false;
            }
        selected = winner.Endpoint;
        return true;
    }

    /// <summary>Give free placement a small deadband around the authored 4m grid.
    /// Socket snaps bypass this function and never lose their exact coordinates.</summary>
    internal static Vector3 SnapGrid(Vector3 pointer, Vector3? previous)
    {
        Vector3 snapped = ConveyorGeometry.Snap(pointer);
        if (previous is not { } old || !float.IsFinite(old.X) || !float.IsFinite(old.Z))
            return snapped;
        float hold = ConstructionGrid.CellSizeMeters * .5f + .35f;
        if (MathF.Abs(pointer.X - old.X) <= hold &&
            MathF.Abs(pointer.Z - old.Z) <= hold)
            return new Vector3(old.X, pointer.Y, old.Z);
        return snapped;
    }

    /// <summary>Find a canonical orientation and pivot that exactly mates a single
    /// authored socket, including ramp tier transitions. The construction service
    /// still validates terrain, support clearance, ownership and material payment.</summary>
    internal static bool TryAlign(ConveyorModule module, in ConveyorEndpoint mate,
        out ConveyorPlacement placement)
    {
        placement = default;
        ConveyorSocket[] sockets = ConveyorGeometry.Sockets(module);
        for (int slot = 0; slot < sockets.Length; slot++)
        {
            ConveyorSocket local = sockets[slot];
            if (local.Input == mate.Input) continue;
            for (int facing = 0; facing < 4; facing++)
            {
                var orientation = (BuildingOrientation)facing;
                Quaternion rotation = BuildingFootprint.GetRotation(orientation);
                if (Vector3.Dot(Vector3.Transform(local.Facing, rotation), mate.Facing) >= -.999f)
                    continue;
                int rise = (int)MathF.Round((local.Position.Y - 5.8f) / 2);
                int tier = mate.Tier - rise;
                if (tier is < 0 or > 1) continue;
                Vector3 pivot = mate.Position + Vector3.UnitY * 5.8f -
                    Vector3.Transform(local.Position, rotation);
                var candidate = new ConveyorPlacement(module, pivot, orientation, tier);
                bool compatible = false;
                foreach (ConveyorSocket socket in candidate.Sockets)
                    if (socket.Input != mate.Input &&
                        Vector3.DistanceSquared(socket.Position, mate.Position + Vector3.UnitY * 5.8f) < .0004f &&
                        Vector3.Dot(socket.Facing, mate.Facing) < -.999f)
                    {
                        compatible = true;
                        break;
                    }
                if (!compatible) continue;
                placement = candidate;
                return true;
            }
        }
        return false;
    }

    // Stable source identities, authored port keys, and physical socket ordinals
    // break exact-distance ties; never depend on snapshot enumeration order.
    private static int Compare(in Candidate a, in Candidate b)
    {
        int distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
        if (distance != 0) return distance;
        if (a.Conveyor != b.Conveyor) return a.Conveyor ? -1 : 1;
        int identity = a.Identity.CompareTo(b.Identity);
        if (identity != 0) return identity;
        int key = string.CompareOrdinal(a.SocketKey, b.SocketKey);
        return key != 0 ? key : a.SocketOrdinal.CompareTo(b.SocketOrdinal);
    }

    private static bool SameSocket(in ConveyorEndpoint a, in ConveyorEndpoint b) =>
        a.Source == b.Source && a.SocketKey == b.SocketKey &&
        a.SocketOrdinal == b.SocketOrdinal && a.Input == b.Input &&
        a.Building == b.Building;
}
