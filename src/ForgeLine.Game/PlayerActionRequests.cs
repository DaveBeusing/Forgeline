using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;

namespace ForgeLine.Game;

public enum PlayerActionRequestKind : byte
{
    None = 0,
    BeginBuildingPlacement = 1,
    QueueProduction = 2,
    SetProductionPaused = 3,
    CancelProduction = 4,
    QueueUnitProduction = 5,
    CancelUnitProduction = 6,
    SetStockPolicy = 7,
    RemoveStockPolicy = 8,
    SetAutomaticResupplyPolicy = 9,
    RequestResupply = 10,
    BeginAttackTargeting = 11,
    BeginAttackMoveTargeting = 12,
    SubmitStopCombat = 13,
    SubmitHoldPosition = 14,
    BeginRetreatTargeting = 15,
    BeginFireMissionTargeting = 16,
    CancelFireMission = 17,
    SubmitAttack = 18,
    SubmitAttackMove = 19,
    SubmitRetreat = 20,
    SubmitFireMissionCoordinate = 21,
    SubmitFireMissionContact = 22,
    Surrender = 23,
    SetSupplyPriority = 24
}

public readonly record struct PlayerActionRequest(
    PlayerActionRequestKind Kind,
    BuildingId BuildingId,
    EntityId Facility,
    RecipeId RecipeId,
    UnitId UnitId,
    EntityId RequestEntity,
    bool Paused,
    ProductionPriority Priority,
    ProductionRequestMode ProductionMode,
    ResourceId DesiredStockResourceId,
    double DesiredStockQuantity,
    ResourceId StockResourceId = default,
    double StockMinimum = 0.0,
    double StockTarget = 0.0,
    double StockMaximum = 0.0,
    LogisticsStockPriority StockPriority = LogisticsStockPriority.Normal,
    bool Enabled = true,
    double AutomaticFuelThreshold = 0.0,
    double AutomaticAmmunitionThreshold = 0.0,
    EntityId[]? TacticalEntities = null,
    EntityId TacticalTarget = default,
    Vector3 TacticalWorldTarget = default,
    IntelligenceContactKey TacticalContactKey = default,
    int TacticalRounds = 0,
    FormationTemplate TacticalFormation = FormationTemplate.Compact,
    BattlefieldSupplyPriority SupplyPriority = BattlefieldSupplyPriority.Normal)
{
    public static PlayerActionRequest BeginBuildingPlacement(
        BuildingId buildingId) =>
        new(
            PlayerActionRequestKind.BeginBuildingPlacement,
            buildingId,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest QueueProduction(
        EntityId facility,
        RecipeId recipeId,
        ProductionPriority priority,
        ProductionRequestMode mode,
        ResourceId desiredStockResourceId,
        double desiredStockQuantity) =>
        new(
            PlayerActionRequestKind.QueueProduction,
            BuildingId.None,
            facility,
            recipeId,
            UnitId.None,
            EntityId.Invalid,
            false,
            priority,
            mode,
            desiredStockResourceId,
            desiredStockQuantity);

    public static PlayerActionRequest SetProductionPaused(
        EntityId requestEntity,
        bool paused) =>
        new(
            PlayerActionRequestKind.SetProductionPaused,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            paused,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest CancelProduction(
        EntityId requestEntity) =>
        new(
            PlayerActionRequestKind.CancelProduction,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest QueueUnitProduction(
        EntityId facility,
        UnitId unitId,
        ProductionPriority priority) =>
        new(
            PlayerActionRequestKind.QueueUnitProduction,
            BuildingId.None,
            facility,
            RecipeId.None,
            unitId,
            EntityId.Invalid,
            false,
            priority,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest CancelUnitProduction(
        EntityId requestEntity) =>
        new(
            PlayerActionRequestKind.CancelUnitProduction,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest SetStockPolicy(
        EntityId target,
        ResourceId resourceId,
        double minimum,
        double desiredTarget,
        double maximum,
        LogisticsStockPriority priority,
        bool enabled) =>
        new(
            PlayerActionRequestKind.SetStockPolicy,
            BuildingId.None,
            target,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            resourceId,
            minimum,
            desiredTarget,
            maximum,
            priority,
            enabled);

    public static PlayerActionRequest RemoveStockPolicy(
        EntityId policyEntity) =>
        new(
            PlayerActionRequestKind.RemoveStockPolicy,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            policyEntity,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest SetAutomaticResupplyPolicy(
        EntityId target,
        double fuelThreshold,
        double ammunitionThreshold,
        bool enabled) =>
        new(
            PlayerActionRequestKind.SetAutomaticResupplyPolicy,
            BuildingId.None,
            target,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            ResourceId.None,
            0.0,
            0.0,
            0.0,
            LogisticsStockPriority.Normal,
            enabled,
            fuelThreshold,
            ammunitionThreshold);

    public static PlayerActionRequest RequestResupply(
        EntityId target) =>
        new(
            PlayerActionRequestKind.RequestResupply,
            BuildingId.None,
            target,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest SetSupplyPriority(
        EntityId target,
        BattlefieldSupplyPriority priority) =>
        new(
            PlayerActionRequestKind.SetSupplyPriority,
            BuildingId.None,
            target,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            SupplyPriority: priority);

    public static PlayerActionRequest BeginAttackTargeting(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.BeginAttackTargeting,
            entities);

    public static PlayerActionRequest BeginAttackMoveTargeting(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.BeginAttackMoveTargeting,
            entities);

    public static PlayerActionRequest StopCombat(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitStopCombat,
            entities);

    public static PlayerActionRequest HoldPosition(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitHoldPosition,
            entities);

    public static PlayerActionRequest BeginRetreatTargeting(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.BeginRetreatTargeting,
            entities);

    public static PlayerActionRequest BeginFireMissionTargeting(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.BeginFireMissionTargeting,
            entities);

    public static PlayerActionRequest CancelFireMission(
        IReadOnlyList<EntityId> entities) =>
        CreateTactical(
            PlayerActionRequestKind.CancelFireMission,
            entities);

    public static PlayerActionRequest Attack(
        IReadOnlyList<EntityId> entities,
        EntityId target) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitAttack,
            entities,
            tacticalTarget: target);

    public static PlayerActionRequest AttackMove(
        IReadOnlyList<EntityId> entities,
        Vector3 destination,
        FormationTemplate formation) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitAttackMove,
            entities,
            tacticalWorldTarget: destination,
            tacticalFormation: formation);

    public static PlayerActionRequest Retreat(
        IReadOnlyList<EntityId> entities,
        Vector3 destination,
        FormationTemplate formation) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitRetreat,
            entities,
            tacticalWorldTarget: destination,
            tacticalFormation: formation);

    public static PlayerActionRequest FireMission(
        IReadOnlyList<EntityId> entities,
        Vector3 coordinate,
        int requestedRounds) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitFireMissionCoordinate,
            entities,
            tacticalWorldTarget: coordinate,
            tacticalRounds: requestedRounds);

    public static PlayerActionRequest FireMission(
        IReadOnlyList<EntityId> entities,
        IntelligenceContactKey contactKey,
        int requestedRounds) =>
        CreateTactical(
            PlayerActionRequestKind.SubmitFireMissionContact,
            entities,
            tacticalContactKey: contactKey,
            tacticalRounds: requestedRounds);

    public static PlayerActionRequest Surrender() =>
        new(
            PlayerActionRequestKind.Surrender,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    private static PlayerActionRequest CreateTactical(
        PlayerActionRequestKind kind,
        IReadOnlyList<EntityId> entities,
        EntityId tacticalTarget = default,
        Vector3 tacticalWorldTarget = default,
        IntelligenceContactKey tacticalContactKey = default,
        int tacticalRounds = 0,
        FormationTemplate tacticalFormation = FormationTemplate.Compact) =>
        new(
            kind,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0,
            TacticalEntities:
                entities?.ToArray() ??
                throw new ArgumentNullException(nameof(entities)),
            TacticalTarget: tacticalTarget,
            TacticalWorldTarget: tacticalWorldTarget,
            TacticalContactKey: tacticalContactKey,
            TacticalRounds: tacticalRounds,
            TacticalFormation: tacticalFormation);
}
