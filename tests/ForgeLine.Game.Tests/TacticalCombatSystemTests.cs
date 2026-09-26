using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TacticalCombatSystemTests
{
    private static readonly PlayerId BluePlayer = new(1);
    private static readonly PlayerId RedPlayer = new(2);
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void AttackUsesCurrentIntelligenceAndPursuesWithinLeash()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 30.0f);

        EntityId attacker =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 150.0f,
                movable: true);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(60.0f, 0.0f, 0.0f));

        var command =
            new AttackCommand(
                BluePlayer,
                [attacker],
                target,
                scenario.Simulation.CurrentTick,
                pursuitLeashMeters: 100.0f);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        TacticalCombatState state =
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker);

        Assert.Equal(
            CombatOrderStatus.Pursuing,
            state.Status);
        Assert.True(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                attacker));
        Assert.False(
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                attacker).Target.IsValid);

        scenario.Simulation.Entities.SetComponent(
            target,
            new WorldTransform(
                new Vector3(15.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));

        scenario.Simulation.AdvanceOneTick();

        state =
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker);

        Assert.Equal(
            CombatOrderStatus.Engaging,
            state.Status);
        Assert.Equal(
            target,
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                attacker).Target);
        Assert.False(
            scenario.Simulation.Entities.GetComponent<TacticalMovementConstraint>(
                attacker).CanMove);
    }

    [Fact]
    public void AttackDoesNotChaseBeyondPursuitLeash()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 30.0f);

        EntityId attacker =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 250.0f);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(120.0f, 0.0f, 0.0f));

        scenario.Simulation.SubmitCommand(
            new AttackCommand(
                BluePlayer,
                [attacker],
                target,
                scenario.Simulation.CurrentTick,
                pursuitLeashMeters: 50.0f),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        TacticalCombatState state =
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker);

        Assert.Equal(
            CombatOrderStatus.Holding,
            state.Status);
        Assert.False(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                attacker));
        Assert.False(
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                attacker).Target.IsValid);
    }

    [Fact]
    public void AttackMovePausesForEngagementAndResumesAfterVisibilityLoss()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 35.0f,
                registerGroundMovement: true);

        EntityId attacker =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 80.0f,
                movable: true);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(20.0f, 0.0f, 0.0f));

        scenario.Simulation.SubmitCommand(
            new AttackMoveCommand(
                BluePlayer,
                [attacker],
                new Vector3(160.0f, 0.0f, 0.0f),
                scenario.Simulation.CurrentTick,
                pursuitLeashMeters: 80.0f),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            CombatOrderStatus.Engaging,
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker).Status);
        Assert.False(
            scenario.Simulation.Entities.GetComponent<TacticalMovementConstraint>(
                attacker).CanMove);

        Vector3 before =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                attacker).Position;

        scenario.Simulation.Entities.SetComponent(
            target,
            new WorldTransform(
                new Vector3(140.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            CombatOrderStatus.Advancing,
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker).Status);
        Assert.True(
            scenario.Simulation.Entities.GetComponent<TacticalMovementConstraint>(
                attacker).CanMove);

        scenario.Simulation.AdvanceOneTick();

        Vector3 after =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                attacker).Position;

        Assert.True(
            after.X > before.X);
    }

    [Fact]
    public void HoldPositionNeverPursuesTarget()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 40.0f);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 100.0f,
                movable: true);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(25.0f, 0.0f, 0.0f));

        scenario.Simulation.SubmitCommand(
            new HoldPositionCommand(
                BluePlayer,
                [unit],
                scenario.Simulation.CurrentTick),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            target,
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                unit).Target);
        Assert.False(
            scenario.Simulation.Entities.GetComponent<TacticalMovementConstraint>(
                unit).CanMove);
        Assert.False(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                unit));
    }

    [Fact]
    public void StopCancelsMovementAndTargeting()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 40.0f);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(20.0f, 0.0f, 0.0f));

        scenario.Simulation.Entities.SetComponent(
            unit,
            scenario.Simulation.Entities.GetComponent<WeaponState>(unit) with
            {
                Target = target
            });
        scenario.Simulation.Entities.AddComponent(
            unit,
            new MovementOrder(
                BluePlayer,
                new Vector3(100.0f, 0.0f, 0.0f),
                SimulationTick.Zero,
                SimulationTick.Zero));

        scenario.Simulation.SubmitCommand(
            new StopCombatCommand(
                BluePlayer,
                [unit],
                scenario.Simulation.CurrentTick),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        Assert.False(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                unit));
        Assert.False(
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                unit).Target.IsValid);
        Assert.Equal(
            CombatOrderStatus.Complete,
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                unit).Status);
    }

    [Fact]
    public void GroupTargetAssignmentSpreadsCompatibleUnits()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 80.0f);

        EntityId first =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 100.0f);
        EntityId second =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero);
        EntityId firstTarget =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(30.0f, 0.0f, 0.0f));
        EntityId secondTarget =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(30.0f, 0.0f, 1.0f));

        scenario.Simulation.SubmitCommand(
            new AttackMoveCommand(
                BluePlayer,
                [first, second],
                new Vector3(120.0f, 0.0f, 0.0f),
                scenario.Simulation.CurrentTick),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        EntityId firstAssigned =
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                first).Target;
        EntityId secondAssigned =
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                second).Target;

        Assert.True(firstAssigned.IsValid);
        Assert.True(secondAssigned.IsValid);
        Assert.NotEqual(
            firstAssigned,
            secondAssigned);
        Assert.Contains(
            firstAssigned,
            new[]
            {
                firstTarget,
                secondTarget
            });
        Assert.Contains(
            secondAssigned,
            new[]
            {
                firstTarget,
                secondTarget
            });
    }

    [Fact]
    public void ReadinessUsesHealthFuelAmmunitionMobilityAndWeaponState()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f,
                registerReadiness: true);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true,
                health: 50.0,
                maximumHealth: 100.0,
                attachSupply: true,
                fuelCapacity: 100.0,
                initialFuel: 0.0,
                ammunitionCapacity: 100.0,
                initialAmmunition: 25.0);

        scenario.Simulation.AdvanceOneTick();

        UnitCombatReadiness readiness =
            scenario.Simulation.Entities.GetComponent<UnitCombatReadiness>(
                unit);

        Assert.Equal(
            0.5,
            readiness.Health,
            precision: 6);
        Assert.Equal(
            0.25,
            readiness.Ammunition,
            precision: 6);
        Assert.Equal(
            0.0,
            readiness.Fuel,
            precision: 6);
        Assert.Equal(
            0.0,
            readiness.Mobility,
            precision: 6);
        Assert.Equal(
            1.0,
            readiness.WeaponAvailability,
            precision: 6);
        Assert.True(
            readiness.OverallReadiness < 0.5);
    }

    [Fact]
    public void AutomaticResupplyUsesRealProviderAndSupplyTransfer()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f,
                registerAutomaticResupply: true,
                registerBattlefieldSupply: true);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true,
                attachSupply: true,
                fuelCapacity: 100.0,
                initialFuel: 10.0,
                ammunitionCapacity: 100.0,
                initialAmmunition: 10.0);

        scenario.Simulation.Entities.AddComponent(
            unit,
            new AutomaticResupplyPolicy(
                ammunitionThreshold: 0.2,
                fuelThreshold: 0.2));

        InventoryId providerInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    400.0,
                    [ResourceIds.Fuel, ResourceIds.Ammunition]));
        Assert.True(
            scenario.Inventories.Add(
                providerInventory,
                ResourceIds.Fuel,
                100.0).Succeeded);
        Assert.True(
            scenario.Inventories.Add(
                providerInventory,
                ResourceIds.Ammunition,
                100.0).Succeeded);

        EntityId provider =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            provider,
            new WorldTransform(
                new Vector3(5.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));
        scenario.Simulation.Entities.AddComponent(
            provider,
            new SupplyProvider(
                providerInventory,
                BluePlayer,
                resupplyRangeMeters: 20.0f));

        scenario.Simulation.AdvanceOneTick();

        AmmunitionState ammunition =
            scenario.Simulation.Entities.GetComponent<AmmunitionState>(
                unit);
        UnitFuelState fuel =
            scenario.Simulation.Entities.GetComponent<UnitFuelState>(
                unit);

        Assert.True(
            scenario.Inventories.GetQuantity(
                ammunition.InventoryId,
                ResourceIds.Ammunition) > 10.0);
        Assert.True(
            scenario.Inventories.GetQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel) > 10.0);
        Assert.True(
            scenario.AutomaticResupply!.Metrics.TotalOrdersIssued > 0);
        Assert.True(
            scenario.BattlefieldSupply!.Metrics.TotalAmmunitionTransferred > 0.0);
    }

    [Fact]
    public void ActiveCargoTransportRefuelsWithoutDroppingDeliveryOrder()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f,
                registerAutomaticResupply: true,
                registerBattlefieldSupply: true);

        EntityId truck =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true,
                attachSupply: true,
                fuelCapacity: 100.0,
                initialFuel: 20.0,
                ammunitionCapacity: 100.0,
                initialAmmunition: 0.0);

        InventoryId cargoInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(100.0));
        scenario.Simulation.Entities.AddComponent(
            truck,
            new CargoTransport(
                cargoInventory,
                capacity: 100.0,
                BluePlayer));
        scenario.Simulation.Entities.AddComponent(
            truck,
            new CargoTransportOrder(
                new LogisticsNodeId(1),
                new LogisticsNodeId(2),
                ResourceIds.FerrousOre,
                requestedQuantity: 20.0,
                scenario.Simulation.CurrentTick));
        scenario.Simulation.Entities.AddComponent(
            truck,
            new CargoTransportRuntimeState(
                CargoTransportLifecycleState.ToOrigin,
                CargoTransportWaitReason.None,
                CargoTransportFailureReason.None,
                LogisticsNodeId.None,
                LogisticsNetworkVersion.Initial,
                LoadedQuantity: 0.0,
                DeliveredQuantity: 0.0,
                StateChangedAtTick:
                    scenario.Simulation.CurrentTick));
        scenario.Simulation.Entities.AddComponent(
            truck,
            new AutomaticResupplyPolicy(
                ammunitionThreshold: 0.2,
                fuelThreshold: 0.8));

        InventoryId providerInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    200.0,
                    [ResourceIds.Fuel]));
        Assert.True(
            scenario.Inventories.Add(
                providerInventory,
                ResourceIds.Fuel,
                100.0).Succeeded);

        EntityId provider =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            provider,
            new WorldTransform(
                new Vector3(5.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));
        scenario.Simulation.Entities.AddComponent(
            provider,
            new SupplyProvider(
                providerInventory,
                BluePlayer,
                resupplyRangeMeters: 20.0f));

        scenario.Simulation.AdvanceOneTick();

        UnitFuelState fuel =
            scenario.Simulation.Entities.GetComponent<UnitFuelState>(
                truck);

        Assert.Equal(
            100.0,
            scenario.Inventories.GetQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel),
            precision: 6);
        Assert.True(
            scenario.AutomaticResupply!.Metrics.TotalOrdersIssued > 0);
        Assert.True(
            scenario.Simulation.Entities.HasComponent<CargoTransportOrder>(
                truck));
        Assert.Equal(
            CargoTransportLifecycleState.ToOrigin,
            scenario.Simulation.Entities.GetComponent<
                CargoTransportRuntimeState>(truck).Lifecycle);
        Assert.False(
            scenario.Simulation.Entities.HasComponent<ResupplyOrder>(
                truck));
    }

    [Fact]
    public void RetreatClearsTargetAndUsesExistingMovementIntent()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true);

        scenario.Simulation.SubmitCommand(
            new RetreatCommand(
                BluePlayer,
                [unit],
                new Vector3(-100.0f, 0.0f, 0.0f),
                scenario.Simulation.CurrentTick),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            CombatOrderStatus.Retreating,
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                unit).Status);
        Assert.True(
            scenario.Simulation.Entities.GetComponent<TacticalMovementConstraint>(
                unit).CanMove);
        Assert.False(
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                unit).Target.IsValid);
        Assert.True(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                unit));
    }

    [Fact]
    public void TacticalTestOpponentUsesDetectedCoordinateThenIdentifiedTarget()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 80.0f,
                registerReadiness: true,
                registerTestOpponent: true);

        EntityId opponent =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                Vector3.Zero,
                attachSupply: true,
                fuelCapacity: 100.0,
                initialFuel: 100.0,
                ammunitionCapacity: 100.0,
                initialAmmunition: 100.0);
        scenario.Simulation.Entities.AddComponent(
            opponent,
            new TacticalTestOpponent());

        scenario.Simulation.Entities.AddComponent(
            opponent,
            new RadarSensorState(
                RedFaction,
                detectionRangeMeters: 150.0f,
                identificationRangeMeters: 0.0f,
                updateIntervalTicks: 1));

        EntityId target =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                new Vector3(60.0f, 0.0f, 0.0f));

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        CombatOrderState detectedOrder =
            scenario.Simulation.Entities.GetComponent<CombatOrderState>(
                opponent);

        Assert.Equal(
            CombatOrderKind.AttackMove,
            detectedOrder.Kind);
        Assert.False(
            detectedOrder.ExplicitTarget.IsValid);

        scenario.Simulation.Entities.RemoveComponent<RadarSensorState>(
            opponent);
        scenario.Simulation.Entities.AddComponent(
            opponent,
            new VisualSensorState(
                RedFaction,
                rangeMeters: 150.0f,
                updateIntervalTicks: 1));

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        CombatOrderState identifiedOrder =
            scenario.Simulation.Entities.GetComponent<CombatOrderState>(
                opponent);

        Assert.Equal(
            CombatOrderKind.Attack,
            identifiedOrder.Kind);
        Assert.Equal(
            target,
            identifiedOrder.ExplicitTarget);
    }

    [Fact]
    public void GroupReadinessTracksSurvivingStrengthAfterEntityLoss()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f,
                registerReadiness: true);

        EntityId first =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero);
        EntityId second =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                new Vector3(2.0f, 0.0f, 0.0f));

        var command =
            new HoldPositionCommand(
                BluePlayer,
                [first, second],
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        EntityId group =
            command.CreatedCombatGroup;

        Assert.True(group.IsValid);
        Assert.Equal(
            1.0,
            scenario.Simulation.Entities.GetComponent<CombatGroupReadiness>(
                group).Strength,
            precision: 6);

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                second));

        scenario.Simulation.AdvanceOneTick();

        CombatGroupReadiness readiness =
            scenario.Simulation.Entities.GetComponent<CombatGroupReadiness>(
                group);

        Assert.Equal(
            0.5,
            readiness.Strength,
            precision: 6);
        Assert.Equal(
            1,
            readiness.SurvivingMembers);
        Assert.Equal(
            2,
            readiness.InitialMembers);
    }

    [Fact]
    public void NormalMoveOrderReplacesExistingCombatIntent()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 50.0f);

        EntityId unit =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                movable: true);

        scenario.Simulation.Entities.AddComponent(
            unit,
            new CombatOrderState(
                CombatOrderKind.HoldPosition,
                BluePlayer,
                EntityId.Invalid,
                Vector3.Zero,
                hasDestination: false,
                Vector3.Zero,
                pursuitLeashMeters: 0.0f,
                FormationTemplate.Compact,
                SimulationTick.Zero,
                SimulationTick.Zero));
        scenario.Simulation.Entities.AddComponent(
            unit,
            new TacticalMovementConstraint(
                CanMove: false));
        scenario.Simulation.Entities.AddComponent(
            unit,
            new AutoTargetState(
                Enabled: false));

        scenario.Simulation.SubmitCommand(
            new MoveEntitiesCommand(
                BluePlayer,
                [unit],
                new Vector3(80.0f, 0.0f, 0.0f),
                scenario.Simulation.CurrentTick),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        Assert.False(
            scenario.Simulation.Entities.HasComponent<CombatOrderState>(
                unit));
        Assert.False(
            scenario.Simulation.Entities.HasComponent<TacticalMovementConstraint>(
                unit));
        Assert.False(
            scenario.Simulation.Entities.HasComponent<AutoTargetState>(
                unit));
        Assert.True(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                unit));
    }

    [Fact]
    public void HoldFirePreventsExplicitAttackPursuit()
    {
        TacticalScenario scenario =
            CreateScenario(
                weaponRange: 30.0f);

        EntityId attacker =
            CreateCombatUnit(
                scenario,
                BluePlayer,
                BlueFaction,
                Vector3.Zero,
                addVisualSensor: true,
                visualRange: 150.0f,
                movable: true);
        EntityId target =
            CreateCombatUnit(
                scenario,
                RedPlayer,
                RedFaction,
                new Vector3(60.0f, 0.0f, 0.0f));

        scenario.Simulation.Entities.SetComponent(
            attacker,
            new FirePolicyState(
                FirePolicy.HoldFire));

        scenario.Simulation.SubmitCommand(
            new AttackCommand(
                BluePlayer,
                [attacker],
                target,
                scenario.Simulation.CurrentTick,
                pursuitLeashMeters: 100.0f),
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();

        TacticalCombatState state =
            scenario.Simulation.Entities.GetComponent<TacticalCombatState>(
                attacker);

        Assert.Equal(
            CombatOrderStatus.Holding,
            state.Status);
        Assert.False(
            scenario.Simulation.Entities.HasComponent<MovementOrder>(
                attacker));
        Assert.False(
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                attacker).Target.IsValid);
    }

    private static TacticalScenario CreateScenario(
        float weaponRange,
        bool registerGroundMovement = false,
        bool registerReadiness = false,
        bool registerAutomaticResupply = false,
        bool registerBattlefieldSupply = false,
        bool registerTestOpponent = false)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: 4242);
        var inventories =
            new InventoryStore();
        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 16.0f
                });
        var weapons =
            new WeaponCatalog();
        weapons.Add(
            new WeaponDefinition(
                new WeaponId(1),
                weaponRange,
                fireIntervalTicks: 4,
                ammunitionPerShot: 1.0,
                new DamagePayload(10.0),
                WeaponDeliveryModel.Hitscan));

        var intelligenceSystem =
            new BattlefieldIntelligenceSystem(
                intelligence);
        var availability =
            new IntelligenceTargetAvailabilityPolicy(
                simulation.Entities,
                intelligence);
        var targetAcquisition =
            new TargetAcquisitionSystem(
                weapons,
                targetAvailability: availability);
        var tacticalPreparation =
            new TacticalOrderPreparationSystem();
        TacticalTestOpponentSystem? opponent =
            registerTestOpponent
                ? new TacticalTestOpponentSystem(
                    intelligence)
                : null;
        AutomaticResupplyDecisionSystem? autoResupply =
            registerAutomaticResupply ||
            registerTestOpponent
                ? new AutomaticResupplyDecisionSystem(
                    inventories)
                : null;
        var tactical =
            new TacticalCombatSystem(
                weapons,
                intelligence);
        CombatReadinessSystem? readiness =
            registerReadiness ||
            registerTestOpponent
                ? new CombatReadinessSystem(
                    inventories,
                    weapons)
                : null;
        BattlefieldSupplySystem? supply =
            registerBattlefieldSupply
                ? new BattlefieldSupplySystem(
                    inventories)
                : null;

        simulation.RegisterSystem(
            tacticalPreparation);

        if (opponent is not null)
        {
            simulation.RegisterSystem(
                opponent);
        }

        if (autoResupply is not null)
        {
            simulation.RegisterSystem(
                autoResupply);
        }

        if (registerGroundMovement)
        {
            simulation.RegisterSystem(
                new GroundMovementSystem());
        }

        simulation.RegisterSystem(
            intelligenceSystem);
        simulation.RegisterSystem(
            targetAcquisition);
        simulation.RegisterSystem(
            tactical);

        if (supply is not null)
        {
            simulation.RegisterSystem(
                supply);
        }

        if (readiness is not null)
        {
            simulation.RegisterSystem(
                readiness);
        }

        return new TacticalScenario(
            simulation,
            inventories,
            intelligence,
            weapons,
            tactical,
            readiness,
            autoResupply,
            supply,
            opponent);
    }

    private static EntityId CreateCombatUnit(
        in TacticalScenario scenario,
        PlayerId owner,
        FactionId faction,
        Vector3 position,
        bool addVisualSensor = false,
        float visualRange = 100.0f,
        bool movable = false,
        double health = 100.0,
        double maximumHealth = 100.0,
        bool attachSupply = false,
        double fuelCapacity = 100.0,
        double initialFuel = 100.0,
        double ammunitionCapacity = 100.0,
        double initialAmmunition = 100.0)
    {
        EntityId entity =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new ControllableEntity(
                owner,
                ControllableEntityCategory.Unit));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new Combatant(faction));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new IntelligenceSignature(
                faction,
                identityKey:
                    entity.Index + 1));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new Targetable(
                TargetClass.LightVehicle));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new HealthState(
                health,
                maximumHealth));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new WeaponState(
                new WeaponId(1),
                EntityId.Invalid));
        scenario.Simulation.Entities.AddComponent(
            entity,
            FirePolicyState.FireAtWill);

        if (addVisualSensor)
        {
            scenario.Simulation.Entities.AddComponent(
                entity,
                new VisualSensorState(
                    faction,
                    visualRange,
                    updateIntervalTicks: 1));
        }

        if (movable)
        {
            scenario.Simulation.Entities.AddComponent(
                entity,
                GroundMovement.CreateDefault());
            scenario.Simulation.Entities.AddComponent(
                entity,
                GroundMovementState.Stationary());
            scenario.Simulation.Entities.AddComponent(
                entity,
                new NavigationAgent(
                    NavigationMovementClass.Tracked));
        }

        if (attachSupply)
        {
            BattlefieldSupplyFactory.AttachUnitSupply(
                scenario.Simulation.Entities,
                scenario.Inventories,
                entity,
                fuelCapacity,
                ammunitionCapacity,
                fuelConsumptionPerMeter: 0.01,
                initialFuel,
                initialAmmunition);
        }

        return entity;
    }

    private readonly record struct TacticalScenario(
        SimulationCoordinator Simulation,
        InventoryStore Inventories,
        FactionIntelligenceStore Intelligence,
        WeaponCatalog Weapons,
        TacticalCombatSystem Tactical,
        CombatReadinessSystem? Readiness,
        AutomaticResupplyDecisionSystem? AutomaticResupply,
        BattlefieldSupplySystem? BattlefieldSupply,
        TacticalTestOpponentSystem? TestOpponent);
}
