using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class SimulationStressTests
{
    [Fact]
    public void TenThousandLightweightEntitiesSurviveHeadlessTicks()
    {
        const int entityCount = 10_000;
        using var harness = new SimulationTestHarness(
            seed: 9001,
            initialEntityCapacity: entityCount);

        for (int index = 0; index < entityCount; index++)
        {
            EntityId entity = harness.CreateEntity();
            harness.Entities.AddComponent(entity, new Position(index, -index));
        }

        ulong executed = harness.Run(64);

        Assert.Equal(64UL, executed);
        Assert.Equal(entityCount, harness.Entities.EntityCount);
        Assert.Equal(entityCount, harness.Entities.GetComponentCount<Position>());

        int iterated = 0;
        foreach (EntityId entity in harness.Entities.Query<Position>())
        {
            Assert.True(harness.Entities.IsAlive(entity));
            iterated++;
        }

        Assert.Equal(entityCount, iterated);
    }

    [Fact]
    public void ThousandEntitiesSupportRepresentativeComponentLoad()
    {
        const int entityCount = 1_000;
        using var harness = new SimulationTestHarness(
            seed: 42,
            initialEntityCapacity: entityCount);

        for (int index = 0; index < entityCount; index++)
        {
            EntityId entity = harness.CreateEntity();
            harness.Entities.AddComponent(entity, new Position(index, index));
            harness.Entities.AddComponent(entity, new Velocity(1, -1));
            harness.Entities.AddComponent(entity, new Health(100));
            harness.Entities.AddComponent(entity, new Supply(100, 100));
        }

        harness.Run(128);

        Assert.Equal(entityCount, harness.Entities.EntityCount);
        Assert.Equal(entityCount * 4, harness.Entities.Diagnostics.TotalComponentCount);
    }

    private readonly record struct Position(int X, int Y);

    private readonly record struct Velocity(int X, int Y);

    private readonly record struct Health(int Value);

    private readonly record struct Supply(int Fuel, int Ammunition);
}
