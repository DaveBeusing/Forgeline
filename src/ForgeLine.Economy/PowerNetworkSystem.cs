using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Economy;

public sealed class PowerNetworkSystem : ISimulationSystem
{
    private readonly Dictionary<PowerNetworkId, NetworkWorkingSet> _networks = new();
    private readonly List<PowerNetworkId> _activeNetworkIds = new();
    private readonly List<PowerNetworkReadModel> _networkReadModels = new();

    public SimulationPhase Phase => SimulationPhase.Infrastructure;

    public IReadOnlyList<PowerNetworkReadModel> Networks => _networkReadModels;

    public PowerNetworkMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResetWorkingSets();

        EntityRegistry entities = context.Entities;
        int generatorCount = 0;
        int activeGeneratorCount = 0;
        int unassignedGeneratorCount = 0;
        int consumerCount = 0;
        int unassignedConsumerCount = 0;

        foreach (EntityId entity in
                 entities.Query<PowerGenerator>(QueryIterationOrder.StableByEntityIndex))
        {
            generatorCount++;
            PowerGenerator generator = entities.GetComponent<PowerGenerator>(entity);

            if (!entities.TryGetComponent(entity, out PowerNetworkMembership membership))
            {
                unassignedGeneratorCount++;
                SetGeneratorState(
                    entities,
                    entity,
                    generator,
                    PowerGeneratorState.Offline);
                continue;
            }

            NetworkWorkingSet network = GetNetwork(membership.NetworkId);
            network.GeneratorCount++;

            if (!generator.Enabled)
            {
                SetGeneratorState(
                    entities,
                    entity,
                    generator,
                    PowerGeneratorState.Offline);
                continue;
            }

            activeGeneratorCount++;
            network.ActiveGeneratorCount++;
            network.Generation = AddFinite(
                network.Generation,
                generator.MaximumGeneration,
                "POWER_GENERATION_OVERFLOW",
                membership.NetworkId);

            SetGeneratorState(
                entities,
                entity,
                generator,
                PowerGeneratorState.Generating);
        }

        foreach (EntityId entity in
                 entities.Query<PowerConsumer>(QueryIterationOrder.StableByEntityIndex))
        {
            consumerCount++;
            PowerConsumer consumer = entities.GetComponent<PowerConsumer>(entity);

            if (!entities.TryGetComponent(entity, out PowerNetworkMembership membership))
            {
                unassignedConsumerCount++;
                SetConsumerAllocation(entities, entity, consumer, 0.0);
                continue;
            }

            NetworkWorkingSet network = GetNetwork(membership.NetworkId);
            network.ConsumerCount++;

            if (!consumer.Enabled)
            {
                network.OfflineConsumerCount++;
                SetConsumerAllocation(entities, entity, consumer, 0.0);
                continue;
            }

            network.AddConsumer(entity, consumer);
            network.Demand = AddFinite(
                network.Demand,
                consumer.Demand,
                "POWER_DEMAND_OVERFLOW",
                membership.NetworkId);
        }

        _activeNetworkIds.Sort();

        double totalGeneration = 0.0;
        double totalDemand = 0.0;
        double totalAllocated = 0.0;
        double totalSpareCapacity = 0.0;
        double totalDeficit = 0.0;
        int poweredConsumerCount = 0;
        int brownoutConsumerCount = 0;
        int offlineConsumerCount = unassignedConsumerCount;

        for (int index = 0; index < _activeNetworkIds.Count; index++)
        {
            PowerNetworkId networkId = _activeNetworkIds[index];
            NetworkWorkingSet network = _networks[networkId];
            AllocateNetwork(entities, network);

            double spareCapacity = Math.Max(0.0, network.Generation - network.Demand);
            double deficit = Math.Max(0.0, network.Demand - network.Generation);

            _networkReadModels.Add(
                new PowerNetworkReadModel(
                    networkId,
                    network.Generation,
                    network.Demand,
                    network.AllocatedPower,
                    spareCapacity,
                    deficit,
                    network.GeneratorCount,
                    network.ActiveGeneratorCount,
                    network.ConsumerCount,
                    network.PoweredConsumerCount,
                    network.BrownoutConsumerCount,
                    network.OfflineConsumerCount));

            totalGeneration = AddFinite(
                totalGeneration,
                network.Generation,
                "POWER_TOTAL_GENERATION_OVERFLOW",
                networkId);
            totalDemand = AddFinite(
                totalDemand,
                network.Demand,
                "POWER_TOTAL_DEMAND_OVERFLOW",
                networkId);
            totalAllocated = AddFinite(
                totalAllocated,
                network.AllocatedPower,
                "POWER_TOTAL_ALLOCATION_OVERFLOW",
                networkId);
            totalSpareCapacity = AddFinite(
                totalSpareCapacity,
                spareCapacity,
                "POWER_TOTAL_SPARE_OVERFLOW",
                networkId);
            totalDeficit = AddFinite(
                totalDeficit,
                deficit,
                "POWER_TOTAL_DEFICIT_OVERFLOW",
                networkId);

            poweredConsumerCount += network.PoweredConsumerCount;
            brownoutConsumerCount += network.BrownoutConsumerCount;
            offlineConsumerCount += network.OfflineConsumerCount;
        }

        Metrics = new PowerNetworkMetrics(
            _activeNetworkIds.Count,
            generatorCount,
            activeGeneratorCount,
            unassignedGeneratorCount,
            consumerCount,
            poweredConsumerCount,
            brownoutConsumerCount,
            offlineConsumerCount,
            unassignedConsumerCount,
            totalGeneration,
            totalDemand,
            totalAllocated,
            totalSpareCapacity,
            totalDeficit);
    }

    private void ResetWorkingSets()
    {
        foreach (NetworkWorkingSet network in _networks.Values)
        {
            network.Reset();
        }

        _activeNetworkIds.Clear();
        _networkReadModels.Clear();
    }

    private NetworkWorkingSet GetNetwork(PowerNetworkId networkId)
    {
        if (!_networks.TryGetValue(networkId, out NetworkWorkingSet? network))
        {
            network = new NetworkWorkingSet();
            _networks.Add(networkId, network);
        }

        if (!network.Active)
        {
            network.Active = true;
            _activeNetworkIds.Add(networkId);
        }

        return network;
    }

    private static void AllocateNetwork(
        EntityRegistry entities,
        NetworkWorkingSet network)
    {
        double remaining = network.Generation;

        remaining = AllocatePriority(
            entities,
            network,
            PowerPriority.Critical,
            network.CriticalDemand,
            remaining);
        remaining = AllocatePriority(
            entities,
            network,
            PowerPriority.Industrial,
            network.IndustrialDemand,
            remaining);
        _ = AllocatePriority(
            entities,
            network,
            PowerPriority.Optional,
            network.OptionalDemand,
            remaining);
    }

    private static double AllocatePriority(
        EntityRegistry entities,
        NetworkWorkingSet network,
        PowerPriority priority,
        double tierDemand,
        double remaining)
    {
        if (tierDemand <= 0.0)
        {
            return remaining;
        }

        double allocationFraction;
        if (remaining >= tierDemand)
        {
            allocationFraction = 1.0;
            remaining -= tierDemand;
        }
        else if (remaining > 0.0)
        {
            allocationFraction = remaining / tierDemand;
            remaining = 0.0;
        }
        else
        {
            allocationFraction = 0.0;
        }

        for (int index = 0; index < network.Consumers.Count; index++)
        {
            ConsumerEntry entry = network.Consumers[index];
            if (entry.Consumer.Priority != priority)
            {
                continue;
            }

            double allocatedPower = allocationFraction switch
            {
                >= 1.0 => entry.Consumer.Demand,
                <= 0.0 => 0.0,
                _ => entry.Consumer.Demand * allocationFraction
            };

            PowerConsumer updated =
                SetConsumerAllocation(
                    entities,
                    entry.Entity,
                    entry.Consumer,
                    allocatedPower);

            network.AllocatedPower = AddFinite(
                network.AllocatedPower,
                updated.AllocatedPower,
                "POWER_NETWORK_ALLOCATION_OVERFLOW",
                PowerNetworkId.None);

            switch (updated.State)
            {
                case PowerOperationalState.Powered:
                    network.PoweredConsumerCount++;
                    break;
                case PowerOperationalState.Brownout:
                    network.BrownoutConsumerCount++;
                    break;
                case PowerOperationalState.Offline:
                    network.OfflineConsumerCount++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported power operational state '{updated.State}'.");
            }
        }

        return remaining;
    }

    private static void SetGeneratorState(
        EntityRegistry entities,
        EntityId entity,
        PowerGenerator generator,
        PowerGeneratorState state)
    {
        if (generator.State == state)
        {
            return;
        }

        entities.SetComponent(entity, generator.WithState(state));
    }

    private static PowerConsumer SetConsumerAllocation(
        EntityRegistry entities,
        EntityId entity,
        PowerConsumer consumer,
        double allocatedPower)
    {
        PowerConsumer updated = consumer.WithAllocation(allocatedPower);

        if (updated != consumer)
        {
            entities.SetComponent(entity, updated);
        }

        return updated;
    }

    private static double AddFinite(
        double current,
        double value,
        string code,
        PowerNetworkId networkId)
    {
        double result = current + value;
        EngineInvariant.Require(
            double.IsFinite(result),
            DiagnosticCategory.Simulation,
            code,
            networkId.IsSpecified
                ? $"Power arithmetic overflowed for network {networkId}."
                : "Power arithmetic overflowed.");

        return result;
    }

    private sealed class NetworkWorkingSet
    {
        public List<ConsumerEntry> Consumers { get; } = new();

        public bool Active { get; set; }

        public double Generation { get; set; }

        public double Demand { get; set; }

        public double CriticalDemand { get; private set; }

        public double IndustrialDemand { get; private set; }

        public double OptionalDemand { get; private set; }

        public double AllocatedPower { get; set; }

        public int GeneratorCount { get; set; }

        public int ActiveGeneratorCount { get; set; }

        public int ConsumerCount { get; set; }

        public int PoweredConsumerCount { get; set; }

        public int BrownoutConsumerCount { get; set; }

        public int OfflineConsumerCount { get; set; }

        public void AddConsumer(EntityId entity, PowerConsumer consumer)
        {
            Consumers.Add(new ConsumerEntry(entity, consumer));

            switch (consumer.Priority)
            {
                case PowerPriority.Critical:
                    CriticalDemand = AddFinite(
                        CriticalDemand,
                        consumer.Demand,
                        "POWER_CRITICAL_DEMAND_OVERFLOW",
                        PowerNetworkId.None);
                    break;
                case PowerPriority.Industrial:
                    IndustrialDemand = AddFinite(
                        IndustrialDemand,
                        consumer.Demand,
                        "POWER_INDUSTRIAL_DEMAND_OVERFLOW",
                        PowerNetworkId.None);
                    break;
                case PowerPriority.Optional:
                    OptionalDemand = AddFinite(
                        OptionalDemand,
                        consumer.Demand,
                        "POWER_OPTIONAL_DEMAND_OVERFLOW",
                        PowerNetworkId.None);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported power priority '{consumer.Priority}'.");
            }
        }

        public void Reset()
        {
            Active = false;
            Generation = 0.0;
            Demand = 0.0;
            CriticalDemand = 0.0;
            IndustrialDemand = 0.0;
            OptionalDemand = 0.0;
            AllocatedPower = 0.0;
            GeneratorCount = 0;
            ActiveGeneratorCount = 0;
            ConsumerCount = 0;
            PoweredConsumerCount = 0;
            BrownoutConsumerCount = 0;
            OfflineConsumerCount = 0;
            Consumers.Clear();
        }
    }

    private readonly record struct ConsumerEntry(
        EntityId Entity,
        PowerConsumer Consumer);
}
