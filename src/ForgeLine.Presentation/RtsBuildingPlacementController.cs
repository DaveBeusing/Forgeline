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

    public RtsBuildingPlacementController(PlayerId issuer)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        _issuer = issuer;
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
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(interaction);

        BuildingId previousBuilding =
            ActiveBuilding;
        UpdateBuildingSelection(input);

        if (previousBuilding != ActiveBuilding)
        {
            ResetPreviewRequest(
                interaction);
        }

        bool rotateDown =
            input.IsKeyDown(
                PlatformKey.F9);

        if (rotateDown &&
            !_rotateWasDown &&
            IsActive)
        {
            Orientation =
                (BuildingOrientation)
                (((int)Orientation + 1) % 4);
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
            ActiveBuilding =
                BuildingId.None;
            Preview = null;
            PreviewFreshness =
                PlacementPreviewFreshness.Unavailable;
            _pendingRequest = null;
            ResetPreviewRequest(
                interaction);
        }

        _escapeWasDown = escapeDown;

        bool leftDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);

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

        if (leftDown &&
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
        }

        _leftWasDown = leftDown;
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
            readModel.RequestId ==
                _previewRequestId
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
        InputState input)
    {
        UpdateSelectionKey(
            input,
            PlatformKey.F4,
            BuildingIds.CommandCore);
        UpdateSelectionKey(
            input,
            PlatformKey.F5,
            BuildingIds.PowerPlant);
        UpdateSelectionKey(
            input,
            PlatformKey.F6,
            BuildingIds.Extractor);
        UpdateSelectionKey(
            input,
            PlatformKey.F7,
            BuildingIds.StorageDepot);
        UpdateSelectionKey(
            input,
            PlatformKey.F8,
            BuildingIds.Smelter);
    }

    private void UpdateSelectionKey(
        InputState input,
        PlatformKey key,
        BuildingId buildingId)
    {
        bool down =
            input.IsKeyDown(key);
        bool wasDown =
            _selectionKeys[key];

        if (down && !wasDown)
        {
            ActiveBuilding =
                buildingId;
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
