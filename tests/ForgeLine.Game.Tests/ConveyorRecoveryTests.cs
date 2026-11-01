using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorRecoveryTests
{
    // These cases move at most a few thousand units in sub-unit transfers. This absolute
    // tolerance covers accumulated double rounding without treating positive cargo as zero.
    private const double QuantityTolerance = 0.00000001;

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(831UL, 7)]
    public void PowerLossAndBrownoutResumeWithoutAccumulatingOfflineThroughput(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        const double quantity = 500;
        fixture.Give(source, ResourceIds.FerrousOre, quantity);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) > 0);

        double[] before = carriers.Select(entity => fixture.Stock(entity, ResourceIds.FerrousOre)).ToArray();
        double[] activeSeconds = carriers.Select(entity => fixture.Flow(entity).ActiveSeconds).ToArray();
        double delivered = fixture.Stock(destination, ResourceIds.FerrousOre);
        foreach (EntityId carrier in carriers) fixture.SetPower(carrier, 0);
        fixture.Step(31);

        for (int index = 0; index < carriers.Length; index++)
        {
            AssertQuantity(before[index], fixture.Stock(carriers[index], ResourceIds.FerrousOre));
            Assert.Equal(activeSeconds[index], fixture.Flow(carriers[index]).ActiveSeconds);
            Assert.Equal(ConveyorLinkStatus.Offline, fixture.Flow(carriers[index]).Status);
        }
        AssertQuantity(delivered, fixture.Stock(destination, ResourceIds.FerrousOre));

        foreach (EntityId carrier in carriers) fixture.SetPower(carrier, 0.25);
        fixture.Step();
        Assert.InRange(fixture.Stock(destination, ResourceIds.FerrousOre) - delivered,
            double.Epsilon, ConveyorSimulationSystem.NominalThroughput * 0.25 * fixture.TickSeconds + QuantityTolerance);
        Assert.All(carriers, carrier =>
        {
            Assert.InRange(fixture.Flow(carrier).InputPerSecond, 0, 25 + QuantityTolerance);
            Assert.InRange(fixture.Flow(carrier).OutputPerSecond, 0, 25 + QuantityTolerance);
        });

        delivered = fixture.Stock(destination, ResourceIds.FerrousOre);
        foreach (EntityId carrier in carriers) fixture.SetPower(carrier, 1);
        fixture.Step();
        Assert.InRange(fixture.Stock(destination, ResourceIds.FerrousOre) - delivered,
            0, ConveyorSimulationSystem.NominalThroughput * fixture.TickSeconds + QuantityTolerance);
        fixture.RunUntil(() => Math.Abs(fixture.Stock(destination, ResourceIds.FerrousOre) - quantity) < QuantityTolerance);
        AssertQuantity(quantity, fixture.Total(ResourceIds.FerrousOre));
        Assert.All(carriers, carrier => AssertQuantity(0, fixture.Stock(carrier, ResourceIds.FerrousOre)));
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(831UL, 7)]
    public void FullDestinationBacksUpOnlyToPhysicalCapacityAndDrainsAfterSpaceReturns(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId source = fixture.AddStorage(new(41, 0, 80), capacity: 3_000);
        EntityId destination = fixture.AddStorage(new(103, 0, 80), capacity: 20);
        EntityId[] carriers = fixture.Connect(source, destination);
        double capacity = carriers.Sum(entity => fixture.Flow(entity).Capacity);
        double supplied = capacity + 100;
        fixture.Give(source, ResourceIds.FerrousOre, supplied);
        fixture.Give(destination, ResourceIds.FerrousOre, 20);
        fixture.RunUntil(() => carriers.All(entity =>
            Math.Abs(fixture.Stock(entity, ResourceIds.FerrousOre) - fixture.Flow(entity).Capacity) < QuantityTolerance));
        fixture.Step(11);

        AssertQuantity(100, fixture.Stock(source, ResourceIds.FerrousOre));
        AssertQuantity(20, fixture.Stock(destination, ResourceIds.FerrousOre));
        AssertQuantity(capacity, carriers.Sum(entity => fixture.Stock(entity, ResourceIds.FerrousOre)));
        Assert.All(carriers, carrier => Assert.Equal(0, fixture.Flow(carrier).OutputPerSecond));

        Assert.True(fixture.Inventories.Remove(fixture.Inventory(destination), ResourceIds.FerrousOre, 20).Succeeded);
        double withdrawn = 20;
        fixture.Step();
        Assert.InRange(fixture.Stock(destination, ResourceIds.FerrousOre), double.Epsilon,
            ConveyorSimulationSystem.NominalThroughput * fixture.TickSeconds + QuantityTolerance);
        for (int tick = 0; tick < 1_000; tick++)
        {
            double received = fixture.Stock(destination, ResourceIds.FerrousOre);
            if (received > 0)
            {
                Assert.True(fixture.Inventories.Remove(fixture.Inventory(destination), ResourceIds.FerrousOre, received).Succeeded);
                withdrawn += received;
            }
            AssertQuantity(supplied + 20, withdrawn + fixture.Total(ResourceIds.FerrousOre));
            if (fixture.Stock(source, ResourceIds.FerrousOre) == 0 &&
                carriers.All(entity => fixture.Stock(entity, ResourceIds.FerrousOre) == 0)) break;
            fixture.Step();
        }
        AssertQuantity(supplied + 20, withdrawn);
        AssertQuantity(0, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void DestroyedSupportRetainsAdjacentCargoAndPaidRepairRestartsIt()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 300);
        fixture.RunUntil(() => fixture.Flow(carriers[3]).OutputPerSecond > 0);
        ConveyorSocket socket = fixture.Entities.GetComponent<ConveyorPiece>(carriers[3]).Placement.Sockets[0];
        EntityId support = Assert.Single(fixture.Pieces, entity =>
        {
            ConveyorPlacement placement = fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement;
            return placement.IsSupport && Vector2.Distance(new(placement.Pivot.X, placement.Pivot.Z),
                new(socket.Position.X, socket.Position.Z)) < 0.1f;
        });
        EntityId[] adjacent = carriers.Where(entity => fixture.Entities.GetComponent<ConveyorPiece>(entity)
            .Placement.Sockets.Any(candidate => Vector2.Distance(new(candidate.Position.X, candidate.Position.Z),
                new(socket.Position.X, socket.Position.Z)) < 0.1f)).ToArray();
        double[] held = adjacent.Select(entity => fixture.Stock(entity, ResourceIds.FerrousOre)).ToArray();
        fixture.Entities.SetComponent(support, new HealthState(0, 200));
        fixture.Step(17);

        for (int index = 0; index < adjacent.Length; index++)
        {
            AssertQuantity(held[index], fixture.Stock(adjacent[index], ResourceIds.FerrousOre));
            Assert.Equal(ConveyorLinkStatus.Offline, fixture.Flow(adjacent[index]).Status);
            Assert.Equal(0, fixture.Flow(adjacent[index]).InputPerSecond);
            Assert.Equal(0, fixture.Flow(adjacent[index]).OutputPerSecond);
        }
        Assert.True(fixture.Edit(support, ConveyorEdit.Repair).Accepted);
        Assert.Equal(1, fixture.Entities.GetComponent<HealthState>(support).Fraction);
        Assert.Contains(adjacent, entity => fixture.Flow(entity).OutputPerSecond > 0);
        fixture.RunUntil(() => Math.Abs(fixture.Stock(destination, ResourceIds.FerrousOre) - 300) < QuantityTolerance);
        AssertQuantity(300, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void OccupiedWreckRejectsReplacementAndPaidRepairRetainsItsInventory()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 300);
        EntityId target = carriers[3];
        fixture.RunUntil(() => fixture.Stock(target, ResourceIds.FerrousOre) > 0);
        InventoryId buffer = fixture.Buffer(target);
        double cargo = fixture.Stock(target, ResourceIds.FerrousOre);
        ConveyorPiece piece = fixture.Entities.GetComponent<ConveyorPiece>(target);
        fixture.Entities.SetComponent(target, new HealthState(0, 200));
        fixture.SetPower(target, 0);

        ConveyorConstructionCommand rejected = fixture.Edit(target, ConveyorEdit.Replace);
        Assert.Equal(ConveyorFailure.Occupied, rejected.Result);
        Assert.True(fixture.Entities.IsAlive(target));
        Assert.True(fixture.Inventories.Contains(buffer));
        Assert.Equal(ConveyorLinkStatus.Destroyed, fixture.Flow(target).Status);
        AssertQuantity(cargo, fixture.Stock(target, ResourceIds.FerrousOre));

        double steel = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        Assert.True(fixture.Edit(target, ConveyorEdit.Repair).Accepted);
        AssertQuantity(steel - piece.PaidSteel, fixture.Stock(fixture.Payer, ResourceIds.Steel));
        Assert.Equal(buffer, fixture.Buffer(target));
        AssertQuantity(cargo, fixture.Stock(target, ResourceIds.FerrousOre));
        Assert.Equal(1, fixture.Entities.GetComponent<HealthState>(target).Fraction);
        fixture.SetPower(target, 1);
        fixture.Step();
        Assert.InRange(fixture.Flow(target).OutputPerSecond, double.Epsilon,
            ConveyorSimulationSystem.NominalThroughput + QuantityTolerance);
        fixture.RunUntil(() => Math.Abs(fixture.Stock(destination, ResourceIds.FerrousOre) - 300) < QuantityTolerance);
        AssertQuantity(300, fixture.Total(ResourceIds.FerrousOre));
    }

    [Theory]
    [InlineData(ConveyorEdit.Remove)]
    [InlineData(ConveyorEdit.Replace)]
    public void ReservedActualCargoProtectsAnEditBeforeTheBufferedReadModelUpdates(ConveyorEdit edit)
    {
        var fixture = new ConveyorTestFixture();
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 20);
        Assert.True(fixture.Inventories.Reserve(fixture.Buffer(target), ResourceIds.FerrousOre, 20).Succeeded);
        Assert.Equal(0, fixture.Entities.GetComponent<ConveyorPiece>(target).BufferedQuantity);

        Assert.Equal(ConveyorFailure.Occupied, fixture.Edit(target, edit).Result);

        Assert.True(fixture.Entities.IsAlive(target));
        AssertQuantity(20, fixture.Stock(target, ResourceIds.FerrousOre));
        Assert.Equal(20, fixture.Inventories.GetReservedQuantity(fixture.Buffer(target), ResourceIds.FerrousOre));
    }

    [Fact]
    public void EmptyReplacementAndRemovalDestroyOldBuffersAndUnusedAutomaticSupports()
    {
        var fixture = new ConveyorTestFixture();
        EntityId original = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        InventoryId oldBuffer = fixture.Buffer(original);
        Assert.True(fixture.Edit(original, ConveyorEdit.Replace).Accepted);
        EntityId replacement = Assert.Single(fixture.Carriers);
        Assert.NotEqual(original, replacement);
        Assert.False(fixture.Entities.IsAlive(original));
        Assert.False(fixture.Inventories.Contains(oldBuffer));
        InventoryId replacementBuffer = fixture.Buffer(replacement);
        Assert.NotEqual(oldBuffer, replacementBuffer);
        Assert.True(fixture.Edit(replacement, ConveyorEdit.Remove).Accepted);
        Assert.False(fixture.Inventories.Contains(replacementBuffer));
        Assert.Empty(fixture.Carriers);
        Assert.Empty(fixture.Pieces);
    }

    [Theory]
    [InlineData(ConveyorModule.InputAdapter)]
    [InlineData(ConveyorModule.OutputAdapter)]
    public void ReplacingAnAdapterRetainsItsExplicitBuildingAttachment(ConveyorModule module)
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        EntityId old = Assert.Single(carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.Module == module);
        EntityId attachment = fixture.Entities.GetComponent<ConveyorPiece>(old).Attachment;
        InventoryId oldBuffer = fixture.Buffer(old);

        Assert.True(fixture.Edit(old, ConveyorEdit.Replace).Accepted);

        EntityId replacement = Assert.Single(fixture.Carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.Module == module);
        Assert.NotEqual(old, replacement);
        Assert.Equal(attachment, fixture.Entities.GetComponent<ConveyorPiece>(replacement).Attachment);
        Assert.False(fixture.Inventories.Contains(oldBuffer));
        fixture.Give(source, ResourceIds.FerrousOre, 50);
        fixture.RunUntil(() => Math.Abs(fixture.Stock(destination, ResourceIds.FerrousOre) - 50) < QuantityTolerance);
        AssertQuantity(50, fixture.Total(ResourceIds.FerrousOre));
    }

    [Theory]
    [InlineData(ConveyorEdit.Remove)]
    [InlineData(ConveyorEdit.Replace)]
    [InlineData(ConveyorEdit.Repair)]
    public void ForeignOwnerCannotEditAnExistingCarrier(ConveyorEdit edit)
    {
        var fixture = new ConveyorTestFixture();
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Entities.SetComponent(target, new HealthState(100, 200));
        InventoryId buffer = fixture.Buffer(target);

        Assert.Equal(ConveyorFailure.NotOwned, fixture.Edit(target, edit, owner: fixture.OtherPlayer).Result);

        Assert.True(fixture.Entities.IsAlive(target));
        Assert.Equal(buffer, fixture.Buffer(target));
        Assert.Equal(100, fixture.Entities.GetComponent<HealthState>(target).Current);
    }

    [Fact]
    public void ForeignCarrierCannotMateWithAnOwnedSocket()
    {
        var fixture = new ConveyorTestFixture();
        EntityId original = fixture.Build(ConveyorModule.Standard, new(160, 0, 160));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Standard,
            new(160, 0, 168), new(160, 0, 168), BuildingOrientation.North, 0, true);

        Assert.Equal(ConveyorFailure.PortMismatch, fixture.Execute(request, fixture.OtherPlayer).Result);
        Assert.Equal(original, Assert.Single(fixture.Carriers));
    }

    [Fact]
    public void ReusedBuildingIndexDoesNotInheritAnOldAdapterAttachment()
    {
        var fixture = new ConveyorTestFixture();
        EntityId oldSource = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(oldSource, destination);
        EntityId adapter = carriers[0];
        ConveyorPiece old = fixture.Entities.GetComponent<ConveyorPiece>(adapter);
        fixture.Spatial.Remove(oldSource);
        Assert.True(fixture.Entities.DestroyEntity(oldSource));
        EntityId newSource = fixture.AddStorage(new(41, 0, 80));
        Assert.Equal(oldSource.Index, newSource.Index);
        Assert.NotEqual(oldSource.Generation, newSource.Generation);
        fixture.Give(newSource, ResourceIds.FerrousOre, 50);
        fixture.Step(30);

        AssertQuantity(50, fixture.Stock(newSource, ResourceIds.FerrousOre));
        AssertQuantity(0, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.False(fixture.Flow(adapter).InputA.IsValid);
        Assert.Equal(oldSource, fixture.Entities.GetComponent<ConveyorPiece>(adapter).Attachment);
        Assert.False(fixture.Edit(adapter, ConveyorEdit.Replace).Accepted);
        Assert.Equal(oldSource, fixture.Entities.GetComponent<ConveyorPiece>(adapter).Attachment);

        var reconnect = new ConveyorBuildRequest(ConveyorEdit.Replace, old.Placement.Module,
            old.Placement.Pivot, old.Placement.Pivot, old.Placement.Orientation, old.Placement.Tier,
            true, Target: adapter, SourcePortBuilding: newSource);
        Assert.True(fixture.Execute(reconnect).Accepted);
        EntityId replacement = Assert.Single(fixture.Carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.Module == ConveyorModule.InputAdapter);
        Assert.Equal(newSource, fixture.Entities.GetComponent<ConveyorPiece>(replacement).Attachment);
        fixture.RunUntil(() => Math.Abs(fixture.Stock(destination, ResourceIds.FerrousOre) - 50) < QuantityTolerance);
    }

    [Fact]
    public void CoincidentSocketsAreRejectedAndMalformedTopologyFailsClosed()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        ConveyorPlacement occupied = fixture.Entities.GetComponent<ConveyorPiece>(carriers[1]).Placement;
        var duplicateRequest = new ConveyorBuildRequest(ConveyorEdit.Build, occupied.Module,
            occupied.Pivot, occupied.Pivot, occupied.Orientation, occupied.Tier, true);
        Assert.Equal(ConveyorFailure.PortMismatch, fixture.Execute(duplicateRequest).Result);

        // Also exercise the simulation guard against coincident authoritative placements.
        // The extra carrier still has its own paid construction and real finite inventory.
        EntityId duplicate = fixture.Build(ConveyorModule.Standard, new(160, 0, 80), BuildingOrientation.East);
        ConveyorPiece separate = fixture.Entities.GetComponent<ConveyorPiece>(duplicate);
        fixture.Entities.SetComponent(duplicate, separate with { Placement = occupied });
        fixture.Give(source, ResourceIds.FerrousOre, 50);
        fixture.Step(30);

        Assert.False(fixture.Flow(carriers[0]).OutputA.IsValid);
        AssertQuantity(50, fixture.Stock(source, ResourceIds.FerrousOre));
        AssertQuantity(0, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.All(fixture.Carriers, entity => AssertQuantity(0, fixture.Stock(entity, ResourceIds.FerrousOre)));
        AssertQuantity(50, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void ClosedPaidCycleWithNoBuildingSinkCannotCirculateOrDuplicateCargo()
    {
        var fixture = new ConveyorTestFixture();
        EntityId first = fixture.Build(ConveyorModule.CurveLeft, new(160, 0, 168), BuildingOrientation.North);
        fixture.Build(ConveyorModule.CurveLeft, new(168, 0, 168), BuildingOrientation.East);
        fixture.Build(ConveyorModule.CurveLeft, new(168, 0, 160), BuildingOrientation.South);
        fixture.Build(ConveyorModule.CurveLeft, new(160, 0, 160), BuildingOrientation.West);
        fixture.Build(ConveyorModule.Standard, new(164, 0, 172), BuildingOrientation.East);
        fixture.Build(ConveyorModule.Standard, new(172, 0, 164), BuildingOrientation.South);
        fixture.Build(ConveyorModule.Standard, new(164, 0, 156), BuildingOrientation.West);
        fixture.Build(ConveyorModule.Standard, new(156, 0, 164), BuildingOrientation.North);
        fixture.Give(first, ResourceIds.FerrousOre, 25);
        fixture.Step(40);

        Assert.All(fixture.Carriers, entity =>
        {
            Assert.True(fixture.Flow(entity).InputA.IsValid);
            Assert.True(fixture.Flow(entity).OutputA.IsValid);
            Assert.Equal(0, fixture.Flow(entity).InputPerSecond);
            Assert.Equal(0, fixture.Flow(entity).OutputPerSecond);
        });
        AssertQuantity(25, fixture.Stock(first, ResourceIds.FerrousOre));
        AssertQuantity(25, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void ConnectedSupplyDepotDoesNotAdmitIncompatibleOre()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddBuilding(BuildingIds.SupplyDepot, new(104, 0, 80));
        EntityId[] carriers = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 50);
        fixture.Step(30);

        Assert.True(fixture.Flow(carriers[^1]).OutputA.IsValid);
        AssertQuantity(50, fixture.Stock(source, ResourceIds.FerrousOre));
        AssertQuantity(0, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.All(carriers, entity =>
        {
            Assert.Equal(0, fixture.Flow(entity).InputPerSecond);
            Assert.Equal(0, fixture.Flow(entity).OutputPerSecond);
        });
        AssertQuantity(50, fixture.Total(ResourceIds.FerrousOre));
    }

    [Fact]
    public void WheeledNavigationPassesUnderCarriersAndKeepsRealSupportCellsBlocked()
    {
        var fixture = new ConveyorTestFixture();
        var gridSettings = new NavigationGridSettings { CellSizeMeters = 1 };
        var sectorSettings = new NavigationSectorSettings { SectorSizeCells = 8 };
        var pathfinder = new HierarchicalPathfinder(NavigationWorld.Build(fixture.Terrain,
            gridSettings: gridSettings, sectorSettings: sectorSettings));
        var navigation = new HierarchicalNavigationSystem(pathfinder);
        fixture.Simulation.RegisterSystem(new StrategicInfrastructureSystem(fixture.Network, fixture.Terrain,
            navigation, [], gridSettings, sectorSettings));
        fixture.Simulation.RegisterSystem(navigation);
        fixture.Simulation.RegisterSystem(new GroundMovementSystem(fixture.Terrain, fixture.Spatial));
        EntityId carrier = fixture.Build(ConveyorModule.Standard, new(160, 0, 160));
        fixture.Step();
        NavigationGrid grid = navigation.World.Grid;
        NavigationCapabilities wheeled = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        Assert.True(grid.TryWorldToCell(new(160, 0, 160), out NavigationCellCoordinate under));
        Assert.True(grid.IsTraversable(under, wheeled));
        foreach (EntityId support in fixture.Pieces.Where(entity => entity != carrier))
        {
            Vector3 position = fixture.Entities.GetComponent<ConveyorPiece>(support).Placement.Pivot;
            Assert.True(grid.TryWorldToCell(position, out NavigationCellCoordinate foot));
            Assert.False(grid.IsTraversable(foot, wheeled));
        }

        EntityId truck = fixture.CreateTruck(new(152, 2, 160));
        Vector3 destination = new(168, 0, 160);
        var move = new MoveEntitiesCommand(fixture.Player, [truck], destination, fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(move, fixture.Simulation.CurrentTick.Next());
        bool crossedUnderCarrier = false;
        fixture.RunUntil(() =>
        {
            Vector3 position = fixture.Entities.GetComponent<WorldTransform>(truck).Position;
            crossedUnderCarrier |= MathF.Abs(position.X - 160) < 1 && MathF.Abs(position.Z - 160) < 1 &&
                position.Y < fixture.Entities.GetComponent<ConveyorPiece>(carrier).Placement.Bounds.Minimum.Y;
            return Vector2.Distance(new(position.X, position.Z), new(destination.X, destination.Z)) < 1;
        });
        Assert.False(fixture.Entities.HasComponent<NavigationFailureState>(truck));
        Assert.Equal(1, move.AcceptedTargetCount);
        Assert.True(crossedUnderCarrier);

        Assert.True(fixture.Edit(carrier, ConveyorEdit.Remove).Accepted);
        fixture.Step();
        Assert.True(navigation.World.Grid.TryWorldToCell(new(160, 0, 156), out NavigationCellCoordinate removedFoot));
        Assert.True(navigation.World.Grid.IsTraversable(removedFoot, wheeled));
    }

    [Theory]
    [InlineData(2.5f, true)]
    [InlineData(12f, false)]
    public void AuthoredDockLaneUsesCarrierClearanceAndRejectsGroundSupports(float vehicleHeight, bool fitsUnderCarrier)
    {
        var fixture = new ConveyorTestFixture();
        EntityId storage = fixture.AddStorage(new(160, 0, 160));
        BuildingLoadingDock dock = DirectorateContent.CreateBuildingCatalog()[BuildingIds.StorageDepot].LoadingDock
            ?? throw new InvalidOperationException("Storage depot must define its truck dock.");
        fixture.Entities.AddComponent(storage, dock);
        WorldTransform transform = fixture.Entities.GetComponent<WorldTransform>(storage);
        var truck = WorldTransform.Identity with { Scale = new(3.4f, 2.5f, 6.8f) };
        Assert.True(VehicleDockPose.TryCreate(dock, transform, truck, out VehicleDockPose pose));
        Vector3 position = ConveyorGeometry.Snap(pose.Service with { Y = 0 });
        var proposed = new ConveyorPlacement(ConveyorModule.Support, position, BuildingOrientation.North, 0);
        Assert.True(pose.IntersectsLane(proposed.Bounds));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Support,
            position, position, BuildingOrientation.North, 0, true);

        Assert.Equal(ConveyorFailure.Obstructed, fixture.Execute(request).Result);

        Assert.Empty(fixture.Pieces);
        Assert.True(pose.HasDrivableTerrain(fixture.Terrain));
        EntityId carrier = fixture.Build(ConveyorModule.Standard, new(160, 0, 172));
        Assert.True(pose.IntersectsLane(fixture.Entities.GetComponent<ConveyorPiece>(carrier).Placement.Bounds));
        Assert.All(fixture.Pieces.Where(entity => entity != carrier), entity =>
            Assert.False(pose.IntersectsLane(fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.Bounds)));
        EntityId vehicle = fixture.CreateTruck(pose.Entry + new Vector3(-8, 2, 0));
        WorldTransform vehicleTransform = fixture.Entities.GetComponent<WorldTransform>(vehicle);
        fixture.Entities.SetComponent(vehicle, vehicleTransform with
        {
            Scale = vehicleTransform.Scale with { Y = vehicleHeight }
        });
        var docking = new VehicleDockingService(fixture.Terrain, fixture.Spatial);
        var reserve = new ReserveDockCommand(docking, vehicle, storage, fixture.Player);
        fixture.Simulation.SubmitCommand(reserve, fixture.Simulation.CurrentTick.Next());
        fixture.Step();
        Assert.Equal(fitsUnderCarrier, reserve.Accepted);
        if (fitsUnderCarrier)
            Assert.Equal(vehicle, fixture.Entities.GetComponent<VehicleDockSlot>(storage).Truck);
        else
        {
            Assert.Equal(VehicleDockBlockReason.Obstructed, reserve.Visit.Reason);
            Assert.False(fixture.Entities.HasComponent<VehicleDockSlot>(storage));
        }
    }

    private static void AssertQuantity(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, QuantityTolerance);

    private sealed class ReserveDockCommand(VehicleDockingService docking, EntityId truck, EntityId building,
        PlayerId owner) : ISimulationCommand
    {
        public bool Accepted { get; private set; }
        public VehicleDockVisit Visit { get; private set; }

        public void Execute(SimulationContext context)
        {
            Accepted = docking.TryReserve(context, truck, building, owner, out VehicleDockVisit visit);
            Visit = visit;
        }
    }
}
