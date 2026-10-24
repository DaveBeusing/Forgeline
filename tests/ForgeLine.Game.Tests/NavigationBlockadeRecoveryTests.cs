using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class NavigationBlockadeRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewBlockadeReplansLoadedTruckOrReportsUnreachableWithoutLosingCargo(bool fullyClosed)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 128, HeightSamplesPerSide = 17 };
        var terrain = new TerrainWorld(settings,
            [new TerrainChunk(new ChunkCoordinate(0, 0), new TerrainHeightfield(17, 128, new float[17 * 17]))]);
        var gridSettings = new NavigationGridSettings { CellSizeMeters = 4 };
        var sectors = new NavigationSectorSettings { SectorSizeCells = 8 };
        var initial = NavigationWorld.Build(terrain, gridSettings: gridSettings, sectorSettings: sectors);
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(initial));
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(navigation);
        var inventories = new InventoryStore();
        var transport = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories,
            new Vector3(10, 1, 10), new PlayerId(1), transport);
        InventoryId cargo = simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory;
        Assert.True(inventories.Add(cargo, ResourceIds.Steel, 40).Succeeded);
        var order = new MovementOrder(new PlayerId(1), new Vector3(106, 0, 10), SimulationTick.Zero, new SimulationTick(1));
        simulation.Entities.AddComponent(truck, order);
        simulation.AdvanceOneTick();
        var wall = new AxisAlignedBounds(new Vector3(52, -1, 0), new Vector3(56, 8, fullyClosed ? 128 : 70));
        NavigationWorld changed = NavigationWorld.Build(terrain, [wall], gridSettings, sectors, new NavigationVersion(2));
        navigation.UpdateWorld(changed);
        simulation.RunTicks(2, TestContext.Current.CancellationToken);
        if (fullyClosed)
        {
            NavigationFailureState failure = simulation.Entities.GetComponent<NavigationFailureState>(truck);
            Assert.Equal(order.WorldTarget, failure.OriginalOrder.WorldTarget);
            Assert.Equal(changed.Version, failure.NavigationVersion);
            Assert.True(failure.FailureReason is NavigationFailureReason.NoHighLevelRoute or NavigationFailureReason.NoLocalRoute);
            Assert.False(simulation.Entities.HasComponent<NavigationRouteState>(truck));
            Assert.False(simulation.Entities.HasComponent<MovementOrder>(truck));
        }
        else
        {
            NavigationRouteState route = simulation.Entities.GetComponent<NavigationRouteState>(truck);
            Assert.Equal(changed.Version, route.Path.Version);
            Assert.Equal(order.WorldTarget, route.Path.Waypoints[^1]);
            Assert.Contains(route.Path.Waypoints, point => point.Z > wall.Maximum.Z);
            AssertTraversable(route.Path, changed.Grid);
        }
        Assert.Equal(40, inventories.GetQuantity(cargo, ResourceIds.Steel));
    }

    private static void AssertTraversable(NavigationPath path, NavigationGrid grid)
    {
        NavigationCapabilities capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        for (int index = 1; index < path.RefinedCells.Count; index++)
        {
            NavigationCellCoordinate current = path.RefinedCells[index - 1];
            NavigationCellCoordinate end = path.RefinedCells[index];
            int dx = Math.Sign(end.X - current.X);
            int dz = Math.Sign(end.Z - current.Z);
            int steps = Math.Max(Math.Abs(end.X - current.X), Math.Abs(end.Z - current.Z));
            for (int step = 0; step < steps; step++)
            {
                if (dx != 0 && dz != 0)
                {
                    Assert.True(grid.IsTraversable(new NavigationCellCoordinate(current.X + dx, current.Z), capabilities));
                    Assert.True(grid.IsTraversable(new NavigationCellCoordinate(current.X, current.Z + dz), capabilities));
                }
                current = new NavigationCellCoordinate(current.X + dx, current.Z + dz);
                Assert.True(grid.IsTraversable(current, capabilities));
            }
            Assert.Equal(end, current);
        }
    }
}
