using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Economy;

public readonly record struct ResourceDepositPlacement
{
    public ResourceDepositPlacement(
        ResourceId resourceId,
        AxisAlignedBounds bounds,
        double totalQuantity,
        double baseExtractionRatePerSecond,
        double richness = 1.0,
        FactionId owner = default)
    {
        ResourceId = resourceId;
        Bounds = bounds;
        TotalQuantity = totalQuantity;
        BaseExtractionRatePerSecond = baseExtractionRatePerSecond;
        Richness = richness;
        Owner = owner;

        _ = new ResourceDeposit(
            resourceId,
            bounds,
            totalQuantity,
            baseExtractionRatePerSecond,
            richness,
            owner);
    }

    public ResourceId ResourceId { get; }

    public AxisAlignedBounds Bounds { get; }

    public double TotalQuantity { get; }

    public double BaseExtractionRatePerSecond { get; }

    public double Richness { get; }

    public FactionId Owner { get; }

    public ResourceDeposit ToDeposit() =>
        new(
            ResourceId,
            Bounds,
            TotalQuantity,
            BaseExtractionRatePerSecond,
            Richness,
            Owner);
}

public static class ResourceDepositSpawner
{
    public static EntityId Place(
        EntityRegistry entities,
        in ResourceDepositPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(entities);

        EntityId entity = entities.CreateEntity();

        try
        {
            entities.AddComponent(entity, placement.ToDeposit());
            return entity;
        }
        catch
        {
            entities.DestroyEntity(entity);
            throw;
        }
    }
}
