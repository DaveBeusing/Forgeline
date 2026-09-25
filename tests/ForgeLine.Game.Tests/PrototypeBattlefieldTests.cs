using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PrototypeBattlefieldTests
{
    [Fact]
    public void CanonicalDefinitionProvidesValidatedVerticalSliceLayout()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                definition);

        Assert.Equal(
            "prototype.vertical_slice",
            definition.Metadata.Key);
        Assert.Equal(
            3_072.0f,
            definition.Metadata.WidthMeters);
        Assert.Equal(
            3_072.0f,
            definition.Metadata.HeightMeters);
        Assert.Equal(2, definition.Starts.Count);
        Assert.True(definition.Crossings.Count >= 2);
        Assert.Contains(
            definition.Sites,
            site =>
                site.Kind ==
                BattlefieldSiteKind.ForwardOperatingBase);
        Assert.Contains(
            definition.Resources,
            resource =>
                resource.Contested);
        Assert.Equal(
            definition.Metadata.WidthMeters,
            terrain.WorldBounds.Maximum.X -
            terrain.WorldBounds.Minimum.X);
        Assert.Equal(
            definition.Metadata.HeightMeters,
            terrain.WorldBounds.Maximum.Z -
            terrain.WorldBounds.Minimum.Z);
    }

    [Fact]
    public void CrossingDisruptionReroutesNavigationAndLogisticsAndRestorationRecovers()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                definition);
        var simulation =
            new SimulationCoordinator();
        var logistics =
            new LogisticsNetwork();
        PrototypeBattlefieldRuntime runtime =
            PrototypeBattlefieldRuntime.Load(
                simulation.Entities,
                definition,
                terrain,
                logistics);

        NavigationWorld initialWorld =
            NavigationWorld.Build(
                terrain,
                definition.CreateNavigationObstacles(),
                CreateGridSettings(),
                CreateSectorSettings());
        var pathfinder =
            new HierarchicalPathfinder(
                initialWorld);
        var navigation =
            new HierarchicalNavigationSystem(
                pathfinder);
        var infrastructure =
            new StrategicInfrastructureSystem(
                logistics,
                terrain,
                navigation,
                definition.StaticNavigationObstacles,
                CreateGridSettings(),
                CreateSectorSettings());

        simulation.RegisterSystem(
            infrastructure);
        simulation.RegisterSystem(
            navigation);
        simulation.AdvanceOneTick();

        LogisticsNodeId west =
            runtime.RoadNodes["west.start"];
        LogisticsNodeId east =
            runtime.RoadNodes["east.start"];
        LogisticsEdgeId northEdge =
            runtime.RoadEdges["north.bridge"];
        LogisticsEdgeId southEdge =
            runtime.RoadEdges["south.ford"];

        LogisticsRouteSearchResult baselineLogistics =
            logistics.FindRoute(
                west,
                east);

        Assert.True(baselineLogistics.Succeeded);
        Assert.NotNull(baselineLogistics.Route);
        Assert.Contains(
            baselineLogistics.Route!.Segments,
            segment =>
                segment.EdgeId == northEdge);

        Vector3 westApproach =
            new(1_250.0f, 0.0f, 920.0f);
        Vector3 eastApproach =
            new(1_820.0f, 0.0f, 920.0f);
        NavigationSearchResult baselineNavigation =
            pathfinder.FindPath(
                westApproach,
                eastApproach,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));

        Assert.True(baselineNavigation.Succeeded);
        Assert.NotNull(baselineNavigation.Path);

        EntityId northCrossing =
            runtime.CrossingEntities[
                "crossing.north_bridge"];
        var disable =
            new DisableStrategicInfrastructureCommand(
                northCrossing,
                simulation.CurrentTick);

        simulation.SubmitCommand(
            disable,
            simulation.CurrentTick.Next());
        simulation.AdvanceOneTick();

        Assert.True(disable.Accepted);
        Assert.True(
            simulation.Entities.TryGetComponent(
                northCrossing,
                out StrategicInfrastructureState disabledState));
        Assert.Equal(
            StrategicInfrastructureOperationalState.Disabled,
            disabledState.State);
        Assert.True(
            logistics.TryGetEdge(
                northEdge,
                out LogisticsEdge disabledEdge));
        Assert.False(disabledEdge.Enabled);

        LogisticsRouteSearchResult disruptedLogistics =
            logistics.FindRoute(
                west,
                east);

        Assert.True(disruptedLogistics.Succeeded);
        Assert.NotNull(disruptedLogistics.Route);
        Assert.DoesNotContain(
            disruptedLogistics.Route!.Segments,
            segment =>
                segment.EdgeId == northEdge);
        Assert.Contains(
            disruptedLogistics.Route.Segments,
            segment =>
                segment.EdgeId == southEdge);

        NavigationSearchResult disruptedNavigation =
            pathfinder.FindPath(
                westApproach,
                eastApproach,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));

        Assert.True(disruptedNavigation.Succeeded);
        Assert.NotNull(disruptedNavigation.Path);
        Assert.True(
            disruptedNavigation.Path!.Diagnostics.RouteLengthMeters >
            baselineNavigation.Path!.Diagnostics.RouteLengthMeters +
            1_000.0f);

        BattlefieldCrossingDefinition north =
            definition.GetCrossing(
                "crossing.north_bridge");
        var restore =
            new RestoreStrategicInfrastructureCommand(
                northCrossing,
                simulation.CurrentTick);

        simulation.SubmitCommand(
            restore,
            simulation.CurrentTick.Next());
        simulation.RunTicks(
            checked((ulong)north.RestorationTicks + 1UL),
            TestContext.Current.CancellationToken);

        Assert.True(restore.Accepted);
        StrategicInfrastructureState restoredState =
            simulation.Entities.GetComponent<StrategicInfrastructureState>(
                northCrossing);
        Assert.Equal(
            StrategicInfrastructureOperationalState.Operational,
            restoredState.State);
        Assert.True(
            logistics.TryGetEdge(
                northEdge,
                out LogisticsEdge restoredEdge));
        Assert.True(restoredEdge.Enabled);

        LogisticsRouteSearchResult restoredLogistics =
            logistics.FindRoute(
                west,
                east);

        Assert.True(restoredLogistics.Succeeded);
        Assert.Contains(
            restoredLogistics.Route!.Segments,
            segment =>
                segment.EdgeId == northEdge);

        NavigationSearchResult restoredNavigation =
            pathfinder.FindPath(
                westApproach,
                eastApproach,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));

        Assert.True(restoredNavigation.Succeeded);
        Assert.True(
            restoredNavigation.Path!.Diagnostics.RouteLengthMeters <
            disruptedNavigation.Path.Diagnostics.RouteLengthMeters);
    }

    [Fact]
    public void CommandCoreObjectivesResolveVictoryFromSimulationOwnedState()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                definition);
        var simulation =
            new SimulationCoordinator();
        var logistics =
            new LogisticsNetwork();
        PrototypeBattlefieldRuntime runtime =
            PrototypeBattlefieldRuntime.Load(
                simulation.Entities,
                definition,
                terrain,
                logistics);

        var commandCores =
            new Dictionary<PlayerId, EntityId>();

        for (int index = 0;
             index < definition.Objectives.Count;
             index++)
        {
            BattlefieldObjectiveDefinition objective =
                definition.Objectives[index];
            EntityId commandCore =
                simulation.Entities.CreateEntity();

            simulation.Entities.AddComponent(
                commandCore,
                new CompletedBuilding(
                    BuildingIds.CommandCore,
                    objective.Owner,
                    SimulationTick.Zero));
            simulation.Entities.AddComponent(
                commandCore,
                new CommandFacility());

            commandCores.Add(
                objective.Owner,
                commandCore);
        }

        _ = runtime.AttachCommandCoreObjectives(
            simulation.Entities,
            commandCores);

        simulation.RegisterSystem(
            new MatchObjectiveSystem(
                runtime.MatchStateEntity));
        simulation.AdvanceOneTick();

        MatchState running =
            simulation.Entities.GetComponent<MatchState>(
                runtime.MatchStateEntity);
        Assert.Equal(
            MatchStatus.Running,
            running.Status);

        Assert.True(
            simulation.Entities.DestroyEntity(
                commandCores[new PlayerId(2)]));
        simulation.AdvanceOneTick();

        MatchState completed =
            simulation.Entities.GetComponent<MatchState>(
                runtime.MatchStateEntity);
        Assert.Equal(
            MatchStatus.Victory,
            completed.Status);
        Assert.Equal(
            new PlayerId(1),
            completed.Winner);
        Assert.True(
            completed.CompletedAtTick >
            SimulationTick.Zero);
    }

    [Fact]
    public void RuntimeSpawnsFiniteDepositsAndStrategicCrossingsHeadlessly()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                definition);
        var simulation =
            new SimulationCoordinator();
        var logistics =
            new LogisticsNetwork();

        PrototypeBattlefieldRuntime runtime =
            PrototypeBattlefieldRuntime.Load(
                simulation.Entities,
                definition,
                terrain,
                logistics);

        Assert.Equal(
            definition.Resources.Count,
            runtime.ResourceEntities.Count);
        Assert.Equal(
            definition.Crossings.Count,
            runtime.CrossingEntities.Count);
        Assert.Equal(
            definition.RoadEdges.Count,
            logistics.EdgeCount);

        foreach (EntityId depositEntity in runtime.ResourceEntities)
        {
            ResourceDeposit deposit =
                simulation.Entities.GetComponent<ResourceDeposit>(
                    depositEntity);

            Assert.True(
                deposit.RemainingQuantity > 0.0);
            Assert.True(
                double.IsFinite(
                    deposit.RemainingQuantity));
        }
    }

    private static NavigationGridSettings CreateGridSettings() =>
        new()
        {
            CellSizeMeters = 16.0f,
            StaticObstacleClearanceMeters = 0.5f
        };

    private static NavigationSectorSettings CreateSectorSettings() =>
        new()
        {
            SectorSizeCells = 8
        };
}
