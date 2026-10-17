using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
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

    private sealed class Fixture : ISimulationSystem
    {
        private Action<SimulationContext>? _action;
        public Fixture()
        {
            Simulation.RegisterSystem(this);
            Building = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Building, WorldTransform.Identity);
            Simulation.Entities.AddComponent(Building, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
            Simulation.Entities.AddComponent(Building, new BuildingLoadingDock(new(0, 0, 14), Vector3.UnitX, 18));
            Truck = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Truck, new WorldTransform(new(0, 0, 14), Quaternion.Identity, new(3.2f, 2.4f, 6.4f)));
            Simulation.Entities.AddComponent(Truck, GroundMovementState.Stationary(MathF.PI / 2));
            OtherTruck = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(OtherTruck, new WorldTransform(new(100, 0, 100), Quaternion.Identity, new(3.2f, 2.4f, 6.4f)));
        }
        public SimulationCoordinator Simulation { get; } = new();
        public VehicleDockingService Docks { get; } = new();
        public EntityId Truck { get; }
        public EntityId OtherTruck { get; }
        public EntityId Building { get; }
        public SimulationPhase Phase => SimulationPhase.Logistics;
        public void Execute(SimulationContext context) => _action!(context);
        public void Step(Action<SimulationContext> action) { _action = action; Simulation.AdvanceOneTick(); }
        public void SetTruck(Vector3 position, float heading, float speed)
        {
            Simulation.Entities.SetComponent(Truck, Simulation.Entities.GetComponent<WorldTransform>(Truck) with { Position = position });
            Simulation.Entities.SetComponent(Truck, GroundMovementState.Stationary(heading) with { Velocity = new(speed, 0, 0) });
        }
    }
}
