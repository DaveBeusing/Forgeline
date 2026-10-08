using System.Collections.ObjectModel;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum PlayerTechnologyState : byte
{
    Available = 0,
    Locked = 1,
    Blocked = 2,
    Researching = 3,
    Completed = 4
}

public readonly record struct PlayerTechnologyPrerequisiteReadModel(
    TechnologyId TechnologyId,
    string DisplayName,
    bool Completed);

public sealed class PlayerTechnologyActionReadModel
{
    private readonly IReadOnlyList<PlayerActionResourceAmount> _costs;
    private readonly IReadOnlyList<PlayerTechnologyPrerequisiteReadModel> _prerequisites;
    private readonly IReadOnlyList<TechnologyCapabilityId> _unlocks;

    public PlayerTechnologyActionReadModel(
        TechnologyId technologyId,
        string key,
        string displayName,
        TechnologyDomain domain,
        TechnologyPhase phase,
        uint researchTicks,
        IReadOnlyList<PlayerActionResourceAmount> costs,
        IReadOnlyList<PlayerTechnologyPrerequisiteReadModel> prerequisites,
        BuildingId requiredFacility,
        string requiredFacilityName,
        EntityId facility,
        EntityId sourceInventory,
        double requiredPowerFraction,
        double currentPowerFraction,
        PlayerTechnologyState state,
        TechnologyResearchBlockReason blockReason,
        double progress,
        EntityId activeRequest,
        IReadOnlyList<TechnologyCapabilityId> unlocks)
    {
        TechnologyId = technologyId;
        Key =
            key ??
            throw new ArgumentNullException(nameof(key));
        DisplayName =
            displayName ??
            throw new ArgumentNullException(nameof(displayName));
        Domain = domain;
        Phase = phase;
        ResearchTicks = researchTicks;
        _costs =
            Array.AsReadOnly(
                costs?.ToArray() ??
                throw new ArgumentNullException(nameof(costs)));
        _prerequisites =
            Array.AsReadOnly(
                prerequisites?.ToArray() ??
                throw new ArgumentNullException(nameof(prerequisites)));
        RequiredFacility = requiredFacility;
        RequiredFacilityName =
            requiredFacilityName ??
            throw new ArgumentNullException(nameof(requiredFacilityName));
        Facility = facility;
        SourceInventory = sourceInventory;
        RequiredPowerFraction = requiredPowerFraction;
        CurrentPowerFraction = currentPowerFraction;
        State = state;
        BlockReason = blockReason;
        Progress =
            Math.Clamp(
                progress,
                0.0,
                1.0);
        ActiveRequest = activeRequest;
        _unlocks =
            Array.AsReadOnly(
                unlocks?.ToArray() ??
                throw new ArgumentNullException(nameof(unlocks)));
    }

    public TechnologyId TechnologyId { get; }

    public string Key { get; }

    public string DisplayName { get; }

    public TechnologyDomain Domain { get; }

    public TechnologyPhase Phase { get; }

    public uint ResearchTicks { get; }

    public IReadOnlyList<PlayerActionResourceAmount> Costs =>
        _costs;

    public IReadOnlyList<PlayerTechnologyPrerequisiteReadModel> Prerequisites =>
        _prerequisites;

    public BuildingId RequiredFacility { get; }

    public string RequiredFacilityName { get; }

    public EntityId Facility { get; }

    public EntityId SourceInventory { get; }

    public double RequiredPowerFraction { get; }

    public double CurrentPowerFraction { get; }

    public PlayerTechnologyState State { get; }

    public TechnologyResearchBlockReason BlockReason { get; }

    public double Progress { get; }

    public EntityId ActiveRequest { get; }

    public IReadOnlyList<TechnologyCapabilityId> Unlocks =>
        _unlocks;

    public bool CanStart =>
        State ==
        PlayerTechnologyState.Available;

    public bool CanCancel =>
        ActiveRequest.IsValid &&
        State is
            PlayerTechnologyState.Researching or
            PlayerTechnologyState.Blocked;
}

internal static class TechnologyActionSnapshotFactory
{
    public static PlayerTechnologyActionReadModel[] Capture(
        EntityRegistry entities,
        PresentationExtractionContext extraction)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(extraction);

        MatchRuntime scenario =
            extraction.Scenario;
        TechnologyDefinitionCatalog catalog =
            scenario.Services.TechnologyDefinitions;
        PlayerId player =
            extraction.Player;
        InventoryId sourceInventory =
            extraction.Side.StartingInventory;
        EntityId sourceInventoryEntity =
            extraction.Side.CommandCore;

        bool hasActive =
            TechnologyStateQueries.TryGetActiveResearch(
                entities,
                player,
                out EntityId activeRequestEntity,
                out TechnologyResearchRequest activeRequest);

        var result =
            new List<PlayerTechnologyActionReadModel>(
                catalog.Count);

        foreach (TechnologyDefinition definition in
                 catalog.Definitions)
        {
            EntityId facility =
                TechnologyResearchPolicy.FindRequiredFacility(
                    entities,
                    player,
                    definition.RequiredFacility);
            bool completed =
                TechnologyStateQueries.IsCompleted(
                    entities,
                    player,
                    definition.Id);
            bool isActive =
                hasActive &&
                activeRequest.TechnologyId ==
                    definition.Id;

            var costs =
                new PlayerActionResourceAmount[
                    definition.Costs.Count];

            for (int index = 0;
                 index < definition.Costs.Count;
                 index++)
            {
                TechnologyResourceCost cost =
                    definition.Costs[index];
                string displayName =
                    scenario.Services.Resources.TryGet(
                        cost.ResourceId,
                        out ResourceDefinition? resource)
                        ? resource.DisplayName
                        : cost.ResourceId.ToString();
                double available =
                    scenario.Inventories.Contains(
                        sourceInventory)
                        ? scenario.Inventories.GetAvailableQuantity(
                            sourceInventory,
                            cost.ResourceId)
                        : 0.0;

                costs[index] =
                    new PlayerActionResourceAmount(
                        cost.ResourceId,
                        displayName,
                        cost.Quantity,
                        available);
            }

            var prerequisites =
                new PlayerTechnologyPrerequisiteReadModel[
                    definition.Prerequisites.Count];

            for (int index = 0;
                 index < definition.Prerequisites.Count;
                 index++)
            {
                TechnologyId prerequisiteId =
                    definition.Prerequisites[index];
                TechnologyDefinition prerequisite =
                    catalog[prerequisiteId];

                prerequisites[index] =
                    new PlayerTechnologyPrerequisiteReadModel(
                        prerequisiteId,
                        prerequisite.DisplayName,
                        TechnologyStateQueries.IsCompleted(
                            entities,
                            player,
                            prerequisiteId));
            }

            double powerFraction =
                facility.IsValid &&
                entities.TryGetComponent(
                    facility,
                    out PowerConsumer power)
                    ? power.SupplyFraction
                    : 0.0;

            TechnologyResearchBlockReason block =
                completed
                    ? TechnologyResearchBlockReason.AlreadyCompleted
                    : isActive
                        ? activeRequest.BlockReason
                        : hasActive
                            ? TechnologyResearchBlockReason.ResearchInProgress
                            : TechnologyResearchPolicy.Evaluate(
                                entities,
                                scenario.Inventories,
                                catalog,
                                player,
                                definition,
                                facility,
                                sourceInventory,
                                materialsConsumed: false);

            PlayerTechnologyState state;
            double progress = 0.0;
            EntityId requestEntity =
                EntityId.Invalid;

            if (completed)
            {
                state =
                    PlayerTechnologyState.Completed;
            }
            else if (isActive)
            {
                requestEntity =
                    activeRequestEntity;
                progress =
                    Math.Clamp(
                        activeRequest.ProgressTicks /
                        (double)definition.ResearchTicks,
                        0.0,
                        1.0);

                if (activeRequest.Status ==
                        TechnologyResearchStatus.Researching ||
                    block ==
                        TechnologyResearchBlockReason.None)
                {
                    state =
                        PlayerTechnologyState.Researching;
                    block =
                        TechnologyResearchBlockReason.None;
                }
                else
                {
                    state =
                        PlayerTechnologyState.Blocked;
                }
            }
            else if (block ==
                     TechnologyResearchBlockReason.None)
            {
                state =
                    PlayerTechnologyState.Available;
            }
            else if (block ==
                     TechnologyResearchBlockReason.UnmetPrerequisite)
            {
                state =
                    PlayerTechnologyState.Locked;
            }
            else
            {
                state =
                    PlayerTechnologyState.Blocked;
            }

            string facilityName =
                scenario.Services.BuildingDefinitions.TryGet(
                    definition.RequiredFacility,
                    out BuildingDefinition? building)
                    ? building.DisplayName
                    : definition.RequiredFacility.ToString();

            result.Add(
                new PlayerTechnologyActionReadModel(
                    definition.Id,
                    definition.Key,
                    definition.DisplayName,
                    definition.Domain,
                    definition.Phase,
                    definition.ResearchTicks,
                    costs,
                    prerequisites,
                    definition.RequiredFacility,
                    facilityName,
                    facility,
                    sourceInventoryEntity,
                    definition.RequiredPowerFraction,
                    powerFraction,
                    state,
                    block,
                    progress,
                    requestEntity,
                    definition.Unlocks));
        }

        return result.ToArray();
    }
}
