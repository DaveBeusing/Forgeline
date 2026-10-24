using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SplitBasinGeographyTests
{
    [Fact]
    public void LoadedMapHasMirroredFiniteBootstrapAndQualifiedOperationalGeography()
    {
        var composition = ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey);
        var map = composition.Battlefield;
        Assert.Equal(2048, map.Metadata.WidthMeters);
        Assert.Equal(2048, map.Metadata.HeightMeters);
        Assert.Equal(SplitBasinBattlefield.MapKey, map.Metadata.Key);
        var artifact = BattlefieldMapArtifact.Capture(SplitBasinBattlefield.Create(), SplitBasinBattlefield.CreateTerrainRecipe());
        Assert.Equal(System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "maps", "split-basin.flmap.json")), artifact.Serialize());
        artifact.ValidateMatches(map);
        foreach (var resource in map.Resources.Where(x => x.Key.StartsWith("west.", StringComparison.Ordinal)))
        {
            var east = Assert.Single(map.Resources, x => x.Key == "east." + resource.Key[5..]);
            Assert.Equal(2048 - resource.Center.X, east.Center.X);
            Assert.Equal(resource.Center.Z, east.Center.Z);
            Assert.Equal(resource.ResourceId, east.ResourceId);
            Assert.Equal(resource.TotalQuantity, east.TotalQuantity);
            Assert.Equal(resource.ExtractionRatePerSecond, east.ExtractionRatePerSecond);
        }
        var terrain = composition.CreateTerrain(map);
        var report = BattlefieldOperationalGeographyValidator.Validate(map, terrain);
        Assert.Equal(49, report.ReachabilityChecks);
        Assert.Equal(2, report.AlternateNavigationChecks);
        Assert.Equal(2, report.AlternateRoadChecks);
        Assert.True(report.TerrainElevationRangeMeters > 20);
        for (int z = 0; z < 2048; z += 32)
        for (int x = 32; x <= 1024; x += 32)
        {
            Assert.True(terrain.TrySampleHeight(x, z, out float a));
            Assert.True(terrain.TrySampleHeight(2048 - x, z, out float b));
            Assert.Equal(a, b, .0001f);
        }
    }

    [Fact]
    public void ExistingScoutSightDistinguishesCentralAndOuterCrossingExposure()
    {
        var composition = ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey);
        var map = composition.Battlefield;
        var terrain = composition.CreateTerrain(map);
        Vector3 Ground(Vector3 p) { Assert.True(terrain.TrySampleHeight(p.X, p.Z, out float h)); return p with { Y = h + 1.2f }; }
        var bridge = map.GetCrossing("crossing.basin_bridge");
        var ford = map.GetCrossing("crossing.outer_ford");
        var simulation = new SimulationCoordinator();
        var intelligence = new FactionIntelligenceStore();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(intelligence));
        var observer = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(observer, new WorldTransform(Ground(bridge.Position + new Vector3(-124, 0, -144)), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(observer, new VisualSensorState(new FactionId(1), composition.Content.Units[UnitIds.ScoutVehicle].VisualSensorRangeMeters));
        var target = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(target, new WorldTransform(Ground(bridge.Position), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(target, new IntelligenceSignature(new FactionId(2), 77));
        simulation.AdvanceOneTick();
        Assert.True(intelligence.IsEntityCurrentlyIdentified(new FactionId(1), target));
        simulation.Entities.SetComponent(target, new WorldTransform(Ground(ford.Position), Quaternion.Identity, Vector3.One));
        simulation.AdvanceOneTick();
        Assert.False(intelligence.IsEntityCurrentlyIdentified(new FactionId(1), target));
    }

    [Theory]
    [InlineData(NavigationMovementClass.Infantry)]
    [InlineData(NavigationMovementClass.Wheeled)]
    [InlineData(NavigationMovementClass.Tracked)]
    public void BothStartsReachEverySiteAndDepositAndEachSingleLossHasLongerRealAlternative(NavigationMovementClass movement)
    {
        var composition = ScenarioRegistry.Default.Resolve(SplitBasinBattlefield.CompositionKey);
        var map = composition.Battlefield;
        var terrain = composition.CreateTerrain(map);
        var capabilities = NavigationCapabilities.For(movement);
        NavigationWorld Build(string? disabled) => NavigationWorld.Build(terrain,
            map.CreateNavigationObstacles(disabled is null ? null : new Dictionary<string, bool> { [disabled] = false }),
            new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = .5f },
            new NavigationSectorSettings { SectorSizeCells = 8 });
        var pathfinder = new HierarchicalPathfinder(Build(null));
        foreach (var start in map.Starts)
        foreach (var target in map.Sites.Select(x => x.Position).Concat(map.Resources.Select(x => x.Center)).Concat(map.Objectives.Select(x => x.CommandCorePosition)))
            Assert.True(pathfinder.FindPath(start.Position, target, capabilities, projectBlockedEndpoints: false).Succeeded, $"{movement}: {start.Player} to {target}");
        float Length(HierarchicalPathfinder finder) => finder.FindPath(map.Starts[0].Position, map.Starts[1].Position, capabilities).Path!.Diagnostics.RouteLengthMeters;
        float direct = Length(pathfinder);
        float alternate = Length(new HierarchicalPathfinder(Build("crossing.basin_bridge")));
        Assert.InRange(direct, 1400, 1700);
        Assert.True(alternate > direct + 200, $"Direct={direct}; alternate={alternate}");
        Assert.True(Length(new HierarchicalPathfinder(Build("crossing.outer_ford"))) <= direct + 1);
        var disconnected = NavigationWorld.Build(terrain, map.CreateNavigationObstacles(new Dictionary<string, bool>
            { ["crossing.basin_bridge"] = false, ["crossing.outer_ford"] = false }));
        Assert.False(new HierarchicalPathfinder(disconnected).FindPath(map.Starts[0].Position, map.Starts[1].Position, capabilities).Succeeded);
    }
}
