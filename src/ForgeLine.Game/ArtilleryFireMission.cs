using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum FireMissionStatus : byte
{
    Ordered = 0,
    Acquiring = 1,
    Firing = 2,
    WaitingReload = 3,
    NoAmmo = 4,
    Complete = 5,
    Cancelled = 6
}

public enum FireMissionTargetKind : byte
{
    Coordinate = 0,
    Contact = 1
}

public readonly record struct FireMissionRequest
{
    public FireMissionRequest(
        PlayerId issuer,
        Vector3 coordinate,
        int requestedRounds,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!IsFinite(coordinate))
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(requestedRounds, 1);

        Issuer = issuer;
        TargetKind = FireMissionTargetKind.Coordinate;
        RequestedCoordinate = coordinate;
        ContactKey = IntelligenceContactKey.None;
        RequestedRounds = requestedRounds;
        SubmittedAtTick = submittedAtTick;
    }

    public FireMissionRequest(
        PlayerId issuer,
        IntelligenceContactKey contactKey,
        int requestedRounds,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!contactKey.IsSpecified)
        {
            throw new ArgumentException(
                "Contact fire missions require a valid contact key.",
                nameof(contactKey));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(requestedRounds, 1);

        Issuer = issuer;
        TargetKind = FireMissionTargetKind.Contact;
        RequestedCoordinate = Vector3.Zero;
        ContactKey = contactKey;
        RequestedRounds = requestedRounds;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerId Issuer { get; }

    public FireMissionTargetKind TargetKind { get; }

    public Vector3 RequestedCoordinate { get; }

    public IntelligenceContactKey ContactKey { get; }

    public int RequestedRounds { get; }

    public SimulationTick SubmittedAtTick { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct FireMissionState(
    Vector3 TargetPosition,
    IntelligenceContactKey ContactKey,
    int RequestedRounds,
    int RoundsFired,
    FireMissionStatus Status,
    SimulationTick OrderedAtTick,
    SimulationTick TargetInformationTick,
    SimulationTick AcquisitionCompleteTick,
    SimulationTick NextFireTick);

public sealed class FireMissionCommand : ISimulationCommand
{
    private readonly EntityId[] _artillery;

    public FireMissionCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery,
        Vector3 targetCoordinate,
        int requestedRounds,
        SimulationTick submittedAtTick)
    {
        ValidateTargets(issuer, artillery);

        Issuer = issuer;
        TargetKind = FireMissionTargetKind.Coordinate;
        TargetCoordinate = targetCoordinate;
        ContactKey = IntelligenceContactKey.None;
        RequestedRounds = requestedRounds;
        SubmittedAtTick = submittedAtTick;

        _ = new FireMissionRequest(
            issuer,
            targetCoordinate,
            requestedRounds,
            submittedAtTick);

        _artillery = artillery.ToArray();
    }

    public FireMissionCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery,
        IntelligenceContactKey contactKey,
        int requestedRounds,
        SimulationTick submittedAtTick)
    {
        ValidateTargets(issuer, artillery);

        Issuer = issuer;
        TargetKind = FireMissionTargetKind.Contact;
        TargetCoordinate = Vector3.Zero;
        ContactKey = contactKey;
        RequestedRounds = requestedRounds;
        SubmittedAtTick = submittedAtTick;

        _ = new FireMissionRequest(
            issuer,
            contactKey,
            requestedRounds,
            submittedAtTick);

        _artillery = artillery.ToArray();
    }

    public PlayerId Issuer { get; }

    public FireMissionTargetKind TargetKind { get; }

    public Vector3 TargetCoordinate { get; }

    public IntelligenceContactKey ContactKey { get; }

    public int RequestedRounds { get; }

    public SimulationTick SubmittedAtTick { get; }

    public ReadOnlySpan<EntityId> Artillery => _artillery;

    public int AcceptedTargetCount { get; private set; }

    public int RejectedTargetCount { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int accepted = 0;
        int rejected = 0;

        for (int index = 0; index < _artillery.Length; index++)
        {
            EntityId entity = _artillery[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                !controllable.IsControllable ||
                controllable.Owner != Issuer ||
                !context.Entities.HasComponent<ArtilleryCapability>(entity) ||
                !context.Entities.HasComponent<WorldTransform>(entity))
            {
                rejected++;
                continue;
            }

            FireMissionRequest request =
                TargetKind == FireMissionTargetKind.Contact
                    ? new FireMissionRequest(
                        Issuer,
                        ContactKey,
                        RequestedRounds,
                        SubmittedAtTick)
                    : new FireMissionRequest(
                        Issuer,
                        TargetCoordinate,
                        RequestedRounds,
                        SubmittedAtTick);

            if (context.Entities.HasComponent<FireMissionRequest>(entity))
            {
                context.Entities.SetComponent(entity, request);
            }
            else
            {
                context.Entities.AddComponent(entity, request);
            }

            accepted++;
        }

        AcceptedTargetCount = accepted;
        RejectedTargetCount = rejected;
        ExecutedAtTick = context.Tick;
    }

    private static void ValidateTargets(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (artillery.IsEmpty)
        {
            throw new ArgumentException(
                "A fire mission requires at least one artillery entity.",
                nameof(artillery));
        }
    }
}

public sealed class CancelFireMissionCommand : ISimulationCommand
{
    private readonly EntityId[] _artillery;

    public CancelFireMissionCommand(
        PlayerId issuer,
        ReadOnlySpan<EntityId> artillery)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (artillery.IsEmpty)
        {
            throw new ArgumentException(
                "Cancellation requires at least one artillery entity.",
                nameof(artillery));
        }

        Issuer = issuer;
        _artillery = artillery.ToArray();
    }

    public PlayerId Issuer { get; }

    public int CancelledTargetCount { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int cancelled = 0;

        for (int index = 0; index < _artillery.Length; index++)
        {
            EntityId entity = _artillery[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                controllable.Owner != Issuer)
            {
                continue;
            }

            if (context.Entities.HasComponent<FireMissionRequest>(entity))
            {
                context.Entities.RemoveComponent<FireMissionRequest>(entity);
            }

            if (!context.Entities.TryGetComponent(
                    entity,
                    out FireMissionState state) ||
                state.Status is FireMissionStatus.Complete or
                    FireMissionStatus.Cancelled)
            {
                continue;
            }

            context.Entities.SetComponent(
                entity,
                state with
                {
                    Status = FireMissionStatus.Cancelled
                });
            cancelled++;
        }

        CancelledTargetCount = cancelled;
    }
}
