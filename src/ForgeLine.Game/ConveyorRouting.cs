using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum ConveyorRoutingPreference : byte { Balanced, OutputA, OutputB }
public enum ConveyorRoutingFailure : byte
{
    None, MatchUnavailable, InvalidTarget, ForeignOwnership, UnsupportedTarget,
    Destroyed, InvalidPreference, TechnologyRequired, StaleRevision, RevisionExhausted
}

/// <summary>One policy per full splitter identity; cursor advances only on real transfers.</summary>
public readonly record struct ConveyorRoutingState(ConveyorRoutingPreference Preference,
    uint Revision, int ServiceCursor = 0);

public static class ConveyorRoutingPolicy
{
    public static ConveyorRoutingFailure Evaluate(EntityRegistry entities, PlayerId owner, EntityId target,
        ConveyorRoutingPreference preference, uint expectedRevision)
    {
        bool running = false;
        foreach (EntityId match in entities.Query<MatchState>())
            running |= entities.GetComponent<MatchState>(match).Lifecycle == MatchLifecyclePhase.Running;
        if (!running) return ConveyorRoutingFailure.MatchUnavailable;
        if (!Enum.IsDefined(preference)) return ConveyorRoutingFailure.InvalidPreference;
        if (!entities.TryGetComponent(target, out ConveyorPiece piece)) return ConveyorRoutingFailure.InvalidTarget;
        if (piece.Owner != owner || !owner.IsSpecified) return ConveyorRoutingFailure.ForeignOwnership;
        if (piece.Placement.Module != ConveyorModule.Splitter ||
            !entities.HasComponent<ConveyorFlowState>(target)) return ConveyorRoutingFailure.UnsupportedTarget;
        if (!entities.TryGetComponent(target, out HealthState health) || health.IsDepleted)
            return ConveyorRoutingFailure.Destroyed;
        var state = entities.TryGetComponent(target, out ConveyorRoutingState current) ? current : default;
        if (state.Revision != expectedRevision) return ConveyorRoutingFailure.StaleRevision;
        if (state.Revision == uint.MaxValue) return ConveyorRoutingFailure.RevisionExhausted;
        if (preference != ConveyorRoutingPreference.Balanced &&
            !TechnologyStateQueries.IsCapabilityUnlocked(entities, owner, TechnologyCapabilityIds.LogisticsCoordination))
            return ConveyorRoutingFailure.TechnologyRequired;
        return ConveyorRoutingFailure.None;
    }

    public static string Describe(ConveyorRoutingFailure failure) => failure switch
    {
        ConveyorRoutingFailure.None => "AVAILABLE",
        ConveyorRoutingFailure.TechnologyRequired => "LOGISTICS COORDINATION REQUIRED",
        ConveyorRoutingFailure.StaleRevision => "ROUTING CHANGED",
        ConveyorRoutingFailure.RevisionExhausted => "ROUTING REVISION EXHAUSTED",
        ConveyorRoutingFailure.MatchUnavailable => "MATCH UNAVAILABLE",
        ConveyorRoutingFailure.Destroyed => "SPLITTER DESTROYED",
        ConveyorRoutingFailure.ForeignOwnership => "FOREIGN OWNER",
        ConveyorRoutingFailure.InvalidPreference => "INVALID ROUTING",
        ConveyorRoutingFailure.UnsupportedTarget => "SELECT SPLITTER",
        _ => "INVALID SPLITTER"
    };
}

public sealed class ConveyorRoutingCommand(PlayerId issuer, EntityId target,
    ConveyorRoutingPreference preference, uint expectedRevision, SimulationTick submittedAtTick) : ISimulationCommand
{
    public PlayerId Issuer { get; } = issuer;
    public EntityId Target { get; } = target;
    public ConveyorRoutingPreference Preference { get; } = preference;
    public uint ExpectedRevision { get; } = expectedRevision;
    public SimulationTick SubmittedAtTick { get; } = submittedAtTick;
    public SimulationTick ExecutedAtTick { get; private set; }
    public ConveyorRoutingFailure Result { get; private set; }
    public bool Accepted => ExecutedAtTick != SimulationTick.Zero && Result == ConveyorRoutingFailure.None;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExecutedAtTick = context.Tick;
        Result = ConveyorRoutingPolicy.Evaluate(context.Entities, Issuer, Target, Preference, ExpectedRevision);
        if (Result != ConveyorRoutingFailure.None) return;
        var state = new ConveyorRoutingState(Preference, checked(ExpectedRevision + 1));
        if (context.Entities.HasComponent<ConveyorRoutingState>(Target)) context.Entities.SetComponent(Target, state);
        else context.Entities.AddComponent(Target, state);
    }
}
