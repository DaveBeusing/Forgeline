namespace ForgeLine.Game;

internal readonly record struct RouteChoiceConfiguration(
    int MaximumCandidates,
    ulong MinimumHoldTicks,
    double MinimumAbsoluteImprovement,
    double MinimumRelativeImprovement)
{
    internal const int CandidateCapacityLimit = 16;

    public void Validate()
    {
        if (MaximumCandidates <= 0 || MaximumCandidates > CandidateCapacityLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCandidates));
        }

        ArgumentOutOfRangeException.ThrowIfZero(MinimumHoldTicks);
        if (!double.IsFinite(MinimumAbsoluteImprovement) || MinimumAbsoluteImprovement < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumAbsoluteImprovement));
        }

        if (!double.IsFinite(MinimumRelativeImprovement) ||
            MinimumRelativeImprovement < 0.0 || MinimumRelativeImprovement >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumRelativeImprovement));
        }
    }
}

internal enum RouteChoiceEligibility : byte
{
    Unverified = 0,
    Incomplete = 1,
    GeometryBlocked = 2,
    FuelPending = 3,
    InsufficientFuel = 4,
    Eligible = 5
}

/// <summary>
/// A numeric assessment inside one already bound goal batch, not a route or
/// movement authorization. Eligible requires complete physical clearance and
/// connectors to the same authorized goal, plus the existing Game fuel gate.
/// Connection and exit costs must include the real checked segments, not an
/// unproven straight-line estimate. Alternative identities are stable producer
/// identities; input position in the batch must not become an identity.
/// </summary>
internal readonly record struct RouteChoiceCandidate(
    ulong AlternativeId,
    RouteChoiceEligibility Eligibility,
    double TravelCost,
    double CongestionCost,
    double ConnectionCost,
    double ExitCost,
    ulong CostRevision,
    ulong EvaluatedAtTick);

internal enum RouteChoiceOutcome : byte
{
    InvalidInput = 0,
    SelectAlternative = 1,
    KeepActive = 2,
    AwaitMoreWork = 3,
    AwaitFuel = 4,
    NoFeasibleAlternative = 5
}

internal enum RouteChoiceReason : byte
{
    InvalidInput = 0,
    InitialSelection = 1,
    LowestCost = 2,
    MinimumHold = 3,
    InsufficientImprovement = 4,
    CostImprovement = 5,
    HardGeometryBlock = 6,
    CandidateLimit = 7,
    IncomparableCosts = 8,
    ActiveAssessmentMissing = 9,
    IncompleteSearch = 10,
    FuelAuthorizationPending = 11,
    InsufficientFuel = 12,
    NoCompleteAlternative = 13
}

internal readonly record struct RouteChoiceDecision(
    RouteChoiceOutcome Outcome,
    ulong AlternativeId,
    RouteChoiceReason Reason);

/// <summary>
/// Stateless preference selection. The integration adapter owns the full
/// entity/order/command/dock/queue binding, knowledge scope, geometry revision
/// and persisted selection tick. It must reject a mixed or superseded batch
/// before calling this policy. Pending/invalid decisions do not discard that
/// binding, an existing route, cargo, reservations or a waiting order.
/// </summary>
internal static class RouteChoicePolicy
{
    public static RouteChoiceDecision Evaluate(
        ReadOnlySpan<RouteChoiceCandidate> candidates,
        in RouteChoiceConfiguration configuration,
        ulong activeAlternativeId,
        ulong selectedAtTick,
        ulong currentTick,
        ulong currentCostRevision,
        bool explorationComplete)
    {
        configuration.Validate();
        if ((activeAlternativeId == 0 && selectedAtTick != 0) || selectedAtTick > currentTick)
        {
            return Invalid();
        }

        // A cap cannot prove disconnection and must not silently choose from
        // an arbitrary truncated prefix of the caller's batch.
        if (candidates.Length > configuration.MaximumCandidates)
        {
            return Waiting(RouteChoiceReason.CandidateLimit);
        }

        int activeIndex = -1;
        int bestIndex = -1;
        double activeCost = 0.0;
        double bestCost = double.PositiveInfinity;
        bool hasIncomplete = false;
        bool hasPendingFuel = false;
        bool hasInsufficientFuel = false;
        for (int index = 0; index < candidates.Length; index++)
        {
            ref readonly RouteChoiceCandidate candidate = ref candidates[index];
            if (candidate.AlternativeId == 0 || !Enum.IsDefined(candidate.Eligibility) ||
                !TryGetCost(candidate, out double cost))
            {
                return Invalid();
            }

            // At most sixteen alternatives: bounded comparison avoids a
            // hash-based tie order or a per-call dictionary allocation.
            for (int previous = 0; previous < index; previous++)
            {
                if (candidates[previous].AlternativeId == candidate.AlternativeId)
                {
                    return Invalid();
                }
            }

            if (candidate.CostRevision != currentCostRevision ||
                candidate.EvaluatedAtTick != currentTick)
            {
                return Waiting(RouteChoiceReason.IncomparableCosts);
            }

            if (candidate.AlternativeId == activeAlternativeId)
            {
                activeIndex = index;
                activeCost = cost;
            }

            hasIncomplete |= candidate.Eligibility is
                RouteChoiceEligibility.Unverified or RouteChoiceEligibility.Incomplete;
            hasPendingFuel |= candidate.Eligibility == RouteChoiceEligibility.FuelPending;
            hasInsufficientFuel |= candidate.Eligibility == RouteChoiceEligibility.InsufficientFuel;
            if (candidate.Eligibility != RouteChoiceEligibility.Eligible)
            {
                continue;
            }

            if (bestIndex < 0 || cost < bestCost ||
                (cost == bestCost && candidate.AlternativeId < candidates[bestIndex].AlternativeId))
            {
                bestIndex = index;
                bestCost = cost;
            }
        }

        if (activeAlternativeId != 0 && activeIndex < 0)
        {
            return Waiting(RouteChoiceReason.ActiveAssessmentMissing);
        }

        if (!explorationComplete || hasIncomplete)
        {
            return Waiting(RouteChoiceReason.IncompleteSearch);
        }

        if (bestIndex < 0)
        {
            return NoEligibleAlternative(hasPendingFuel, hasInsufficientFuel);
        }

        ulong bestId = candidates[bestIndex].AlternativeId;
        if (activeAlternativeId == 0)
        {
            return Select(bestId, RouteChoiceReason.InitialSelection);
        }

        RouteChoiceEligibility activeEligibility = candidates[activeIndex].Eligibility;
        if (activeEligibility == RouteChoiceEligibility.GeometryBlocked)
        {
            // This bypass is only for a current hard geometry rejection;
            // congestion by itself never supplies the GeometryBlocked state.
            return Select(bestId, RouteChoiceReason.HardGeometryBlock);
        }

        if (activeEligibility == RouteChoiceEligibility.FuelPending)
        {
            return AwaitFuel(RouteChoiceReason.FuelAuthorizationPending);
        }

        if (bestId == activeAlternativeId)
        {
            return Keep(activeAlternativeId, RouteChoiceReason.LowestCost);
        }

        if (currentTick - selectedAtTick < configuration.MinimumHoldTicks)
        {
            return RetainOrWaitForFuel(activeAlternativeId, activeEligibility, RouteChoiceReason.MinimumHold);
        }

        double improvement = activeCost - bestCost;
        double requiredImprovement = Math.Max(
            configuration.MinimumAbsoluteImprovement,
            activeCost * configuration.MinimumRelativeImprovement);
        if (improvement <= 0.0 || improvement < requiredImprovement)
        {
            return RetainOrWaitForFuel(
                activeAlternativeId, activeEligibility, RouteChoiceReason.InsufficientImprovement);
        }

        return Select(bestId, RouteChoiceReason.CostImprovement);
    }

    private static bool TryGetCost(in RouteChoiceCandidate candidate, out double cost)
    {
        cost = candidate.TravelCost + candidate.CongestionCost + candidate.ConnectionCost + candidate.ExitCost;
        return double.IsFinite(candidate.TravelCost) && candidate.TravelCost >= 0.0 &&
            double.IsFinite(candidate.CongestionCost) && candidate.CongestionCost >= 0.0 &&
            double.IsFinite(candidate.ConnectionCost) && candidate.ConnectionCost >= 0.0 &&
            double.IsFinite(candidate.ExitCost) && candidate.ExitCost >= 0.0 && double.IsFinite(cost);
    }

    private static RouteChoiceDecision NoEligibleAlternative(
        bool hasPendingFuel,
        bool hasInsufficientFuel)
    {
        if (hasPendingFuel)
        {
            return AwaitFuel(RouteChoiceReason.FuelAuthorizationPending);
        }

        if (hasInsufficientFuel)
        {
            return AwaitFuel(RouteChoiceReason.InsufficientFuel);
        }

        // This only describes the supplied batch. The search kernel alone
        // decides whether all admissible endpoints were actually exhausted.
        return new(RouteChoiceOutcome.NoFeasibleAlternative, 0, RouteChoiceReason.NoCompleteAlternative);
    }

    private static RouteChoiceDecision RetainOrWaitForFuel(
        ulong activeAlternativeId,
        RouteChoiceEligibility activeEligibility,
        RouteChoiceReason reason) =>
        activeEligibility == RouteChoiceEligibility.Eligible
            ? Keep(activeAlternativeId, reason)
            : AwaitFuel(RouteChoiceReason.InsufficientFuel);

    private static RouteChoiceDecision Invalid() =>
        new(RouteChoiceOutcome.InvalidInput, 0, RouteChoiceReason.InvalidInput);

    private static RouteChoiceDecision Waiting(RouteChoiceReason reason) =>
        new(RouteChoiceOutcome.AwaitMoreWork, 0, reason);

    private static RouteChoiceDecision AwaitFuel(RouteChoiceReason reason) =>
        new(RouteChoiceOutcome.AwaitFuel, 0, reason);

    private static RouteChoiceDecision Keep(ulong alternativeId, RouteChoiceReason reason) =>
        new(RouteChoiceOutcome.KeepActive, alternativeId, reason);

    private static RouteChoiceDecision Select(ulong alternativeId, RouteChoiceReason reason) =>
        new(RouteChoiceOutcome.SelectAlternative, alternativeId, reason);
}
