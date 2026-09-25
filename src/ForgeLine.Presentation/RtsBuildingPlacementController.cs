using System.Numerics;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct BuildingPlacementRequest(
    BuildingId BuildingId,
    Vector3 Position,
    BuildingOrientation Orientation);

public sealed class RtsBuildingPlacementController
{
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

    public bool IsActive => ActiveBuilding.IsSpecified;

    public void Update(
        InputState input,
        RtsCamera camera,
        ITerrainQuery terrain,
        EntityRegistry entities,
        BuildingPlacementService placement,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(placement);

        UpdateBuildingSelection(input);

        bool rotateDown = input.IsKeyDown(PlatformKey.F9);
        if (rotateDown && !_rotateWasDown && IsActive)
        {
            Orientation = (BuildingOrientation)
                (((int)Orientation + 1) % 4);
        }

        _rotateWasDown = rotateDown;

        bool escapeDown = input.IsKeyDown(PlatformKey.Escape);
        if (escapeDown && !_escapeWasDown)
        {
            ActiveBuilding = BuildingId.None;
            Preview = null;
            _pendingRequest = null;
        }

        _escapeWasDown = escapeDown;

        bool leftDown =
            input.IsMouseButtonDown(PlatformMouseButton.Left);

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
            _leftWasDown = leftDown;
            return;
        }

        BuildingPlacementPreview preview =
            placement.CreatePreview(
                entities,
                _issuer,
                ActiveBuilding,
                target,
                Orientation);

        Preview = preview;

        if (leftDown &&
            !_leftWasDown &&
            preview.IsValid)
        {
            _pendingRequest = new BuildingPlacementRequest(
                preview.BuildingId,
                preview.GroundPosition,
                preview.Orientation);
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

        request = _pendingRequest.Value;
        _pendingRequest = null;
        return true;
    }

    private void UpdateBuildingSelection(InputState input)
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
        bool down = input.IsKeyDown(key);
        bool wasDown = _selectionKeys[key];

        if (down && !wasDown)
        {
            ActiveBuilding = buildingId;
        }

        _selectionKeys[key] = down;
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

        worldTarget = new Vector3(
            horizontalTarget.X,
            terrainHeight,
            horizontalTarget.Z);
        return true;
    }
}
