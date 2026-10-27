using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>A shared cause is backed by a real network deficit or the identical observed cause object.</summary>
public sealed class OperationsCauseGroup
{
    public OperationsCauseGroup(OperationsDiagnosisReason reason, EntityId cause, PowerNetworkId network,
        ResourceId resource, IReadOnlyList<EntityId> members)
    {
        Reason = reason; Cause = cause; Network = network; Resource = resource;
        Members = Array.AsReadOnly(members.Take(256).ToArray());
    }
    public OperationsDiagnosisReason Reason { get; }
    public EntityId Cause { get; }
    public PowerNetworkId Network { get; }
    public ResourceId Resource { get; }
    public IReadOnlyList<EntityId> Members { get; }
}

public sealed class OperationsCauseSnapshot
{
    public const int MaximumGroups = 64;
    public const int MaximumMembers = 256;
    public const int MaximumScannedInstances = 4096;
    public OperationsCauseSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IReadOnlyList<OperationsCauseGroup> groups, bool limited)
    {
        Session = session; Tick = tick; Player = player;
        Groups = Array.AsReadOnly(groups.Take(MaximumGroups).ToArray()); Limited = limited;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<OperationsCauseGroup> Groups { get; }
    public bool Limited { get; }
    public static bool SameMembership(OperationsCauseSnapshot? first, OperationsCauseSnapshot? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null || first.Session != second.Session || first.Player != second.Player ||
            first.Limited != second.Limited || first.Groups.Count != second.Groups.Count) return false;
        for (int i = 0; i < first.Groups.Count; i++)
        {
            var a = first.Groups[i]; var b = second.Groups[i];
            if (a.Reason != b.Reason || a.Cause != b.Cause || a.Network != b.Network ||
                a.Resource != b.Resource || a.Members.Count != b.Members.Count) return false;
            for (int m = 0; m < a.Members.Count; m++) if (a.Members[m] != b.Members[m]) return false;
        }
        return true;
    }
    public EntityId NextRelatedMember(EntityId selected, EntityId previous = default)
    {
        for (int index = 0; index < Groups.Count; index++)
        {
            var group = Groups[index];
            bool contains = false;
            for (int m = 0; m < group.Members.Count; m++)
                if (group.Members[m] == selected) { contains = true; break; }
            if (!contains) continue;
            EntityId after = previous.IsValid ? previous : selected;
            for (int i = 0; i < group.Members.Count; i++)
                if (group.Members[i] == after) return group.Members[(i + 1) % group.Members.Count];
            return group.Members[0];
        }
        return default;
    }
    public EntityId NextPowerMember(EntityId previous)
    {
        EntityId first = default;
        bool next = !previous.IsValid;
        foreach (var group in Groups)
        {
            if (group.Reason != OperationsDiagnosisReason.NoPower) continue;
            foreach (var member in group.Members)
            {
                if (!first.IsValid) first = member;
                if (next) return member;
                if (member == previous) next = true;
            }
        }
        return first;
    }
    public static OperationsCauseSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        snapshot is { PlayerExperience: { IsMatchComplete: false } experience, OperationsCauses: { } causes } &&
        snapshot.SessionId.IsSpecified && causes.Session == snapshot.SessionId && causes.Tick == snapshot.Tick &&
        experience.Tick == snapshot.Tick && experience.Player == causes.Player ? causes : null;
}

internal static class OperationsCauseCapture
{
    internal static OperationsCauseSnapshot Capture(SimulationContext context, PresentationExtractionContext extraction,
        RenderInstance[] instances)
    {
        var scenario = extraction.Scenario;
        var visible = new HashSet<EntityId>();
        bool limited = instances.Length > OperationsCauseSnapshot.MaximumScannedInstances;
        int scanned = Math.Min(instances.Length, OperationsCauseSnapshot.MaximumScannedInstances);
        for (int i = 0; i < scanned; i++)
            if (OperationsWorldOverlay.IsOwnedVisible(instances[i], extraction.Player) && Authorized(instances[i].Entity))
                visible.Add(instances[i].Entity);
        var groups = new Dictionary<(OperationsDiagnosisReason Reason, EntityId Cause, PowerNetworkId Network, ResourceId Resource), List<EntityId>>();
        var deficientNetworks = scenario.Power.Networks.Take(OperationsCauseSnapshot.MaximumScannedInstances)
            .Where(x => x.Deficit > 0).Select(x => x.NetworkId).ToHashSet();
        limited |= scenario.Power.Networks.Count > OperationsCauseSnapshot.MaximumScannedInstances;
        int members = 0;
        // Stable full-entity order makes bounded admission independent of draw ordering inside the observed slice.
        foreach (var entity in visible.Order())
        {
            if (context.Entities.TryGetComponent(entity, out PowerConsumer power) && power.Enabled &&
                power.State != PowerOperationalState.Powered &&
                context.Entities.TryGetComponent(entity, out PowerNetworkMembership membership) &&
                deficientNetworks.Contains(membership.NetworkId))
                Add((OperationsDiagnosisReason.NoPower, default, membership.NetworkId, default), entity);
            if (scenario.Services.ConveyorFlow.ObservationTick != context.Tick ||
                !scenario.Services.ConveyorFlow.TryGetObservation(entity, out var flow)) continue;
            var fact = flow.First;
            var reason = OperationsChainCapture.ConveyorReason(fact.Reason);
            if (!fact.Cause.IsValid || !visible.Contains(fact.Cause) ||
                reason is OperationsDiagnosisReason.Unknown or OperationsDiagnosisReason.None or OperationsDiagnosisReason.NoPower) continue;
            Add((reason, fact.Cause, default, fact.Resource), entity);
        }
        var copies = groups.Where(x => x.Value.Count > 1)
            .Select(x => new OperationsCauseGroup(x.Key.Reason, x.Key.Cause, x.Key.Network, x.Key.Resource, x.Value)).ToArray();
        return new(scenario.Simulation.SessionId, context.Tick, extraction.Player, copies, limited);

        bool Authorized(EntityId entity) => context.Entities.TryGetComponent(entity, out ControllableEntity owner) &&
            owner.Owner == extraction.Player && owner.IsControllable &&
            (!context.Entities.TryGetComponent(entity, out CompletedBuilding building) || building.Owner == extraction.Player) &&
            (!context.Entities.TryGetComponent(entity, out ConveyorPiece conveyor) || conveyor.Owner == extraction.Player) &&
            (!context.Entities.TryGetComponent(entity, out UnitProductionFacility factory) || factory.Owner == extraction.Player);
        void Add((OperationsDiagnosisReason Reason, EntityId Cause, PowerNetworkId Network, ResourceId Resource) key, EntityId entity)
        {
            if (!groups.TryGetValue(key, out var affected))
            {
                if (groups.Count >= OperationsCauseSnapshot.MaximumGroups) { limited = true; return; }
                groups.Add(key, affected = []);
            }
            if (affected.Contains(entity)) return;
            if (members >= OperationsCauseSnapshot.MaximumMembers) { limited = true; return; }
            affected.Add(entity); members++;
        }
    }
}
