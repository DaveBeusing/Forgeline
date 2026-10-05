using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
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
        Assert.Contains(
            definition.Resources,
            resource =>
                resource.ResourceId ==
                ResourceIds.RareElements);
        Assert.Contains(
            definition.WorldObjects,
            worldObject =>
                worldObject.Kind ==
                WorldPresentationKind.Decal);
        Assert.Contains(
            definition.WorldObjects,
            worldObject =>
                worldObject.Kind ==
                WorldPresentationKind.Vegetation);
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
    public void CanonicalMapArtifactRoundTripsStrategicDefinition()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();

        BattlefieldMapArtifact artifact =
            BattlefieldMapArtifact.Capture(
                definition);
        byte[] payload =
            artifact.Serialize();
        BattlefieldMapArtifact loaded =
            BattlefieldMapArtifact.Deserialize(
                payload);

        loaded.ValidateMatches(
            definition);

        Assert.Equal(
            BattlefieldMapArtifact.CurrentFormatVersion,
            loaded.FormatVersion);
        Assert.Equal(
            definition.Metadata,
            loaded.Metadata);
        Assert.Equal(
            definition.Resources.Count,
            loaded.Resources.Length);
        Assert.Equal(
            definition.Crossings.Count,
            loaded.Crossings.Length);
        Assert.Equal(
            BattlefieldMapArtifact.CurrentFormatVersion,
            loaded.FormatVersion);
        Assert.Equal(
            BattlefieldTerrainControlEncoding.RgbaFourLayer,
            loaded.TerrainVisual.ControlEncoding);
        Assert.Equal(
            4,
            loaded.TerrainVisual.ActiveLayerLimit);
        Assert.Equal(
            33,
            loaded.TerrainVisual.ControlSamplesPerSide);
        Assert.Equal(
            8,
            loaded.TerrainVisual.MaterialAssetIds.Length);
        Assert.Equal(
            definition.TerrainVisual.MaterialAssetIds,
            loaded.TerrainVisual.MaterialAssetIds);

        Assert.Equal(
            payload,
            loaded.Serialize());
    }

    [Fact]
    public void CanonicalMapOperationalGeographyQualifiesHeadlessly()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                definition);

        BattlefieldOperationalGeographyReport report =
            BattlefieldOperationalGeographyValidator.Validate(
                definition,
                terrain,
                new NavigationGridSettings
                {
                    CellSizeMeters = 32.0f,
                    StaticObstacleClearanceMeters = 0.5f
                },
                new NavigationSectorSettings
                {
                    SectorSizeCells = 4
                });

        Assert.Equal(
            definition.Metadata.Key,
            report.MapKey);
        Assert.True(
            report.ReachabilityChecks > 0);
        Assert.Equal(
            definition.Crossings.Count,
            report.AlternateNavigationChecks);
        Assert.Equal(
            definition.Crossings.Count,
            report.AlternateRoadChecks);
        Assert.True(
            report.TerrainElevationRangeMeters >= 20.0f);
        Assert.True(
            report.LongestRouteMeters >
            report.ShortestRouteMeters);
        Assert.Equal(
            definition.Resources.Count(
                resource => resource.Contested),
            report.ContestedResourceCount);
    }

    [Fact]
    public void CanonicalMapUsesExplicitStrategicBuildZones()
    {
        PrototypeBattlefieldDefinition definition =
            PrototypeBattlefieldDefinition.Create();
        var query =
            new BattlefieldBuildableAreaQuery(
                definition);
        BattlefieldStartPosition west =
            definition.Starts[0];
        BattlefieldResourceDepositDefinition contested =
            Assert.Single(
                definition.Resources,
                resource =>
                    resource.Key ==
                    "center.contested.silicates");

        Assert.True(
            query.IsBuildable(
                west.Player,
                SmallFootprint(
                    west.CommandCorePosition)));
        Assert.True(
            query.IsBuildable(
                west.Player,
                SmallFootprint(
                    contested.Center)));
        Assert.False(
            query.IsBuildable(
                west.Player,
                SmallFootprint(
                    new Vector3(
                        definition.Metadata.WidthMeters * 0.5f,
                        0.0f,
                        320.0f))));
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

        MatchState ready =
            simulation.Entities.GetComponent<MatchState>(
                runtime.MatchStateEntity);
        Assert.Equal(
            MatchLifecyclePhase.Ready,
            ready.Lifecycle);
        Assert.Equal(
            MatchStatus.Loading,
            ready.Status);

        MatchObjectiveSystem.ActivateMatch(
            simulation.Entities,
            runtime.MatchStateEntity);

        foreach (EntityId commandCore in commandCores.Values)
        {
            Assert.True(
                simulation.Entities.HasComponent<Combatant>(
                    commandCore));
            Assert.True(
                simulation.Entities.HasComponent<Targetable>(
                    commandCore));
            Assert.Equal(
                TargetClass.Structure,
                simulation.Entities.GetComponent<Targetable>(
                    commandCore).Class);
            Assert.True(
                simulation.Entities.HasComponent<HealthState>(
                    commandCore));
        }

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
            Assert.True(
                simulation.Entities.HasComponent<WorldTransform>(
                    depositEntity));
            Assert.True(
                simulation.Entities.HasComponent<VisualIdentity>(
                    depositEntity));
            Assert.True(
                simulation.Entities.HasComponent<WorldPresentationIdentity>(
                    depositEntity));
        }

        Assert.Equal(
            definition.WorldObjects.Count,
            runtime.WorldPresentationEntities.Count);

        int roadPresentationCount =
            0;
        foreach (EntityId entity in
                 simulation.Entities.Query<InfrastructurePresentationIdentity>())
        {
            InfrastructurePresentationIdentity presentation =
                simulation.Entities.GetComponent<InfrastructurePresentationIdentity>(
                    entity);

            if (presentation.Kind ==
                InfrastructurePresentationKind.RoadSegment)
            {
                roadPresentationCount++;
            }
        }

        Assert.Equal(
            definition.RoadEdges.Count -
            definition.Crossings.Count,
            roadPresentationCount);

        InfrastructurePresentationIdentity northBridge =
            simulation.Entities.GetComponent<InfrastructurePresentationIdentity>(
                runtime.CrossingEntities[
                    "crossing.north_bridge"]);
        InfrastructurePresentationIdentity southFord =
            simulation.Entities.GetComponent<InfrastructurePresentationIdentity>(
                runtime.CrossingEntities[
                    "crossing.south_ford"]);

        Assert.Equal(
            InfrastructurePresentationKind.RoadBridge,
            northBridge.Kind);
        Assert.Equal(
            InfrastructurePresentationKind.Ford,
            southFord.Kind);
    }

    private static AxisAlignedBounds SmallFootprint(
        Vector3 position) =>
        new(
            new Vector3(
                position.X - 2.0f,
                -64.0f,
                position.Z - 2.0f),
            new Vector3(
                position.X + 2.0f,
                128.0f,
                position.Z + 2.0f));

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
