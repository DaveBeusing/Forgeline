using ForgeLine.Core;

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
