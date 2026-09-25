using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum BuildCommandRejectionReason
{
    None = 0,
    UnknownBuilding = 1,
    InvalidSourceInventory = 2,
    SourceInventoryOwnershipMismatch = 3,
    InsufficientResources = 4,
    PlacementInvalid = 5
}

public readonly record struct BuildCommandMetrics(
    long AcceptedCommands,
    long RejectedCommands,
    BuildCommandRejectionReason LastRejection,
    BuildingPlacementFailureReason LastPlacementFailure,
    EntityId LastCreatedSite);

public readonly record struct BuildingConstructionMetrics(
    int ActiveSites,
    long CancelledSites,
    long CompletedBuildings);

public sealed class BuildCommand : ISimulationCommand
{
    public BuildCommand(
        PlayerId issuer,
        BuildingId buildingId,
        Vector3 position,
        BuildingOrientation orientation,
        EntityId sourceInventory,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!buildingId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(buildingId));
        }

        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(orientation));
        }

        if (!sourceInventory.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceInventory));
        }

        Issuer = issuer;
        BuildingId = buildingId;
        Position = position;
        Orientation = orientation;
        SourceInventory = sourceInventory;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerId Issuer { get; }

    public BuildingId BuildingId { get; }

    public Vector3 Position { get; }

    public BuildingOrientation Orientation { get; }

    public EntityId SourceInventory { get; }

    public SimulationTick SubmittedAtTick { get; }

    public EntityId RequestEntity { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        EntityId request = context.Entities.CreateEntity();
        context.Entities.AddComponent(
            request,
            new BuildingBuildRequest(
                Issuer,
                BuildingId,
                Position,
                Orientation,
                SourceInventory,
                SubmittedAtTick,
                context.Tick));

        RequestEntity = request;
        ExecutedAtTick = context.Tick;
    }
}

public sealed class CancelConstructionCommand : ISimulationCommand
{
    public CancelConstructionCommand(
        PlayerId issuer,
        EntityId constructionSite,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!constructionSite.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(constructionSite));
        }

        Issuer = issuer;
        ConstructionSite = constructionSite;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerId Issuer { get; }

    public EntityId ConstructionSite { get; }

    public SimulationTick SubmittedAtTick { get; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.IsAlive(ConstructionSite) ||
            !context.Entities.TryGetComponent(
                ConstructionSite,
                out ConstructionSite site) ||
            site.Owner != Issuer)
        {
            Accepted = false;
            return;
        }

        if (!context.Entities.HasComponent<ConstructionCancellationRequest>(
                ConstructionSite))
        {
            context.Entities.AddComponent(
                ConstructionSite,
                new ConstructionCancellationRequest(
                    Issuer,
                    SubmittedAtTick,
                    context.Tick));
        }

        Accepted = true;
    }
}

internal readonly record struct BuildingBuildRequest(
    PlayerId Issuer,
    BuildingId BuildingId,
    Vector3 Position,
    BuildingOrientation Orientation,
    EntityId SourceInventory,
    SimulationTick SubmittedAtTick,
    SimulationTick ExecutedAtTick);

public readonly record struct ConstructionCancellationRequest(
    PlayerId Issuer,
    SimulationTick SubmittedAtTick,
    SimulationTick ExecutedAtTick);

public readonly record struct ConstructionSite
{
    public ConstructionSite(
        BuildingId buildingId,
        PlayerId owner,
        InventoryId sourceInventory,
        SimulationTick startedAtTick,
        uint requiredTicks,
        uint progressTicks,
        EntityId resourceDeposit = default)
    {
        if (!buildingId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(buildingId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }

        if (!sourceInventory.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceInventory));
        }

        if (requiredTicks == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredTicks));
        }

        if (progressTicks > requiredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(progressTicks));
        }

        BuildingId = buildingId;
        Owner = owner;
        SourceInventory = sourceInventory;
        StartedAtTick = startedAtTick;
        RequiredTicks = requiredTicks;
        ProgressTicks = progressTicks;
        ResourceDeposit = resourceDeposit;
    }

    public BuildingId BuildingId { get; }

    public PlayerId Owner { get; }

    public InventoryId SourceInventory { get; }

    public SimulationTick StartedAtTick { get; }

    public uint RequiredTicks { get; }

    public uint ProgressTicks { get; }

    public EntityId ResourceDeposit { get; }

    public bool IsComplete => ProgressTicks >= RequiredTicks;

    public float Progress =>
        RequiredTicks == 0
            ? 1.0f
            : Math.Clamp((float)ProgressTicks / RequiredTicks, 0.0f, 1.0f);

    public ConstructionSite Advance()
    {
        uint next = ProgressTicks < RequiredTicks
            ? ProgressTicks + 1
            : RequiredTicks;

        return new ConstructionSite(
            BuildingId,
            Owner,
            SourceInventory,
            StartedAtTick,
            RequiredTicks,
            next,
            ResourceDeposit);
    }
}

public readonly record struct CompletedBuilding(
    BuildingId BuildingId,
    PlayerId Owner,
    SimulationTick CompletedAtTick);

public readonly record struct CommandFacility;

public readonly record struct ProcessingFacility;
