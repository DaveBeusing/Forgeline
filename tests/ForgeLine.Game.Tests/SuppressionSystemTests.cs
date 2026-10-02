using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SuppressionSystemTests
{
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void InfantryDamageCreatesPinnedStateAndMovementConstraint()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories = new InventoryStore();
        var weapons = new WeaponCatalog();
        var runtime = new CombatRuntime();
        var suppression =
            new SuppressionSystem();

        var weapon =
            new WeaponDefinition(
                new WeaponId(91),
                rangeMeters: 100.0f,
                fireIntervalTicks: 100,
                ammunitionPerShot: 1.0,
                new DamagePayload(30.0),
                WeaponDeliveryModel.Hitscan);
        weapons.Add(weapon);

        simulation.RegisterSystem(suppression);
        simulation.RegisterSystem(
            new CombatExecutionSystem(
                weapons,
                inventories,
                runtime));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));

        EntityId shooter =
            CreateCombatant(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.Infantry);
        EntityId target =
            CreateCombatant(
                simulation,
                RedFaction,
                new Vector3(10.0f, 0.0f, 0.0f),
                TargetClass.Infantry);

        InventoryId ammunition =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 1.0,
                    acceptedResources:
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
                capacity: 1.0));
        simulation.Entities.AddComponent(
            shooter,
            new WeaponState(
                weapon.Id,
                target));
        simulation.Entities.AddComponent(
            target,
            SuppressionProfile.InfantryDefault);
        simulation.Entities.AddComponent(
            target,
            SuppressionState.Clear);

        simulation.AdvanceOneTick();

        SuppressionState impactState =
            simulation.Entities.GetComponent<SuppressionState>(
                target);

        Assert.Equal(
            SuppressionLevel.Pinned,
            impactState.Level);
        Assert.InRange(
            impactState.Value,
            0.89,
            0.91);

        simulation.AdvanceOneTick();

        SuppressionMovementConstraint constraint =
            simulation.Entities.GetComponent<SuppressionMovementConstraint>(
                target);

        Assert.False(constraint.CanMove);
        Assert.Equal(0.0f, constraint.MaximumSpeedScale);
    }

    [Fact]
    public void SuppressionDecaysThroughSuppressedToNormal()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var suppression =
            new SuppressionSystem();
        simulation.RegisterSystem(suppression);

        EntityId unit =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            unit,
            SuppressionProfile.InfantryDefault);
        simulation.Entities.AddComponent(
            unit,
            new SuppressionState(
                0.9,
                SuppressionLevel.Pinned,
                SimulationTick.Zero,
                SimulationTick.Zero));

        simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        SuppressionState suppressed =
            simulation.Entities.GetComponent<SuppressionState>(
                unit);
        Assert.Equal(
            SuppressionLevel.Suppressed,
            suppressed.Level);

        SuppressionMovementConstraint limited =
            simulation.Entities.GetComponent<SuppressionMovementConstraint>(
                unit);
        Assert.True(limited.CanMove);
        Assert.Equal(
            SuppressionProfile.InfantryDefault.SuppressedSpeedScale,
            limited.MaximumSpeedScale);

        simulation.RunTicks(
            120,
            TestContext.Current.CancellationToken);

        SuppressionState recovered =
            simulation.Entities.GetComponent<SuppressionState>(
                unit);
        Assert.Equal(
            SuppressionLevel.Normal,
            recovered.Level);
        Assert.False(
            simulation.Entities.HasComponent<SuppressionMovementConstraint>(
                unit));
        Assert.Equal(
            1UL,
            suppression.Metrics.TotalRecoveries);
    }

    [Fact]
    public void PinnedShooterCannotFire()
    {
        var simulation =
            new SimulationCoordinator();
        var inventories = new InventoryStore();
        var weapons = new WeaponCatalog();
        var runtime = new CombatRuntime();

        var weapon =
            new WeaponDefinition(
                new WeaponId(92),
                rangeMeters: 100.0f,
                fireIntervalTicks: 1,
                ammunitionPerShot: 1.0,
                new DamagePayload(10.0),
                WeaponDeliveryModel.Hitscan);
        weapons.Add(weapon);

        simulation.RegisterSystem(
            new CombatExecutionSystem(
                weapons,
                inventories,
                runtime));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));

        EntityId shooter =
            CreateCombatant(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.Infantry);
        EntityId target =
            CreateCombatant(
                simulation,
                RedFaction,
                new Vector3(10.0f, 0.0f, 0.0f),
                TargetClass.Infantry);

        InventoryId ammunition =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 1.0,
                    acceptedResources:
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
                capacity: 1.0));
        simulation.Entities.AddComponent(
            shooter,
            new WeaponState(
                weapon.Id,
                target));
        simulation.Entities.AddComponent(
            shooter,
            new SuppressionState(
                0.9,
                SuppressionLevel.Pinned,
                SimulationTick.Zero,
                SimulationTick.Zero));

        simulation.AdvanceOneTick();

        Assert.Equal(
            0UL,
            runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            100.0,
            simulation.Entities.GetComponent<HealthState>(
                target).Current);
    }

    private static EntityId CreateCombatant(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position,
        TargetClass targetClass)
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
            new Combatant(faction));
        simulation.Entities.AddComponent(
            entity,
            new Targetable(targetClass));
        simulation.Entities.AddComponent(
            entity,
            HealthState.Full(100.0));
        simulation.Entities.AddComponent(
            entity,
            CombatHitbox.Default);

        return entity;
    }
}
