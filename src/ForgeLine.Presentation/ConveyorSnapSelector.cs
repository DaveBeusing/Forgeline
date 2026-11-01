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
        ConveyorEndpoint? previous, float scale, out ConveyorEndpoint selected)
    {
        selected = default;
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
        if (retained is { } hold &&
            (best is null || hold.DistanceSquared <= best.Value.DistanceSquared +
             MathF.Pow(SwitchAdvantagePixels * factor, 2)))
        {
            selected = hold.Endpoint;
            return true;
        }
        if (best is not { } winner) return false;
        selected = winner.Endpoint;
        return true;
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
                if (!candidate.Sockets.Any(socket => socket.Input != mate.Input &&
                    Vector3.DistanceSquared(socket.Position, mate.Position + Vector3.UnitY * 5.8f) < .0004f &&
                    Vector3.Dot(socket.Facing, mate.Facing) < -.999f))
                    continue;
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
