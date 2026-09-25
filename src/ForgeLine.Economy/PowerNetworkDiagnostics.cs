using ForgeLine.Core;
using ForgeLine.Ecs;

namespace ForgeLine.Economy;

public readonly record struct PowerNetworkReadModel(
    PowerNetworkId NetworkId,
    double Generation,
    double Demand,
    double AllocatedPower,
    double SpareCapacity,
    double Deficit,
    int GeneratorCount,
    int ActiveGeneratorCount,
    int ConsumerCount,
    int PoweredConsumerCount,
    int BrownoutConsumerCount,
    int OfflineConsumerCount);

public readonly record struct PowerGeneratorReadModel(
    EntityId Entity,
    PowerNetworkId NetworkId,
    double MaximumGeneration,
    bool Enabled,
    PowerGeneratorState State);

public readonly record struct PowerConsumerReadModel(
    EntityId Entity,
    PowerNetworkId NetworkId,
    double Demand,
    double AllocatedPower,
    double SupplyFraction,
    PowerPriority Priority,
    bool Enabled,
    PowerOperationalState State);

public readonly record struct PowerNetworkMetrics(
    int NetworkCount,
    int GeneratorCount,
    int ActiveGeneratorCount,
    int UnassignedGeneratorCount,
    int ConsumerCount,
    int PoweredConsumerCount,
    int BrownoutConsumerCount,
    int OfflineConsumerCount,
    int UnassignedConsumerCount,
    double TotalGeneration,
    double TotalDemand,
    double TotalAllocatedPower,
    double TotalSpareCapacity,
    double TotalDeficit);

public sealed class PowerNetworkDebugSnapshot
{
    public PowerNetworkDebugSnapshot(
        IReadOnlyList<PowerNetworkReadModel> networks,
        IReadOnlyList<PowerGeneratorReadModel> generators,
        IReadOnlyList<PowerConsumerReadModel> consumers,
        PowerNetworkMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(generators);
        ArgumentNullException.ThrowIfNull(consumers);

        Networks = networks;
        Generators = generators;
        Consumers = consumers;
        Metrics = metrics;
    }

    public IReadOnlyList<PowerNetworkReadModel> Networks { get; }

    public IReadOnlyList<PowerGeneratorReadModel> Generators { get; }

    public IReadOnlyList<PowerConsumerReadModel> Consumers { get; }

    public PowerNetworkMetrics Metrics { get; }

    public static PowerNetworkDebugSnapshot Capture(
        EntityRegistry entities,
        PowerNetworkSystem system)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(system);

        var generators = new List<PowerGeneratorReadModel>(
            entities.GetComponentCount<PowerGenerator>());
        var consumers = new List<PowerConsumerReadModel>(
            entities.GetComponentCount<PowerConsumer>());

        foreach (EntityId entity in
                 entities.Query<PowerGenerator>(QueryIterationOrder.StableByEntityIndex))
        {
            PowerGenerator generator = entities.GetComponent<PowerGenerator>(entity);
            PowerNetworkId networkId =
                entities.TryGetComponent(entity, out PowerNetworkMembership membership)
                    ? membership.NetworkId
                    : PowerNetworkId.None;

            generators.Add(
                new PowerGeneratorReadModel(
                    entity,
                    networkId,
                    generator.MaximumGeneration,
                    generator.Enabled,
                    generator.State));
        }

        foreach (EntityId entity in
                 entities.Query<PowerConsumer>(QueryIterationOrder.StableByEntityIndex))
        {
            PowerConsumer consumer = entities.GetComponent<PowerConsumer>(entity);
            PowerNetworkId networkId =
                entities.TryGetComponent(entity, out PowerNetworkMembership membership)
                    ? membership.NetworkId
                    : PowerNetworkId.None;

            consumers.Add(
                new PowerConsumerReadModel(
                    entity,
                    networkId,
                    consumer.Demand,
                    consumer.AllocatedPower,
                    consumer.SupplyFraction,
                    consumer.Priority,
                    consumer.Enabled,
                    consumer.State));
        }

        return new PowerNetworkDebugSnapshot(
            system.Networks.ToArray(),
            generators,
            consumers,
            system.Metrics);
    }
}
