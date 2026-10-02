using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct SuppressionMetrics(
    int ActiveUnits,
    int SuppressedUnits,
    int PinnedUnits,
    ulong TotalRecoveries);

public readonly record struct SuppressionDebugEntry(
    EntityId Entity,
    Vector3 Position,
    double Value,
    SuppressionLevel Level);

public sealed class SuppressionSystem : ISimulationSystem
{
    private readonly List<EntityId> _units = new();
    private readonly List<SuppressionDebugEntry> _debugEntries = new();
    private ulong _totalRecoveries;

    public SimulationPhase Phase =>
        SimulationPhase.OrderProcessing;

    public bool DebugCaptureEnabled { get; set; }

    public SuppressionMetrics Metrics { get; private set; }

    public IReadOnlyList<SuppressionDebugEntry> DebugEntries =>
        _debugEntries;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int active = 0;
        int suppressed = 0;
        int pinned = 0;

        _debugEntries.Clear();
        _units.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     SuppressionState,
                     SuppressionProfile>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _units.Add(entity);
        }

        for (int unitIndex = 0;
             unitIndex < _units.Count;
             unitIndex++)
        {
            EntityId entity =
                _units[unitIndex];
            SuppressionState previous =
                context.Entities.GetComponent<SuppressionState>(
                    entity);
            SuppressionProfile profile =
                context.Entities.GetComponent<SuppressionProfile>(
                    entity);
            SuppressionState current =
                SuppressionRules.Decay(
                    previous,
                    profile,
                    context.TickDuration,
                    context.Tick);

            if (current != previous)
            {
                context.Entities.SetComponent(
                    entity,
                    current);
            }

            ApplyMovementConstraint(
                context.Entities,
                entity,
                current,
                profile);

            if (current.Level != SuppressionLevel.Normal)
            {
                active++;
            }

            if (current.Level == SuppressionLevel.Suppressed)
            {
                suppressed++;
            }
            else if (current.Level == SuppressionLevel.Pinned)
            {
                pinned++;
            }

            if (previous.Level != SuppressionLevel.Normal &&
                current.Level == SuppressionLevel.Normal)
            {
                _totalRecoveries++;
            }

            if (DebugCaptureEnabled &&
                context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                _debugEntries.Add(
                    new SuppressionDebugEntry(
                        entity,
                        transform.Position,
                        current.Value,
                        current.Level));
            }
        }

        Metrics =
            new SuppressionMetrics(
                active,
                suppressed,
                pinned,
                _totalRecoveries);
    }

    private static void ApplyMovementConstraint(
        EntityRegistry entities,
        EntityId entity,
        in SuppressionState state,
        in SuppressionProfile profile)
    {
        if (state.Level == SuppressionLevel.Normal)
        {
            if (entities.HasComponent<SuppressionMovementConstraint>(
                    entity))
            {
                entities.RemoveComponent<SuppressionMovementConstraint>(
                    entity);
            }

            return;
        }

        SuppressionMovementConstraint constraint =
            state.Level == SuppressionLevel.Pinned
                ? new SuppressionMovementConstraint(
                    maximumSpeedScale: 0.0f,
                    canMove: false)
                : new SuppressionMovementConstraint(
                    profile.SuppressedSpeedScale,
                    canMove: true);

        if (entities.HasComponent<SuppressionMovementConstraint>(
                entity))
        {
            entities.SetComponent(
                entity,
                constraint);
        }
        else
        {
            entities.AddComponent(
                entity,
                constraint);
        }
    }
}
