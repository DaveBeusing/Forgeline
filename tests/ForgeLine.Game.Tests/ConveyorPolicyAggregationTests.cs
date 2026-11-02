using System.Numerics;
using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorPolicyAggregationTests
{
    [Fact]
    public void DuplicateInputMaximumsUseTheSmallestEnabledCeilingAndRefreshAfterDisable()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 200);
        EntityId lower = fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 40, 40);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 80, 80);
        EntityId disabled = fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 1, 100);
        fixture.Entities.SetComponent(disabled, new LogisticsStockPolicy(destination, ResourceIds.FerrousOre,
            0, 1, 1, enabled: false));

        fixture.Step(100);
        Assert.Equal(40, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));

        fixture.Entities.SetComponent(lower, new LogisticsStockPolicy(destination, ResourceIds.FerrousOre,
            0, 40, 40, enabled: false));
        fixture.Step(100);
        Assert.Equal(80, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void DuplicateSourceMinimumsRetainTheLargestEnabledFloorAndRefreshAfterDisable()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 200);
        fixture.AddPolicy(source, ResourceIds.FerrousOre, 60, 60, 200);
        EntityId higher = fixture.AddPolicy(source, ResourceIds.FerrousOre, 100, 100, 200);

        fixture.Step(100);
        Assert.Equal(100, fixture.Stock(source, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));

        fixture.Entities.SetComponent(higher, new LogisticsStockPolicy(source, ResourceIds.FerrousOre,
            100, 100, 200, enabled: false));
        fixture.Step(100);
        Assert.Equal(60, fixture.Stock(source, ResourceIds.FerrousOre));
        Assert.Equal(140, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void AutomaticTargetDoesNotConstrainInboundBeforeMaximum()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 150);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 20, 100);

        fixture.Step(100);

        Assert.Equal(100, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(150, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void LoweringMaximumKeepsExistingStockAndPermitsRecovery()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.Give(destination, ResourceIds.FerrousOre, 120);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 40, 60);

        fixture.Step(100);
        Assert.Equal(120, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(220, fixture.Total(ResourceIds.FerrousOre));

        Assert.True(fixture.Inventories.Remove(fixture.Inventory(destination), ResourceIds.FerrousOre, 80).Succeeded);
        fixture.Step(100);
        Assert.Equal(60, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(140, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void DisabledZeroMaximumReleasesBackpressure()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        EntityId policy = fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 0, 0);

        fixture.Step(100);
        Assert.Equal(0, fixture.Stock(destination, ResourceIds.FerrousOre));
        fixture.Entities.SetComponent(policy, new LogisticsStockPolicy(destination, ResourceIds.FerrousOre,
            0, 0, 0, enabled: false));
        fixture.Step(100);
        Assert.Equal(100, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void ResourceLimitsAndSharedTotalCapacityBothApply()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80), capacity: 120);
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.Give(source, ResourceIds.Steel, 100);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 10, 40);
        fixture.AddPolicy(destination, ResourceIds.Steel, 0, 20, 100);
        double totalBefore = fixture.Total(ResourceIds.FerrousOre) + fixture.Total(ResourceIds.Steel);

        fixture.Step(150);
        double iron = fixture.Stock(destination, ResourceIds.FerrousOre);
        double steel = fixture.Stock(destination, ResourceIds.Steel);
        Assert.InRange(iron, 0, 40);
        Assert.InRange(steel, 0, 100);
        Assert.InRange(iron + steel, 0, 120);
        Assert.Equal(totalBefore, fixture.Total(ResourceIds.FerrousOre) + fixture.Total(ResourceIds.Steel));
    }

    [Fact]
    public void AliasedInventoriesUseTheStrictestBuildingMaximum()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        EntityId alias = fixture.Entities.CreateEntity();
        fixture.Entities.AddComponent(alias, new ForgeLine.Economy.InventoryStorage(fixture.Inventory(destination)));
        fixture.AddPolicy(alias, ResourceIds.FerrousOre, 0, 10, 20);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 50, 80);

        fixture.Step(100);
        Assert.Equal(20, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.Total(ResourceIds.FerrousOre));
    }
}
