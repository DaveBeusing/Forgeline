using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct BuildingPlacementRequest(
    BuildingId BuildingId,
    Vector3 Position,
    BuildingOrientation Orientation,
    ulong PreviewRequestId,
    SimulationTick PreviewTick);

public sealed class RtsBuildingPlacementController
{
    private const float TargetChangeToleranceSquared = 0.01f;

    private readonly PlayerId _issuer;
    private readonly GameplayBindingRegistry _bindings;
    private readonly string _placementHint;
    private bool _leftWasDown;
    private bool _escapeWasDown;
    private bool _rotateWasDown;
    private readonly Dictionary<PlatformKey, bool> _selectionKeys = new()
    {
        [PlatformKey.F4] = false,
        [PlatformKey.F5] = false,
        [PlatformKey.F6] = false,
        [PlatformKey.F7] = false,
        [PlatformKey.F8] = false
    };
    private BuildingPlacementRequest? _pendingRequest;
    private bool _hasPreviewRequest;
    private ulong _previewRequestId;
    private Vector3 _requestedPosition;
    private BuildingId _requestedBuilding;
    private BuildingOrientation _requestedOrientation;
    private SimulationSessionId _session;
    private BuildingId _rememberedBuilding;
    private BuildingOrientation _rememberedOrientation;
    private bool _awaitingResult;
    private bool _repeatAtClick;
    private PlayerCommandCorrelationId _correlation;
    private SimulationTick _minimumPreviewTick;
    private int _width;
    private int _height;
    private float _displayScale;

    public GameplayBindingRegistry Bindings => _bindings;
    public bool AwaitingResult => _awaitingResult;
    public string InteractionHint => _awaitingResult ? "BUILD REQUEST PENDING" :
        IsActive ? _placementHint : string.Empty;

    public RtsBuildingPlacementController(PlayerId issuer, GameplayBindingRegistry? bindings = null)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        _issuer = issuer;
        _bindings = bindings ?? GameplayBindingRegistry.Default;
        _placementHint = $"SHIFT + CLICK REPEAT AFTER ACCEPTANCE / {_bindings.Prompt(GameplayAction.RotatePlacement)} ROTATE / ESC CANCEL";
        _selectionKeys.Clear();
        for (int i = (int)GameplayAction.CorePlacement; i <= (int)GameplayAction.SmelterPlacement; i++)
            _selectionKeys.TryAdd(_bindings.Key((GameplayAction)i), false);
    }

    public BuildingId ActiveBuilding { get; private set; } = BuildingId.None;

    public BuildingOrientation Orientation { get; private set; } =
        BuildingOrientation.North;

    public BuildingPlacementPreview? Preview { get; private set; }

    public PlacementPreviewFreshness PreviewFreshness { get; private set; } =
        PlacementPreviewFreshness.Unavailable;

    public bool IsActive => ActiveBuilding.IsSpecified;

    public void Update(
        InputState input,
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        PresentationInteractionState interaction,
        int viewportWidth,
        int viewportHeight,
        bool pointerCaptured = false,
        float displayScale = 1)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(interaction);
        bool sessionChanged = snapshot?.SessionId is { IsSpecified: true } session && _session.IsSpecified && session != _session;
        if (snapshot?.SessionId is { IsSpecified: true } currentSession) _session = currentSession;
        bool displayChanged = _width != 0 && (_width != viewportWidth || _height != viewportHeight || _displayScale != displayScale);
        _width = viewportWidth; _height = viewportHeight; _displayScale = displayScale;
        if (sessionChanged || displayChanged || input.FocusLostThisFrame || snapshot?.PlayerExperience?.IsMatchComplete == true)
        {
            Cancel(interaction);
            _leftWasDown = input.IsMouseButtonDown(PlatformMouseButton.Left);
            _rotateWasDown = input.IsKeyDown(_bindings.Key(GameplayAction.RotatePlacement));
            foreach (var key in _selectionKeys.Keys) _selectionKeys[key] = input.IsKeyDown(key);
            return;
        }

        bool shiftDown =
            input.IsKeyDown(
                PlatformKey.LeftShift) ||
            input.IsKeyDown(
                PlatformKey.RightShift);

        BuildingId previousBuilding =
            ActiveBuilding;
        UpdateBuildingSelection(
            input,
            allowSelection:
                !shiftDown && !_awaitingResult);

        if (previousBuilding != ActiveBuilding)
        {
            ResetPreviewRequest(
                interaction);
        }

        bool rotateDown =
            input.IsKeyDown(
                _bindings.Key(GameplayAction.RotatePlacement));

        if (rotateDown &&
            !_rotateWasDown &&
            !shiftDown &&
            IsActive && !_awaitingResult)
        {
            Orientation =
                (BuildingOrientation)
                (((int)Orientation + 1) % 4);
            _rememberedBuilding = ActiveBuilding;
            _rememberedOrientation = Orientation;
            ResetPreviewRequest(
                interaction);
        }

        _rotateWasDown = rotateDown;

        bool escapeDown =
            input.IsKeyDown(
                PlatformKey.Escape);

        if (escapeDown &&
            !_escapeWasDown)
        {
            Cancel(interaction);
        }

        _escapeWasDown = escapeDown;

        bool leftDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);

        if (pointerCaptured)
        {
            _pendingRequest = null;
            Preview = null;
            PreviewFreshness = PlacementPreviewFreshness.Unavailable;
            ResetPreviewRequest(interaction);
            _leftWasDown = leftDown;
            return;
        }

        if (_awaitingResult)
        { _leftWasDown = leftDown; return; }

        if (!IsActive ||
            !input.HasPointerPosition ||
            !TryResolveWorldTarget(
                camera,
                terrain,
                input.PointerPosition,
                viewportWidth,
                viewportHeight,
                out Vector3 target))
        {
            Preview = null;
            PreviewFreshness =
                PlacementPreviewFreshness.Unavailable;
            ResetPreviewRequest(
                interaction);
            _leftWasDown = leftDown;
            return;
        }

        EnsurePreviewRequest(
            interaction,
            target);

        ApplyPreview(
            snapshot);

        if ((leftDown || input.TryGetMousePressPosition(PlatformMouseButton.Left, out _)) &&
            !_leftWasDown &&
            PreviewFreshness ==
                PlacementPreviewFreshness.Current &&
            Preview is
                BuildingPlacementPreview preview &&
            preview.IsValid)
        {
            _pendingRequest =
                new BuildingPlacementRequest(
                    preview.BuildingId,
                    preview.GroundPosition,
                    preview.Orientation,
                    _previewRequestId,
                    snapshot!.Tick);
            _awaitingResult = true;
            _repeatAtClick = shiftDown;
            _correlation = default;
            Preview = null;
            PreviewFreshness = PlacementPreviewFreshness.Unavailable;
            ResetPreviewRequest(interaction);
        }

        _leftWasDown = leftDown;
    }

    public void Cancel(
        PresentationInteractionState interaction,
        bool clearOrientation = true)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        ActiveBuilding = BuildingId.None;
        Preview = null;
        PreviewFreshness =
            PlacementPreviewFreshness.Unavailable;
        _pendingRequest = null;
        _awaitingResult = false;
        _correlation = default;
        _repeatAtClick = false;
        if (clearOrientation) { _rememberedBuilding = default; _rememberedOrientation = default; Orientation = default; _minimumPreviewTick = default; }
        ResetPreviewRequest(interaction);
    }

    public void SelectBuilding(
        BuildingId buildingId)
    {
        if (!buildingId.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(buildingId));
        }

        ActiveBuilding = buildingId;
        Orientation = _rememberedBuilding == buildingId ? _rememberedOrientation : BuildingOrientation.North;
        _rememberedBuilding = buildingId;
        _rememberedOrientation = Orientation;
        Preview = null;
        PreviewFreshness =
            PlacementPreviewFreshness.Unavailable;
        _pendingRequest = null;
        _hasPreviewRequest = false;
        _previewRequestId = 0;
        _requestedBuilding = BuildingId.None;
        _awaitingResult = false;
        _correlation = default;
    }

    public void ObserveSubmission(PlayerCommandSubmissionReceipt? receipt, PresentationInteractionState interaction)
    {
        if (!_awaitingResult || _correlation.IsSpecified) return;
        if (receipt is not { Accepted: true, Kind: PlayerCommandKind.Construction } accepted || accepted.SessionId != _session)
        { _awaitingResult = false; ResetPreviewRequest(interaction); return; }
        _correlation = accepted.CorrelationId;
    }

    public void ObserveResult(in PlayerCommandResultReadModel result, PresentationInteractionState interaction)
    {
        if (!_awaitingResult || !_correlation.IsSpecified || result.SessionId != _session ||
            result.Kind != PlayerCommandKind.Construction || result.CorrelationId != _correlation) return;
        _awaitingResult = false;
        _correlation = default;
        _minimumPreviewTick = result.ResolvedAtTick;
        Preview = null;
        PreviewFreshness = PlacementPreviewFreshness.Unavailable;
        ResetPreviewRequest(interaction);
        if (result.State == PlayerCommandFeedbackState.Accepted && !_repeatAtClick) Cancel(interaction, clearOrientation: false);
        _repeatAtClick = false;
    }

    public bool TryTakePlacementRequest(
        out BuildingPlacementRequest request)
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

    private void EnsurePreviewRequest(
        PresentationInteractionState interaction,
        Vector3 target)
    {
        bool changed =
            !_hasPreviewRequest ||
            _requestedBuilding !=
                ActiveBuilding ||
            _requestedOrientation !=
                Orientation ||
            Vector3.DistanceSquared(
                _requestedPosition,
                target) >
                TargetChangeToleranceSquared;

        if (!changed)
        {
            return;
        }

        _previewRequestId =
            interaction.RequestPlacementPreview(
                _issuer,
                ActiveBuilding,
                target,
                Orientation);
        _requestedPosition = target;
        _requestedBuilding =
            ActiveBuilding;
        _requestedOrientation =
            Orientation;
        _hasPreviewRequest = true;
    }

    private void ApplyPreview(
        PresentationSnapshot? snapshot)
    {
        if (snapshot?.PlacementPreview is not
            BuildingPlacementPreviewReadModel readModel)
        {
            Preview = null;
            PreviewFreshness =
                PlacementPreviewFreshness.Unavailable;
            return;
        }

        Preview =
            readModel.Preview;

        PreviewFreshness =
            readModel.RequestId == _previewRequestId && _hasPreviewRequest &&
                readModel.CompletedTick == snapshot.Tick && snapshot.Tick.Value >= _minimumPreviewTick.Value &&
                readModel.Preview.BuildingId == ActiveBuilding && readModel.Preview.Orientation == Orientation &&
                // The authoritative footprint raises Y to its highest terrain sample; X/Z identify the requested site.
                float.IsFinite(readModel.Preview.GroundPosition.Y) &&
                Vector2.DistanceSquared(new(readModel.Preview.GroundPosition.X, readModel.Preview.GroundPosition.Z),
                    new(_requestedPosition.X, _requestedPosition.Z)) <= TargetChangeToleranceSquared
                ? PlacementPreviewFreshness.Current
                : PlacementPreviewFreshness.Stale;
    }

    private void ResetPreviewRequest(
        PresentationInteractionState interaction)
    {
        _hasPreviewRequest = false;
        _previewRequestId = 0;
        interaction.ClearPlacementPreview();
    }

    private void UpdateBuildingSelection(
        InputState input,
        bool allowSelection)
    {
        UpdateSelectionKey(
            input,
            _bindings.Key(GameplayAction.CorePlacement),
            BuildingIds.CommandCore,
            allowSelection);
        UpdateSelectionKey(
            input,
            _bindings.Key(GameplayAction.PowerPlacement),
            BuildingIds.PowerPlant,
            allowSelection);
        UpdateSelectionKey(
            input,
            _bindings.Key(GameplayAction.ExtractorPlacement),
            BuildingIds.Extractor,
            allowSelection);
        UpdateSelectionKey(
            input,
            _bindings.Key(GameplayAction.StoragePlacement),
            BuildingIds.StorageDepot,
            allowSelection);
        UpdateSelectionKey(
            input,
            _bindings.Key(GameplayAction.SmelterPlacement),
            BuildingIds.Smelter,
            allowSelection);
    }

    private void UpdateSelectionKey(
        InputState input,
        PlatformKey key,
        BuildingId buildingId,
        bool allowSelection)
    {
        bool down =
            input.IsKeyDown(key);
        bool wasDown =
            _selectionKeys[key];

        if (down &&
            !wasDown &&
            allowSelection)
        {
            SelectBuilding(buildingId);
        }

        _selectionKeys[key] =
            down;
    }

    private static bool TryResolveWorldTarget(
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
}
