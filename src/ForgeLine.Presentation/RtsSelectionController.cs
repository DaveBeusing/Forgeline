using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class RtsSelectionController
{
    private const float DragThresholdPixels = 6.0f;

    private readonly SelectionFilter _filter;

    private bool _leftWasDown;
    private bool _rightWasDown;
    private bool _selectionGestureActive;
    private Vector2 _selectionStart;
    private Vector2 _selectionCurrent;
    private MovementOrderRequest? _pendingMovementRequest;

    public RtsSelectionController(SelectionFilter filter)
    {
        _filter = filter;

        if (!_filter.Owner.IsSpecified ||
            _filter.Categories == ControllableEntityCategory.None)
        {
            throw new ArgumentOutOfRangeException(nameof(filter));
        }
    }

    public SelectionSet Selection { get; } = new();

    public EntityId HoveredEntity { get; private set; } = EntityId.Invalid;

    public bool IsDragSelecting =>
        _selectionGestureActive &&
        Vector2.DistanceSquared(_selectionStart, _selectionCurrent) >=
        DragThresholdPixels * DragThresholdPixels;

    public Vector2 DragStart => _selectionStart;

    public Vector2 DragCurrent => _selectionCurrent;

    public void Update(
        InputState input,
        RtsCamera camera,
        RenderWorld world,
        ITerrainQuery terrain,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(terrain);

        bool leftDown =
            input.IsMouseButtonDown(PlatformMouseButton.Left);
        bool rightDown =
            input.IsMouseButtonDown(PlatformMouseButton.Right);

        if (!input.HasPointerPosition)
        {
            HoveredEntity = EntityId.Invalid;
            _selectionGestureActive = false;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        Vector2 pointer = input.PointerPosition;

        HoveredEntity = SelectionPicking.TryPick(
            camera,
            world,
            _filter,
            pointer,
            viewportWidth,
            viewportHeight,
            interpolationAlpha,
            out EntityId hovered)
            ? hovered
            : EntityId.Invalid;

        if (leftDown && !_leftWasDown)
        {
            _selectionGestureActive = true;
            _selectionStart = pointer;
            _selectionCurrent = pointer;
        }
        else if (leftDown && _selectionGestureActive)
        {
            _selectionCurrent = pointer;
        }

        if (!leftDown &&
            _leftWasDown &&
            _selectionGestureActive)
        {
            _selectionCurrent = pointer;
            CompleteSelection(
                input,
                camera,
                world,
                viewportWidth,
                viewportHeight,
                interpolationAlpha);
            _selectionGestureActive = false;
        }

        if (rightDown &&
            !_rightWasDown &&
            Selection.Count > 0 &&
            TryResolveMovementTarget(
                camera,
                terrain,
                pointer,
                viewportWidth,
                viewportHeight,
                out Vector3 worldTarget))
        {
            _pendingMovementRequest = new MovementOrderRequest(
                Selection.ToArray(),
                worldTarget);
        }

        _leftWasDown = leftDown;
        _rightWasDown = rightDown;
    }

    public bool TryTakeMovementRequest(
        out MovementOrderRequest request)
    {
        if (_pendingMovementRequest is null)
        {
            request = null!;
            return false;
        }

        request = _pendingMovementRequest;
        _pendingMovementRequest = null;
        return true;
    }

    private void CompleteSelection(
        InputState input,
        RtsCamera camera,
        RenderWorld world,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha)
    {
        bool toggle =
            input.IsKeyDown(PlatformKey.LeftShift) ||
            input.IsKeyDown(PlatformKey.RightShift);

        if (IsDragSelecting)
        {
            EntityId[] entities = SelectionPicking.PickBox(
                camera,
                world,
                _filter,
                _selectionStart,
                _selectionCurrent,
                viewportWidth,
                viewportHeight,
                interpolationAlpha);

            if (toggle)
            {
                Selection.ToggleRange(entities);
            }
            else
            {
                Selection.Replace(entities);
            }

            return;
        }

        if (HoveredEntity.IsValid)
        {
            if (toggle)
            {
                Selection.Toggle(HoveredEntity);
            }
            else
            {
                Selection.SetSingle(HoveredEntity);
            }

            return;
        }

        if (!toggle)
        {
            Selection.Clear();
        }
    }

    private static bool TryResolveMovementTarget(
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
