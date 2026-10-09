using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class RtsSelectionController
{
    private const float DragThresholdPixels = 6.0f;

    private readonly SelectionFilter _filter;

    private bool _leftWasDown;
    private bool _rightWasDown;
    private bool _selectionGestureActive;
    private bool _dragThresholdCrossed;
    private float _dragThreshold = DragThresholdPixels;
    private Vector2 _selectionStart;
    private Vector2 _selectionCurrent;
    private MovementOrderRequest? _pendingMovementRequest;
    private SimulationSessionId _sessionId;
    private int _viewportWidth;
    private int _viewportHeight;
    private float _pointerScale;

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

    public RtsCommandFeedback CommandFeedback { get; } = new();

    public bool PointerMovementTargetValid { get; private set; }

    public EntityId HoveredEntity { get; private set; } = EntityId.Invalid;

    public bool CanSelectHoveredEntity { get; private set; }

    public EntityId InspectedEntity { get; private set; } = EntityId.Invalid;

    public bool IsDragSelecting =>
        _selectionGestureActive &&
        _dragThresholdCrossed;

    public Vector2 DragStart => _selectionStart;

    public Vector2 DragCurrent => _selectionCurrent;

    public void CancelPointerInteraction()
    {
        _selectionGestureActive = false;
        CommandFeedback.Clear();
        PointerMovementTargetValid = false;
        _pendingMovementRequest = null;
        _leftWasDown = false;
        _rightWasDown = false;
        HoveredEntity = EntityId.Invalid;
        CanSelectHoveredEntity = false;
    }

    public void Update(
        InputState input,
        RtsCamera camera,
        RenderWorld world,
        ITerrainQuery terrain,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha,
        bool pointerCaptured = false,
        float pointerScale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(terrain);

        SynchronizeSession(world);
        CanSelectHoveredEntity = false;

        bool leftDown =
            input.IsMouseButtonDown(PlatformMouseButton.Left);
        bool rightDown =
            input.IsMouseButtonDown(PlatformMouseButton.Right);

        float normalizedScale = float.IsFinite(pointerScale) ? Math.Clamp(pointerScale, 0.5f, 4.0f) : 1.0f;
        bool displayChangedDuringGesture = _selectionGestureActive &&
            (_viewportWidth != viewportWidth || _viewportHeight != viewportHeight || _pointerScale != normalizedScale);
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        _pointerScale = normalizedScale;

        if (pointerCaptured || input.FocusLostThisFrame || displayChangedDuringGesture || viewportWidth <= 0 || viewportHeight <= 0)
        {
            _pendingMovementRequest = null;
            PointerMovementTargetValid = false;
            HoveredEntity = EntityId.Invalid;
            _selectionGestureActive = false;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        if (!input.HasPointerPosition)
        {
            _pendingMovementRequest = null;
            PointerMovementTargetValid = false;
            HoveredEntity = EntityId.Invalid;
            _selectionGestureActive = false;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        Vector2 pointer = input.PointerPosition;
        if (!float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) ||
            pointer.X < 0 || pointer.Y < 0 || pointer.X > viewportWidth || pointer.Y > viewportHeight)
        {
            CancelPointerInteraction();
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        if (SelectionPicking.TryPick(
                camera,
                world,
                _filter,
                pointer,
                viewportWidth,
                viewportHeight,
                interpolationAlpha,
                out EntityId hovered,
                requireOwnership: false))
        {
            HoveredEntity = hovered;
            CanSelectHoveredEntity = world.TryGetInterpolatedInstance(hovered, interpolationAlpha, out RenderInstance hoveredInstance) &&
                _filter.Allows(hoveredInstance.Selectable);
        }
        else
        {
            HoveredEntity =
                SelectionPicking.TryPickInspectable(
                    camera,
                    world,
                    pointer,
                    viewportWidth,
                    viewportHeight,
                    interpolationAlpha,
                    out EntityId inspectable)
                    ? inspectable
                    : EntityId.Invalid;
        }

        bool leftPressed = input.TryGetMousePressPosition(PlatformMouseButton.Left, out Vector2 pressPosition);
        if ((leftPressed || leftDown) && !_leftWasDown)
        {
            _selectionGestureActive = true;
            _dragThresholdCrossed = false;
            _dragThreshold = DragThresholdPixels * normalizedScale;
            _selectionStart = leftPressed ? pressPosition : pointer;
            _selectionCurrent = pointer;
            UpdateDragThreshold();
        }
        else if (leftDown && _selectionGestureActive)
        {
            _selectionCurrent = pointer;
            UpdateDragThreshold();
        }

        if (!leftDown &&
            (_leftWasDown || input.WasMouseButtonReleased(PlatformMouseButton.Left)) &&
            _selectionGestureActive)
        {
            _selectionCurrent = pointer;
            UpdateDragThreshold();
            CompleteSelection(
                input,
                camera,
                world,
                viewportWidth,
                viewportHeight,
                interpolationAlpha);
            _selectionGestureActive = false;
        }

        PointerMovementTargetValid = TryResolveMovementTarget(
                camera,
                terrain,
                pointer,
                viewportWidth,
                viewportHeight,
                out Vector3 worldTarget);
        if ((input.TryGetMousePressPosition(PlatformMouseButton.Right, out _) || rightDown) && !_rightWasDown &&
            Selection.Count > 0)
        {
            if (camera.TryScreenPointToWorldOnHorizontalPlane(pointer, camera.Target.Y,
                    viewportWidth, viewportHeight, out Vector3 fallbackTarget))
            {
                CommandFeedback.Show(PointerMovementTargetValid ? worldTarget : fallbackTarget, PointerMovementTargetValid);
            }
            if (PointerMovementTargetValid)
            {
                _pendingMovementRequest = new MovementOrderRequest(Selection.ToArray(), worldTarget);
            }
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

    private void UpdateDragThreshold()
    {
        _dragThresholdCrossed |= Vector2.DistanceSquared(_selectionStart, _selectionCurrent) >=
            _dragThreshold * _dragThreshold;
    }

    private void SynchronizeSession(
        RenderWorld world)
    {
        SimulationSessionId sessionId =
            world.CurrentSnapshot?.SessionId ??
            SimulationSessionId.None;

        if (!sessionId.IsSpecified ||
            sessionId == _sessionId)
        {
            return;
        }

        _sessionId = sessionId;
        CommandFeedback.Clear();
        Selection.Clear();
        HoveredEntity = EntityId.Invalid;
        InspectedEntity = EntityId.Invalid;
        _pendingMovementRequest = null;
        _selectionGestureActive = false;
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

            if (entities.Length > 0)
            {
                InspectedEntity = EntityId.Invalid;
            }

            return;
        }

        EntityId clickedEntity = SelectionPicking.TryPick(camera, world, _filter, _selectionCurrent,
            viewportWidth, viewportHeight, interpolationAlpha, out EntityId selectable)
            ? selectable : HoveredEntity;
        if (clickedEntity.IsValid &&
            world.TryGetInterpolatedInstance(
                clickedEntity,
                interpolationAlpha,
                out RenderInstance hoveredInstance))
        {
            if (_filter.Allows(
                    hoveredInstance.Selectable))
            {
                if (toggle)
                {
                    Selection.Toggle(clickedEntity);
                }
                else
                {
                    Selection.SetSingle(clickedEntity);
                }

                InspectedEntity = EntityId.Invalid;
                return;
            }

            if (hoveredInstance.WorldFeature.IsInspectable)
            {
                InspectedEntity = clickedEntity;
                if (!toggle)
                {
                    Selection.Clear();
                }

                return;
            }
        }

        if (!toggle)
        {
            Selection.Clear();
            InspectedEntity = EntityId.Invalid;
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
