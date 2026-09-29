using System.Collections.ObjectModel;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct PlayerActionResourceAmount(
    ResourceId ResourceId,
    string DisplayName,
    double RequiredQuantity,
    double AvailableQuantity)
{
    public bool IsAvailable =>
        AvailableQuantity >= RequiredQuantity;
}

public sealed class PlayerConstructionActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _costs;

    public PlayerConstructionActionReadModel(
        BuildingId buildingId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> costs,
        bool requiresResourceDeposit)
    {
        BuildingId = buildingId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _costs =
            Array.AsReadOnly(
                costs?.ToArray() ??
                throw new ArgumentNullException(nameof(costs)));
        RequiresResourceDeposit = requiresResourceDeposit;
    }

    public BuildingId BuildingId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public bool RequiresResourceDeposit { get; }

    public bool HasRequiredResources =>
        _costs.All(static cost => cost.IsAvailable);
}

public sealed class PlayerProductionRecipeActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _inputs;
    private readonly IReadOnlyList<PlayerActionResourceAmount> _outputs;

    public PlayerProductionRecipeActionReadModel(
        RecipeId recipeId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> inputs,
        IReadOnlyList<PlayerActionResourceAmount> outputs)
    {
        RecipeId = recipeId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _inputs =
            Array.AsReadOnly(
                inputs?.ToArray() ??
                throw new ArgumentNullException(nameof(inputs)));
        _outputs =
            Array.AsReadOnly(
                outputs?.ToArray() ??
                throw new ArgumentNullException(nameof(outputs)));
    }

    public RecipeId RecipeId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Inputs => _inputs;

    public IReadOnlyList<PlayerActionResourceAmount> Outputs => _outputs;

    public bool HasInputs =>
        _inputs.All(static input => input.IsAvailable);
}

public readonly record struct PlayerProductionRequestReadModel(
    EntityId RequestEntity,
    RecipeId RecipeId,
    string DisplayName,
    ProductionPriority Priority,
    ProductionRequestMode Mode,
    bool Paused,
    bool IsActive);

public sealed class PlayerProductionFacilityActionReadModel
{
    private readonly IReadOnlyList<PlayerProductionRecipeActionReadModel> _recipes;
    private readonly IReadOnlyList<PlayerProductionRequestReadModel> _requests;

    public PlayerProductionFacilityActionReadModel(
        EntityId entity,
        EntityId activeRequest,
        RecipeId activeRecipe,
        ProductionStatus status,
        ProductionBlockReason blockReason,
        double progress,
        IReadOnlyList<PlayerProductionRecipeActionReadModel> recipes,
        IReadOnlyList<PlayerProductionRequestReadModel> requests)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveRecipe = activeRecipe;
        Status = status;
        BlockReason = blockReason;
        Progress = progress;
        _recipes =
            Array.AsReadOnly(
                recipes?.ToArray() ??
                throw new ArgumentNullException(nameof(recipes)));
        _requests =
            Array.AsReadOnly(
                requests?.ToArray() ??
                throw new ArgumentNullException(nameof(requests)));
    }

    public EntityId Entity { get; }

    public EntityId ActiveRequest { get; }

    public RecipeId ActiveRecipe { get; }

    public ProductionStatus Status { get; }

    public ProductionBlockReason BlockReason { get; }

    public double Progress { get; }

    public IReadOnlyList<PlayerProductionRecipeActionReadModel> Recipes =>
        _recipes;

    public IReadOnlyList<PlayerProductionRequestReadModel> Requests =>
        _requests;
}

public sealed class PlayerUnitProductionActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _costs;

    public PlayerUnitProductionActionReadModel(
        UnitId unitId,
        string displayName,
        IReadOnlyList<PlayerActionResourceAmount> costs,
        uint productionTicks)
    {
        UnitId = unitId;
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        _costs =
            Array.AsReadOnly(
                costs?.ToArray() ??
                throw new ArgumentNullException(nameof(costs)));
        ProductionTicks = productionTicks;
    }

    public UnitId UnitId { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs => _costs;

    public uint ProductionTicks { get; }

    public bool HasInputs =>
        _costs.All(static cost => cost.IsAvailable);
}

public readonly record struct PlayerUnitProductionRequestReadModel(
    EntityId RequestEntity,
    UnitId UnitId,
    string DisplayName,
    ProductionPriority Priority,
    bool Paused,
    bool IsActive);

public sealed class PlayerUnitProductionFacilityActionReadModel
{
    private readonly IReadOnlyList<PlayerUnitProductionActionReadModel> _units;
    private readonly IReadOnlyList<PlayerUnitProductionRequestReadModel> _requests;

    public PlayerUnitProductionFacilityActionReadModel(
        EntityId entity,
        EntityId activeRequest,
        UnitId activeUnit,
        UnitProductionStatus status,
        UnitProductionBlockReason blockReason,
        double progress,
        IReadOnlyList<PlayerUnitProductionActionReadModel> units,
        IReadOnlyList<PlayerUnitProductionRequestReadModel> requests)
    {
        Entity = entity;
        ActiveRequest = activeRequest;
        ActiveUnit = activeUnit;
        Status = status;
        BlockReason = blockReason;
        Progress = progress;
        _units =
            Array.AsReadOnly(
                units?.ToArray() ??
                throw new ArgumentNullException(nameof(units)));
        _requests =
            Array.AsReadOnly(
                requests?.ToArray() ??
                throw new ArgumentNullException(nameof(requests)));
    }

    public EntityId Entity { get; }

    public EntityId ActiveRequest { get; }

    public UnitId ActiveUnit { get; }

    public UnitProductionStatus Status { get; }

    public UnitProductionBlockReason BlockReason { get; }

    public double Progress { get; }

    public IReadOnlyList<PlayerUnitProductionActionReadModel> Units =>
        _units;

    public IReadOnlyList<PlayerUnitProductionRequestReadModel> Requests =>
        _requests;
}

public sealed class PlayerActionSnapshot
{
    private readonly IReadOnlyList<PlayerConstructionActionReadModel> _construction;

    public PlayerActionSnapshot(
        SimulationSessionId sessionId,
        SimulationTick tick,
        IReadOnlyList<PlayerConstructionActionReadModel> construction,
        PlayerProductionFacilityActionReadModel? production,
        PlayerUnitProductionFacilityActionReadModel? unitProduction)
    {
        SessionId = sessionId;
        Tick = tick;
        _construction =
            Array.AsReadOnly(
                construction?.ToArray() ??
                throw new ArgumentNullException(nameof(construction)));
        Production = production;
        UnitProduction = unitProduction;
    }

    public SimulationSessionId SessionId { get; }

    public SimulationTick Tick { get; }

    public IReadOnlyList<PlayerConstructionActionReadModel> Construction =>
        _construction;

    public PlayerProductionFacilityActionReadModel? Production { get; }

    public PlayerUnitProductionFacilityActionReadModel? UnitProduction { get; }
}

internal static class PlayerActionSnapshotFactory
{
    public static PlayerActionSnapshot Capture(
        SimulationContext context,
        PresentationExtractionContext extraction,
        in PresentationInteractionRequestSnapshot interaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extraction);

        VerticalSliceScenario scenario =
            extraction.Scenario;

        PlayerConstructionActionReadModel[] construction =
            CaptureConstructionActions(
                scenario);

        EntityId selectedFacility =
            ResolveSingleOwnedSelection(
                context.Entities,
                extraction.Player,
                interaction.SelectedEntities);

        PlayerProductionFacilityActionReadModel? production =
            selectedFacility.IsValid &&
            context.Entities.TryGetComponent(
                selectedFacility,
                out ProductionFacility productionFacility)
                ? CaptureProductionFacility(
                    context.Entities,
                    scenario,
                    selectedFacility,
                    in productionFacility)
                : null;

        PlayerUnitProductionFacilityActionReadModel? unitProduction =
            selectedFacility.IsValid &&
            context.Entities.TryGetComponent(
                selectedFacility,
                out UnitProductionFacility unitFacility) &&
            unitFacility.Owner == extraction.Player
                ? CaptureUnitProductionFacility(
                    context.Entities,
                    scenario,
                    selectedFacility,
                    in unitFacility)
                : null;

        return new PlayerActionSnapshot(
            scenario.Simulation.SessionId,
            context.Tick,
            construction,
            production,
            unitProduction);
    }

    private static PlayerConstructionActionReadModel[]
        CaptureConstructionActions(
            VerticalSliceScenario scenario)
    {
        var actions =
            new List<PlayerConstructionActionReadModel>();

        foreach (BuildingDefinition definition in
                 scenario.Services.BuildingDefinitions.Definitions)
        {
            PlayerActionResourceAmount[] costs =
                definition.Costs
                    .Select(
                        cost =>
                            CreateAmount(
                                scenario,
                                scenario.West.Player ==
                                scenario.West.Player
                                    ? scenario.West.StartingInventory
                                    : scenario.West.StartingInventory,
                                cost.ResourceId,
                                cost.Quantity))
                    .ToArray();

            actions.Add(
                new PlayerConstructionActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    costs,
                    definition.RequiresResourceDeposit));
        }

        return actions.ToArray();
    }

    private static PlayerProductionFacilityActionReadModel
        CaptureProductionFacility(
            EntityRegistry entities,
            VerticalSliceScenario scenario,
            EntityId entity,
            in ProductionFacility facility)
    {
        var recipes =
            new List<PlayerProductionRecipeActionReadModel>();

        foreach (ProductionRecipeDefinition definition in
                 scenario.Services.ProductionRecipes.Definitions)
        {
            if (!facility.Supports(
                    definition.RequiredCapability))
            {
                continue;
            }

            PlayerActionResourceAmount[] inputs =
                definition.Inputs
                    .Select(
                        ingredient =>
                            CreateAmount(
                                scenario,
                                facility.InputInventory,
                                ingredient.ResourceId,
                                ingredient.Quantity))
                    .ToArray();
            PlayerActionResourceAmount[] outputs =
                definition.Outputs
                    .Select(
                        ingredient =>
                            CreateAmount(
                                scenario,
                                facility.OutputInventory,
                                ingredient.ResourceId,
                                ingredient.Quantity))
                    .ToArray();

            recipes.Add(
                new PlayerProductionRecipeActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    inputs,
                    outputs));
        }

        var requests =
            new List<PlayerProductionRequestReadModel>();

        foreach (EntityId requestEntity in
                 entities.Query<ProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProductionRequest request =
                entities.GetComponent<ProductionRequest>(
                    requestEntity);

            if (request.Facility != entity)
            {
                continue;
            }

            string displayName =
                scenario.Services.ProductionRecipes.TryGet(
                    request.RecipeId,
                    out ProductionRecipeDefinition? definition)
                    ? definition.DisplayName
                    : request.RecipeId.ToString();

            requests.Add(
                new PlayerProductionRequestReadModel(
                    requestEntity,
                    request.RecipeId,
                    displayName,
                    request.Priority,
                    request.Mode,
                    request.Paused,
                    requestEntity == facility.ActiveRequest));
        }

        double progress = 0.0;
        if (facility.ActiveRecipe.IsSpecified &&
            scenario.Services.ProductionRecipes.TryGet(
                facility.ActiveRecipe,
                out ProductionRecipeDefinition? activeDefinition))
        {
            progress =
                Math.Clamp(
                    facility.ProgressTicks /
                    (double)activeDefinition.DurationTicks,
                    0.0,
                    1.0);
        }

        return new PlayerProductionFacilityActionReadModel(
            entity,
            facility.ActiveRequest,
            facility.ActiveRecipe,
            facility.Status,
            facility.BlockReason,
            progress,
            recipes,
            requests);
    }

    private static PlayerUnitProductionFacilityActionReadModel
        CaptureUnitProductionFacility(
            EntityRegistry entities,
            VerticalSliceScenario scenario,
            EntityId entity,
            in UnitProductionFacility facility)
    {
        var units =
            new List<PlayerUnitProductionActionReadModel>();

        foreach (UnitDefinition definition in
                 scenario.Services.UnitDefinitions.Definitions)
        {
            if (!facility.Supports(
                    definition.RequiredProductionCapability))
            {
                continue;
            }

            PlayerActionResourceAmount[] costs =
                definition.Costs
                    .Select(
                        cost =>
                            CreateAmount(
                                scenario,
                                facility.InputInventory,
                                cost.ResourceId,
                                cost.Quantity))
                    .ToArray();

            units.Add(
                new PlayerUnitProductionActionReadModel(
                    definition.Id,
                    definition.DisplayName,
                    costs,
                    definition.ProductionTicks));
        }

        var requests =
            new List<PlayerUnitProductionRequestReadModel>();

        foreach (EntityId requestEntity in
                 entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (request.Facility != entity)
            {
                continue;
            }

            string displayName =
                scenario.Services.UnitDefinitions.TryGet(
                    request.UnitId,
                    out UnitDefinition? definition)
                    ? definition.DisplayName
                    : request.UnitId.ToString();

            requests.Add(
                new PlayerUnitProductionRequestReadModel(
                    requestEntity,
                    request.UnitId,
                    displayName,
                    request.Priority,
                    request.Paused,
                    requestEntity == facility.ActiveRequest));
        }

        double progress = 0.0;
        if (facility.ActiveUnit.IsSpecified &&
            scenario.Services.UnitDefinitions.TryGet(
                facility.ActiveUnit,
                out UnitDefinition? activeDefinition))
        {
            progress =
                Math.Clamp(
                    facility.ProgressTicks /
                    (double)activeDefinition.ProductionTicks,
                    0.0,
                    1.0);
        }

        return new PlayerUnitProductionFacilityActionReadModel(
            entity,
            facility.ActiveRequest,
            facility.ActiveUnit,
            facility.Status,
            facility.BlockReason,
            progress,
            units,
            requests);
    }

    private static EntityId ResolveSingleOwnedSelection(
        EntityRegistry entities,
        PlayerId player,
        IReadOnlyList<EntityId> selectedEntities)
    {
        if (selectedEntities.Count != 1)
        {
            return EntityId.Invalid;
        }

        EntityId entity =
            selectedEntities[0];

        return entities.IsAlive(entity) &&
               entities.TryGetComponent(
                   entity,
                   out ControllableEntity controllable) &&
               controllable.Owner == player
            ? entity
            : EntityId.Invalid;
    }

    private static PlayerActionResourceAmount CreateAmount(
        VerticalSliceScenario scenario,
        InventoryId inventory,
        ResourceId resourceId,
        double requiredQuantity)
    {
        string displayName =
            scenario.Services.Resources.TryGet(
                resourceId,
                out ResourceDefinition? definition)
                ? definition.DisplayName
                : resourceId.ToString();

        double available =
            scenario.Inventories.Contains(inventory)
                ? scenario.Inventories.GetQuantity(
                    inventory,
                    resourceId)
                : 0.0;

        return new PlayerActionResourceAmount(
            resourceId,
            displayName,
            requiredQuantity,
            available);
    }
}
