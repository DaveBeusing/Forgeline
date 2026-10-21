using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleQueuePhysicalTests
{
    [Theory]
    [InlineData(4u)]
    [InlineData(10u)]
    [InlineData(8u)]
    public void LogisticsFamiliesServiceTwoThroughTenTrucksAcrossEveryCardinalOrientation(uint family)
    {
        foreach (int count in new[] { 2, 10 })
        for (int orientation = 0; orientation < 4; orientation++)
        {
            var fixture = new Traffic(count, false, new BuildingId(family), orientation);
            for (int tick = 0; tick < 25_000 && fixture.Completed.Count < count; tick++)
                fixture.Simulation.AdvanceOneTick();
            Assert.Equal(fixture.Trucks, fixture.Completed);
            Assert.False(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(8, false)]
    [InlineData(20, false)]
    [InlineData(3, true)]
    [InlineData(8, true)]
    public void PhysicalLineServicesEveryParticipantInConfirmedOrderWithoutPositionJumps(int count, bool opposingApproaches)
    {
        var fixture = new Traffic(count, opposingApproaches);
        for (int tick = 0; tick < 25_000 && fixture.Completed.Count < count; tick++)
        {
            Vector3[] before = fixture.Trucks.Select(id => fixture.Simulation.Entities.GetComponent<WorldTransform>(id).Position).ToArray();
            fixture.Simulation.AdvanceOneTick();
            for (int index = 0; index < count; index++)
            {
                var after = fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Trucks[index]).Position;
                Assert.True(Vector3.Distance(before[index], after) <= .651f, $"Position jump: {before[index]} -> {after}");
                for (int other = index + 1; other < count; other++)
                    Assert.False(BodiesOverlap(
                        fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Trucks[index]),
                        fixture.Simulation.Entities.GetComponent<WorldTransform>(fixture.Trucks[other])),
                        $"Truck bodies overlap: {index}/{other} at tick {tick}");
            }
            var queued = fixture.Trucks.Where(id => fixture.Simulation.Entities.HasComponent<VehicleQueueEntry>(id))
                .OrderBy(id => fixture.Simulation.Entities.GetComponent<VehicleQueueEntry>(id).Position).ToArray();
            for (int index = 1; index < queued.Length; index++)
            {
                var first = fixture.Simulation.Entities.GetComponent<VehicleQueueEntry>(queued[index - 1]);
                var second = fixture.Simulation.Entities.GetComponent<VehicleQueueEntry>(queued[index]);
                float firstLength = fixture.Simulation.Entities.GetComponent<WorldTransform>(queued[index - 1]).Scale.Z;
                float secondLength = fixture.Simulation.Entities.GetComponent<WorldTransform>(queued[index]).Scale.Z;
                if (!first.ArrivedAtService && !second.ArrivedAtService)
                    Assert.True(Vector3.Distance(first.Target, second.Target) >= (firstLength + secondLength) * .5f + 5.79f);
            }
        }
        Assert.True(fixture.Completed.Count == count, string.Join("; ", fixture.Trucks.Select(id =>
            $"{id} pos={fixture.Simulation.Entities.GetComponent<WorldTransform>(id).Position} " +
            $"motion={fixture.Simulation.Entities.GetComponent<GroundMovementState>(id)} " +
            (fixture.Simulation.Entities.TryGetComponent(id, out VehicleDockVisit visit) ? $"dock={visit}" : "") +
            (fixture.Simulation.Entities.TryGetComponent(id, out VehicleQueueEntry entry) ? $"queue={entry}" : ""))));
        Assert.Equal(fixture.Trucks, fixture.Completed);
        Assert.False(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        foreach (var truck in fixture.Trucks) Assert.False(fixture.Simulation.Entities.HasComponent<VehicleQueueEntry>(truck));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(7, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public void FrontMiddleAndTailInterruptionsPreserveRemainingPhysicalService(int index, bool destroyed)
    {
        var fixture = new Traffic(8, false);
        fixture.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        EntityId removed = fixture.Trucks[index];
        fixture.Ignored.Add(removed);
        if (destroyed) Assert.True(fixture.Simulation.Entities.DestroyEntity(removed));
        else
        {
            VehicleDockingService.Cancel(fixture.Simulation.Entities, removed);
            fixture.Simulation.Entities.AddComponent(removed, new MovementOrder(new(1), new(900, 1, 100),
                fixture.Simulation.CurrentTick, fixture.Simulation.CurrentTick));
        }
        for (int tick = 0; tick < 25_000 && fixture.Completed.Count < 7; tick++) fixture.Simulation.AdvanceOneTick();
        Assert.Equal(fixture.Trucks.Where(id => id != removed), fixture.Completed);
        Assert.False(fixture.Simulation.Entities.HasComponent<VehicleDockSlot>(fixture.Building));
        foreach (EntityId truck in fixture.Trucks) Assert.False(fixture.Simulation.Entities.HasComponent<VehicleQueueEntry>(truck));
    }

    [Fact]
    public void RoadCorridorTruncatesTheFiniteRowInsteadOfInventingWaitingPlaces()
    {
        var fixture = new Traffic(20, false);
        EntityId road = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(road, new RoadTrafficCorridor(new(500, 0, 0), new(500, 0, 1024), 6));
        fixture.Simulation.AdvanceOneTick();
        var lane = fixture.Simulation.Entities.GetComponent<VehicleQueueLane>(fixture.Building);
        Assert.InRange(lane.Capacity, 1, 19);
        int registered = 0;
        foreach (EntityId truck in fixture.Trucks)
            if (fixture.Simulation.Entities.TryGetComponent(truck, out VehicleQueueEntry entry))
            {
                registered++;
                Assert.True(MathF.Abs(entry.Target.X - 500) > 6 + 3.4f);
            }
        Assert.InRange(registered, 1, lane.Capacity);
        Assert.Contains(fixture.Trucks, truck => fixture.Simulation.Entities.HasComponent<VehicleQueueHolding>(truck));
    }

    private static bool BodiesOverlap(in WorldTransform first, in WorldTransform second)
    {
        Vector3 aRight = Vector3.Transform(Vector3.UnitX, first.Rotation), aForward = Vector3.Transform(Vector3.UnitZ, first.Rotation);
        Vector3 bRight = Vector3.Transform(Vector3.UnitX, second.Rotation), bForward = Vector3.Transform(Vector3.UnitZ, second.Rotation);
        Vector3 delta = second.Position - first.Position; delta.Y = 0;
        foreach (var axis in new[] { aRight, aForward, bRight, bForward })
        {
            float a = MathF.Abs(Vector3.Dot(axis, aRight)) * first.Scale.X * .5f + MathF.Abs(Vector3.Dot(axis, aForward)) * first.Scale.Z * .5f;
            float b = MathF.Abs(Vector3.Dot(axis, bRight)) * second.Scale.X * .5f + MathF.Abs(Vector3.Dot(axis, bForward)) * second.Scale.Z * .5f;
            if (MathF.Abs(Vector3.Dot(delta, axis)) >= a + b - .001f) return false;
        }
        return true;
    }

    private sealed class Traffic : ISimulationSystem
    {
        private readonly VehicleDockingService _docks;
        private readonly SpatialGridIndex _spatial = new();
        private readonly SpatialIndexSynchronizer _synchronizer;
        private readonly HashSet<EntityId> _departing = new();
        private readonly Quaternion _rotation;
        public SimulationCoordinator Simulation { get; } = new(ticksPerSecond: 20);
        public EntityId Building { get; }
        public EntityId[] Trucks { get; }
        public List<EntityId> Completed { get; } = new();
        public HashSet<EntityId> Ignored { get; } = new();
        public SimulationPhase Phase => SimulationPhase.Logistics;

        public Traffic(int count, bool opposing, BuildingId? family = null, int quarterTurns = 0)
        {
            _rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, quarterTurns * MathF.PI / 2);
            _synchronizer = new(_spatial);
            var terrain = new TerrainWorld(new WorldGridSettings { ChunkSizeMeters = 1024, HeightSamplesPerSide = 3 },
                [new TerrainChunk(new(0, 0), new TerrainHeightfield(3, 1024, new float[9]))]);
            _docks = new(terrain, _spatial);
            Building = Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(Building, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
            Simulation.Entities.AddComponent(Building, new WorldTransform(new(600, 0, 512), Quaternion.Identity, new(20, 8, 20)));
            Simulation.Entities.AddComponent(Building, new BuildingLoadingDock(new(0, 0, 14.5f), Vector3.UnitX, 12));
            Simulation.Entities.AddComponent(Building, new SpatialPresence(new(10, 4, 10), new(1, 2, SpatialMobility.Static)));
            float lane = 14.5f;
            if (family is BuildingId id)
            {
                var building = DirectorateContent.CreateBuildingCatalog()[id];
                var size = new Vector3(building.Footprint.Width, building.Footprint.Height, building.Footprint.Depth);
                Simulation.Entities.AddComponent(Building, new CompletedBuilding(id, new(1), SimulationTick.Zero));
                Simulation.Entities.AddComponent(Building, new WorldTransform(new(600, size.Y * .5f, 512), _rotation, size));
                var dock = building.LoadingDock!.Value;
                Simulation.Entities.AddComponent(Building, dock);
                Simulation.Entities.AddComponent(Building, new SpatialPresence(size * .5f, new(1, 2, SpatialMobility.Static)));
                lane = dock.ServicePosition.Z;
            }
            Trucks = new EntityId[count];
            for (int index = 0; index < count; index++)
            {
                var truck = Simulation.Entities.CreateEntity();
                Trucks[index] = truck;
                bool supply = index % 2 == 1;
                Vector3 size = supply ? new(3.4f, 2.5f, 6.8f) : new(3.2f, 2.4f, 6.4f);
                float heading = (opposing && supply ? -MathF.PI / 2 : MathF.PI / 2) + quarterTurns * MathF.PI / 2;
                Simulation.Entities.AddComponent(truck, new WorldTransform(Rotate(new(560 - index * 14, 1, 512 + lane + (opposing ? supply ? 20 : -20 : 0))),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading), size));
                Simulation.Entities.AddComponent(truck, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
                Simulation.Entities.AddComponent(truck, new GroundMovement(13, 6.5f, 10, 2.35f, 2, 1, 5.8f, 8, 24, 1));
                Simulation.Entities.AddComponent(truck, GroundMovementState.Stationary(heading));
                Simulation.Entities.AddComponent(truck, new SpatialPresence(size * .5f, new(1, 4, SpatialMobility.Mobile)));
                if (supply) Simulation.Entities.AddComponent(truck, new SupplyLoadingOrder(Building, SimulationTick.Zero));
            }
            Simulation.RegisterSystem(new GroundMovementSystem(terrain, _spatial));
            Simulation.RegisterSystem(this);
        }

        public void Execute(SimulationContext context)
        {
            _synchronizer.CleanupLifecycle(context.Entities);
            _synchronizer.SynchronizeMovement(context.Entities);
            _docks.Cleanup(context.Entities);
            foreach (EntityId truck in Trucks.Reverse())
            {
                if (Completed.Contains(truck) || Ignored.Contains(truck)) continue;
                if (_departing.Contains(truck))
                {
                    if (_docks.Exit(context, truck))
                    {
                        Completed.Add(truck);
                        // Normal onward travel must clear the departure area for the remaining fleet.
                        context.Entities.AddComponent(truck, new MovementOrder(new(1), Rotate(new(900, 1, 80 + Completed.Count * 35)),
                            context.Tick, context.Tick));
                    }
                    continue;
                }
                if (!_docks.Approach(context, truck, Building, new(1))) continue;
                var visit = context.Entities.GetComponent<VehicleDockVisit>(truck);
                if (!_docks.CanTransfer(context, truck, visit.Generation, 20)) continue;
                VehicleDockingService.RecordTransfer(context, truck, visit.Generation);
                _departing.Add(truck);
            }
        }

        private Vector3 Rotate(Vector3 point) => new Vector3(600, 0, 512) +
            Vector3.Transform(point - new Vector3(600, 0, 512), _rotation);

    }
}
