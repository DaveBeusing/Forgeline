using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

[Flags]
public enum PlayerAlertState : byte
{
    None = 0,
    LowPower = 1 << 0,
    ProductionBlocked = 1 << 1,
    SupplyCritical = 1 << 2,
    CommandCoreDamaged = 1 << 3,
    CommandCoreDestroyed = 1 << 4
}

public enum PlayerCommandFeedbackKind : byte
{
    None = 0,
    Movement = 1,
    Construction = 2
}

public enum PlayerCommandFeedbackState : byte
{
    None = 0,
    Accepted = 1,
    Partial = 2,
    Rejected = 3
}

public readonly record struct PlayerCommandFeedback(
    PlayerCommandFeedbackKind Kind,
    PlayerCommandFeedbackState State,
    int AcceptedTargets,
    int RejectedTargets,
    BuildCommandRejectionReason BuildRejection,
    BuildingPlacementFailureReason PlacementFailure,
    SimulationTick ResolvedAtTick)
{
    public static PlayerCommandFeedback None =>
        new(
            PlayerCommandFeedbackKind.None,
            PlayerCommandFeedbackState.None,
            0,
            0,
            BuildCommandRejectionReason.None,
            BuildingPlacementFailureReason.None,
            SimulationTick.Zero);
}

public enum PlayerSelectionKind : byte
{
    None = 0,
    Unit = 1,
    Building = 2,
    Construction = 3,
    Mixed = 4
}

public enum PlayerWorkKind : byte
{
    None = 0,
    Construction = 1,
    UnitProduction = 2,
    Processing = 3
}

public enum PlayerWorkState : byte
{
    None = 0,
    Idle = 1,
    Running = 2,
    Blocked = 3
}

public readonly record struct PlayerResourceSummary(
    double FerrousOre,
    double Volatiles,
    double Silicates,
    double Steel,
    double Fuel,
    double Electronics,
    double Ammunition);

public readonly record struct PlayerPowerSummary(
    PowerNetworkId NetworkId,
    double Generation,
    double Demand,
    double AllocatedPower,
    double Deficit,
    int BrownoutConsumers,
    int OfflineConsumers)
{
    public bool IsConstrained =>
        Deficit > 0.0 ||
        BrownoutConsumers > 0 ||
        OfflineConsumers > 0;
}

public readonly record struct PlayerIntelligenceSummary(
    int ExploredCells,
    int VisibleCells,
    int KnownContacts);

public readonly record struct PlayerWorkSummary(
    PlayerWorkKind Kind,
    PlayerWorkState State,
    double Progress,
    string Activity,
    string BlockReason)
{
    public static PlayerWorkSummary None =>
        new(
            PlayerWorkKind.None,
            PlayerWorkState.None,
            0.0,
            string.Empty,
            string.Empty);
}

public readonly record struct PlayerSelectionSummary(
    int Count,
    EntityId PrimaryEntity,
    PlayerSelectionKind Kind,
    string DisplayName,
    bool HasHealth,
    double HealthFraction,
    bool HasSupply,
    BattlefieldSupplyStatus SupplyStatus,
    double FuelFraction,
    double AmmunitionFraction,
    bool HasReadiness,
    double Readiness,
    bool HasPower,
    PowerOperationalState PowerState,
    bool HasInventory,
    double InventoryQuantity,
    PlayerWorkSummary Work)
{
    public static PlayerSelectionSummary Empty =>
        new(
            0,
            EntityId.Invalid,
            PlayerSelectionKind.None,
            string.Empty,
            false,
            0.0,
            false,
            BattlefieldSupplyStatus.Supplied,
            0.0,
            0.0,
            false,
            0.0,
            false,
            PowerOperationalState.Offline,
            false,
            0.0,
            PlayerWorkSummary.None);
}

public readonly record struct PlayerMatchStatistics(
    ulong DurationTicks,
    double DurationSeconds,
    ulong UnitsProduced,
    int BuildingsConstructed,
    double ProcessedOutput);

public readonly record struct PlayerExperienceSnapshot(
    PlayerId Player,
    SimulationTick Tick,
    PlayerMatchStatus MatchStatus,
    PlayerId Winner,
    PlayerResourceSummary Resources,
    PlayerPowerSummary Power,
    PlayerIntelligenceSummary Intelligence,
    PlayerSelectionSummary Selection,
    PlayerAlertState Alerts,
    int CriticalSupplyUnits,
    int BlockedProductionFacilities,
    PlayerCommandFeedback Feedback,
    PlayerMatchStatistics Statistics)
{
    public bool IsMatchComplete =>
        MatchStatus is
            PlayerMatchStatus.Victory or
            PlayerMatchStatus.Defeat or
            PlayerMatchStatus.Draw or
            PlayerMatchStatus.Ended;
}

public static class PlayerExperienceSnapshotFactory
{
    public static PlayerExperienceSnapshot Capture(
        EntityRegistry entities,
        PlayerId player,
        EntityId commandCore,
        InventoryId coreInventory,
        EntityId matchStateEntity,
        IReadOnlyCollection<EntityId> selectedEntities,
        InventoryStore inventories,
        PowerNetworkSystem powerNetworks,
        ProductionSystem production,
        UnitProductionSystem unitProduction,
        BuildingCommandProcessingSystem buildingCommands,
        MoveEntitiesCommand? lastMovementCommand,
        FactionIntelligenceStore intelligence,
        UnitDefinitionCatalog units,
        BuildingDefinitionCatalog buildings,
        SimulationTick tick,
        int ticksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(selectedEntities);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(powerNetworks);
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(unitProduction);
        ArgumentNullException.ThrowIfNull(buildingCommands);
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(buildings);

        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        if (!commandCore.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(commandCore));
        }

        if (!matchStateEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStateEntity));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ticksPerSecond, 1);

        if (!entities.TryGetComponent(
                matchStateEntity,
                out MatchState matchState))
        {
            throw new InvalidOperationException(
                "The configured match-state entity is unavailable.");
        }

        PlayerResourceSummary resources =
            CaptureResources(
                inventories,
                coreInventory);
        PlayerPowerSummary power =
            CapturePower(
                powerNetworks,
                player);
        PlayerSelectionSummary selection =
            CaptureSelection(
                entities,
                player,
                selectedEntities,
                inventories,
                production,
                unitProduction,
                units,
                buildings);

        int criticalSupplyUnits =
            CountCriticalSupplyUnits(
                entities,
                player);
        int blockedProductionFacilities =
            CountBlockedProductionFacilities(
                entities,
                player);

        PlayerAlertState alerts =
            PlayerAlertState.None;

        if (power.IsConstrained)
        {
            alerts |= PlayerAlertState.LowPower;
        }

        if (blockedProductionFacilities > 0)
        {
            alerts |= PlayerAlertState.ProductionBlocked;
        }

        if (criticalSupplyUnits > 0)
        {
            alerts |= PlayerAlertState.SupplyCritical;
        }

        if (!entities.IsAlive(commandCore))
        {
            alerts |= PlayerAlertState.CommandCoreDestroyed;
        }
        else if (entities.TryGetComponent(
                     commandCore,
                     out HealthState commandCoreHealth) &&
                 commandCoreHealth.Fraction < 0.75)
        {
            alerts |= PlayerAlertState.CommandCoreDamaged;
        }

        FactionId faction =
            ToFactionId(player);
        var intel =
            new PlayerIntelligenceSummary(
                intelligence.GetExploredCellCount(faction),
                intelligence.GetVisibleCellCount(faction),
                intelligence.GetContactCount(faction));

        PlayerCommandFeedback feedback =
            CaptureCommandFeedback(
                player,
                buildingCommands,
                lastMovementCommand);

        PlayerMatchStatistics statistics =
            CaptureStatistics(
                entities,
                player,
                tick,
                ticksPerSecond);

        return new PlayerExperienceSnapshot(
            player,
            tick,
            matchState.ForPlayer(player),
            matchState.Winner,
            resources,
            power,
            intel,
            selection,
            alerts,
            criticalSupplyUnits,
            blockedProductionFacilities,
            feedback,
            statistics);
    }

    private static PlayerCommandFeedback CaptureCommandFeedback(
        PlayerId player,
        BuildingCommandProcessingSystem buildingCommands,
        MoveEntitiesCommand? lastMovementCommand)
    {
        bool hasBuildResult =
            buildingCommands.TryGetLastResult(
                player,
                out BuildCommandResult buildResult);
        bool hasMovementResult =
            lastMovementCommand is not null &&
            lastMovementCommand.ExecutedAtTick >
                SimulationTick.Zero;

        if (!hasBuildResult &&
            !hasMovementResult)
        {
            return PlayerCommandFeedback.None;
        }

        if (hasBuildResult &&
            (!hasMovementResult ||
             buildResult.ResolvedAtTick >=
                lastMovementCommand!.ExecutedAtTick))
        {
            return new PlayerCommandFeedback(
                PlayerCommandFeedbackKind.Construction,
                buildResult.Accepted
                    ? PlayerCommandFeedbackState.Accepted
                    : PlayerCommandFeedbackState.Rejected,
                buildResult.Accepted
                    ? 1
                    : 0,
                buildResult.Accepted
                    ? 0
                    : 1,
                buildResult.RejectionReason,
                buildResult.PlacementFailure,
                buildResult.ResolvedAtTick);
        }

        int accepted =
            lastMovementCommand!.AcceptedTargetCount;
        int rejected =
            lastMovementCommand.RejectedTargetCount;
        PlayerCommandFeedbackState state =
            accepted > 0 && rejected > 0
                ? PlayerCommandFeedbackState.Partial
                : accepted > 0
                    ? PlayerCommandFeedbackState.Accepted
                    : rejected > 0
                        ? PlayerCommandFeedbackState.Rejected
                        : PlayerCommandFeedbackState.None;

        return new PlayerCommandFeedback(
            PlayerCommandFeedbackKind.Movement,
            state,
            accepted,
            rejected,
            BuildCommandRejectionReason.None,
            BuildingPlacementFailureReason.None,
            lastMovementCommand.ExecutedAtTick);
    }

    private static PlayerResourceSummary CaptureResources(
        InventoryStore inventories,
        InventoryId inventory)
    {
        if (!inventories.Contains(inventory))
        {
            return default;
        }

        return new PlayerResourceSummary(
            inventories.GetQuantity(
                inventory,
                ResourceIds.FerrousOre),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Volatiles),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Silicates),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Steel),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Fuel),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Electronics),
            inventories.GetQuantity(
                inventory,
                ResourceIds.Ammunition));
    }

    private static PlayerPowerSummary CapturePower(
        PowerNetworkSystem powerNetworks,
        PlayerId player)
    {
        PowerNetworkId networkId =
            new(
                checked((uint)player.Value));

        for (int index = 0;
             index < powerNetworks.Networks.Count;
             index++)
        {
            PowerNetworkReadModel network =
                powerNetworks.Networks[index];

            if (network.NetworkId != networkId)
            {
                continue;
            }

            return new PlayerPowerSummary(
                networkId,
                network.Generation,
                network.Demand,
                network.AllocatedPower,
                network.Deficit,
                network.BrownoutConsumerCount,
                network.OfflineConsumerCount);
        }

        return new PlayerPowerSummary(
            networkId,
            0.0,
            0.0,
            0.0,
            0.0,
            0,
            0);
    }

    private static PlayerSelectionSummary CaptureSelection(
        EntityRegistry entities,
        PlayerId player,
        IReadOnlyCollection<EntityId> selectedEntities,
        InventoryStore inventories,
        ProductionSystem production,
        UnitProductionSystem unitProduction,
        UnitDefinitionCatalog units,
        BuildingDefinitionCatalog buildings)
    {
        EntityId primary =
            EntityId.Invalid;
        int ownedCount = 0;
        PlayerSelectionKind combinedKind =
            PlayerSelectionKind.None;

        foreach (EntityId entity in selectedEntities)
        {
            if (!entities.IsAlive(entity) ||
                !entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                controllable.Owner != player)
            {
                continue;
            }

            ownedCount++;

            if (!primary.IsValid)
            {
                primary = entity;
            }

            PlayerSelectionKind entityKind =
                ResolveSelectionKind(
                    entities,
                    entity);

            combinedKind =
                combinedKind == PlayerSelectionKind.None
                    ? entityKind
                    : combinedKind == entityKind
                        ? combinedKind
                        : PlayerSelectionKind.Mixed;
        }

        if (!primary.IsValid)
        {
            return PlayerSelectionSummary.Empty;
        }

        string displayName =
            ResolveDisplayName(
                entities,
                primary,
                units,
                buildings);

        bool hasHealth =
            entities.TryGetComponent(
                primary,
                out HealthState health);
        bool hasSupply =
            entities.TryGetComponent(
                primary,
                out UnitSupplyState supply);
        bool hasReadiness =
            entities.TryGetComponent(
                primary,
                out UnitCombatReadiness readiness);
        bool hasPower =
            entities.TryGetComponent(
                primary,
                out PowerConsumer power);
        bool hasInventory =
            entities.TryGetComponent(
                primary,
                out InventoryStorage storage) &&
            inventories.Contains(storage.InventoryId);

        return new PlayerSelectionSummary(
            ownedCount,
            primary,
            combinedKind,
            displayName,
            hasHealth,
            hasHealth
                ? health.Fraction
                : 0.0,
            hasSupply,
            hasSupply
                ? supply.Status
                : BattlefieldSupplyStatus.Supplied,
            hasSupply
                ? supply.FuelFraction
                : 0.0,
            hasSupply
                ? supply.AmmunitionFraction
                : 0.0,
            hasReadiness,
            hasReadiness
                ? readiness.OverallReadiness
                : 0.0,
            hasPower,
            hasPower
                ? power.State
                : PowerOperationalState.Offline,
            hasInventory,
            hasInventory
                ? inventories.GetTotalQuantity(
                    storage.InventoryId)
                : 0.0,
            CaptureWork(
                entities,
                primary,
                production,
                unitProduction,
                units,
                buildings));
    }

    private static PlayerWorkSummary CaptureWork(
        EntityRegistry entities,
        EntityId entity,
        ProductionSystem production,
        UnitProductionSystem unitProduction,
        UnitDefinitionCatalog units,
        BuildingDefinitionCatalog buildings)
    {
        if (entities.TryGetComponent(
                entity,
                out ConstructionSite construction))
        {
            string activity =
                buildings.TryGet(
                    construction.BuildingId,
                    out BuildingDefinition? definition)
                    ? definition.DisplayName
                    : "Construction";

            return new PlayerWorkSummary(
                PlayerWorkKind.Construction,
                PlayerWorkState.Running,
                construction.Progress,
                activity,
                string.Empty);
        }

        for (int index = 0;
             index < unitProduction.Facilities.Count;
             index++)
        {
            UnitProductionFacilityReadModel facility =
                unitProduction.Facilities[index];

            if (facility.Entity != entity)
            {
                continue;
            }

            string activity =
                facility.ActiveUnit.IsSpecified &&
                units.TryGet(
                    facility.ActiveUnit,
                    out UnitDefinition? definition)
                    ? definition.DisplayName
                    : "Unit Production";

            return new PlayerWorkSummary(
                PlayerWorkKind.UnitProduction,
                MapUnitProductionState(
                    facility.Status),
                facility.Progress,
                activity,
                facility.BlockReason ==
                    UnitProductionBlockReason.None
                    ? string.Empty
                    : facility.BlockReason.ToString());
        }

        for (int index = 0;
             index < production.Facilities.Count;
             index++)
        {
            ProductionFacilityReadModel facility =
                production.Facilities[index];

            if (facility.Entity != entity)
            {
                continue;
            }

            return new PlayerWorkSummary(
                PlayerWorkKind.Processing,
                MapProductionState(
                    facility.Status),
                facility.Progress,
                "Processing",
                facility.BlockReason ==
                    ProductionBlockReason.None
                    ? string.Empty
                    : facility.BlockReason.ToString());
        }

        return PlayerWorkSummary.None;
    }

    private static PlayerWorkState MapUnitProductionState(
        UnitProductionStatus status) =>
        status switch
        {
            UnitProductionStatus.Idle =>
                PlayerWorkState.Idle,
            UnitProductionStatus.Running =>
                PlayerWorkState.Running,
            _ =>
                PlayerWorkState.Blocked
        };

    private static PlayerWorkState MapProductionState(
        ProductionStatus status) =>
        status switch
        {
            ProductionStatus.Idle =>
                PlayerWorkState.Idle,
            ProductionStatus.Running =>
                PlayerWorkState.Running,
            _ =>
                PlayerWorkState.Blocked
        };

    private static PlayerSelectionKind ResolveSelectionKind(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.HasComponent<ConstructionSite>(entity))
        {
            return PlayerSelectionKind.Construction;
        }

        if (entities.HasComponent<UnitIdentity>(entity))
        {
            return PlayerSelectionKind.Unit;
        }

        if (entities.HasComponent<CompletedBuilding>(entity))
        {
            return PlayerSelectionKind.Building;
        }

        return PlayerSelectionKind.None;
    }

    private static string ResolveDisplayName(
        EntityRegistry entities,
        EntityId entity,
        UnitDefinitionCatalog units,
        BuildingDefinitionCatalog buildings)
    {
        if (entities.TryGetComponent(
                entity,
                out UnitIdentity unit) &&
            units.TryGet(
                unit.UnitId,
                out UnitDefinition? unitDefinition))
        {
            return unitDefinition.DisplayName;
        }

        if (entities.TryGetComponent(
                entity,
                out CompletedBuilding building) &&
            buildings.TryGet(
                building.BuildingId,
                out BuildingDefinition? buildingDefinition))
        {
            return buildingDefinition.DisplayName;
        }

        if (entities.TryGetComponent(
                entity,
                out ConstructionSite construction) &&
            buildings.TryGet(
                construction.BuildingId,
                out BuildingDefinition? constructionDefinition))
        {
            return constructionDefinition.DisplayName;
        }

        return "Selection";
    }

    private static int CountCriticalSupplyUnits(
        EntityRegistry entities,
        PlayerId player)
    {
        int count = 0;

        foreach (EntityId entity in
                 entities.Query<
                     ControllableEntity,
                     UnitSupplyState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                entities.GetComponent<ControllableEntity>(
                    entity);

            if (controllable.Owner != player)
            {
                continue;
            }

            BattlefieldSupplyStatus status =
                entities.GetComponent<UnitSupplyState>(
                    entity).Status;

            if (status is
                BattlefieldSupplyStatus.Critical or
                BattlefieldSupplyStatus.Unsupplied)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountBlockedProductionFacilities(
        EntityRegistry entities,
        PlayerId player)
    {
        int count = 0;

        foreach (EntityId entity in
                 entities.Query<
                     ControllableEntity,
                     UnitProductionFacility>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                entities.GetComponent<ControllableEntity>(
                    entity);
            UnitProductionFacility production =
                entities.GetComponent<UnitProductionFacility>(
                    entity);

            if (controllable.Owner == player &&
                production.Status is
                    UnitProductionStatus.NoInput or
                    UnitProductionStatus.NoPower or
                    UnitProductionStatus.Paused)
            {
                count++;
            }
        }

        foreach (EntityId entity in
                 entities.Query<
                     ControllableEntity,
                     ProductionFacility>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                entities.GetComponent<ControllableEntity>(
                    entity);
            ProductionFacility production =
                entities.GetComponent<ProductionFacility>(
                    entity);

            if (controllable.Owner == player &&
                production.Status is
                    ProductionStatus.NoInput or
                    ProductionStatus.OutputFull or
                    ProductionStatus.NoPower or
                    ProductionStatus.Paused)
            {
                count++;
            }
        }

        return count;
    }

    private static PlayerMatchStatistics CaptureStatistics(
        EntityRegistry entities,
        PlayerId player,
        SimulationTick tick,
        int ticksPerSecond)
    {
        ulong unitsProduced = 0;
        int buildingsConstructed = 0;
        double processedOutput = 0.0;

        foreach (EntityId entity in
                 entities.Query<ControllableEntity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                entities.GetComponent<ControllableEntity>(
                    entity);

            if (controllable.Owner != player)
            {
                continue;
            }

            if (entities.TryGetComponent(
                    entity,
                    out UnitProductionFacility unitProduction))
            {
                unitsProduced +=
                    unitProduction.CompletedUnits;
            }

            if (entities.TryGetComponent(
                    entity,
                    out ProductionFacility production))
            {
                processedOutput +=
                    production.TotalOutputQuantity;
            }

            if (entities.TryGetComponent(
                    entity,
                    out CompletedBuilding building) &&
                building.CompletedAtTick >
                    SimulationTick.Zero)
            {
                buildingsConstructed++;
            }
        }

        return new PlayerMatchStatistics(
            tick.Value,
            tick.Value /
                (double)ticksPerSecond,
            unitsProduced,
            buildingsConstructed,
            processedOutput);
    }

    private static FactionId ToFactionId(PlayerId player)
    {
        if (player.Value > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"Player {player} cannot be represented as an intelligence faction.");
        }

        return new FactionId(
            (uint)player.Value);
    }
}
