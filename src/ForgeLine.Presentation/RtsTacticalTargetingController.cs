using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum TacticalTargetingMode : byte
{
    None = 0,
    Attack = 1,
    AttackMove = 2,
    Retreat = 3,
    FireMission = 4
}

public readonly record struct TacticalTargetingView(
    TacticalTargetingMode Mode,
    int SelectedEntityCount,
    int AvailableAttackTargets,
    int KnownContacts,
    int RequestedRounds)
{
    public bool IsActive =>
        Mode != TacticalTargetingMode.None;

    public bool HasPointerTarget { get; init; }

    public bool PointerTargetValid { get; init; }

    public Vector3 PointerWorldTarget { get; init; }
}

public sealed class RtsTacticalTargetingController
{
    private const float TargetPickRadiusPixels = 22.0f;
    private const int DefaultFireMissionRounds = 3;

    private EntityId[] _entities = [];
    private PlayerActionRequest? _pendingRequest;
    private SimulationSessionId _sessionId;
    private bool _leftWasDown;
    private bool _escapeWasDown;
    private bool _pointerCaptured;
    private bool _hasPointerTarget;
    private bool _pointerTargetValid;
    private Vector3 _pointerWorldTarget;

    public TacticalTargetingMode Mode { get; private set; }

    public RtsCommandFeedback CommandFeedback { get; } = new();

    public bool IsActive =>
        Mode != TacticalTargetingMode.None;

    public bool PointerCaptured =>
        _pointerCaptured;

    public void Begin(
        in PlayerActionRequest request,
        SimulationSessionId sessionId)
    {
        TacticalTargetingMode mode =
            request.Kind switch
            {
                PlayerActionRequestKind.BeginAttackTargeting =>
                    TacticalTargetingMode.Attack,
                PlayerActionRequestKind.BeginAttackMoveTargeting =>
                    TacticalTargetingMode.AttackMove,
                PlayerActionRequestKind.BeginRetreatTargeting =>
                    TacticalTargetingMode.Retreat,
                PlayerActionRequestKind.BeginFireMissionTargeting =>
                    TacticalTargetingMode.FireMission,
                _ =>
                    TacticalTargetingMode.None
            };

        if (mode == TacticalTargetingMode.None ||
            request.TacticalEntities is not
                { Length: > 0 })
        {
            return;
        }

        _entities =
            request.TacticalEntities.ToArray();
        _sessionId = sessionId;
        Mode = mode;
        _pendingRequest = null;
        _leftWasDown = false;
        _escapeWasDown = false;
    }

    public void Update(
        InputState input,
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        int viewportWidth,
        int viewportHeight,
        FormationTemplate formation,
        bool pointerBlocked = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);

        SynchronizeSession(
            snapshot?.SessionId ??
            SimulationSessionId.None);

        if (snapshot?.PlayerExperience?.IsMatchComplete ==
            true)
        {
            Cancel();
        }

        _pointerCaptured =
            IsActive;

        if (IsActive &&
            !pointerBlocked &&
            input.HasPointerPosition)
        {
            UpdatePreview(
                camera,
                terrain,
                snapshot,
                input.PointerPosition,
                viewportWidth,
                viewportHeight);
        }
        else
        {
            ResetPreview();
        }

        bool leftDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);
        bool escapeDown =
            input.IsKeyDown(
                PlatformKey.Escape);

        if (IsActive &&
            input.FocusLostThisFrame)
        {
            Cancel();
        }

        if (IsActive &&
            escapeDown &&
            !_escapeWasDown)
        {
            Cancel();
        }

        if (IsActive &&
            !pointerBlocked &&
            input.HasPointerPosition &&
            leftDown &&
            !_leftWasDown)
        {
            if (_hasPointerTarget)
            {
                CommandFeedback.Show(_pointerWorldTarget, _pointerTargetValid,
                    Mode is TacticalTargetingMode.Attack or TacticalTargetingMode.AttackMove or TacticalTargetingMode.FireMission);
            }
            TryResolveTarget(
                camera,
                terrain,
                snapshot,
                input.PointerPosition,
                viewportWidth,
                viewportHeight,
                formation);
        }

        _leftWasDown = leftDown;
        _escapeWasDown = escapeDown;
    }

    public bool TryTakeRequest(
        out PlayerActionRequest request)
    {
        if (_pendingRequest is null)
        {
            request = default;
            return false;
        }

        request =
            _pendingRequest.Value;
        _pendingRequest = null;
        return true;
    }

    public TacticalTargetingView CreateView(
        PresentationSnapshot? snapshot) =>
        new(
            Mode,
            _entities.Length,
            snapshot?.PlayerActions?.Tactical?.Targets.Count ??
                0,
            snapshot?.Intelligence?.Contacts.Count ??
                0,
            DefaultFireMissionRounds)
        {
            HasPointerTarget =
                _hasPointerTarget,
            PointerTargetValid =
                _pointerTargetValid,
            PointerWorldTarget =
                _pointerWorldTarget
        };

    public void Cancel()
    {
        CommandFeedback.Clear();
        Mode =
            TacticalTargetingMode.None;
        _entities = [];
        _leftWasDown = false;
        _escapeWasDown = false;
        _pointerCaptured = false;
        ResetPreview();
    }

    private void UpdatePreview(
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight)
    {
        _hasPointerTarget =
            TryResolveTerrainTarget(
                camera,
                terrain,
                pointer,
                viewportWidth,
                viewportHeight,
                out _pointerWorldTarget);

        _pointerTargetValid =
            Mode switch
            {
                TacticalTargetingMode.Attack =>
                    TryPickAttackTarget(
                        camera,
                        snapshot,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out _),
                TacticalTargetingMode.AttackMove or
                TacticalTargetingMode.Retreat =>
                    _hasPointerTarget,
                TacticalTargetingMode.FireMission =>
                    TryPickContact(
                        camera,
                        snapshot?.Intelligence,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out _) ||
                    _hasPointerTarget,
                _ =>
                    false
            };
    }

    private void ResetPreview()
    {
        _hasPointerTarget = false;
        _pointerTargetValid = false;
        _pointerWorldTarget = default;
    }

    private void TryResolveTarget(
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight,
        FormationTemplate formation)
    {
        switch (Mode)
        {
            case TacticalTargetingMode.Attack:
                if (TryPickAttackTarget(
                        camera,
                        snapshot,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out EntityId target))
                {
                    _pendingRequest =
                        PlayerActionRequest.Attack(
                            _entities,
                            target);
                    Complete();
                }

                break;

            case TacticalTargetingMode.AttackMove:
                if (TryResolveTerrainTarget(
                        camera,
                        terrain,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out Vector3 attackMoveTarget))
                {
                    _pendingRequest =
                        PlayerActionRequest.AttackMove(
                            _entities,
                            attackMoveTarget,
                            formation);
                    Complete();
                }

                break;

            case TacticalTargetingMode.Retreat:
                if (TryResolveTerrainTarget(
                        camera,
                        terrain,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out Vector3 retreatTarget))
                {
                    _pendingRequest =
                        PlayerActionRequest.Retreat(
                            _entities,
                            retreatTarget,
                            formation);
                    Complete();
                }

                break;

            case TacticalTargetingMode.FireMission:
                if (TryPickContact(
                        camera,
                        snapshot?.Intelligence,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out IntelligenceContactKey contactKey))
                {
                    _pendingRequest =
                        PlayerActionRequest.FireMission(
                            _entities,
                            contactKey,
                            DefaultFireMissionRounds);
                    Complete();
                    break;
                }

                if (TryResolveTerrainTarget(
                        camera,
                        terrain,
                        pointer,
                        viewportWidth,
                        viewportHeight,
                        out Vector3 fireMissionTarget))
                {
                    _pendingRequest =
                        PlayerActionRequest.FireMission(
                            _entities,
                            fireMissionTarget,
                            DefaultFireMissionRounds);
                    Complete();
                }

                break;
        }
    }

    private static bool TryPickAttackTarget(
        RtsCamera camera,
        PresentationSnapshot? snapshot,
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight,
        out EntityId target)
    {
        target = EntityId.Invalid;

        if (snapshot?.PlayerActions?.Tactical is not
            PlayerTacticalActionReadModel tactical)
        {
            return false;
        }

        float maximumDistanceSquared =
            TargetPickRadiusPixels *
            TargetPickRadiusPixels;
        float nearestDistanceSquared =
            maximumDistanceSquared;

        for (int index = 0;
             index < tactical.Targets.Count;
             index++)
        {
            PlayerTacticalTargetReadModel candidate =
                tactical.Targets[index];

            if (candidate.CompatibleUnitCount <= 0 ||
                candidate.State !=
                    IntelligenceState.Identified)
            {
                continue;
            }

            ScreenProjection projection =
                camera.WorldToScreen(
                    candidate.LastKnownPosition,
                    viewportWidth,
                    viewportHeight);

            if (!projection.IsVisible)
            {
                continue;
            }

            float distanceSquared =
                Vector2.DistanceSquared(
                    pointer,
                    projection.Position);

            if (distanceSquared >
                    nearestDistanceSquared ||
                (distanceSquared ==
                     nearestDistanceSquared &&
                 target.IsValid &&
                 candidate.Entity >= target))
            {
                continue;
            }

            nearestDistanceSquared =
                distanceSquared;
            target =
                candidate.Entity;
        }

        return target.IsValid;
    }

    private static bool TryPickContact(
        RtsCamera camera,
        FactionIntelligenceSnapshot? intelligence,
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight,
        out IntelligenceContactKey contactKey)
    {
        contactKey =
            IntelligenceContactKey.None;

        if (intelligence is null)
        {
            return false;
        }

        float maximumDistanceSquared =
            TargetPickRadiusPixels *
            TargetPickRadiusPixels;
        float nearestDistanceSquared =
            maximumDistanceSquared;

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                intelligence.Contacts[index];

            if (!contact.ContactKey.IsSpecified ||
                contact.State is not
                    IntelligenceState.Detected and not
                    IntelligenceState.Identified)
            {
                continue;
            }

            ScreenProjection projection =
                camera.WorldToScreen(
                    contact.LastKnownPosition,
                    viewportWidth,
                    viewportHeight);

            if (!projection.IsVisible)
            {
                continue;
            }

            float distanceSquared =
                Vector2.DistanceSquared(
                    pointer,
                    projection.Position);

            if (distanceSquared >
                    nearestDistanceSquared ||
                (distanceSquared ==
                     nearestDistanceSquared &&
                 contactKey.IsSpecified &&
                 contact.ContactKey >= contactKey))
            {
                continue;
            }

            nearestDistanceSquared =
                distanceSquared;
            contactKey =
                contact.ContactKey;
        }

        return contactKey.IsSpecified;
    }

    private static bool TryResolveTerrainTarget(
        RtsCamera camera,
        ITerrainQuery terrain,
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight,
        out Vector3 worldTarget)
    {
        if (!camera.TryScreenPointToWorldOnHorizontalPlane(
                pointer,
                camera.Target.Y,
                viewportWidth,
                viewportHeight,
                out Vector3 horizontalTarget) ||
            !terrain.TrySampleHeight(
                horizontalTarget.X,
                horizontalTarget.Z,
                out float terrainHeight))
        {
            worldTarget = default;
            return false;
        }

        worldTarget =
            new Vector3(
                horizontalTarget.X,
                terrainHeight,
                horizontalTarget.Z);
        return true;
    }

    private void Complete()
    {
        Mode =
            TacticalTargetingMode.None;
        _entities = [];
        _leftWasDown = false;
        _escapeWasDown = false;
        ResetPreview();
    }

    private void SynchronizeSession(
        SimulationSessionId sessionId)
    {
        if (!sessionId.IsSpecified ||
            sessionId == _sessionId)
        {
            return;
        }

        _sessionId = sessionId;
        _pendingRequest = null;
        Cancel();
    }
}
