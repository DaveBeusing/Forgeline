using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class FormationMovementSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void MultiUnitCommandCreatesOneMovementGroup()
    {
        TerrainWorld terrain = CreateFlatWorld(2, 2);
        var simulation = new SimulationCoordinator();
        EntityId[] units = CreateUnits(
            simulation,
            count: 4,
            start: new Vector3(8.0f, 0.5f, 8.0f));

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            units,
            new Vector3(48.0f, 0.0f, 48.0f),
            SimulationTick.Zero,
            FormationTemplate.Line);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));
        simulation.AdvanceOneTick();

        Assert.True(command.CreatedMovementGroup.IsValid);
        Assert.True(
            simulation.Entities.HasComponent<MovementGroupOrder>(
                command.CreatedMovementGroup));
        Assert.Equal(units.Length, command.AcceptedTargetCount);

        foreach (EntityId unit in units)
        {
            Assert.True(
                simulation.Entities.TryGetComponent(
                    unit,
                    out MovementGroupMember member));
            Assert.Equal(command.CreatedMovementGroup, member.Group);
            Assert.False(
                simulation.Entities.HasComponent<MovementOrder>(unit));
        }

        _ = terrain;
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void LargeSelectionUsesOneSharedStrategicRoute(int unitCount)
    {
        FormationScenario scenario = CreateScenario(
            unitCount,
            FormationTemplate.Compact);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        FormationMovementDiagnosticsSnapshot diagnostics =
            scenario.FormationSystem.LastDiagnostics;

        Assert.Equal(1UL, diagnostics.SharedPathRequestCount);
        Assert.Equal(1, diagnostics.ActiveGroupCount);
        Assert.Equal(unitCount, diagnostics.ActiveMemberCount);
        Assert.Equal(
            0UL,
            scenario.NavigationSystem.LastDiagnostics.QueuedPathCount);

        int assigned = 0;
        foreach (EntityId unit in scenario.Units)
        {
            Assert.True(
                scenario.Simulation.Entities.TryGetComponent(
                    unit,
                    out MovementGroupMember member));
            Assert.InRange(
                member.SlotIndex,
                0,
                unitCount - 1);
            Assert.True(
                scenario.Simulation.Entities.TryGetComponent(
                    unit,
                    out MovementOrder localOrder));
            Assert.Equal(
                MovementOrderKind.FormationLocal,
                localOrder.Kind);
            assigned++;
        }

        Assert.Equal(unitCount, assigned);
    }

    [Fact]
    public void LineFormationProducesStableLateralSlotOrdering()
    {
        FormationScenario scenario = CreateScenario(
            8,
            FormationTemplate.Line);
        scenario.FormationSystem.DebugCaptureEnabled = true;

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        FormationMovementDebugSnapshot snapshot =
            scenario.FormationSystem.CaptureDebugSnapshot();

        FormationMovementDebugGroup group =
            Assert.Single(snapshot.Groups);
        Assert.Equal(FormationTemplate.Line, group.Formation);
        Assert.Equal(8, snapshot.Slots.Count);

        Vector3 right =
            new(group.Forward.Z, 0.0f, -group.Forward.X);
        float minimumLateral = float.PositiveInfinity;
        float maximumLateral = float.NegativeInfinity;
        float minimumLongitudinal = float.PositiveInfinity;
        float maximumLongitudinal = float.NegativeInfinity;

        foreach (FormationMovementDebugSlot slot in snapshot.Slots)
        {
            Vector3 offset = slot.SlotTarget - group.ActiveWaypoint;
            float lateral = Vector3.Dot(offset, right);
            float longitudinal = Vector3.Dot(offset, group.Forward);
            minimumLateral = MathF.Min(minimumLateral, lateral);
            maximumLateral = MathF.Max(maximumLateral, lateral);
            minimumLongitudinal =
                MathF.Min(minimumLongitudinal, longitudinal);
            maximumLongitudinal =
                MathF.Max(maximumLongitudinal, longitudinal);
        }

        Assert.True(
            maximumLateral - minimumLateral >
            maximumLongitudinal - minimumLongitudinal);

        Dictionary<EntityId, int> firstAssignments =
            scenario.Units.ToDictionary(
                static entity => entity,
                entity =>
                    scenario.Simulation.Entities.GetComponent<
                        MovementGroupMember>(entity).SlotIndex);

        scenario.Simulation.AdvanceOneTick();

        foreach (EntityId unit in scenario.Units)
        {
            MovementGroupMember member =
                scenario.Simulation.Entities.GetComponent<
                    MovementGroupMember>(unit);
            Assert.Equal(
                firstAssignments[unit],
                member.SlotIndex);
        }
    }

    [Fact]
    public void DestroyedMemberIsRemovedWithoutBreakingGroup()
    {
        FormationScenario scenario = CreateScenario(
            12,
            FormationTemplate.Wedge);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        EntityId destroyed = scenario.Units[4];
        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(destroyed));

        scenario.Simulation.AdvanceOneTick();

        FormationMovementDiagnosticsSnapshot diagnostics =
            scenario.FormationSystem.LastDiagnostics;
        Assert.Equal(1, diagnostics.ActiveGroupCount);
        Assert.Equal(11, diagnostics.ActiveMemberCount);
        Assert.Equal(11, diagnostics.LargestGroupSize);
    }

    [Fact]
    public void ReplacementOrderCreatesFreshGroupAndRetiresOldGroup()
    {
        FormationScenario scenario = CreateScenario(
            16,
            FormationTemplate.Compact);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        EntityId oldGroup =
            scenario.InitialCommand.CreatedMovementGroup;

        var replacement = new MoveEntitiesCommand(
            LocalPlayer,
            scenario.Units,
            new Vector3(104.0f, 0.0f, 20.0f),
            scenario.Simulation.CurrentTick,
            FormationTemplate.Column);

        scenario.Simulation.SubmitCommand(
            replacement,
            scenario.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        Assert.True(replacement.CreatedMovementGroup.IsValid);
        Assert.NotEqual(
            oldGroup,
            replacement.CreatedMovementGroup);
        Assert.False(
            scenario.Simulation.Entities.IsAlive(oldGroup));
        Assert.True(
            scenario.FormationSystem.LastDiagnostics.SharedPathRequestCount >=
            2UL);

        foreach (EntityId unit in scenario.Units)
        {
            MovementGroupMember member =
                scenario.Simulation.Entities.GetComponent<
                    MovementGroupMember>(unit);
            Assert.Equal(
                replacement.CreatedMovementGroup,
                member.Group);
        }
    }

    [Fact]
    public void NarrowCorridorTriggersControlledSplitFallback()
    {
        TerrainWorld terrain = CreateFlatWorld(4, 2);
        var obstacles = new[]
        {
            new AxisAlignedBounds(
                new Vector3(36.0f, -1.0f, 0.0f),
                new Vector3(92.0f, 4.0f, 24.0f)),
            new AxisAlignedBounds(
                new Vector3(36.0f, -1.0f, 40.0f),
                new Vector3(92.0f, 4.0f, 64.0f))
        };

        NavigationWorld navigationWorld = NavigationWorld.Build(
            terrain,
            obstacles,
            new NavigationGridSettings
            {
                CellSizeMeters = 4.0f,
                StaticObstacleClearanceMeters = 0.0f
            },
            new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var pathfinder = new HierarchicalPathfinder(navigationWorld);
        var formationSystem = new FormationMovementSystem(pathfinder);
        var navigationSystem = new HierarchicalNavigationSystem(pathfinder);
        var movementSystem = new GroundMovementSystem(terrain);

        simulation.RegisterSystem(formationSystem);
        simulation.RegisterSystem(navigationSystem);
        simulation.RegisterSystem(movementSystem);

        EntityId[] units = CreateUnits(
            simulation,
            24,
            new Vector3(8.0f, 0.5f, 32.0f));

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            units,
            new Vector3(116.0f, 0.0f, 32.0f),
            SimulationTick.Zero,
            FormationTemplate.Line);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));

        for (int tick = 0; tick < 220; tick++)
        {
            simulation.AdvanceOneTick();

            if (formationSystem.LastDiagnostics.SplitEventCount > 0)
            {
                break;
            }
        }

        Assert.True(
            formationSystem.LastDiagnostics.SplitEventCount > 0);
    }

    [Fact]
    public void NavigationVersionChangeRejectsStaleGroupRouteAndLocalTargets()
    {
        FormationScenario scenario = CreateScenario(
            10,
            FormationTemplate.Compact);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        EntityId group =
            scenario.InitialCommand.CreatedMovementGroup;
        Assert.True(
            scenario.Simulation.Entities.HasComponent<
                MovementGroupRoute>(group));

        TerrainWorld replacementTerrain =
            CreateFlatWorld(4, 4);
        NavigationWorld replacementWorld =
            NavigationWorld.Build(
                replacementTerrain,
                gridSettings: new NavigationGridSettings
                {
                    CellSizeMeters = 4.0f
                },
                sectorSettings: new NavigationSectorSettings
                {
                    SectorSizeCells = 4
                },
                version: new NavigationVersion(2));

        scenario.FormationSystem.UpdateWorld(replacementWorld);
        scenario.Simulation.AdvanceOneTick();

        Assert.False(
            scenario.Simulation.Entities.HasComponent<
                MovementGroupRoute>(group));
        Assert.True(
            scenario.Simulation.Entities.HasComponent<
                MovementGroupPendingPath>(group));

        foreach (EntityId unit in scenario.Units)
        {
            Assert.False(
                scenario.Simulation.Entities.HasComponent<
                    MovementOrder>(unit));
            Assert.False(
                scenario.Simulation.Entities.HasComponent<
                    FormationMovementConstraint>(unit));
        }

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            scenario.Simulation.Entities.TryGetComponent(
                group,
                out MovementGroupRoute replacementRoute));
        Assert.Equal(
            new NavigationVersion(2),
            replacementRoute.Path.Version);
    }

    [Fact]
    public void MultipleGroupsKeepIndependentSharedRoutes()
    {
        TerrainWorld terrain = CreateFlatWorld(4, 4);
        NavigationWorld navigationWorld = CreateNavigationWorld(terrain);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var pathfinder = new HierarchicalPathfinder(navigationWorld);
        var formationSystem = new FormationMovementSystem(pathfinder);
        var navigationSystem = new HierarchicalNavigationSystem(pathfinder);
        var movementSystem = new GroundMovementSystem(terrain);

        simulation.RegisterSystem(formationSystem);
        simulation.RegisterSystem(navigationSystem);
        simulation.RegisterSystem(movementSystem);

        EntityId[] first = CreateUnits(
            simulation,
            20,
            new Vector3(8.0f, 0.5f, 8.0f));
        EntityId[] second = CreateUnits(
            simulation,
            20,
            new Vector3(8.0f, 0.5f, 72.0f));

        simulation.SubmitCommand(
            new MoveEntitiesCommand(
                LocalPlayer,
                first,
                new Vector3(108.0f, 0.0f, 40.0f),
                SimulationTick.Zero,
                FormationTemplate.Wedge),
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));
        simulation.SubmitCommand(
            new MoveEntitiesCommand(
                LocalPlayer,
                second,
                new Vector3(108.0f, 0.0f, 88.0f),
                SimulationTick.Zero,
                FormationTemplate.Column),
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));

        simulation.AdvanceOneTick();
        simulation.AdvanceOneTick();
        simulation.AdvanceOneTick();

        Assert.Equal(
            2,
            formationSystem.LastDiagnostics.ActiveGroupCount);
        Assert.Equal(
            40,
            formationSystem.LastDiagnostics.ActiveMemberCount);
        Assert.Equal(
            2UL,
            formationSystem.LastDiagnostics.SharedPathRequestCount);
        Assert.Equal(
            0UL,
            navigationSystem.LastDiagnostics.QueuedPathCount);
    }

    private static FormationScenario CreateScenario(
        int unitCount,
        FormationTemplate formation)
    {
        TerrainWorld terrain = CreateFlatWorld(4, 4);
        NavigationWorld navigationWorld = CreateNavigationWorld(terrain);
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var pathfinder = new HierarchicalPathfinder(navigationWorld);
        var formationSystem = new FormationMovementSystem(pathfinder);
        var navigationSystem = new HierarchicalNavigationSystem(pathfinder);
        var movementSystem = new GroundMovementSystem(terrain);

        simulation.RegisterSystem(formationSystem);
        simulation.RegisterSystem(navigationSystem);
        simulation.RegisterSystem(movementSystem);

        EntityId[] units = CreateUnits(
            simulation,
            unitCount,
            new Vector3(8.0f, 0.5f, 8.0f));

        var command = new MoveEntitiesCommand(
            LocalPlayer,
            units,
            new Vector3(104.0f, 0.0f, 104.0f),
            SimulationTick.Zero,
            formation);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));

        return new FormationScenario(
            simulation,
            formationSystem,
            navigationSystem,
            units,
            command);
    }

    private static EntityId[] CreateUnits(
        SimulationCoordinator simulation,
        int count,
        Vector3 start)
    {
        int side = checked((int)MathF.Ceiling(MathF.Sqrt(count)));
        const float spacing = 3.0f;
        var units = new EntityId[count];

        for (int index = 0; index < count; index++)
        {
            int x = index % side;
            int z = index / side;
            Vector3 position = start +
                new Vector3(x * spacing, 0.0f, z * spacing);

            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                entity,
                new ControllableEntity(
                    LocalPlayer,
                    ControllableEntityCategory.Unit));
            simulation.Entities.AddComponent(
                entity,
                new GroundMovement(
                    maximumSpeed: 16.0f,
                    acceleration: 12.0f,
                    deceleration: 16.0f,
                    turnRateRadiansPerSecond: MathF.PI * 2.0f,
                    radius: 1.0f,
                    stopRadius: 0.5f,
                    separationRadius: 3.0f,
                    obstacleLookAhead: 0.0f,
                    maximumSlopeDegrees: 35.0f,
                    heightOffset: 0.5f));
            simulation.Entities.AddComponent(
                entity,
                GroundMovementState.Stationary());
            simulation.Entities.AddComponent(
                entity,
                new NavigationAgent(
                    NavigationMovementClass.Tracked));
            units[index] = entity;
        }

        return units;
    }

    private static NavigationWorld CreateNavigationWorld(
        TerrainWorld terrain) =>
        NavigationWorld.Build(
            terrain,
            gridSettings: new NavigationGridSettings
            {
                CellSizeMeters = 4.0f
            },
            sectorSettings: new NavigationSectorSettings
            {
                SectorSizeCells = 4
            });

    private static TerrainWorld CreateFlatWorld(
        int chunksX,
        int chunksZ)
    {
        var settings = new WorldGridSettings
        {
            ChunkSizeMeters = 32.0f,
            HeightSamplesPerSide = 9
        };

        var chunks = new List<TerrainChunk>();

        for (int z = 0; z < chunksZ; z++)
        {
            for (int x = 0; x < chunksX; x++)
            {
                int count =
                    settings.HeightSamplesPerSide *
                    settings.HeightSamplesPerSide;

                chunks.Add(
                    new TerrainChunk(
                        new ChunkCoordinate(x, z),
                        new TerrainHeightfield(
                            settings.HeightSamplesPerSide,
                            settings.ChunkSizeMeters,
                            new float[count])));
            }
        }

        return new TerrainWorld(settings, chunks);
    }

    private sealed record FormationScenario(
        SimulationCoordinator Simulation,
        FormationMovementSystem FormationSystem,
        HierarchicalNavigationSystem NavigationSystem,
        EntityId[] Units,
        MoveEntitiesCommand InitialCommand);
}
