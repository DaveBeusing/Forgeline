using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CargoTransportSystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Theory]
    [InlineData(14.0f)]
    [InlineData(16.0f)]
    [InlineData(18.0f)]
    public void RadiusArrivalWithoutLoadingGeometryCannotStartLoading(float buildingWidth)
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 80.0,
            sourcePosition: new Vector3(64.0f, 0.0f, 16.0f),
            destinationPosition: new Vector3(100.0f, 0.0f, 16.0f),
            worldChunkCountX: 4);
        fixture.ConnectDirect();
        Assert.True(fixture.Network.TryGetNode(fixture.SourceNode, out LogisticsNode source));
        fixture.Simulation.Entities.AddComponent(
            source.Entity,
            new SpatialPresence(
                new Vector3(buildingWidth * .5f),
                new SpatialEntryMetadata(1, 0, SpatialMobility.Static)));
        EntityId truck = fixture.CreateTruckAtSource();
        GroundMovement movement = fixture.Simulation.Entities.GetComponent<GroundMovement>(truck);
        Vector3 approach = fixture.SourcePosition - new Vector3(
            buildingWidth * .5f + movement.ObstacleLookAhead + movement.Radius + 32.0f, 0.0f, 0.0f);
        WorldTransform transform = fixture.Simulation.Entities.GetComponent<WorldTransform>(truck);
        fixture.Simulation.Entities.SetComponent(
            truck,
            transform with { Position = approach - new Vector3(movement.Radius * 2.0f, 0.0f, 0.0f) });
        Assert.True(fixture.TransportSystem.TryAssignOrder(
            fixture.Simulation.Entities,
            truck,
            new CargoTransportOrder(fixture.SourceNode, fixture.DestinationNode,
                ResourceIds.FerrousOre, 50.0, SimulationTick.Zero),
            SimulationTick.Zero));
        fixture.Simulation.Entities.AddComponent(
            truck,
            new CargoTransportMovementTarget(fixture.SourceNode, approach, SimulationTick.Zero));
        fixture.Simulation.Entities.AddComponent(
            truck,
            new MovementOrder(LocalPlayer, approach, SimulationTick.Zero, SimulationTick.Zero.Next()));

        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(CargoTransportLifecycleState.ToOrigin,
            fixture.Simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck).Lifecycle);
        Assert.False(fixture.Simulation.Entities.HasComponent<NavigationPendingPath>(truck));
        Assert.Equal(80.0, fixture.TotalConservedQuantity(truck));
    }

    [Fact]
    public void RegionalConvoyKeepsIndependentTransportOrdersAndConservesCargo()
    {
        TransportFixture fixture = CreateFixture(sourceQuantity: 100.0);
        fixture.ConnectDirect();
        EntityId first = fixture.CreateTruckAtSource();
        EntityId second = fixture.CreateTruckAtSource();
        foreach (EntityId truck in new[] { first, second })
            Assert.True(fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities, truck,
                new CargoTransportOrder(fixture.SourceNode, fixture.DestinationNode,
                    ResourceIds.FerrousOre, 25.0, SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(fixture, () => new[] { first, second }.All(truck =>
            fixture.Simulation.Entities.TryGetComponent(truck, out CargoTransportRouteState route) &&
            route.Route is not null &&
            fixture.Simulation.Entities.TryGetComponent(truck, out CargoTransportRuntimeState state) &&
            state.Lifecycle == CargoTransportLifecycleState.ToDestination));

        double before = fixture.TotalConservedQuantity(first, second);
        var create = PlayerLogisticsActionCommand.CreateCargoConvoy(
            LocalPlayer, [second, first], [], fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(
            create, fixture.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.True(create.Accepted);
        Assert.True(create.CreatedConvoy.IsValid);
        Assert.Equal(first, fixture.Simulation.Entities
            .GetComponent<CargoConvoyGroup>(create.CreatedConvoy).Leader);
        Assert.Equal(create.CreatedConvoy,
            fixture.Simulation.Entities.GetComponent<CargoConvoyMember>(first).Group);
        Assert.Equal(create.CreatedConvoy,
            fixture.Simulation.Entities.GetComponent<CargoConvoyMember>(second).Group);
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(before, fixture.TotalConservedQuantity(first, second));

        var groupCapture = new CargoConvoySystem(fixture.Network);
        CargoConvoyReadModel observed = Assert.Single(groupCapture.CaptureOwned(
            fixture.Simulation.Entities, LocalPlayer, fixture.Simulation.CurrentTick));
        Assert.Equal(2, observed.TruckCount);
        Assert.Empty(groupCapture.CaptureOwned(
            fixture.Simulation.Entities, new PlayerId(2), fixture.Simulation.CurrentTick));

        var foreign = PlayerLogisticsActionCommand.DissolveCargoConvoy(
            new PlayerId(2), create.CreatedConvoy, fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(foreign,
            fixture.Simulation.CurrentTick.Next(), new SimulationCommandSource(2));
        fixture.Simulation.AdvanceOneTick();
        Assert.False(foreign.Accepted);
        Assert.Equal(PlayerLogisticsActionFailureReason.ForeignOwnership, foreign.FailureReason);

        var dissolve = PlayerLogisticsActionCommand.DissolveCargoConvoy(
            LocalPlayer, create.CreatedConvoy, fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(dissolve,
            fixture.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.True(dissolve.Accepted);
        Assert.False(fixture.Simulation.Entities.IsAlive(create.CreatedConvoy));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(first));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(second));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(before, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void RouteChangeDissolvesConvoyWithoutDeletingActiveShipments()
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        double conserved = fixture.TotalConservedQuantity(first, second);
        _ = fixture.AddIntermediateNode(new Vector3(28, 0, 28));
        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.IsAlive(group));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMember>(first));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMember>(second));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(first));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(second));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(conserved, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void DestroyedConvoyLeaderReleasesSurvivingTruckToIndividualDelivery()
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        Assert.True(fixture.Simulation.Entities.DestroyEntity(first));
        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.IsAlive(group));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMember>(second));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(second));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
    }

    [Fact]
    public void OutOfFuelConvoyMemberReleasesAllTrucksWithoutChangingCargo()
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        double before = fixture.TotalConservedQuantity(first, second);
        fixture.Simulation.Entities.AddComponent(first,
            new SupplyMovementConstraint(0.0f, canMove: false));
        fixture.Simulation.AdvanceOneTick();

        Assert.False(fixture.Simulation.Entities.IsAlive(group));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMember>(second));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMotionConstraint>(second));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(before, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void ManualEscortMovementReplacesConvoyFollowWithoutCancelingCargo()
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        double conserved = fixture.TotalConservedQuantity(first, second);
        EntityId escort = CreateConvoyEscort(fixture, group);

        fixture.Simulation.AdvanceOneTick();
        Assert.True(fixture.Simulation.Entities.IsAlive(group));
        CargoConvoyEscort following =
            fixture.Simulation.Entities.GetComponent<CargoConvoyEscort>(escort);
        Assert.NotEqual(SimulationTick.Zero, following.LastIssuedAtTick);
        Assert.True(fixture.Simulation.Entities.HasComponent<NavigationPendingPath>(escort));

        var manual = new MoveEntitiesCommand(
            LocalPlayer, [escort], new Vector3(40, 0, 24),
            fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(manual,
            fixture.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(1, manual.AcceptedTargetCount);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        AssertManualMovementIntent(fixture, escort, manual);
        Assert.True(fixture.Simulation.Entities.IsAlive(group));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(conserved, fixture.TotalConservedQuantity(first, second));

        RunUntilOrderCompletes(fixture, first);
        RunUntilOrderCompletes(fixture, second);
        Assert.Equal(50.0, fixture.Inventories.GetTotalQuantity(fixture.DestinationInventory));
        Assert.Equal(conserved, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void ManualEscortMovementReplacesActiveRouteWithoutMistakingManagedWaypointsForPlayerOrders()
    {
        var (fixture, first, second, group) = CreateActiveConvoy(CreateFixture(
            sourceQuantity: 100,
            sourcePosition: new Vector3(32, 0, 16),
            destinationPosition: new Vector3(220, 0, 16),
            worldChunkCountX: 8));
        double conserved = fixture.TotalConservedQuantity(first, second);
        EntityId escort = CreateConvoyEscort(fixture, group);
        fixture.Simulation.AdvanceOneTick();
        CargoConvoyEscort following =
            fixture.Simulation.Entities.GetComponent<CargoConvoyEscort>(escort);

        RunUntil(fixture, () =>
            fixture.Simulation.Entities.TryGetComponent(escort, out NavigationRouteState route) &&
            route.HasActiveWaypoint &&
            fixture.Simulation.Entities.HasComponent<MovementOrder>(escort));
        NavigationRouteState activeRoute =
            fixture.Simulation.Entities.GetComponent<NavigationRouteState>(escort);
        MovementOrder waypoint = fixture.Simulation.Entities.GetComponent<MovementOrder>(escort);
        Assert.Equal(following.LastIssuedAtTick, activeRoute.OriginalOrder.AcceptedAtTick);
        Assert.Equal(activeRoute.ActiveWaypointTick, waypoint.AcceptedAtTick);
        Assert.NotEqual(following.LastIssuedAtTick, waypoint.AcceptedAtTick);

        // Navigation owns this newer waypoint; it must not release convoy ownership.
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(following,
            fixture.Simulation.Entities.GetComponent<CargoConvoyEscort>(escort));
        Assert.True(fixture.Simulation.Entities.HasComponent<NavigationRouteState>(escort));

        var manual = new MoveEntitiesCommand(
            LocalPlayer, [escort], new Vector3(80, 0, 24), fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(manual,
            fixture.Simulation.CurrentTick.Next(), new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(1, manual.AcceptedTargetCount);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        AssertManualMovementIntent(fixture, escort, manual);
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(conserved, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void DissolvingConvoyPreservesManualEscortMovementAcceptedInTheSameTick()
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        double conserved = fixture.TotalConservedQuantity(first, second);
        EntityId escort = CreateConvoyEscort(fixture, group);
        fixture.Simulation.AdvanceOneTick();
        Assert.True(fixture.Simulation.Entities.HasComponent<NavigationPendingPath>(escort));

        var manual = new MoveEntitiesCommand(
            LocalPlayer, [escort], new Vector3(40, 0, 24), fixture.Simulation.CurrentTick);
        var dissolve = PlayerLogisticsActionCommand.DissolveCargoConvoy(
            LocalPlayer, group, fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(manual,
            fixture.Simulation.CurrentTick.Next(), new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.SubmitCommand(dissolve,
            fixture.Simulation.CurrentTick.Next(), new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.Equal(1, manual.AcceptedTargetCount);
        Assert.True(dissolve.Accepted);
        Assert.False(fixture.Simulation.Entities.IsAlive(group));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        AssertManualMovementIntent(fixture, escort, manual);
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(conserved, fixture.TotalConservedQuantity(first, second));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ManualEscortReplacementPreservesLoadedCargoAndReservations(int escortCount)
    {
        var (fixture, first, second, group) = CreateActiveConvoy();
        EntityId[] escorts = Enumerable.Range(0, escortCount).Select(_ => CreateConvoyEscort(fixture, group)).ToArray();
        fixture.Simulation.AdvanceOneTick();
        double cargo = fixture.GetTruckCargo(first) + fixture.GetTruckCargo(second);
        double reserved = fixture.Inventories.GetReservedQuantity(fixture.SourceInventory, ResourceIds.FerrousOre);
        var firstOrder = fixture.Simulation.Entities.GetComponent<CargoTransportOrder>(first);
        var secondOrder = fixture.Simulation.Entities.GetComponent<CargoTransportOrder>(second);
        var manual = new MoveEntitiesCommand(LocalPlayer, [escorts[0], escorts[0]], new Vector3(40, 0, 24), fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(manual, fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(2, manual.AcceptedTargetCount);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escorts[0]));
        AssertManualMovementIntent(fixture, escorts[0], manual);
        if (escortCount > 1) Assert.True(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escorts[1]));
        Assert.Equal(firstOrder, fixture.Simulation.Entities.GetComponent<CargoTransportOrder>(first));
        Assert.Equal(secondOrder, fixture.Simulation.Entities.GetComponent<CargoTransportOrder>(second));
        Assert.Equal(cargo, fixture.GetTruckCargo(first) + fixture.GetTruckCargo(second));
        Assert.Equal(reserved, fixture.Inventories.GetReservedQuantity(fixture.SourceInventory, ResourceIds.FerrousOre));
        RunUntilOrderCompletes(fixture, first);
        RunUntilOrderCompletes(fixture, second);
        Assert.Equal(50, fixture.Inventories.GetQuantity(fixture.DestinationInventory, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.TotalConservedQuantity(first, second));
    }

    [Fact]
    public void LoadedConvoyDetourAndResupplyRemainValidAfterManualEscortReplacement()
    {
        var wall = new AxisAlignedBounds(new Vector3(28.1f, -1, 8.1f), new Vector3(28.9f, 2, 31.9f));
        TransportFixture fixture = CreateFixture(100,
            sourcePosition: new Vector3(22, 0, 26), destinationPosition: new Vector3(42, 0, 26),
            navigationObstacles: [wall]);
        var intelligence = new FactionIntelligenceStore(new IntelligenceGridSettings { CellSizeMeters = 8 });
        var weapons = new WeaponCatalog();
        weapons.Add(new WeaponDefinition(new WeaponId(1), 80, 4, 1, new DamagePayload(10), WeaponDeliveryModel.Hitscan));
        fixture.Simulation.RegisterSystem(new TacticalOrderPreparationSystem());
        fixture.Simulation.RegisterSystem(new AutomaticResupplyDecisionSystem(fixture.Inventories));
        fixture.Simulation.RegisterSystem(new BattlefieldIntelligenceSystem(intelligence));
        fixture.Simulation.RegisterSystem(new TargetAcquisitionSystem(weapons));
        fixture.Simulation.RegisterSystem(new TacticalCombatSystem(weapons, intelligence));
        fixture.Simulation.RegisterSystem(new BattlefieldSupplySystem(fixture.Inventories));
        var (_, first, second, group) = CreateActiveConvoy(fixture);
        EntityId escort = CreateConvoyEscort(fixture, group);
        fixture.Simulation.Entities.SetComponent(escort, new WorldTransform(new Vector3(18, 0, 26), Quaternion.Identity, Vector3.One));
        fixture.Simulation.AdvanceOneTick();
        EntityId recipient = CreateIntegrationCombatant(fixture, new Vector3(8, 0, 20), new FactionId(1), 10);
        EntityId other = CreateIntegrationCombatant(fixture, new Vector3(8, 0, 22), new FactionId(1), 100);
        _ = CreateIntegrationCombatant(fixture, new Vector3(14, 0, 20), new FactionId(2), 100);
        fixture.Simulation.Entities.AddComponent(recipient, new VisualSensorState(new FactionId(1), 100, 1));
        fixture.Simulation.Entities.AddComponent(recipient, new AutomaticResupplyPolicy(.2, .2));
        InventoryId stock = fixture.Inventories.CreateInventory(new InventorySpecification(200, [ResourceIds.Fuel]));
        Assert.True(fixture.Inventories.Add(stock, ResourceIds.Fuel, 5).Succeeded);
        EntityId provider = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(provider, new WorldTransform(new Vector3(5, 0, 20), Quaternion.Identity, Vector3.One));
        fixture.Simulation.Entities.AddComponent(provider, new SupplyProvider(stock, LocalPlayer, 20));
        var manual = new MoveEntitiesCommand(LocalPlayer, [escort], new Vector3(10, 0, 26), fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(new HoldPositionCommand(LocalPlayer, [recipient, other], fixture.Simulation.CurrentTick), fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.SubmitCommand(manual, fixture.Simulation.CurrentTick.Next());
        fixture.Simulation.AdvanceOneTick();
        Assert.Equal(CombatOrderStatus.Resupplying, fixture.Simulation.Entities.GetComponent<TacticalCombatState>(recipient).Status);
        Assert.Equal(CombatOrderStatus.Engaging, fixture.Simulation.Entities.GetComponent<TacticalCombatState>(other).Status);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        AssertManualMovementIntent(fixture, escort, manual);
        Assert.True(fixture.Inventories.Add(stock, ResourceIds.Fuel, 100).Succeeded);
        bool sawDetour = false;
        for (int tick = 0; tick < 1600 && (fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first) ||
            fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second)); tick++)
        {
            foreach (EntityId truck in new[] { first, second })
                if (fixture.Simulation.Entities.TryGetComponent(truck, out NavigationRouteState route))
                    sawDetour |= route.Path.RefinedCells.Any(cell => cell.Z < 8);
            fixture.Simulation.AdvanceOneTick();
        }
        Assert.True(sawDetour);
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(first));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(second));
        Assert.Equal(50, fixture.Inventories.GetQuantity(fixture.DestinationInventory, ResourceIds.FerrousOre));
        Assert.Equal(100, fixture.TotalConservedQuantity(first, second));
        Assert.Equal(0, fixture.Inventories.GetReservedQuantity(fixture.SourceInventory, ResourceIds.FerrousOre));
        Assert.False(fixture.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.Equal(CombatOrderKind.HoldPosition, fixture.Simulation.Entities.GetComponent<CombatOrderState>(recipient).Kind);
    }

    private static EntityId CreateIntegrationCombatant(TransportFixture fixture, Vector3 position, FactionId faction, double fuel)
    {
        EntityId unit = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(unit, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        fixture.Simulation.Entities.AddComponent(unit, new ControllableEntity(new PlayerId(faction.Value), ControllableEntityCategory.Unit));
        fixture.Simulation.Entities.AddComponent(unit, new Combatant(faction));
        fixture.Simulation.Entities.AddComponent(unit, new IntelligenceSignature(faction, unit.Index + 1));
        fixture.Simulation.Entities.AddComponent(unit, new HealthState(100, 100));
        fixture.Simulation.Entities.AddComponent(unit, new Targetable(TargetClass.LightVehicle));
        fixture.Simulation.Entities.AddComponent(unit, new WeaponState(new WeaponId(1), EntityId.Invalid));
        fixture.Simulation.Entities.AddComponent(unit, FirePolicyState.FireAtWill);
        fixture.Simulation.Entities.AddComponent(unit, GroundMovement.CreateDefault());
        fixture.Simulation.Entities.AddComponent(unit, GroundMovementState.Stationary());
        BattlefieldSupplyFactory.AttachUnitSupply(fixture.Simulation.Entities, fixture.Inventories, unit, 100, 100, .01, fuel, 100);
        return unit;
    }

    private static EntityId CreateConvoyEscort(TransportFixture fixture, EntityId group)
    {
        EntityId escort = fixture.Simulation.Entities.CreateEntity();
        fixture.Simulation.Entities.AddComponent(escort,
            new ControllableEntity(LocalPlayer, ControllableEntityCategory.Unit));
        fixture.Simulation.Entities.AddComponent(escort, new Combatant(new FactionId(1)));
        fixture.Simulation.Entities.AddComponent(escort,
            new WorldTransform(fixture.SourcePosition + new Vector3(8, 0, 8),
                Quaternion.Identity, Vector3.One));
        fixture.Simulation.Entities.AddComponent(escort, GroundMovement.CreateDefault());
        fixture.Simulation.Entities.AddComponent(escort, GroundMovementState.Stationary());
        fixture.Simulation.Entities.AddComponent(escort,
            new NavigationAgent(NavigationMovementClass.Wheeled));
        fixture.Simulation.Entities.AddComponent(escort,
            new CargoConvoyEscort(group, SimulationTick.Zero, default));
        return escort;
    }

    private static void AssertManualMovementIntent(
        TransportFixture fixture, EntityId escort, MoveEntitiesCommand manual)
    {
        MovementOrder intent = fixture.Simulation.Entities.TryGetComponent(
            escort, out NavigationPendingPath pending)
            ? pending.OriginalOrder
            : fixture.Simulation.Entities.TryGetComponent(escort, out NavigationRouteState route)
                ? route.OriginalOrder
                : fixture.Simulation.Entities.GetComponent<MovementOrder>(escort);
        Assert.Equal(manual.WorldTarget, intent.WorldTarget);
        Assert.Equal(manual.ExecutedAtTick, intent.AcceptedAtTick);
    }

    private static (TransportFixture Fixture, EntityId First, EntityId Second, EntityId Group)
        CreateActiveConvoy(TransportFixture? fixture = null)
    {
        fixture ??= CreateFixture(sourceQuantity: 100);
        fixture.ConnectDirect();
        EntityId first = fixture.CreateTruckAtSource();
        EntityId second = fixture.CreateTruckAtSource();
        foreach (EntityId truck in new[] { first, second })
            Assert.True(fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities, truck,
                new CargoTransportOrder(fixture.SourceNode, fixture.DestinationNode,
                    ResourceIds.FerrousOre, 25, SimulationTick.Zero),
                SimulationTick.Zero));
        RunUntil(fixture, () => new[] { first, second }.All(truck =>
            fixture.Simulation.Entities.TryGetComponent(truck, out CargoTransportRouteState route) &&
            route.Route is not null &&
            fixture.Simulation.Entities.TryGetComponent(truck, out CargoTransportRuntimeState state) &&
            state.Lifecycle == CargoTransportLifecycleState.ToDestination));

        var request = PlayerLogisticsActionCommand.CreateCargoConvoy(
            LocalPlayer, [first, second], [], fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(request,
            fixture.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();
        Assert.True(request.Accepted);
        Assert.True(fixture.Simulation.Entities.IsAlive(request.CreatedConvoy));
        return (fixture, first, second, request.CreatedConvoy);
    }

    [Fact]
    public void ConvoyRejectsDuplicatesWithoutChangingCargoOrders()
    {
        TransportFixture fixture = CreateFixture(sourceQuantity: 100);
        fixture.ConnectDirect();
        EntityId truck = fixture.CreateTruckAtSource();
        Assert.True(fixture.TransportSystem.TryAssignOrder(fixture.Simulation.Entities,
            truck, new CargoTransportOrder(fixture.SourceNode, fixture.DestinationNode,
                ResourceIds.FerrousOre, 25, SimulationTick.Zero), SimulationTick.Zero));
        var create = PlayerLogisticsActionCommand.CreateCargoConvoy(
            LocalPlayer, [truck, truck], [], fixture.Simulation.CurrentTick);
        fixture.Simulation.SubmitCommand(create, fixture.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(LocalPlayer.Value));
        fixture.Simulation.AdvanceOneTick();

        Assert.False(create.Accepted);
        Assert.Equal(PlayerLogisticsActionFailureReason.ConvoyIneligible, create.FailureReason);
        Assert.True(fixture.Simulation.Entities.HasComponent<CargoTransportOrder>(truck));
        Assert.False(fixture.Simulation.Entities.HasComponent<CargoConvoyMember>(truck));
    }

    [Fact]
    public void FullTransportCycleMovesCargoPhysicallyAndConservesResources()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 80.0,
            sourcePosition: new Vector3(4.0f, 0.0f, 4.0f),
            destinationPosition: new Vector3(52.0f, 0.0f, 4.0f));

        fixture.Network.AddEdge(
            fixture.SourceNode,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 48.0,
            baseCost: 48.0,
            capacityPerSecond: 100.0);

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 50.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntilOrderCompletes(fixture, truck);

        double after = fixture.TotalConservedQuantity(truck);

        Assert.Equal(before, after);
        Assert.Equal(
            30.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            50.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
        Assert.Equal(
            1,
            fixture.TransportSystem.Metrics.CompletedOrderCount);
        Assert.Equal(
            50.0,
            fixture.TransportSystem.Metrics.DeliveredQuantity);
    }

    [Fact]
    public void PartialLoadDeliversAvailableQuantityWithoutCreatingResources()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 20.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 50.0,
                    SimulationTick.Zero,
                    CargoPartialLoadPolicy.AllowPartial),
                SimulationTick.Zero));

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));
        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            20.0,
            fixture.TransportSystem.Metrics.DeliveredQuantity);
    }

    [Fact]
    public void RequireRequestedQuantityWaitsForOriginInventoryToRecover()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 20.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero,
                    CargoPartialLoadPolicy.RequireRequestedQuantity),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRuntimeState state) &&
                state.Lifecycle ==
                    CargoTransportLifecycleState.Waiting &&
                state.WaitReason ==
                    CargoTransportWaitReason.OriginResourceUnavailable);

        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
        Assert.True(
            fixture.Inventories.Add(
                fixture.SourceInventory,
                ResourceIds.FerrousOre,
                10.0).Succeeded);

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            30.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
    }

    [Fact]
    public void DestinationCapacityBlocksUnloadUntilSpaceBecomesAvailable()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 50.0,
            destinationCapacity: 20.0);

        fixture.ConnectDirect();

        Assert.True(
            fixture.Inventories.Add(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        EntityId truck = fixture.CreateTruckAtSource();
        double before = fixture.TotalConservedQuantity(truck);

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRuntimeState state) &&
                state.Lifecycle ==
                    CargoTransportLifecycleState.Waiting &&
                state.WaitReason ==
                    CargoTransportWaitReason.DestinationCapacity);

        Assert.Equal(
            30.0,
            fixture.GetTruckCargo(truck));
        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));

        Assert.True(
            fixture.Inventories.Remove(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        before -= 20.0;

        RunUntil(
            fixture,
            () =>
                fixture.GetTruckCargo(truck) < 30.0);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));

        Assert.True(
            fixture.Inventories.Remove(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre,
                20.0).Succeeded);

        before -= 20.0;

        RunUntilOrderCompletes(fixture, truck);

        Assert.Equal(
            before,
            fixture.TotalConservedQuantity(truck));
        Assert.Equal(
            10.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void InvalidatedRouteReturnsToLastAnchorAndUsesAlternateRoute()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 40.0,
            sourcePosition: new Vector3(4.0f, 0.0f, 4.0f),
            destinationPosition: new Vector3(84.0f, 0.0f, 4.0f),
            worldChunkCountX: 3);

        LogisticsNodeId primary =
            fixture.AddIntermediateNode(
                new Vector3(36.0f, 0.0f, 4.0f));
        LogisticsNodeId alternate =
            fixture.AddIntermediateNode(
                new Vector3(36.0f, 0.0f, 20.0f));

        LogisticsEdgeId primaryEntry =
            fixture.Network.AddEdge(
                fixture.SourceNode,
                primary,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: 32.0,
                baseCost: 10.0,
                capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            primary,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 48.0,
            baseCost: 10.0,
            capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            fixture.SourceNode,
            alternate,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 36.0,
            baseCost: 30.0,
            capacityPerSecond: 100.0);
        fixture.Network.AddEdge(
            alternate,
            fixture.DestinationNode,
            LogisticsTransportMode.GroundRoad,
            distanceMeters: 52.0,
            baseCost: 30.0,
            capacityPerSecond: 100.0);

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 25.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransportRouteState routeState) &&
                routeState.Route is not null &&
                routeState.Route.Segments.Count == 2);

        CargoTransportRouteState initialRoute =
            fixture.Simulation.Entities.GetComponent<
                CargoTransportRouteState>(truck);
        Assert.Equal(
            primary,
            initialRoute.Route!.Segments[0].To);

        Assert.True(
            fixture.Network.SetEdgeEnabled(
                primaryEntry,
                enabled: false));

        RunUntil(
            fixture,
            () =>
                fixture.TransportSystem.Metrics.RerouteCount > 0);

        RunUntilOrderCompletes(
            fixture,
            truck,
            maximumTicks: 800);

        Assert.True(
            fixture.TransportSystem.Metrics.RerouteCount > 0);
        Assert.Equal(
            25.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.GetTruckCargo(truck));
    }

    [Fact]
    public void DestroyedTransportDestroysCargoInventoryAndAccountsForLoss()
    {
        TransportFixture fixture = CreateFixture(
            sourceQuantity: 50.0);

        fixture.ConnectDirect();

        EntityId truck = fixture.CreateTruckAtSource();

        Assert.True(
            fixture.TransportSystem.TryAssignOrder(
                fixture.Simulation.Entities,
                truck,
                new CargoTransportOrder(
                    fixture.SourceNode,
                    fixture.DestinationNode,
                    ResourceIds.FerrousOre,
                    requestedQuantity: 30.0,
                    SimulationTick.Zero),
                SimulationTick.Zero));

        RunUntil(
            fixture,
            () =>
                fixture.GetTruckCargo(truck) >= 30.0);

        CargoTransport transport =
            fixture.Simulation.Entities.GetComponent<
                CargoTransport>(truck);
        InventoryId cargoInventory =
            transport.CargoInventory;

        Assert.True(
            fixture.Simulation.Entities.DestroyEntity(
                truck));

        fixture.Simulation.AdvanceOneTick();

        Assert.False(
            fixture.Inventories.Contains(
                cargoInventory));
        Assert.Equal(
            30.0,
            fixture.TransportSystem.Metrics.LostQuantity);
        Assert.Equal(
            20.0,
            fixture.Inventories.GetQuantity(
                fixture.SourceInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0.0,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
    }

    [Fact]
    public void HundredsOfTransportsCompleteWithoutResourceDrift()
    {
        const int transportCount = 200;
        const double quantityPerTransport = 10.0;

        TransportFixture fixture = CreateFixture(
            sourceQuantity:
                transportCount * quantityPerTransport,
            sourceCapacity:
                transportCount * quantityPerTransport + 100.0,
            destinationCapacity:
                transportCount * quantityPerTransport + 100.0,
            destinationPosition:
                new Vector3(28.0f, 0.0f, 4.0f));

        fixture.ConnectDirect();

        var trucks =
            new EntityId[transportCount];

        for (int index = 0;
             index < transportCount;
             index++)
        {
            EntityId truck =
                fixture.CreateTruckAtSource();
            trucks[index] = truck;

            Assert.True(
                fixture.TransportSystem.TryAssignOrder(
                    fixture.Simulation.Entities,
                    truck,
                    new CargoTransportOrder(
                        fixture.SourceNode,
                        fixture.DestinationNode,
                        ResourceIds.FerrousOre,
                        quantityPerTransport,
                        SimulationTick.Zero),
                    SimulationTick.Zero));
        }

        double before =
            fixture.TotalConservedQuantity(
                trucks);

        RunUntil(
            fixture,
            () =>
                fixture.TransportSystem.Metrics.CompletedOrderCount ==
                transportCount,
            maximumTicks: 600);

        double after =
            fixture.TotalConservedQuantity(
                trucks);

        Assert.Equal(before, after);
        Assert.Equal(
            transportCount * quantityPerTransport,
            fixture.Inventories.GetQuantity(
                fixture.DestinationInventory,
                ResourceIds.FerrousOre));
        Assert.Equal(
            0,
            fixture.TransportSystem.Metrics.ActiveTransportCount);
        Assert.Equal(
            transportCount,
            fixture.TransportSystem.Metrics.CompletedOrderCount);
    }

    private static TransportFixture CreateFixture(
        double sourceQuantity,
        double sourceCapacity = 500.0,
        double destinationCapacity = 500.0,
        Vector3? sourcePosition = null,
        Vector3? destinationPosition = null,
        int worldChunkCountX = 2,
        AxisAlignedBounds[]? navigationObstacles = null)
    {
        TerrainWorld terrain =
            CreateFlatWorld(
                worldChunkCountX);

        NavigationWorld navigationWorld =
            NavigationWorld.Build(
                terrain,
                navigationObstacles,
                gridSettings:
                    new NavigationGridSettings
                    {
                        CellSizeMeters = navigationObstacles is null ? 4.0f : 1.0f,
                        StaticObstacleClearanceMeters = navigationObstacles is null ? 0.5f : 3.0f
                    },
                sectorSettings:
                    new NavigationSectorSettings
                    {
                        SectorSizeCells = navigationObstacles is null ? 4 : 8
                    });

        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var network =
            new LogisticsNetwork();
        var transportSystem =
            new CargoTransportSystem(
                network,
                inventories);

        SpatialGridIndex? spatialIndex = null;
        if (navigationObstacles is not null)
        {
            spatialIndex = new SpatialGridIndex(new SpatialGridSettings { World = terrain.Settings, CellSizeMeters = 8 });
            var synchronizer = new SpatialIndexSynchronizer(spatialIndex);
            simulation.RegisterSystem(new SpatialIndexSystem(synchronizer));
            simulation.RegisterSystem(new SpatialIndexCleanupSystem(synchronizer));
            foreach (var bounds in navigationObstacles)
            {
                EntityId obstacle = simulation.Entities.CreateEntity();
                simulation.Entities.AddComponent(obstacle, new WorldTransform((bounds.Minimum + bounds.Maximum) * .5f,
                    Quaternion.Identity, Vector3.One));
                simulation.Entities.AddComponent(obstacle, new SpatialPresence((bounds.Maximum - bounds.Minimum) * .5f,
                    new SpatialEntryMetadata(1, 0, SpatialMobility.Static)));
            }
        }
        simulation.RegisterSystem(new CargoConvoySystem(network));
        simulation.RegisterSystem(
            new HierarchicalNavigationSystem(
                new HierarchicalPathfinder(
                    navigationWorld)));
        simulation.RegisterSystem(
            new GroundMovementSystem(
                terrain, spatialIndex));
        simulation.RegisterSystem(
            transportSystem);

        Vector3 resolvedSourcePosition =
            sourcePosition ??
            new Vector3(
                4.0f,
                0.0f,
                4.0f);
        Vector3 resolvedDestinationPosition =
            destinationPosition ??
            new Vector3(
                52.0f,
                0.0f,
                4.0f);

        InventoryId sourceInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    sourceCapacity));
        InventoryId destinationInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    destinationCapacity));

        Assert.True(
            inventories.Add(
                sourceInventory,
                ResourceIds.FerrousOre,
                sourceQuantity).Succeeded);

        EntityId sourceEntity =
            CreateInventoryEntity(
                simulation,
                sourceInventory,
                resolvedSourcePosition);
        EntityId destinationEntity =
            CreateInventoryEntity(
                simulation,
                destinationInventory,
                resolvedDestinationPosition);

        LogisticsNodeId sourceNode =
            network.AddNode(
                sourceEntity,
                resolvedSourcePosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);
        LogisticsNodeId destinationNode =
            network.AddNode(
                destinationEntity,
                resolvedDestinationPosition,
                LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution);

        return new TransportFixture(
            terrain,
            simulation,
            inventories,
            network,
            transportSystem,
            sourceInventory,
            destinationInventory,
            sourceNode,
            destinationNode,
            resolvedSourcePosition);
    }

    private static EntityId CreateInventoryEntity(
        SimulationCoordinator simulation,
        InventoryId inventoryId,
        Vector3 position)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new InventoryStorage(
                inventoryId));
        simulation.Entities.AddComponent(
            entity,
            new StorageDepot(
                inventoryId,
                new FactionId(1)));
        return entity;
    }

    private static void RunUntilOrderCompletes(
        TransportFixture fixture,
        EntityId truck,
        int maximumTicks = 500)
    {
        RunUntil(
            fixture,
            () =>
                !fixture.Simulation.Entities.IsAlive(
                    truck) ||
                !fixture.Simulation.Entities.HasComponent<
                    CargoTransportOrder>(truck),
            maximumTicks);

        Assert.True(
            fixture.Simulation.Entities.IsAlive(
                truck));
        Assert.False(
            fixture.Simulation.Entities.HasComponent<
                CargoTransportOrder>(truck));

        CargoTransportRuntimeState state =
            fixture.Simulation.Entities.GetComponent<
                CargoTransportRuntimeState>(truck);
        Assert.Equal(
            CargoTransportLifecycleState.Idle,
            state.Lifecycle);
    }

    private static void RunUntil(
        TransportFixture fixture,
        Func<bool> condition,
        int maximumTicks = 500)
    {
        for (int tick = 0;
             tick < maximumTicks && !condition();
             tick++)
        {
            fixture.Simulation.AdvanceOneTick();
        }

        Assert.True(
            condition(),
            $"Condition was not reached within {maximumTicks} simulation ticks.");
    }

    private static TerrainWorld CreateFlatWorld(
        int chunkCountX)
    {
        var settings =
            new WorldGridSettings
            {
                ChunkSizeMeters = 32.0f,
                HeightSamplesPerSide = 9
            };

        var chunks =
            new List<TerrainChunk>(
                chunkCountX);

        for (int x = 0;
             x < chunkCountX;
             x++)
        {
            int sampleCount =
                settings.HeightSamplesPerSide *
                settings.HeightSamplesPerSide;

            chunks.Add(
                new TerrainChunk(
                    new ChunkCoordinate(
                        x,
                        0),
                    new TerrainHeightfield(
                        settings.HeightSamplesPerSide,
                        settings.ChunkSizeMeters,
                        new float[sampleCount])));
        }

        return new TerrainWorld(
            settings,
            chunks);
    }

    private sealed class TransportFixture
    {
        public TransportFixture(
            TerrainWorld terrain,
            SimulationCoordinator simulation,
            InventoryStore inventories,
            LogisticsNetwork network,
            CargoTransportSystem transportSystem,
            InventoryId sourceInventory,
            InventoryId destinationInventory,
            LogisticsNodeId sourceNode,
            LogisticsNodeId destinationNode,
            Vector3 sourcePosition)
        {
            Terrain = terrain;
            Simulation = simulation;
            Inventories = inventories;
            Network = network;
            TransportSystem = transportSystem;
            SourceInventory = sourceInventory;
            DestinationInventory = destinationInventory;
            SourceNode = sourceNode;
            DestinationNode = destinationNode;
            SourcePosition = sourcePosition;
        }

        public TerrainWorld Terrain { get; }

        public SimulationCoordinator Simulation { get; }

        public InventoryStore Inventories { get; }

        public LogisticsNetwork Network { get; }

        public CargoTransportSystem TransportSystem { get; }

        public InventoryId SourceInventory { get; }

        public InventoryId DestinationInventory { get; }

        public LogisticsNodeId SourceNode { get; }

        public LogisticsNodeId DestinationNode { get; }

        public Vector3 SourcePosition { get; }

        public void ConnectDirect()
        {
            LogisticsNode source =
                GetNode(
                    SourceNode);
            LogisticsNode destination =
                GetNode(
                    DestinationNode);

            double distance =
                Vector3.Distance(
                    source.WorldPosition,
                    destination.WorldPosition);

            Network.AddEdge(
                SourceNode,
                DestinationNode,
                LogisticsTransportMode.GroundRoad,
                distanceMeters: distance,
                baseCost: distance,
                capacityPerSecond: 100.0);
        }

        public LogisticsNodeId AddIntermediateNode(
            Vector3 position)
        {
            EntityId entity =
                Simulation.Entities.CreateEntity();
            Simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));

            return Network.AddNode(
                entity,
                position,
                LogisticsNodeKind.LogisticsHub,
                LogisticsNodeCapabilities.Distribution);
        }

        public EntityId CreateTruckAtSource()
        {
            return CargoTruckFactory.Create(
                Simulation.Entities,
                Inventories,
                SourcePosition,
                LocalPlayer,
                transportSystem:
                    TransportSystem);
        }

        public double GetTruckCargo(
            EntityId truck)
        {
            if (!Simulation.Entities.IsAlive(truck) ||
                !Simulation.Entities.TryGetComponent(
                    truck,
                    out CargoTransport transport) ||
                !Inventories.Contains(
                    transport.CargoInventory))
            {
                return 0.0;
            }

            return Inventories.GetTotalQuantity(
                transport.CargoInventory);
        }

        public double TotalConservedQuantity(
            params EntityId[] trucks)
        {
            double total =
                Inventories.GetTotalQuantity(
                    SourceInventory) +
                Inventories.GetTotalQuantity(
                    DestinationInventory);

            for (int index = 0;
                 index < trucks.Length;
                 index++)
            {
                total += GetTruckCargo(
                    trucks[index]);
            }

            return total;
        }

        private LogisticsNode GetNode(
            LogisticsNodeId id)
        {
            Assert.True(
                Network.TryGetNode(
                    id,
                    out LogisticsNode node));
            return node;
        }
    }
}
