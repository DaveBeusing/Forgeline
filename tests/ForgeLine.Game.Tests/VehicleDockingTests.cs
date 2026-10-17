using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Combat;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleDockingTests
{
    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void BuildingRotationPreservesLongitudinalFacadeLane(BuildingOrientation orientation)
    {
        var building = new WorldTransform(new(30, 0, 40), BuildingFootprint.GetRotation(orientation), Vector3.One);
        var truck = new WorldTransform(Vector3.Zero, Quaternion.Identity, new(3.2f, 2.4f, 6.4f));
        Assert.True(VehicleDockPose.TryCreate(new(new(0, 0, 14), Vector3.UnitX, 18), building, truck, out var pose));
        var actual = Vector3.Normalize(pose.Exit - pose.Entry);
        var expected = Vector3.Transform(Vector3.UnitX, building.Rotation);
        Assert.True(Vector3.Distance(actual, expected) < 0.0001f);
        Assert.True(Vector3.Distance((pose.Entry + pose.Exit) * 0.5f, pose.Service) < 0.0001f);
        Assert.Equal(3.2f, pose.HalfLength);
    }

    [Fact]
    public void InvalidGeometryAndNonfiniteVehicleDimensionsAreRejected()
    {
        var truck = WorldTransform.Identity with { Scale = new(float.NaN, 2, 6) };
        Assert.False(VehicleDockPose.TryCreate(new(new(0, 0, 14), Vector3.UnitX, 18), WorldTransform.Identity, truck, out _));
        Assert.False(new BuildingLoadingDock(new(0, 0, 14), Vector3.Zero, 18).IsValid);
        Assert.False(new BuildingLoadingDock(new(float.NaN, 0, 14), Vector3.UnitX, 18).IsValid);
    }

    [Theory]
    [InlineData(0.3f, 0, 0)]
    [InlineData(0, 0.2f, 0)]
    [InlineData(0, 0, 1)]
    public void RadiusHeadingOrMotionCannotAuthorizeService(float positionError, float headingError, float speed)
    {
        var fixture = new Fixture();
        fixture.SetTruck(new(positionError, 0, 14), MathF.PI / 2 + headingError, speed);
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out _));
            Assert.False(fixture.Docks.ObservePose(context, fixture.Truck, out _));
        });
    }

    [Fact]
    public void ExclusiveLaneRemainsOwnedUntilPhysicalExitAndRejectsStaleRelease()
    {
        var fixture = new Fixture();
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
            Assert.False(fixture.Docks.TryReserve(context, fixture.OtherTruck, fixture.Building, new(1), out var busy));
            Assert.Equal(VehicleDockStatus.Busy, busy.Status);
            Assert.False(fixture.Docks.TryRelease(context.Entities, fixture.Truck, fixture.Building, visit.Generation));
            fixture.SetTruck(new(100, 0, 100), MathF.PI / 2, 0);
            Assert.False(fixture.Docks.TryRelease(context.Entities, fixture.Truck, fixture.Building, visit.Generation + 1));
            Assert.True(fixture.Docks.TryRelease(context.Entities, fixture.Truck, fixture.Building, visit.Generation));
        });
    }

    [Fact]
    public void WrongOwnerCannotReserve()
    {
        var fixture = new Fixture();
        fixture.Step(context => Assert.False(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(2), out _)));
    }

    [Fact]
    public void LocalApproachAndExitUseContinuousMovementAndKeepTheLaneUntilClear()
    {
        var fixture = new Fixture();
        fixture.Simulation.RegisterSystem(new GroundMovementSystem());
        var movement = GroundMovement.CreateDefault();
        fixture.Simulation.Entities.AddComponent(fixture.Truck, movement);
        fixture.SetTruck(new(-45, movement.HeightOffset, 14), MathF.PI / 2, 0);
        bool docked = false;
        for (int i = 0; i < 2000 && !docked; i++)
        {
            var before = fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Truck).Position;
            fixture.Step(context => docked = fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1)));
            var after = fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Truck).Position;
            Vector3 delta = after - before;
            Assert.True(delta.X * delta.X + delta.Z * delta.Z <= MathF.Pow(movement.MaximumSpeed / 20 + 0.001f, 2));
        }
        Assert.True(docked);
        Assert.True(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        bool exited = false;
        for (int i = 0; i < 2000 && !exited; i++) fixture.Step(context => exited = fixture.Docks.Exit(context, fixture.Truck));
        Assert.True(exited);
        Assert.False(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
    }

    [Fact]
    public void SettlingNeedsDistinctTicksAndTransferConsumesPositiveTime()
    {
        var fixture = new Fixture();
        ulong generation = 0;
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
            generation = visit.Generation;
            Assert.False(fixture.Docks.ObservePose(context, fixture.Truck, out _));
            Assert.False(fixture.Docks.ObservePose(context, fixture.Truck, out _));
        });
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.ObservePose(context, fixture.Truck, out _));
            Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 40));
        });
        for (int i = 0; i < 19; i++) fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 40)));
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 40));
            VehicleDockingService.RecordTransfer(context, fixture.Truck, generation);
            Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 40));
        });
    }

    [Fact]
    public void CancelledVisitDoesNotFreePhysicalOccupancyOrPermitAStaleTransfer()
    {
        var fixture = new Fixture();
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
            VehicleDockingService.Cancel(context.Entities, fixture.Truck);
            fixture.Docks.Cleanup(context.Entities);
            Assert.True(context.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
            Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, visit.Generation, 20));
            Assert.False(fixture.Docks.TryReserve(context, fixture.OtherTruck, fixture.Building, new(1), out _));
            fixture.SetTruck(new(100, 0, 100), 0, 0);
            fixture.Docks.Cleanup(context.Entities);
            Assert.False(context.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        });
    }

    [Fact]
    public void RepeatedVisitsKeepMonotonicIdentityAndNoOrphanSlot()
    {
        var fixture = new Fixture();
        ulong previous = 0;
        for (int cycle = 0; cycle < 100; cycle++)
        {
            fixture.SetTruck(new(0, 0, 14), MathF.PI / 2, 0);
            fixture.Step(context =>
            {
                Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
                Assert.True(visit.Generation > previous);
                previous = visit.Generation;
                fixture.SetTruck(new(100, 0, 100), 0, 0);
                Assert.True(fixture.Docks.TryRelease(context.Entities, fixture.Truck, fixture.Building, visit.Generation));
                Assert.False(context.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
                Assert.False(context.Entities.HasComponent<VehicleDockVisit>(fixture.Truck));
            });
        }
    }

    [Fact]
    public void ADepartedOwnerCannotBlockItsOwnReleaseThroughTheSpatialIndex()
    {
        var spatial = new SpatialGridIndex();
        var fixture = new Fixture(new VehicleDockingService(spatial: spatial));
        fixture.Simulation.Entities.AddComponent(fixture.Truck,
            new SpatialPresence(new(1.6f, 1.2f, 3.2f), new(1, 0, SpatialMobility.Mobile)));
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
            fixture.SetTruck(new(45, 0, 14), MathF.PI / 2, 0);
            spatial.Upsert(context.Entities.GetComponent<SpatialPresence>(fixture.Truck).CreateEntry(fixture.Truck,
                context.Entities.GetComponent<WorldTransform>(fixture.Truck)));
            Assert.True(fixture.Docks.TryRelease(context.Entities, fixture.Truck, fixture.Building, visit.Generation));
        });
    }

    [Fact]
    public void PoseLossDuringServiceRequiresNewSettlingAndServiceTime()
    {
        var fixture = new Fixture();
        ulong generation = 0;
        fixture.Step(context => { Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit)); generation = visit.Generation; fixture.Docks.ObservePose(context, fixture.Truck, out _); });
        fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 20)));
        fixture.SetTruck(new(1, 0, 14), MathF.PI / 2, 0);
        fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 20)));
        fixture.SetTruck(new(0, 0, 14), MathF.PI / 2, 0);
        fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 20)));
        fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 20)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DepletedHealthBlocksServiceBeforeEntityRemoval(bool destroyTruck)
    {
        var fixture = new Fixture();
        ulong generation = 0;
        fixture.Step(context => { Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit)); generation = visit.Generation; });
        fixture.Simulation.Entities.AddComponent(destroyTruck ? fixture.Truck : fixture.Building, new HealthState(0, 100));
        fixture.Step(context => Assert.False(fixture.Docks.CanTransfer(context, fixture.Truck, generation, 20)));
    }

    [Fact]
    public void SettlingAtServiceNeverOrdersADetourToEntry()
    {
        var fixture = new Fixture();
        fixture.Simulation.Entities.AddComponent(fixture.Truck, GroundMovement.CreateDefault());
        fixture.Step(context =>
        {
            Assert.False(fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1)));
            var order = context.Entities.GetComponent<MovementOrder>(fixture.Truck);
            Assert.Equal(new Vector3(0, 0, 14), order.WorldTarget);
            Assert.Equal(MovementOrderKind.LocalApproach, order.Kind);
        });
        fixture.Step(context => Assert.True(fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1))));
    }

    [Fact]
    public void PhysicalObstructionPreventsReservationAndNeverTimesOut()
    {
        var spatial = new SpatialGridIndex();
        var fixture = new Fixture(new VehicleDockingService(spatial: spatial));
        var blocker = fixture.Simulation.Entities.CreateEntity();
        var transform = new WorldTransform(new(5, 0, 14), Quaternion.Identity, Vector3.One);
        var presence = new SpatialPresence(new(2, 2, 2), new(1, 0, SpatialMobility.Static));
        fixture.Simulation.Entities.AddComponent(blocker, transform);
        fixture.Simulation.Entities.AddComponent(blocker, presence);
        spatial.Upsert(presence.CreateEntry(blocker, transform));
        for (int tick = 0; tick < 120; tick++)
            fixture.Step(context =>
            {
                Assert.False(fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1)));
                Assert.False(context.Entities.HasComponent<MovementOrder>(fixture.Truck));
                Assert.False(context.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
            });
        fixture.Simulation.Entities.SetComponent(blocker, transform with { Position = new(100, 0, 100) });
        spatial.Upsert(presence.CreateEntry(blocker, fixture.Simulation.Entities.GetComponent<WorldTransform>(blocker)));
        fixture.Step(context => Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out _)));
    }

    [Fact]
    public void MapEdgeRejectsAnOtherwiseValidServicePoint()
    {
        var fixture = new Fixture(new VehicleDockingService(terrain: new BoundedTerrain()));
        fixture.Step(context =>
        {
            Assert.False(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out _));
            Assert.False(context.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        });
    }

    private sealed class BoundedTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-10, -10, -10), new(20, 10, 30));
        public bool TrySampleHeight(float worldX, float worldZ, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }

    [Fact]
    public void ArrivalToleranceStillCompletesTheRequestedBodyHeading()
    {
        var fixture = new Fixture();
        var movement = GroundMovement.CreateDefault();
        fixture.Simulation.Entities.AddComponent(fixture.Truck, movement);
        fixture.SetTruck(new(0.105f, movement.HeightOffset, 14), 0, 0);
        fixture.Simulation.Entities.AddComponent(fixture.Truck,
            new MovementOrder(new(1), new(0, movement.HeightOffset, 14), SimulationTick.Zero, new(1), MovementOrderKind.LocalApproach, MathF.PI / 2));
        fixture.Simulation.RegisterSystem(new GroundMovementSystem());
        for (int tick = 0; tick < 60; tick++) fixture.Step(_ => { });
        var state = fixture.Simulation.Entities.GetComponent<GroundMovementState>(fixture.Truck);
        Assert.True(MathF.Abs(state.HeadingRadians - MathF.PI / 2) <= MathF.PI / 90);
        Assert.False(fixture.Simulation.Entities.HasComponent<MovementOrder>(fixture.Truck));
        Assert.True(Vector3.Distance(fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Truck).Position,
            new Vector3(0, movement.HeightOffset, 14)) <= 0.11f);
    }

    [Fact]
    public void CoarseBlockedEntryUsesAFreeApproachWithoutMovingTheAuthoredDock()
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 256, HeightSamplesPerSide = 33 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(0, 0),
            new TerrainHeightfield(33, 256, new float[33 * 33]))]);
        var buildingBounds = new AxisAlignedBounds(new(54, -1, 54), new(74, 8, 74));
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(NavigationWorld.Build(terrain,
            [buildingBounds], new NavigationGridSettings { CellSizeMeters = 32 },
            new NavigationSectorSettings { SectorSizeCells = 4 })));
        var spatial = new SpatialGridIndex();
        var fixture = new Fixture(new VehicleDockingService(terrain, spatial, navigation));
        var transform = new WorldTransform(new(64, 0, 64), Quaternion.Identity, Vector3.One);
        fixture.Simulation.Entities.SetComponent(fixture.Building, transform);
        var presence = new SpatialPresence(new(10, 4, 10), new(1, 0, SpatialMobility.Static));
        fixture.Simulation.Entities.AddComponent(fixture.Building, presence);
        spatial.Upsert(presence.CreateEntry(fixture.Building, transform));
        var movement = GroundMovement.CreateDefault();
        fixture.Simulation.Entities.AddComponent(fixture.Truck, movement);
        fixture.Simulation.Entities.AddComponent(fixture.Truck, new NavigationAgent(NavigationMovementClass.Wheeled));
        fixture.SetTruck(new(120, movement.HeightOffset, 120), 0, 0);
        fixture.Simulation.RegisterSystem(navigation);
        fixture.Simulation.RegisterSystem(new GroundMovementSystem(terrain, spatial));
        VehicleDockPose reserved = default;
        fixture.Step(context =>
        {
            Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out var visit));
            reserved = visit.Pose;
            Assert.NotNull(reserved.ApproachStart);
            Assert.Equal(new Vector3(64, movement.HeightOffset, 78), reserved.Service);
            Assert.True(MathF.Abs(reserved.Entry.X - 42.8f) < 0.001f);
        });
        bool docked = false;
        for (int tick = 0; tick < 2000 && !docked; tick++)
            fixture.Step(context => docked = fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1)));
        Assert.True(docked, fixture.Simulation.Entities.GetComponent<VehicleDockVisit>(fixture.Truck).ToString());
        Assert.Equal(reserved, fixture.Simulation.Entities.GetComponent<VehicleDockVisit>(fixture.Truck).Pose);
    }

    [Fact]
    public void AuthoredServicePointsMatchTheLoadingSocketsAndRealTruckLengths()
    {
        var buildings = InitialBuildingDefinitions.CreateCatalog();
        var units = DirectorateContent.CreateUnitCatalog();
        var depot = buildings[BuildingIds.SupplyDepot];
        var supplyDock = Assert.IsType<BuildingLoadingDock>(depot.LoadingDock);
        foreach (var truck in new[] { UnitIds.CargoTruck, UnitIds.SupplyTruck })
        {
            float halfLength = units[truck].VisualScale.Z * 0.5f;
            Assert.True(MathF.Abs(-0.14f * depot.Footprint.Width - supplyDock.ServicePosition.X) <= halfLength + 0.001f);
            Assert.True(MathF.Abs(0.26f * depot.Footprint.Width - supplyDock.ServicePosition.X) <= halfLength + 0.001f);
        }
        var ammunition = buildings[BuildingIds.AmmunitionPlant];
        Assert.Equal(-0.25f * ammunition.Footprint.Width,
            Assert.IsType<BuildingLoadingDock>(ammunition.LoadingDock).ServicePosition.X);
    }

    [Fact]
    public void DistantArrivalTravelsOutsideTheLaneBeforeClaimingTheBay()
    {
        var fixture = new Fixture();
        fixture.Simulation.Entities.AddComponent(fixture.Truck, GroundMovement.CreateDefault());
        fixture.SetTruck(new(100, 0, 100), 0, 0);
        fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1))));
        Assert.False(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        var staged = fixture.Simulation.Entities.GetComponent<VehicleDockVisit>(fixture.Truck);
        Assert.Equal(0UL, staged.Generation);
        var move = fixture.Simulation.Entities.GetComponent<MovementOrder>(fixture.Truck);
        Assert.False((staged.Pose with { ApproachStart = null }).Contains(move.WorldTarget, 0));
        fixture.SetTruck(move.WorldTarget, 0, 0);
        fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.Truck, fixture.Building, new(1))));
        Assert.Equal(fixture.Truck, fixture.Simulation.Entities.GetComponent<VehicleDockSlot>(fixture.Building).Truck);
    }

    [Fact]
    public void SecondArrivalStopsItsOutsideApproachWhenAnotherTruckClaimsTheBay()
    {
        var fixture = new Fixture();
        fixture.Simulation.RegisterSystem(new GroundMovementSystem());
        var entities = fixture.Simulation.Entities;
        entities.AddComponent(fixture.OtherTruck, GroundMovement.CreateDefault());
        entities.AddComponent(fixture.OtherTruck, GroundMovementState.Stationary(0));
        entities.SetComponent(fixture.OtherTruck, entities.GetComponent<WorldTransform>(fixture.OtherTruck) with { Position = new(100, 0, 14) });
        fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.OtherTruck, fixture.Building, new(1))));
        var move = entities.GetComponent<MovementOrder>(fixture.OtherTruck);
        fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.OtherTruck, fixture.Building, new(1))));
        Assert.Equal(move, entities.GetComponent<MovementOrder>(fixture.OtherTruck));
        fixture.Step(context => Assert.True(fixture.Docks.TryReserve(context, fixture.Truck, fixture.Building, new(1), out _)));
        for (int tick = 0; tick < 200; tick++)
            fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.OtherTruck, fixture.Building, new(1))));
        Assert.Equal(VehicleDockStatus.Busy, entities.GetComponent<VehicleDockVisit>(fixture.OtherTruck).Status);
        Assert.Equal(GroundMovementStatus.TacticallyPaused, entities.GetComponent<GroundMovementState>(fixture.OtherTruck).Status);
        Vector3 waiting = entities.GetComponent<WorldTransform>(fixture.OtherTruck).Position;
        var slot = entities.GetComponent<VehicleDockSlot>(fixture.Building);
        Assert.True(waiting.X > slot.Pose.Exit.X + slot.Pose.HalfLength * 4 + 3);
        for (int tick = 0; tick < 120; tick++)
            fixture.Step(context => Assert.False(fixture.Docks.Approach(context, fixture.OtherTruck, fixture.Building, new(1))));
        Assert.Equal(waiting, entities.GetComponent<WorldTransform>(fixture.OtherTruck).Position);
        Assert.Equal(move, entities.GetComponent<MovementOrder>(fixture.OtherTruck));
        Assert.Equal(fixture.Truck, entities.GetComponent<VehicleDockSlot>(fixture.Building).Truck);
    }

    [Fact]
    public void SweptApproachUsesRoundedCornersWithoutAcceptingARealIntersection()
    {
        var pose = new VehicleDockPose(Vector3.Zero, new(10, 0, 0), new(20, 0, 0), MathF.PI / 2, 3.2f, 1.6f, new(-16, 0, 0));
        Assert.False(pose.IntersectsLane(new AxisAlignedBounds(new(-18.5f, -1, -4.8f), new(-18, 1, -4.3f))));
        Assert.True(pose.IntersectsLane(new AxisAlignedBounds(new(-18.5f, -1, -4.3f), new(-18, 1, -3.8f))));
    }

    private sealed class Fixture : ISimulationSystem
    {
        private Action<SimulationContext>? _action;
        public Fixture(VehicleDockingService? docking = null)
        {
            Docks = docking ?? new VehicleDockingService();
            Simulation.RegisterSystem(this);
            Building = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Building, WorldTransform.Identity);
            Simulation.Entities.AddComponent(Building, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
            Simulation.Entities.AddComponent(Building, new BuildingLoadingDock(new(0, 0, 14), Vector3.UnitX, 18));
            Truck = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Truck, new WorldTransform(new(0, 0, 14), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(3.2f, 2.4f, 6.4f)));
            Simulation.Entities.AddComponent(Truck, GroundMovementState.Stationary(MathF.PI / 2));
            OtherTruck = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(OtherTruck, new WorldTransform(new(100, 0, 100), Quaternion.Identity, new(3.2f, 2.4f, 6.4f)));
        }
        public SimulationCoordinator Simulation { get; } = new();
        public VehicleDockingService Docks { get; }
        public EntityId Truck { get; }
        public EntityId OtherTruck { get; }
        public EntityId Building { get; }
        public SimulationPhase Phase => SimulationPhase.Logistics;
        public void Execute(SimulationContext context) => _action!(context);
        public void Step(Action<SimulationContext> action) { _action = action; Simulation.AdvanceOneTick(); }
        public void SetTruck(Vector3 position, float heading, float speed)
        {
            Simulation.Entities.SetComponent(Truck, Simulation.Entities.GetComponent<WorldTransform>(Truck) with { Position = position, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading) });
            Simulation.Entities.SetComponent(Truck, GroundMovementState.Stationary(heading) with { Velocity = new(speed, 0, 0) });
        }
    }
}
