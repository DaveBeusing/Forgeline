using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishRaidPlannerTests
{
    [Theory]
    [InlineData(false, IntelligenceState.Identified)]
    [InlineData(true, IntelligenceState.Detected)]
    public void MissingOrDetectedOnlyContactsCannotStartTargetedRaids(bool observe, IntelligenceState state)
    {
        var fixture = new Fixture { Observe = observe, Observation = state };
        fixture.Step();
        Assert.False(fixture.State.IsActive);
        Assert.All(fixture.Units, unit => Assert.False(fixture.Entities.HasComponent<CombatOrderState>(unit)));
    }

    [Fact]
    public void IdentifiedTruckGetsTwoRaidersWhileHomeAndScoutStayReserved()
    {
        var fixture = new Fixture();
        EntityId scout = fixture.CreateUnit(UnitIds.ScoutVehicle, new(90, 0, 90));
        fixture.Step();
        Assert.True(fixture.State.Phase == SkirmishRaidPhase.Approaching, fixture.Diagnostic.ToString());
        Assert.Equal(fixture.Truck, fixture.State.Target);
        Assert.True(fixture.State.First.IsValid && fixture.State.Second.IsValid);
        Assert.False(fixture.State.Third.IsValid);
        Assert.False(fixture.State.Contains(scout));
        Assert.Equal(2, fixture.Units.Count(unit => !fixture.State.Contains(unit)));
        Assert.Equal(CombatOrderKind.AttackMove, fixture.Entities.GetComponent<CombatOrderState>(fixture.State.First).Kind);
    }

    [Fact]
    public void UnchangedTargetKeepsOneGroupAcrossDecisionsAndDirectAttackRequiresRange()
    {
        var fixture = new Fixture();
        fixture.Step();
        EntityId group = fixture.State.Group;
        fixture.Step(5);
        Assert.Equal(group, fixture.State.Group);
        foreach (EntityId unit in fixture.Raiders())
            fixture.Entities.SetComponent(unit, new WorldTransform(new(590, 0, 128), Quaternion.Identity, Vector3.One));
        fixture.Step();
        Assert.Equal(SkirmishRaidPhase.Attacking, fixture.State.Phase);
        var order = fixture.Entities.GetComponent<CombatOrderState>(fixture.State.First);
        Assert.Equal(CombatOrderKind.Attack, order.Kind);
        Assert.Equal(fixture.Truck, order.ExplicitTarget);
        group = fixture.State.Group;
        fixture.Step(3);
        Assert.Equal(group, fixture.State.Group);
    }

    [Fact]
    public void HiddenTargetMovementCannotUpdateSearchPointOrKeepDirectAttack()
    {
        var fixture = new Fixture();
        fixture.Step();
        var remembered = fixture.State.LastKnownPosition;
        foreach (EntityId unit in fixture.Raiders())
            fixture.Entities.SetComponent(unit, new WorldTransform(new(550, 0, 128), Quaternion.Identity, Vector3.One));
        fixture.Observe = false;
        fixture.Entities.SetComponent(fixture.Truck, new WorldTransform(new(900, 0, 128), Quaternion.Identity, Vector3.One));
        fixture.Step();
        Assert.Equal(SkirmishRaidPhase.Searching, fixture.State.Phase);
        Assert.Equal(remembered, fixture.State.LastKnownPosition);
        Assert.Equal(remembered, fixture.Entities.GetComponent<CombatOrderState>(fixture.State.First).Destination);
        fixture.Step(81);
        Assert.Equal(SkirmishRaidPhase.Returning, fixture.State.Phase);
        Assert.Equal(SkirmishRaidReason.ContactLost, fixture.State.Reason);
    }

    [Fact]
    public void GuardedOrReroutedTruckProvidesCounterplay()
    {
        var fixture = new Fixture();
        fixture.Step();
        fixture.Guard = fixture.CreateUnit(UnitIds.MainBattleTank, new(610, 0, 128), new PlayerId(2));
        fixture.Step();
        Assert.Equal(SkirmishRaidPhase.Returning, fixture.State.Phase);
        Assert.Equal(SkirmishRaidReason.KnownDefense, fixture.State.Reason);

        var reroute = new Fixture();
        reroute.Step();
        reroute.Entities.SetComponent(reroute.Truck, new WorldTransform(new(750, 0, 128), Quaternion.Identity, Vector3.One));
        reroute.Step();
        Assert.Equal(SkirmishRaidPhase.Returning, reroute.State.Phase);
        Assert.Equal(SkirmishRaidReason.TargetEscaped, reroute.State.Reason);
    }

    [Fact]
    public void SupplyAndUnknownRoutesFailClosed()
    {
        var fuel = new Fixture(UnitIds.MainBattleTank, ownCount: 7);
        foreach (EntityId unit in fuel.Units)
        {
            var tank = fuel.Entities.GetComponent<UnitFuelState>(unit);
            Assert.True(fuel.Inventories.Remove(tank.InventoryId, ResourceIds.Fuel,
                fuel.Inventories.GetQuantity(tank.InventoryId, ResourceIds.Fuel) - 1).Succeeded);
        }
        fuel.Step();
        Assert.False(fuel.State.IsActive);
        Assert.Equal(SkirmishRaidReason.FuelReserve, fuel.Diagnostic.AdmissionReason);

        var ammunition = new Fixture();
        foreach (EntityId unit in ammunition.Units)
        {
            var ammo = ammunition.Entities.GetComponent<AmmunitionState>(unit);
            Assert.True(ammunition.Inventories.Remove(ammo.InventoryId, ResourceIds.Ammunition,
                ammunition.Inventories.GetQuantity(ammo.InventoryId, ResourceIds.Ammunition)).Succeeded);
        }
        ammunition.Step();
        Assert.False(ammunition.State.IsActive);

        var route = new Fixture();
        route.BlockRoute(); route.Step();
        Assert.False(route.State.IsActive);
        Assert.Equal(SkirmishRaidReason.UnknownRoute, route.Diagnostic.AdmissionReason);
    }

    [Fact]
    public void RifleWeaponsCannotBeAssignedAgainstStructuresAndLastPressureTankStaysHome()
    {
        var fixture = new Fixture();
        fixture.Entities.AddComponent(fixture.Truck, new CompletedBuilding(BuildingIds.PowerPlant, new PlayerId(2), SimulationTick.Zero));
        fixture.Entities.RemoveComponent<UnitIdentity>(fixture.Truck);
        fixture.Entities.SetComponent(fixture.Truck, new Targetable(TargetClass.Structure));
        fixture.Step();
        Assert.False(fixture.State.IsActive);
        var mixed = new Fixture();
        EntityId tank = mixed.CreateUnit(UnitIds.MainBattleTank, new(100, 0, 100));
        mixed.Units.Add(tank);
        mixed.Step();
        Assert.False(mixed.State.Contains(tank));
    }

    [Fact]
    public void DefensePriorityAndExternalOrdersDoNotDeleteOtherCommandOwnership()
    {
        var fixture = new Fixture();
        fixture.Step(); fixture.Priority = true; fixture.Step();
        Assert.Equal(SkirmishRaidPhase.Complete, fixture.State.Phase);
        Assert.Equal(SkirmishRaidReason.Priority, fixture.State.Reason);
        fixture.Priority = false; fixture.Step();
        Assert.False(fixture.State.IsActive);
        Assert.Equal(SkirmishRaidReason.Cooldown, fixture.Diagnostic.AdmissionReason);

        var replaced = new Fixture(); replaced.Step();
        var original = replaced.State;
        replaced.BeforeDecision = context => new HoldPositionCommand(new PlayerId(1),
            [original.First, original.Second], context.Tick).Execute(context);
        replaced.Step();
        Assert.Equal(SkirmishRaidReason.OrdersReplaced, replaced.State.Reason);
        Assert.Equal(CombatOrderKind.HoldPosition, replaced.Entities.GetComponent<CombatOrderState>(original.First).Kind);
    }

    [Fact]
    public void ActiveTimeAndNavigationLossBoundTheMission()
    {
        var fixture = new Fixture(configuration: new() { Raids = new() { ActiveTicks = 3 } });
        fixture.Step(4);
        Assert.Equal(SkirmishRaidPhase.Returning, fixture.State.Phase);
        Assert.Equal(SkirmishRaidReason.DurationExpired, fixture.State.Reason);
        var blocked = new Fixture(); blocked.Step(); blocked.BlockRoute(); blocked.Step();
        Assert.Equal(SkirmishRaidPhase.Returning, blocked.State.Phase);
        Assert.Equal(SkirmishRaidReason.UnknownRoute, blocked.State.Reason);
    }

    [Fact]
    public void StableIdentityAndSeedProduceTheSameIntent()
    {
        var first = new Fixture(); var second = new Fixture();
        first.Step(8); second.Step(8);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Diagnostic, second.Diagnostic);
    }

    [Fact]
    public void DestroyedTargetGenerationCannotBeReusedAsAnAttackTarget()
    {
        var fixture = new Fixture(); fixture.Step();
        EntityId old = fixture.Truck;
        fixture.Observe = false;
        Assert.True(fixture.Entities.DestroyEntity(old));
        EntityId replacement = fixture.CreateUnit(UnitIds.CargoTruck, new(600, 0, 128), new PlayerId(2));
        Assert.NotEqual(old, replacement);
        fixture.Step();
        Assert.Equal(SkirmishRaidPhase.Searching, fixture.State.Phase);
        fixture.Step(80);
        Assert.Equal(SkirmishRaidReason.ContactLost, fixture.State.Reason);
    }

    [Fact]
    public void DistantRifleRaidAndLossOfReturnFuelAreRejectedWithoutFreeRefill()
    {
        var far = new Fixture();
        far.Entities.SetComponent(far.Truck, new WorldTransform(new(900, 0, 128), Quaternion.Identity, Vector3.One));
        far.Step();
        Assert.False(far.State.IsActive);
        Assert.Equal(SkirmishRaidReason.FuelReserve, far.Diagnostic.AdmissionReason);
        var stranded = new Fixture(); stranded.Step();
        foreach (EntityId unit in stranded.Raiders())
        {
            var fuel = stranded.Entities.GetComponent<UnitFuelState>(unit);
            Assert.True(stranded.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel,
                stranded.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel)).Succeeded);
        }
        stranded.Step();
        Assert.Equal(SkirmishRaidPhase.Resupplying, stranded.State.Phase);
        Assert.Equal(SkirmishRaidReason.FuelReserve, stranded.State.Reason);
        stranded.Step(4);
        Assert.All(stranded.Raiders(), unit => Assert.Equal(0,
            stranded.Inventories.GetQuantity(stranded.Entities.GetComponent<UnitFuelState>(unit).InventoryId, ResourceIds.Fuel)));
    }

    [Fact]
    public void CandidateWindowRotatesAndAVisibleGuardOutsideItStillPreventsAdmission()
    {
        var fixture = new Fixture(configuration: new() { Raids = new() { MaximumContacts = 1 } });
        EntityId decoy = fixture.CreateUnit(UnitIds.CargoTruck, new(1200, 0, 128), new PlayerId(2));
        fixture.BeforeDecision = context => fixture.Intelligence.Observe(new FactionId(1), decoy,
            fixture.Entities.GetComponent<IntelligenceSignature>(decoy), new(1200, 0, 128), IntelligenceState.Identified, context.Tick);
        fixture.Step();
        // Contact identity ordering is stable; whichever contact comes first, one bounded window reaches the truck.
        fixture.Step();
        Assert.True(fixture.State.IsActive);
        Assert.Equal(fixture.Truck, fixture.State.Target);
        Assert.Equal(1, fixture.Diagnostic.ContactsConsidered);
        var guarded = new Fixture(configuration: new() { Raids = new() { MaximumContacts = 1 } });
        guarded.Guard = guarded.CreateUnit(UnitIds.MainBattleTank, new(610, 0, 128), new PlayerId(2));
        guarded.Step(3);
        Assert.False(guarded.State.IsActive);
    }

    private sealed class Fixture
    {
        public SimulationCoordinator Simulation { get; } = new(20, 17);
        public EntityRegistry Entities => Simulation.Entities;
        public InventoryStore Inventories { get; } = new();
        public FactionIntelligenceStore Intelligence { get; } = new();
        public List<EntityId> Units { get; } = new();
        public EntityId Truck { get; }
        public EntityId Guard { get; set; }
        public EntityId Controller { get; }
        public SkirmishRaidPlanner Planner { get; }
        public HierarchicalNavigationSystem Navigation { get; }
        public bool Observe { get; set; } = true;
        public IntelligenceState Observation { get; set; } = IntelligenceState.Identified;
        public bool Priority { get; set; }
        public Action<SimulationContext>? BeforeDecision { get; set; }
        private readonly UnitFactory _factory;
        private readonly MatchContent _content;
        private readonly TerrainWorld _terrain;
        private readonly SkirmishOpponentConfiguration _configuration;
        public SkirmishRaidState State => Entities.TryGetComponent(Controller, out SkirmishRaidState state) ? state : default;
        public SkirmishRaidReadModel Diagnostic => Planner.Capture(Controller);

        public Fixture(UnitId? ownUnit = null, int ownCount = 4, SkirmishOpponentConfiguration? configuration = null)
        {
            _configuration = configuration ?? new();
            _content = CentralDivideScenario.CreateComposition().Content;
            _factory = new UnitFactory(Entities, Inventories, new CargoTransportSystem(new LogisticsNetwork(), Inventories));
            var grid = new WorldGridSettings { ChunkSizeMeters = 256, HeightSamplesPerSide = 17 };
            _terrain = new TerrainWorld(grid, Enumerable.Range(0, 5).Select(x =>
                new TerrainChunk(new ChunkCoordinate(x, 0), new TerrainHeightfield(17, 256, new float[17 * 17]))));
            Navigation = new(new HierarchicalPathfinder(NavigationWorld.Build(_terrain,
                gridSettings: new() { CellSizeMeters = 8 }, sectorSettings: new() { SectorSizeCells = 4 })));
            Planner = new(Inventories, _content.Weapons, Intelligence, Navigation);
            var payer = Entities.CreateEntity();
            Controller = Entities.CreateEntity();
            Entities.AddComponent(Controller, new SkirmishOpponentController(new PlayerId(1), new FactionId(1), payer, new(360, 0, 128)));
            for (int i = 0; i < ownCount; i++) Units.Add(CreateUnit(ownUnit ?? UnitIds.RifleSquad, new(360 + i * 6, 0, 128)));
            Truck = CreateUnit(UnitIds.CargoTruck, new(600, 0, 128), new PlayerId(2));
            Simulation.RegisterSystem(new Decision(this));
        }

        public EntityId CreateUnit(UnitId unit, Vector3 point, PlayerId? owner = null)
        {
            EntityId result = _factory.Create(_content.Units[unit], point, owner ?? new PlayerId(1));
            Entities.AddComponent(result, new UnitCombatReadiness(1, 1, 1, 1, 1, 1, 1, 1, 1, SimulationTick.Zero));
            return result;
        }

        public IEnumerable<EntityId> Raiders() => Units.Where(State.Contains);
        public void Step(int ticks = 1) => Simulation.RunTicks((ulong)ticks, TestContext.Current.CancellationToken);
        public void BlockRoute() => Navigation.UpdateWorld(NavigationWorld.Build(_terrain,
            [new AxisAlignedBounds(new(480, -10, 0), new(512, 100, 256))],
            new() { CellSizeMeters = 8 }, new() { SectorSizeCells = 4 }, new NavigationVersion(2)));

        private sealed class Decision(Fixture fixture) : ISimulationSystem
        {
            public SimulationPhase Phase => SimulationPhase.AiDecisions;
            public void Execute(SimulationContext context)
            {
                fixture.Intelligence.BeginTick(context.Tick);
                if (fixture.Observe)
                {
                    Observe(fixture.Truck);
                    if (fixture.Guard.IsValid) Observe(fixture.Guard);
                }
                fixture.BeforeDecision?.Invoke(context);
                fixture.BeforeDecision = null;
                fixture.Planner.Evaluate(context, fixture.Controller,
                    context.Entities.GetComponent<SkirmishOpponentController>(fixture.Controller), fixture.Units,
                    fixture.Intelligence.Capture(new FactionId(1)), fixture._configuration, fixture.Priority);
                void Observe(EntityId entity) => fixture.Intelligence.Observe(new FactionId(1), entity,
                    context.Entities.GetComponent<IntelligenceSignature>(entity), context.Entities.GetComponent<WorldTransform>(entity).Position,
                    fixture.Observation, context.Tick);
            }
        }
    }
}
