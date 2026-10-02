using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

public static class RetreatRecoveryPlanner
{
    public static bool TryResolve(
        EntityRegistry entities,
        PlayerId owner,
        IReadOnlyList<EntityId> units,
        out EntityId provider,
        out Vector3 destination,
        out RetreatRecoveryReason reason)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(units);

        provider = EntityId.Invalid;
        destination = default;
        reason = RetreatRecoveryReason.NoProvider;

        if (!owner.IsSpecified ||
            units.Count == 0)
        {
            return false;
        }

        Vector3 centroid =
            ResolveCentroid(
                entities,
                units,
                out int positioned);

        if (positioned == 0)
        {
            return false;
        }

        ResolveNeeds(
            entities,
            units,
            out bool needsRepair,
            out bool needsSupply);

        float bestDistanceSquared =
            float.PositiveInfinity;
        float bestRange = 0.0f;
        RetreatRecoveryReason bestReason =
            RetreatRecoveryReason.NoProvider;

        foreach (EntityId candidate in
                 entities.Query<WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            bool hasRepair =
                entities.TryGetComponent(
                    candidate,
                    out RepairProvider repair) &&
                repair.Owner == owner;
            bool hasSupply =
                TryResolveSupplyRange(
                    entities,
                    candidate,
                    owner,
                    out float supplyRange);

            if (!hasRepair &&
                !hasSupply)
            {
                continue;
            }

            if (needsRepair &&
                !hasRepair)
            {
                continue;
            }

            if (needsSupply &&
                !hasSupply)
            {
                continue;
            }

            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    candidate);
            float distanceSquared =
                HorizontalDistanceSquared(
                    centroid,
                    transform.Position);

            if (distanceSquared > bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 provider.IsValid &&
                 candidate >= provider))
            {
                continue;
            }

            provider = candidate;
            bestDistanceSquared = distanceSquared;
            bestReason =
                hasRepair && hasSupply
                    ? RetreatRecoveryReason.RepairAndSupply
                    : hasRepair
                        ? RetreatRecoveryReason.Repair
                        : RetreatRecoveryReason.Supply;

            float repairRange =
                hasRepair
                    ? repair.RepairRangeMeters
                    : float.PositiveInfinity;
            float resolvedSupplyRange =
                hasSupply
                    ? supplyRange
                    : float.PositiveInfinity;
            bestRange =
                MathF.Min(
                    repairRange,
                    resolvedSupplyRange);

            if (!float.IsFinite(bestRange))
            {
                bestRange =
                    MathF.Max(
                        hasRepair
                            ? repair.RepairRangeMeters
                            : 0.0f,
                        hasSupply
                            ? supplyRange
                            : 0.0f);
            }
        }

        if (!provider.IsValid)
        {
            return false;
        }

        WorldTransform providerTransform =
            entities.GetComponent<WorldTransform>(
                provider);
        destination =
            ResolveApproachPoint(
                centroid,
                providerTransform.Position,
                bestRange);
        reason = bestReason;
        return true;
    }

    private static void ResolveNeeds(
        EntityRegistry entities,
        IReadOnlyList<EntityId> units,
        out bool needsRepair,
        out bool needsSupply)
    {
        needsRepair = false;
        needsSupply = false;

        for (int index = 0;
             index < units.Count;
             index++)
        {
            EntityId unit =
                units[index];

            if (entities.TryGetComponent(
                    unit,
                    out HealthState health) &&
                !health.IsDepleted &&
                health.Current < health.Maximum)
            {
                needsRepair = true;
            }

            if (entities.TryGetComponent(
                    unit,
                    out UnitSupplyState supply) &&
                supply.Status != BattlefieldSupplyStatus.Supplied)
            {
                needsSupply = true;
            }

            if (needsRepair &&
                needsSupply)
            {
                return;
            }
        }
    }

    private static Vector3 ResolveCentroid(
        EntityRegistry entities,
        IReadOnlyList<EntityId> units,
        out int positioned)
    {
        Vector3 sum = Vector3.Zero;
        positioned = 0;

        for (int index = 0;
             index < units.Count;
             index++)
        {
            EntityId unit =
                units[index];

            if (!entities.TryGetComponent(
                    unit,
                    out WorldTransform transform))
            {
                continue;
            }

            sum += transform.Position;
            positioned++;
        }

        return positioned == 0
            ? Vector3.Zero
            : sum / positioned;
    }

    private static bool TryResolveSupplyRange(
        EntityRegistry entities,
        EntityId candidate,
        PlayerId owner,
        out float range)
    {
        if (entities.TryGetComponent(
                candidate,
                out SupplyProvider provider) &&
            provider.Owner == owner &&
            provider.Enabled)
        {
            range = provider.ResupplyRangeMeters;
            return true;
        }

        if (entities.TryGetComponent(
                candidate,
                out SupplyTruck truck) &&
            truck.Owner == owner)
        {
            range = truck.ResupplyRangeMeters;
            return true;
        }

        range = 0.0f;
        return false;
    }

    private static Vector3 ResolveApproachPoint(
        Vector3 centroid,
        Vector3 provider,
        float supportRange)
    {
        Vector3 direction =
            centroid -
            provider;
        direction.Y = 0.0f;

        if (direction.LengthSquared() <= 0.0001f)
        {
            direction = Vector3.UnitZ;
        }
        else
        {
            direction = Vector3.Normalize(
                direction);
        }

        float approachDistance =
            Math.Clamp(
                supportRange * 0.65f,
                4.0f,
                MathF.Max(
                    4.0f,
                    supportRange - 1.0f));

        return provider +
            direction *
            approachDistance;
    }

    private static float HorizontalDistanceSquared(
        Vector3 left,
        Vector3 right)
    {
        float x =
            right.X -
            left.X;
        float z =
            right.Z -
            left.Z;
        return x * x +
            z * z;
    }
}
