using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Combat;
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
