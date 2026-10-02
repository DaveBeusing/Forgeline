using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct RepairRecoveryMetrics(
    int DamagedUnits,
    int RepairingUnits,
    int MaterialBlockedUnits,
    double HealthRestoredThisTick,
    double ResourceConsumedThisTick,
    double TotalHealthRestored,
    double TotalResourceConsumed);

public readonly record struct RepairRecoveryDebugEntry(
    EntityId Entity,
    EntityId Provider,
    Vector3 Position,
    RepairRecoveryStatus Status,
    double CurrentHealth,
    double MaximumHealth);

public sealed class RepairRecoverySystem : ISimulationSystem
{
    private readonly InventoryStore _inventories;
    private readonly List<EntityId> _providers = new();
    private readonly List<EntityId> _damagedCandidates = new();
    private readonly List<RepairRecoveryDebugEntry> _debugEntries = new();
    private double _totalHealthRestored;
    private double _totalResourceConsumed;

    public RepairRecoverySystem(
        InventoryStore inventories)
    {
        _inventories =
            inventories ??
            throw new ArgumentNullException(nameof(inventories));
    }

    public SimulationPhase Phase =>
        SimulationPhase.Supply;

    public bool DebugCaptureEnabled { get; set; }

    public RepairRecoveryMetrics Metrics { get; private set; }

    public IReadOnlyList<RepairRecoveryDebugEntry> DebugEntries =>
        _debugEntries;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        GatherProviders(context);

        int damaged = 0;
        int repairing = 0;
        int materialBlocked = 0;
        double healthRestored = 0.0;
        double resourceConsumed = 0.0;

        _debugEntries.Clear();
        _damagedCandidates.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<HealthState>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _damagedCandidates.Add(entity);
        }

        for (int candidateIndex = 0;
             candidateIndex < _damagedCandidates.Count;
             candidateIndex++)
        {
            EntityId entity =
                _damagedCandidates[candidateIndex];

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable))
            {
                continue;
            }

            HealthState health =
                context.Entities.GetComponent<HealthState>(
                    entity);

            if (!controllable.Category.HasFlag(
                    ControllableEntityCategory.Unit) ||
                health.IsDepleted)
            {
                continue;
            }

            if (health.Current >= health.Maximum)
            {
                ClearRecoveryState(
                    context,
                    entity);
                continue;
            }

            damaged++;

            if (!TryResolveProvider(
                    context,
                    controllable.Owner,
                    transform.Position,
                    out EntityId providerEntity,
                    out RepairProvider provider))
            {
                ClearRecoveryState(
                    context,
                    entity);
                continue;
            }

            double missingHealth =
                health.Maximum -
                health.Current;
            double desiredHealth =
                Math.Min(
                    missingHealth,
                    provider.HealthPerTick);
            double availableResource =
                _inventories.GetAvailableQuantity(
                    provider.InventoryId,
                    provider.ResourceId);
            double affordableHealth =
                availableResource /
                provider.ResourcePerHealth;
            double restored =
                Math.Min(
                    desiredHealth,
                    affordableHealth);

            if (restored <= 0.0)
            {
                materialBlocked++;
                SetRecoveryState(
                    context,
                    entity,
                    new RepairRecoveryState(
                        providerEntity,
                        RepairRecoveryStatus.NoMaterial,
                        0.0,
                        0.0,
                        context.Tick));
                AddDebug(
                    entity,
                    providerEntity,
                    transform.Position,
                    RepairRecoveryStatus.NoMaterial,
                    health);
                continue;
            }

            double consumed =
                restored *
                provider.ResourcePerHealth;
            InventoryOperationResult removal =
                _inventories.Remove(
                    provider.InventoryId,
                    provider.ResourceId,
                    consumed);

            if (!removal.Succeeded)
            {
                materialBlocked++;
                SetRecoveryState(
                    context,
                    entity,
                    new RepairRecoveryState(
                        providerEntity,
                        RepairRecoveryStatus.NoMaterial,
                        0.0,
                        0.0,
                        context.Tick));
                AddDebug(
                    entity,
                    providerEntity,
                    transform.Position,
                    RepairRecoveryStatus.NoMaterial,
                    health);
                continue;
            }

            HealthState updated =
                new(
                    Math.Min(
                        health.Maximum,
                        health.Current +
                        restored),
                    health.Maximum);
            context.Entities.SetComponent(
                entity,
                updated);

            RepairRecoveryStatus status =
                updated.Current >= updated.Maximum
                    ? RepairRecoveryStatus.FullyRecovered
                    : RepairRecoveryStatus.Repairing;

            SetRecoveryState(
                context,
                entity,
                new RepairRecoveryState(
                    providerEntity,
                    status,
                    restored,
                    consumed,
                    context.Tick));

            repairing++;
            healthRestored += restored;
            resourceConsumed += consumed;
            _totalHealthRestored += restored;
            _totalResourceConsumed += consumed;

            AddDebug(
                entity,
                providerEntity,
                transform.Position,
                status,
                updated);
        }

        Metrics =
            new RepairRecoveryMetrics(
                damaged,
                repairing,
                materialBlocked,
                healthRestored,
                resourceConsumed,
                _totalHealthRestored,
                _totalResourceConsumed);
    }

    private void GatherProviders(
        SimulationContext context)
    {
        _providers.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     RepairProvider,
                     WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _providers.Add(entity);
        }
    }

    private bool TryResolveProvider(
        SimulationContext context,
        PlayerId owner,
        Vector3 position,
        out EntityId providerEntity,
        out RepairProvider provider)
    {
        providerEntity =
            EntityId.Invalid;
        provider = default;
        float bestDistanceSquared =
            float.PositiveInfinity;

        for (int index = 0;
             index < _providers.Count;
             index++)
        {
            EntityId candidate =
                _providers[index];
            RepairProvider candidateProvider =
                context.Entities.GetComponent<RepairProvider>(
                    candidate);

            if (candidateProvider.Owner != owner ||
                !_inventories.Contains(
                    candidateProvider.InventoryId))
            {
                continue;
            }

            WorldTransform providerTransform =
                context.Entities.GetComponent<WorldTransform>(
                    candidate);
            float distanceSquared =
                HorizontalDistanceSquared(
                    position,
                    providerTransform.Position);
            float rangeSquared =
                candidateProvider.RepairRangeMeters *
                candidateProvider.RepairRangeMeters;

            if (distanceSquared > rangeSquared)
            {
                continue;
            }

            if (distanceSquared < bestDistanceSquared ||
                (distanceSquared == bestDistanceSquared &&
                 (!providerEntity.IsValid ||
                  candidate < providerEntity)))
            {
                providerEntity = candidate;
                provider = candidateProvider;
                bestDistanceSquared = distanceSquared;
            }
        }

        return providerEntity.IsValid;
    }

    private void AddDebug(
        EntityId entity,
        EntityId provider,
        Vector3 position,
        RepairRecoveryStatus status,
        in HealthState health)
    {
        if (!DebugCaptureEnabled)
        {
            return;
        }

        _debugEntries.Add(
            new RepairRecoveryDebugEntry(
                entity,
                provider,
                position,
                status,
                health.Current,
                health.Maximum));
    }

    private static void SetRecoveryState(
        SimulationContext context,
        EntityId entity,
        in RepairRecoveryState state)
    {
        if (context.Entities.HasComponent<RepairRecoveryState>(
                entity))
        {
            context.Entities.SetComponent(
                entity,
                state);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                state);
        }
    }

    private static void ClearRecoveryState(
        SimulationContext context,
        EntityId entity)
    {
        if (context.Entities.HasComponent<RepairRecoveryState>(
                entity))
        {
            context.Entities.RemoveComponent<RepairRecoveryState>(
                entity);
        }
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
