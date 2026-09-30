using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VfxPresentationTests
{
    [Fact]
    public void CombatEventsMapToDirectorateEffectFamilies()
    {
        var shot =
            new CombatEvent(
                new SimulationTick(
                    7),
                CombatEventType.ShotFired,
                new EntityId(
                    1,
                    1),
                new EntityId(
                    2,
                    1),
                EntityId.Invalid,
                DirectorateContent.WeaponIds.MainBattleCannon,
                Vector3.Zero,
                1.0);
        var artilleryImpact =
            shot with
            {
                Type =
                    CombatEventType.Impact,
                Weapon =
                    DirectorateContent.WeaponIds.MobileArtillery
            };

        Assert.Equal(
            VfxEffectKind.MuzzleTankCannon,
            VfxPresentationCatalog.ResolveCombatEvent(
                shot));
        Assert.Equal(
            VfxEffectKind.ProjectileCannonShell,
            VfxPresentationCatalog.ResolveProjectile(
                DirectorateContent.WeaponIds.MainBattleCannon));
        Assert.Equal(
            VfxEffectKind.ImpactExplosive,
            VfxPresentationCatalog.ResolveCombatEvent(
                artilleryImpact,
                TargetClass.Structure));
        Assert.Equal(
            VfxEffectKind.ExplosionMedium,
            VfxPresentationCatalog.ResolveImpactExplosion(
                DirectorateContent.WeaponIds.MobileArtillery));
    }

    [Fact]
    public void UnknownWeaponsUseStableFallbackEffectFamilies()
    {
        var unknown =
            new WeaponId(
                999_999);

        Assert.Equal(
            VfxEffectKind.MuzzleMachineGun,
            VfxPresentationCatalog.ResolveMuzzle(
                unknown));
        Assert.Equal(
            VfxEffectKind.ProjectileGenericExplosive,
            VfxPresentationCatalog.ResolveProjectile(
                unknown));
        Assert.Equal(
            VfxEffectKind.ImpactArmor,
            VfxPresentationCatalog.ResolveImpact(
                unknown,
                TargetClass.ArmoredVehicle));
    }

    [Fact]
    public void EffectPoolReusesExpiredSlotsAndDropsWhenFull()
    {
        var pool =
            new VfxEffectPool(
                capacity: 1);
        var transform =
            new RenderTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);

        Assert.True(
            pool.TrySpawn(
                VfxEffectKind.ExplosionSmall,
                transform,
                new SimulationTick(
                    1)));
        Assert.False(
            pool.TrySpawn(
                VfxEffectKind.ExplosionSmall,
                transform,
                new SimulationTick(
                    1)));

        pool.BeginTick(
            new SimulationTick(
                7));

        Assert.True(
            pool.TrySpawn(
                VfxEffectKind.ExplosionSmall,
                transform,
                new SimulationTick(
                    7)));

        VfxPresentationMetrics metrics =
            pool.Metrics;

        Assert.Equal(
            1,
            metrics.ActiveTransientEffects);
        Assert.Equal(
            1,
            metrics.PoolCapacity);
        Assert.Equal(
            2UL,
            metrics.TotalSpawned);
        Assert.Equal(
            1UL,
            metrics.TotalReused);
        Assert.Equal(
            1UL,
            metrics.TotalDropped);
    }

    [Fact]
    public void DistancePolicySuppressesFineEffectsAtStrategicRange()
    {
        VfxPresentationDefinition muzzle =
            VfxPresentationCatalog.Get(
                VfxEffectKind.MuzzleInfantry);

        Assert.True(
            VfxPresentationCatalog.ShouldRender(
                muzzle.Kind,
                muzzle.MaximumDistanceMeters));
        Assert.False(
            VfxPresentationCatalog.ShouldRender(
                muzzle.Kind,
                muzzle.MaximumDistanceMeters +
                1.0f));
        Assert.False(
            VfxPresentationCatalog.ShouldRender(
                muzzle.Kind,
                float.PositiveInfinity));
    }

    [Fact]
    public void DamageAndCargoStateProducePresentationOnlyEffects()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId damaged =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            damaged);
        simulation.Entities.AddComponent(
            damaged,
            new UnitIdentity(
                UnitIds.MainBattleTank,
                DirectorateContent.FactionId));
        simulation.Entities.AddComponent(
            damaged,
            new HealthState(
                120.0,
                560.0));

        EntityId cargo =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            cargo);
        simulation.Entities.AddComponent(
            cargo,
            new CargoTransportRuntimeState(
                CargoTransportLifecycleState.Loading,
                CargoTransportWaitReason.None,
                CargoTransportFailureReason.None,
                LogisticsNodeId.None,
                default,
                0.0,
                0.0,
                SimulationTick.Zero));

        int entityCountBefore =
            simulation.Entities.EntityCount;

        simulation.AdvanceOneTick();

        Assert.Equal(
            entityCountBefore,
            simulation.Entities.EntityCount);
        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        VfxEffectKind[] effects =
            snapshot.Instances
                .ToArray()
                .Where(
                    static instance =>
                        instance.VfxFeature.IsSpecified)
                .Select(
                    static instance =>
                        instance.VfxFeature.Kind)
                .ToArray();

        Assert.Contains(
            VfxEffectKind.PersistentHeavySmoke,
            effects);
        Assert.Contains(
            VfxEffectKind.PersistentFire,
            effects);
        Assert.Contains(
            VfxEffectKind.PersistentSparks,
            effects);
        Assert.Contains(
            VfxEffectKind.LogisticsLoading,
            effects);
        Assert.Contains(
            VfxEffectKind.LogisticsResourceTransfer,
            effects);
    }

    [Fact]
    public void SupplyFractionIncreaseProducesTransferRefuelAndRearmEffects()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId unit =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            unit);
        simulation.Entities.AddComponent(
            unit,
            new UnitSupplyState(
                0.20,
                0.25,
                BattlefieldSupplyStatus.Critical,
                SimulationTick.Zero));

        simulation.AdvanceOneTick();

        simulation.Entities.SetComponent(
            unit,
            new UnitSupplyState(
                0.55,
                0.70,
                BattlefieldSupplyStatus.LowSupply,
                simulation.CurrentTick));
        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot snapshot));

        VfxEffectKind[] effects =
            snapshot.Instances
                .ToArray()
                .Where(
                    static instance =>
                        instance.VfxFeature.IsSpecified)
                .Select(
                    static instance =>
                        instance.VfxFeature.Kind)
                .ToArray();

        Assert.Contains(
            VfxEffectKind.LogisticsSupplyTransfer,
            effects);
        Assert.Contains(
            VfxEffectKind.LogisticsRefuel,
            effects);
        Assert.Contains(
            VfxEffectKind.LogisticsRearm,
            effects);
    }

    [Fact]
    public void NewVehicleAndBuildingWrecksProduceDestructionEffectsOnce()
    {
        var simulation =
            new SimulationCoordinator();
        var buffer =
            new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer));

        EntityId vehicleWreck =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            vehicleWreck);
        simulation.Entities.AddComponent(
            vehicleWreck,
            new UnitWreckPresentationIdentity(
                UnitIds.MainBattleTank,
                DirectorateContent.FactionId));

        EntityId buildingWreck =
            simulation.Entities.CreateEntity();
        AddRenderable(
            simulation,
            buildingWreck);
        simulation.Entities.AddComponent(
            buildingWreck,
            new BuildingWreckPresentationIdentity(
                BuildingIds.CommandCore,
                new PlayerId(
                    1)));

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot first));

        VfxEffectKind[] firstEffects =
            first.Instances
                .ToArray()
                .Where(
                    static instance =>
                        instance.VfxFeature.IsSpecified)
                .Select(
                    static instance =>
                        instance.VfxFeature.Kind)
                .ToArray();

        Assert.Contains(
            VfxEffectKind.DestructionVehicleBurst,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.ExplosionVehicle,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionBuildingBurst,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.ExplosionBuilding,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionDebris,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionSmokePlume,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionPersistentFire,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionSparkEmission,
            firstEffects);
        Assert.Contains(
            VfxEffectKind.DestructionDustCloud,
            firstEffects);

        ulong spawnedAfterFirst =
            first.VfxMetrics.TotalSpawned;

        simulation.AdvanceOneTick();

        Assert.True(
            buffer.TryReadLatest(
                out PresentationSnapshot second));
        Assert.Equal(
            spawnedAfterFirst,
            second.VfxMetrics.TotalSpawned);
    }

    [Fact]
    public void SustainedRepresentativeEffectLoadStaysBoundedAndReusesPoolSlots()
    {
        const int Capacity = 512;
        const int EffectsPerTick = 128;
        const int Ticks = 200;

        var pool =
            new VfxEffectPool(
                Capacity);
        var transform =
            new RenderTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);

        for (ulong tick = 1;
             tick <= Ticks;
             tick++)
        {
            var simulationTick =
                new SimulationTick(
                    tick);
            pool.BeginTick(
                simulationTick);

            for (int effect = 0;
                 effect < EffectsPerTick;
                 effect++)
            {
                Assert.True(
                    pool.TrySpawn(
                        VfxEffectKind.MuzzleMachineGun,
                        transform,
                        simulationTick));
            }

            Assert.InRange(
                pool.ActiveCount,
                1,
                Capacity);
        }

        VfxPresentationMetrics metrics =
            pool.Metrics;

        Assert.Equal(
            Capacity,
            metrics.PoolCapacity);
        Assert.True(
            metrics.TotalSpawned >=
            (ulong)(EffectsPerTick * Ticks));
        Assert.True(
            metrics.TotalReused >
            0UL);
        Assert.Equal(
            0UL,
            metrics.TotalDropped);
    }

    [Fact]
    public void CatalogContainsAllRequiredProductionFamilies()
    {
        Assert.Equal(
            36,
            VfxPresentationCatalog.All.Count);

        foreach (VfxPresentationDefinition definition in
                 VfxPresentationCatalog.All)
        {
            definition.Validate();
        }
    }

    private static void AddRenderable(
        SimulationCoordinator simulation,
        EntityId entity)
    {
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                new Vector3(
                    4.0f,
                    3.0f,
                    7.0f)));
        simulation.Entities.AddComponent(
            entity,
            new VisualIdentity(
                1));
    }
}
