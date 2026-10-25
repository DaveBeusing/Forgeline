namespace ForgeLine.Game;

/// <summary>Explicit, unmeasured bounds for the stateless corridor follower.</summary>
public sealed record NavigationMotionPlannerOptions(
    int MaximumRouteWaypoints,
    int MaximumLookaheadWaypoints,
    float MaximumLookaheadMeters,
    float PlanningHorizonSeconds,
    int MaximumGroundedRefinements)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumRouteWaypoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumLookaheadWaypoints);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumGroundedRefinements);
        if (MaximumLookaheadWaypoints > MaximumRouteWaypoints ||
            !float.IsFinite(MaximumLookaheadMeters) || MaximumLookaheadMeters <= 0 ||
            !float.IsFinite(PlanningHorizonSeconds) || PlanningHorizonSeconds <= 0)
            throw new InvalidOperationException("Motion bounds must be finite, positive and internally consistent.");
    }
}
