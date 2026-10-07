using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PlayerTechnologyOperation : byte
{
    Start = 1,
    Cancel = 2
}

public sealed class PlayerTechnologyActionCommand : ISimulationCommand
{
    private readonly TechnologyDefinitionCatalog? _catalog;

    private PlayerTechnologyActionCommand(
        PlayerTechnologyOperation operation,
        PlayerId issuer,
        TechnologyId technologyId,
        EntityId facility,
        EntityId sourceInventory,
        EntityId requestEntity,
        SimulationTick submittedAtTick,
        TechnologyDefinitionCatalog? catalog)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(issuer));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operation));
        }

        Operation = operation;
        Issuer = issuer;
        TechnologyId = technologyId;
        Facility = facility;
        SourceInventory = sourceInventory;
        RequestEntity = requestEntity;
        SubmittedAtTick = submittedAtTick;
        _catalog = catalog;
    }

    public PlayerTechnologyOperation Operation { get; }

    public PlayerId Issuer { get; }

    public TechnologyId TechnologyId { get; }

    public EntityId Facility { get; }

    public EntityId SourceInventory { get; }

    public EntityId RequestEntity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public EntityId CreatedRequestEntity { get; private set; }

    public bool Accepted { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public static PlayerTechnologyActionCommand Start(
        PlayerId issuer,
        TechnologyId technologyId,
        EntityId facility,
        EntityId sourceInventory,
        SimulationTick submittedAtTick,
        TechnologyDefinitionCatalog catalog) =>
        new(
            PlayerTechnologyOperation.Start,
            issuer,
            technologyId,
            facility,
            sourceInventory,
            EntityId.Invalid,
            submittedAtTick,
            catalog ??
                throw new ArgumentNullException(nameof(catalog)));

    public static PlayerTechnologyActionCommand Cancel(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick submittedAtTick) =>
        new(
            PlayerTechnologyOperation.Cancel,
            issuer,
            TechnologyId.None,
            EntityId.Invalid,
            EntityId.Invalid,
            requestEntity,
            submittedAtTick,
            catalog: null);

    public void Execute(
        SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        Accepted = false;
        CreatedRequestEntity = EntityId.Invalid;

        switch (Operation)
        {
            case PlayerTechnologyOperation.Start:
                ExecuteStart(
                    context);
                break;

            case PlayerTechnologyOperation.Cancel:
                ExecuteCancel(
                    context);
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported technology operation '{Operation}'.");
        }
    }

    private void ExecuteStart(
        SimulationContext context)
    {
        if (_catalog is null ||
            !_catalog.TryGet(
                TechnologyId,
                out TechnologyDefinition? definition) ||
            TechnologyStateQueries.IsCompleted(
                context.Entities,
                Issuer,
                TechnologyId) ||
            TechnologyStateQueries.TryGetActiveResearch(
                context.Entities,
                Issuer,
                out _,
                out _))
        {
            return;
        }

        if (!context.Entities.TryGetComponent(
                Facility,
                out CompletedBuilding facility) ||
            facility.Owner != Issuer ||
            facility.BuildingId !=
                definition.RequiredFacility)
        {
            return;
        }

        if (!context.Entities.TryGetComponent(
                SourceInventory,
                out InventoryStorage storage) ||
            !OwnsEntity(
                context,
                SourceInventory))
        {
            return;
        }

        EntityId request =
            context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new TechnologyResearchRequest(
                Issuer,
                TechnologyId,
                Facility,
                storage.InventoryId,
                SubmittedAtTick));

        CreatedRequestEntity = request;
        Accepted = true;
    }

    private void ExecuteCancel(
        SimulationContext context)
    {
        if (!RequestEntity.IsValid ||
            !context.Entities.TryGetComponent(
                RequestEntity,
                out TechnologyResearchRequest request) ||
            request.Player != Issuer)
        {
            return;
        }

        if (!context.Entities.HasComponent<
                TechnologyResearchCancellationRequest>(
                    RequestEntity))
        {
            context.Entities.AddComponent(
                RequestEntity,
                new TechnologyResearchCancellationRequest());
        }

        Accepted = true;
    }

    private bool OwnsEntity(
        SimulationContext context,
        EntityId entity)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out ControllableEntity controllable))
        {
            return controllable.Owner ==
                Issuer;
        }

        return context.Entities.TryGetComponent(
                   entity,
                   out CompletedBuilding building) &&
               building.Owner ==
                   Issuer;
    }
}
