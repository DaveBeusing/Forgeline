using System.Numerics;
using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorPolicyAggregationTests
{
    [Fact]
    public void DuplicateInputTargetsUseTheSmallestEnabledTargetAndRefreshAfterDisable()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 200);
        EntityId lower = fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 40, 100);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 80, 100);
        EntityId disabled = fixture.AddPolicy(destination, ResourceIds.FerrousOre, 0, 1, 100);
        fixture.Entities.SetComponent(disabled, new LogisticsStockPolicy(destination, ResourceIds.FerrousOre,
            0, 1, 100, enabled: false));

        fixture.Step(100);
        Assert.Equal(40, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));

        fixture.Entities.SetComponent(lower, new LogisticsStockPolicy(destination, ResourceIds.FerrousOre,
            0, 40, 100, enabled: false));
        fixture.Step(100);
        Assert.Equal(80, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void DuplicateSourceTargetsRetainTheLargestEnabledStockAndRefreshAfterDisable()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 200);
        fixture.AddPolicy(source, ResourceIds.FerrousOre, 0, 60, 200);
        EntityId higher = fixture.AddPolicy(source, ResourceIds.FerrousOre, 0, 100, 200);

        fixture.Step(100);
        Assert.Equal(100, fixture.Stock(source, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));

        fixture.Entities.SetComponent(higher, new LogisticsStockPolicy(source, ResourceIds.FerrousOre,
            0, 100, 200, enabled: false));
        fixture.Step(100);
        Assert.Equal(60, fixture.Stock(source, ResourceIds.FerrousOre));
        Assert.Equal(140, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.Equal(200, fixture.Total(ResourceIds.FerrousOre));
    }
}
