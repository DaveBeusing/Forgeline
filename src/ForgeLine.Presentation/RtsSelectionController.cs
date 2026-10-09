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
    private Vector2 _selectionStart;
    private Vector2 _selectionCurrent;
    private MovementOrderRequest? _pendingMovementRequest;
    private SimulationSessionId _sessionId;

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

    public EntityId InspectedEntity { get; private set; } = EntityId.Invalid;

    public bool IsDragSelecting =>
        _selectionGestureActive &&
        Vector2.DistanceSquared(_selectionStart, _selectionCurrent) >=
        DragThresholdPixels * DragThresholdPixels;

    public Vector2 DragStart => _selectionStart;

    public Vector2 DragCurrent => _selectionCurrent;

    public void CancelPointerInteraction()
    {
        _selectionGestureActive = false;
        _pendingMovementRequest = null;
        _leftWasDown = false;
        _rightWasDown = false;
        HoveredEntity = EntityId.Invalid;
    }

    public void Update(
        InputState input,
        RtsCamera camera,
        RenderWorld world,
        ITerrainQuery terrain,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha,
        bool pointerCaptured = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(terrain);

        SynchronizeSession(world);

        bool leftDown =
            input.IsMouseButtonDown(PlatformMouseButton.Left);
        bool rightDown =
            input.IsMouseButtonDown(PlatformMouseButton.Right);

        if (pointerCaptured)
        {
            HoveredEntity = EntityId.Invalid;
            _selectionGestureActive = false;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        if (!input.HasPointerPosition)
        {
            HoveredEntity = EntityId.Invalid;
            _selectionGestureActive = false;
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
            return;
        }

        Vector2 pointer = input.PointerPosition;

        if (SelectionPicking.TryPick(
                camera,
                world,
                _filter,
                pointer,
                viewportWidth,
                viewportHeight,
                interpolationAlpha,
                out EntityId hovered))
        {
            HoveredEntity = hovered;
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

        if (HoveredEntity.IsValid &&
            world.TryGetInterpolatedInstance(
                HoveredEntity,
                interpolationAlpha,
                out RenderInstance hoveredInstance))
        {
            if (_filter.Allows(
                    hoveredInstance.Selectable))
            {
                if (toggle)
                {
                    Selection.Toggle(HoveredEntity);
                }
                else
                {
                    Selection.SetSingle(HoveredEntity);
                }

                InspectedEntity = EntityId.Invalid;
                return;
            }

            if (hoveredInstance.WorldFeature.IsInspectable)
            {
                InspectedEntity = HoveredEntity;
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
