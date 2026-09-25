using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ArmorFacingAndTargetAcquisitionTests
{
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Theory]
    [InlineData(0.0f, 0.0f, -1.0f, ArmorZone.Front)]
    [InlineData(0.0f, 0.0f, 1.0f, ArmorZone.Rear)]
    [InlineData(-1.0f, 0.0f, 0.0f, ArmorZone.Side)]
    [InlineData(1.0f, 0.0f, 0.0f, ArmorZone.Side)]
    [InlineData(0.0f, -1.0f, 0.0f, ArmorZone.Top)]
    public void FacingClassificationUsesAuthoritativeTargetRotation(
        float incomingX,
        float incomingY,
        float incomingZ,
        ArmorZone expected)
    {
        ArmorZone actual =
            ArmorFacing.Classify(
                Quaternion.Identity,
                new Vector3(
                    incomingX,
                    incomingY,
                    incomingZ));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DirectionalArmorMitigatesDamageUsingWeaponPenetration()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var weapons =
            new WeaponCatalog();
        var armor =
            new ArmorCatalog();
        var runtime =
            new CombatRuntime();

        var weapon =
            new WeaponDefinition(
                new WeaponId(1),
                rangeMeters: 100.0f,
                fireIntervalTicks: 20,
                ammunitionPerShot: 1.0,
                new DamagePayload(100.0),
                WeaponDeliveryModel.Hitscan,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.ArmoredVehicle,
                        penetration: 50.0));
        var armorProfile =
            new ArmorProfileDefinition(
                new ArmorProfileId(1),
                front: 100.0,
                side: 50.0,
                rear: 25.0,
                top: 20.0);

        weapons.Add(weapon);
        armor.Add(armorProfile);

        simulation.RegisterSystem(
            new CombatExecutionSystem(
                weapons,
                inventories,
                runtime));
        var damageResolution =
            new CombatDamageResolutionSystem(
                runtime,
                weapons,
                armor);
        simulation.RegisterSystem(
            damageResolution);
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        EntityId target =
            CreateCombatant(
                simulation,
                RedFaction,
                Vector3.Zero,
                TargetClass.ArmoredVehicle);
        simulation.Entities.AddComponent(
            target,
            HealthState.Full(100.0));
        simulation.Entities.AddComponent(
            target,
            new ArmorState(
                armorProfile.Id));

        EntityId shooter =
            CreateCombatant(
                simulation,
                BlueFaction,
                new Vector3(0.0f, 0.0f, 10.0f),
                TargetClass.ArmoredVehicle);
        InventoryId ammunition =
            inventories.CreateInventory(
                new InventorySpecification(
                    10.0,
                    [ResourceIds.Ammunition]));
        Assert.True(
            inventories.Add(
                ammunition,
                ResourceIds.Ammunition,
                1.0).Succeeded);
        simulation.Entities.AddComponent(
            shooter,
            new AmmunitionState(
                ammunition,
                10.0));
        simulation.Entities.AddComponent(
            shooter,
            new WeaponState(
                weapon.Id,
                target));

        simulation.AdvanceOneTick();

        HealthState health =
            simulation.Entities.GetComponent<HealthState>(
                target);

        Assert.Equal(
            50.0,
            health.Current,
            precision: 6);
        Assert.Equal(
            1UL,
            damageResolution.Metrics.FrontHits);
        Assert.Equal(
            50.0,
            damageResolution.Metrics.TotalMitigatedDamage,
            precision: 6);
    }

    [Fact]
    public void AcquisitionUsesTargetClassPriorityDistanceAndStableEntityTie()
    {
        TargetingScenario scenario =
            CreateTargetingScenario(
                TargetClassMask.ArmoredVehicle);

        _ =
            AddCandidate(
                scenario,
                BlueFaction,
                new Vector3(3.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                priority: 100);
        _ =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(4.0f, 0.0f, 0.0f),
                TargetClass.Infantry,
                priority: 100);

        EntityId expected =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(10.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                priority: 20);
        _ =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(10.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                priority: 20);
        _ =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(2.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                priority: 10);

        scenario.Simulation.AdvanceOneTick();

        WeaponState weapon =
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                scenario.Source);

        Assert.Equal(
            expected,
            weapon.Target);
        Assert.True(
            scenario.Targeting.Metrics.FriendlyRejections > 0);
        Assert.True(
            scenario.Targeting.Metrics.TargetClassRejections > 0);
    }

    [Fact]
    public void AcquisitionReacquiresAfterTargetDestruction()
    {
        TargetingScenario scenario =
            CreateTargetingScenario(
                TargetClassMask.All);

        EntityId first =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(5.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle,
                priority: 10);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            first,
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                scenario.Source).Target);
        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                first));

        EntityId replacement =
            AddCandidate(
                scenario,
                RedFaction,
                new Vector3(6.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle,
                priority: 10);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            replacement,
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                scenario.Source).Target);
        Assert.Equal(
            1UL,
            scenario.Targeting.Metrics.TotalReacquisitions);
    }

    [Fact]
    public void HoldFirePreventsAuthoritativeWeaponExecution()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var weapons =
            new WeaponCatalog();
        var runtime =
            new CombatRuntime();

        WeaponDefinition weapon =
            CreateWeapon(TargetClassMask.All);
        weapons.Add(weapon);
        simulation.RegisterSystem(
            new CombatExecutionSystem(
                weapons,
                inventories,
                runtime));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        EntityId target =
            CreateCombatant(
                simulation,
                RedFaction,
                new Vector3(5.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle);
        simulation.Entities.AddComponent(
            target,
            HealthState.Full(100.0));

        EntityId source =
            CreateCombatant(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.LightVehicle);
        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    10.0,
                    [ResourceIds.Ammunition]));
        Assert.True(
            inventories.Add(
                inventory,
                ResourceIds.Ammunition,
                5.0).Succeeded);
        simulation.Entities.AddComponent(
            source,
            new AmmunitionState(
                inventory,
                10.0));
        simulation.Entities.AddComponent(
            source,
            new WeaponState(
                weapon.Id,
                target));
        simulation.Entities.AddComponent(
            source,
            new FirePolicyState(
                FirePolicy.HoldFire));

        simulation.AdvanceOneTick();

        Assert.Equal(
            0UL,
            runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            100.0,
            simulation.Entities.GetComponent<HealthState>(
                target).Current,
            precision: 6);
        Assert.Equal(
            5.0,
            inventories.GetQuantity(
                inventory,
                ResourceIds.Ammunition),
            precision: 6);
    }

    [Fact]
    public void ReturnFireAcquiresTheActualAttackerOnFollowingTick()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var weapons =
            new WeaponCatalog();
        var runtime =
            new CombatRuntime();
        var spatial =
            new SpatialGridIndex();
        var synchronizer =
            new SpatialIndexSynchronizer(
                spatial);

        WeaponDefinition weapon =
            CreateWeapon(TargetClassMask.All);
        weapons.Add(weapon);

        var targeting =
            new TargetAcquisitionSystem(
                weapons,
                spatial);
        simulation.RegisterSystem(
            new SpatialIndexSystem(
                synchronizer));
        simulation.RegisterSystem(
            targeting);
        simulation.RegisterSystem(
            new CombatExecutionSystem(
                weapons,
                inventories,
                runtime,
                spatial));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime,
                spatial));

        EntityId defender =
            CreateCombatant(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.LightVehicle,
                withSpatialPresence: true);
        simulation.Entities.AddComponent(
            defender,
            HealthState.Full(100.0));
        simulation.Entities.AddComponent(
            defender,
            new WeaponState(
                weapon.Id,
                EntityId.Invalid));
        simulation.Entities.AddComponent(
            defender,
            new FirePolicyState(
                FirePolicy.ReturnFire));

        EntityId attacker =
            CreateCombatant(
                simulation,
                RedFaction,
                new Vector3(5.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle,
                withSpatialPresence: true);
        simulation.Entities.AddComponent(
            attacker,
            HealthState.Full(100.0));
        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    10.0,
                    [ResourceIds.Ammunition]));
        Assert.True(
            inventories.Add(
                inventory,
                ResourceIds.Ammunition,
                2.0).Succeeded);
        simulation.Entities.AddComponent(
            attacker,
            new AmmunitionState(
                inventory,
                10.0));
        simulation.Entities.AddComponent(
            attacker,
            new WeaponState(
                weapon.Id,
                defender));

        simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        FirePolicyState defenderPolicy =
            simulation.Entities.GetComponent<FirePolicyState>(
                defender);
        WeaponState defenderWeapon =
            simulation.Entities.GetComponent<WeaponState>(
                defender);

        Assert.Equal(
            attacker,
            defenderPolicy.RetaliationTarget);
        Assert.Equal(
            attacker,
            defenderWeapon.Target);
    }

    [Fact]
    public void AvailabilityAndLineOfFireHooksRejectAutoTargets()
    {
        var deniedAvailability =
            new DenyAllAvailabilityPolicy();
        var blockedLineOfFire =
            new BlockAllLineOfFirePolicy();

        TargetingScenario unavailable =
            CreateTargetingScenario(
                TargetClassMask.All,
                deniedAvailability,
                UnobstructedLineOfFirePolicy.Instance);
        _ =
            AddCandidate(
                unavailable,
                RedFaction,
                new Vector3(5.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle,
                priority: 1);

        unavailable.Simulation.AdvanceOneTick();

        Assert.False(
            unavailable.Simulation.Entities.GetComponent<WeaponState>(
                unavailable.Source).Target.IsValid);
        Assert.True(
            unavailable.Targeting.Metrics.AvailabilityRejections > 0);

        TargetingScenario blocked =
            CreateTargetingScenario(
                TargetClassMask.All,
                AlwaysTargetAvailablePolicy.Instance,
                blockedLineOfFire);
        _ =
            AddCandidate(
                blocked,
                RedFaction,
                new Vector3(5.0f, 0.0f, 0.0f),
                TargetClass.LightVehicle,
                priority: 1);

        blocked.Simulation.AdvanceOneTick();

        Assert.False(
            blocked.Simulation.Entities.GetComponent<WeaponState>(
                blocked.Source).Target.IsValid);
        Assert.True(
            blocked.Targeting.Metrics.LineOfFireRejections > 0);
    }

    private static TargetingScenario CreateTargetingScenario(
        TargetClassMask validTargets,
        ITargetAvailabilityPolicy? availability = null,
        ILineOfFirePolicy? lineOfFire = null)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var spatial =
            new SpatialGridIndex();
        var synchronizer =
            new SpatialIndexSynchronizer(
                spatial);
        var weapons =
            new WeaponCatalog();
        WeaponDefinition weapon =
            CreateWeapon(validTargets);
        weapons.Add(weapon);

        var targeting =
            new TargetAcquisitionSystem(
                weapons,
                spatial,
                availability,
                lineOfFire);

        simulation.RegisterSystem(
            new SpatialIndexSystem(
                synchronizer));
        simulation.RegisterSystem(
            targeting);

        EntityId source =
            CreateCombatant(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.LightVehicle,
                withSpatialPresence: true);
        simulation.Entities.AddComponent(
            source,
            new WeaponState(
                weapon.Id,
                EntityId.Invalid));

        return new TargetingScenario(
            simulation,
            targeting,
            source);
    }

    private static EntityId AddCandidate(
        in TargetingScenario scenario,
        FactionId faction,
        Vector3 position,
        TargetClass targetClass,
        int priority)
    {
        EntityId entity =
            CreateCombatant(
                scenario.Simulation,
                faction,
                position,
                targetClass,
                withSpatialPresence: true);
        scenario.Simulation.Entities.AddComponent(
            entity,
            HealthState.Full(100.0));
        scenario.Simulation.Entities.AddComponent(
            entity,
            new TargetPriority(priority));
        return entity;
    }

    private static EntityId CreateCombatant(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position,
        TargetClass targetClass,
        bool withSpatialPresence = false)
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
            new Combatant(
                faction));
        simulation.Entities.AddComponent(
            entity,
            new Targetable(
                targetClass));

        if (withSpatialPresence)
        {
            simulation.Entities.AddComponent(
                entity,
                new SpatialPresence(
                    new Vector3(0.5f),
                    new SpatialEntryMetadata(
                        faction.Value,
                        CategoryMask: 1,
                        SpatialMobility.Mobile)));
        }

        return entity;
    }

    private static WeaponDefinition CreateWeapon(
        TargetClassMask validTargets) =>
        new(
            new WeaponId(1),
            rangeMeters: 100.0f,
            fireIntervalTicks: 20,
            ammunitionPerShot: 1.0,
            new DamagePayload(10.0),
            WeaponDeliveryModel.Hitscan,
            effectiveness:
                new WeaponEffectiveness(
                    validTargets,
                    penetration: 100.0));

    private readonly record struct TargetingScenario(
        SimulationCoordinator Simulation,
        TargetAcquisitionSystem Targeting,
        EntityId Source);

    private sealed class DenyAllAvailabilityPolicy :
        ITargetAvailabilityPolicy
    {
        public bool IsTargetAvailable(
            EntityId observer,
            EntityId target) =>
            false;
    }

    private sealed class BlockAllLineOfFirePolicy :
        ILineOfFirePolicy
    {
        public bool HasLineOfFire(
            EntityId source,
            EntityId target,
            Vector3 sourcePosition,
            Vector3 targetPosition) =>
            false;
    }
}
