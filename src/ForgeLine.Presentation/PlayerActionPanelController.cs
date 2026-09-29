using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum PlayerActionPanelMode : byte
{
    Closed = 0,
    Construction = 1,
    Production = 2,
    UnitProduction = 3
}

public enum PlayerActionRequestKind : byte
{
    None = 0,
    BeginBuildingPlacement = 1,
    QueueProduction = 2,
    SetProductionPaused = 3,
    CancelProduction = 4,
    QueueUnitProduction = 5,
    CancelUnitProduction = 6
}

public readonly record struct PlayerActionRequest(
    PlayerActionRequestKind Kind,
    BuildingId BuildingId,
    EntityId Facility,
    RecipeId RecipeId,
    UnitId UnitId,
    EntityId RequestEntity,
    bool Paused,
    ProductionPriority Priority,
    ProductionRequestMode ProductionMode,
    ResourceId DesiredStockResourceId,
    double DesiredStockQuantity)
{
    public static PlayerActionRequest BeginBuildingPlacement(
        BuildingId buildingId) =>
        new(
            PlayerActionRequestKind.BeginBuildingPlacement,
            buildingId,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            EntityId.Invalid,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest QueueProduction(
        EntityId facility,
        RecipeId recipeId,
        ProductionPriority priority,
        ProductionRequestMode mode,
        ResourceId desiredStockResourceId,
        double desiredStockQuantity) =>
        new(
            PlayerActionRequestKind.QueueProduction,
            BuildingId.None,
            facility,
            recipeId,
            UnitId.None,
            EntityId.Invalid,
            false,
            priority,
            mode,
            desiredStockResourceId,
            desiredStockQuantity);

    public static PlayerActionRequest SetProductionPaused(
        EntityId requestEntity,
        bool paused) =>
        new(
            PlayerActionRequestKind.SetProductionPaused,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            paused,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest CancelProduction(
        EntityId requestEntity) =>
        new(
            PlayerActionRequestKind.CancelProduction,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest QueueUnitProduction(
        EntityId facility,
        UnitId unitId,
        ProductionPriority priority) =>
        new(
            PlayerActionRequestKind.QueueUnitProduction,
            BuildingId.None,
            facility,
            RecipeId.None,
            unitId,
            EntityId.Invalid,
            false,
            priority,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);

    public static PlayerActionRequest CancelUnitProduction(
        EntityId requestEntity) =>
        new(
            PlayerActionRequestKind.CancelUnitProduction,
            BuildingId.None,
            EntityId.Invalid,
            RecipeId.None,
            UnitId.None,
            requestEntity,
            false,
            ProductionPriority.Normal,
            ProductionRequestMode.OneShot,
            ResourceId.None,
            0.0);
}

public readonly record struct PlayerActionPanelView(
    PlayerActionPanelMode Mode,
    int SelectedIndex,
    ProductionPriority Priority,
    ProductionRequestMode ProductionMode,
    double DesiredStockQuantity,
    bool PointerCaptured,
    float OriginX,
    float OriginY)
{
    public bool IsOpen =>
        Mode != PlayerActionPanelMode.Closed;
}

public sealed class PlayerActionPanelController
{
    private const float PanelWidth = 608.0f;
    private const float PanelTop = 96.0f;
    private const float RowStartOffset = 76.0f;
    private const float RowHeight = 20.0f;
    private const float PanelBottomPadding = 28.0f;

    private readonly Dictionary<PlatformKey, bool> _heldKeys = new();
    private bool _leftWasDown;
    private PlayerActionRequest? _pendingRequest;
    private SimulationSessionId _sessionId;
    private double _desiredStockQuantity;

    public PlayerActionPanelMode Mode { get; private set; }

    public int SelectedIndex { get; private set; }

    public ProductionPriority Priority { get; private set; } =
        ProductionPriority.Normal;

    public ProductionRequestMode ProductionMode { get; private set; } =
        ProductionRequestMode.OneShot;

    public bool PointerCaptured { get; private set; }

    public bool HasKeyboardFocus =>
        Mode != PlayerActionPanelMode.Closed;

    public void Update(
        InputState input,
        PresentationSnapshot? snapshot,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);

        SynchronizeSession(
            snapshot?.SessionId ??
            SimulationSessionId.None);

        PlayerActionSnapshot? actions =
            snapshot?.PlayerActions;

        if (Pressed(input, PlatformKey.B))
        {
            ToggleMode(
                PlayerActionPanelMode.Construction);
        }

        if (Pressed(input, PlatformKey.P))
        {
            ToggleMode(
                PlayerActionPanelMode.Production);
        }

        if (Pressed(input, PlatformKey.U))
        {
            ToggleMode(
                PlayerActionPanelMode.UnitProduction);
        }

        if (Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, PlatformKey.Escape))
        {
            Close();
        }
        else
        {
            _ = Pressed(input, PlatformKey.Escape);
        }

        int rowCount =
            GetRowCount(
                actions);

        if (rowCount == 0)
        {
            SelectedIndex = 0;
        }
        else
        {
            SelectedIndex =
                Math.Clamp(
                    SelectedIndex,
                    0,
                    rowCount - 1);

            if (Pressed(input, PlatformKey.Tab))
            {
                SelectedIndex =
                    (SelectedIndex + 1) %
                    rowCount;
                _desiredStockQuantity = 0.0;
            }
            else
            {
                _ = Pressed(input, PlatformKey.Tab);
            }
        }

        if (Mode is
                PlayerActionPanelMode.Production or
                PlayerActionPanelMode.UnitProduction &&
            Pressed(input, PlatformKey.T))
        {
            Priority =
                Priority switch
                {
                    ProductionPriority.Normal =>
                        ProductionPriority.High,
                    ProductionPriority.High =>
                        ProductionPriority.Low,
                    _ =>
                        ProductionPriority.Normal
                };
        }
        else
        {
            _ = Pressed(input, PlatformKey.T);
        }

        if (Mode == PlayerActionPanelMode.Production &&
            Pressed(input, PlatformKey.M))
        {
            ProductionMode =
                ProductionMode switch
                {
                    ProductionRequestMode.OneShot =>
                        ProductionRequestMode.Repeat,
                    ProductionRequestMode.Repeat =>
                        ProductionRequestMode.DesiredStock,
                    _ =>
                        ProductionRequestMode.OneShot
                };
            _desiredStockQuantity = 0.0;
        }
        else
        {
            _ = Pressed(input, PlatformKey.M);
        }

        UpdateDesiredStockTarget(
            input,
            actions);

        PlayerActionPanelView view =
            CreateView(
                viewportWidth,
                viewportHeight,
                actions);

        bool leftDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);

        PointerCaptured =
            view.IsOpen &&
            input.HasPointerPosition &&
            IsPointerInsidePanel(
                input.PointerPosition,
                view,
                rowCount,
                viewportHeight);

        if (PointerCaptured &&
            leftDown &&
            !_leftWasDown &&
            TryResolvePointerRow(
                input.PointerPosition,
                view,
                rowCount,
                out int pointerRow))
        {
            SelectedIndex = pointerRow;
            _desiredStockQuantity = 0.0;
        }

        bool activate =
            Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, PlatformKey.Enter);

        if (activate)
        {
            ActivateSelected(actions);
        }

        bool cancel =
            Mode is
                PlayerActionPanelMode.Production or
                PlayerActionPanelMode.UnitProduction &&
            Pressed(input, PlatformKey.C);

        if (cancel)
        {
            CancelSelected(actions);
        }
        else
        {
            _ = Pressed(input, PlatformKey.C);
        }

        _leftWasDown = leftDown;
    }

    public PlayerActionPanelView CreateView(
        int viewportWidth,
        int viewportHeight,
        PlayerActionSnapshot? actions) =>
        new(
            Mode,
            SelectedIndex,
            Priority,
            ProductionMode,
            ResolveDesiredStockQuantity(actions),
            PointerCaptured,
            MathF.Max(
                12.0f,
                viewportWidth - PanelWidth - 12.0f),
            MathF.Min(
                PanelTop,
                MathF.Max(
                    12.0f,
                    viewportHeight * 0.12f)));

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

    public void Close()
    {
        Mode =
            PlayerActionPanelMode.Closed;
        SelectedIndex = 0;
        PointerCaptured = false;
        _desiredStockQuantity = 0.0;
    }

    private void ToggleMode(
        PlayerActionPanelMode requested)
    {
        Mode =
            Mode == requested
                ? PlayerActionPanelMode.Closed
                : requested;
        SelectedIndex = 0;
        _desiredStockQuantity = 0.0;
    }

    private void ActivateSelected(
        PlayerActionSnapshot? actions)
    {
        if (actions is null)
        {
            return;
        }

        switch (Mode)
        {
            case PlayerActionPanelMode.Construction:
                if (SelectedIndex <
                    actions.Construction.Count)
                {
                    _pendingRequest =
                        PlayerActionRequest.BeginBuildingPlacement(
                            actions.Construction[
                                SelectedIndex].BuildingId);
                    Close();
                }

                break;

            case PlayerActionPanelMode.Production:
                ActivateProduction(
                    actions.Production);
                break;

            case PlayerActionPanelMode.UnitProduction:
                ActivateUnitProduction(
                    actions.UnitProduction);
                break;
        }
    }

    private void ActivateProduction(
        PlayerProductionFacilityActionReadModel? facility)
    {
        if (facility is null)
        {
            return;
        }

        if (SelectedIndex <
            facility.Recipes.Count)
        {
            PlayerProductionRecipeActionReadModel recipe =
                facility.Recipes[
                    SelectedIndex];

            ResourceId desiredResource =
                ResourceId.None;
            double desiredQuantity = 0.0;

            if (ProductionMode ==
                ProductionRequestMode.DesiredStock)
            {
                if (recipe.Outputs.Count == 0)
                {
                    return;
                }

                desiredResource =
                    recipe.Outputs[0].ResourceId;
                desiredQuantity =
                    ResolveDesiredStockQuantity(
                        recipe);
            }

            _pendingRequest =
                PlayerActionRequest.QueueProduction(
                    facility.Entity,
                    recipe.RecipeId,
                    Priority,
                    ProductionMode,
                    desiredResource,
                    desiredQuantity);
            return;
        }

        int requestIndex =
            SelectedIndex -
            facility.Recipes.Count;

        if (requestIndex < 0 ||
            requestIndex >= facility.Requests.Count)
        {
            return;
        }

        PlayerProductionRequestReadModel request =
            facility.Requests[
                requestIndex];

        _pendingRequest =
            PlayerActionRequest.SetProductionPaused(
                request.RequestEntity,
                !request.Paused);
    }

    private void ActivateUnitProduction(
        PlayerUnitProductionFacilityActionReadModel? facility)
    {
        if (facility is null ||
            SelectedIndex >=
                facility.Units.Count)
        {
            return;
        }

        PlayerUnitProductionActionReadModel unit =
            facility.Units[
                SelectedIndex];

        _pendingRequest =
            PlayerActionRequest.QueueUnitProduction(
                facility.Entity,
                unit.UnitId,
                Priority);
    }

    private void CancelSelected(
        PlayerActionSnapshot? actions)
    {
        if (actions is null)
        {
            return;
        }

        if (Mode ==
                PlayerActionPanelMode.Production &&
            actions.Production is
                PlayerProductionFacilityActionReadModel production)
        {
            int requestIndex =
                SelectedIndex -
                production.Recipes.Count;

            if (requestIndex >= 0 &&
                requestIndex <
                    production.Requests.Count)
            {
                _pendingRequest =
                    PlayerActionRequest.CancelProduction(
                        production.Requests[
                            requestIndex].RequestEntity);
            }

            return;
        }

        if (Mode ==
                PlayerActionPanelMode.UnitProduction &&
            actions.UnitProduction is
                PlayerUnitProductionFacilityActionReadModel unitProduction)
        {
            int requestIndex =
                SelectedIndex -
                unitProduction.Units.Count;

            if (requestIndex >= 0 &&
                requestIndex <
                    unitProduction.Requests.Count)
            {
                _pendingRequest =
                    PlayerActionRequest.CancelUnitProduction(
                        unitProduction.Requests[
                            requestIndex].RequestEntity);
            }
        }
    }

    private void UpdateDesiredStockTarget(
        InputState input,
        PlayerActionSnapshot? actions)
    {
        if (Mode !=
                PlayerActionPanelMode.Production ||
            ProductionMode !=
                ProductionRequestMode.DesiredStock ||
            actions?.Production is not
                PlayerProductionFacilityActionReadModel facility ||
            SelectedIndex < 0 ||
            SelectedIndex >= facility.Recipes.Count)
        {
            _ = Pressed(input, PlatformKey.Left);
            _ = Pressed(input, PlatformKey.Right);
            return;
        }

        PlayerProductionRecipeActionReadModel recipe =
            facility.Recipes[
                SelectedIndex];

        if (recipe.Outputs.Count == 0)
        {
            return;
        }

        double step =
            recipe.Outputs[0].RequiredQuantity;
        double minimum =
            step;
        double current =
            ResolveDesiredStockQuantity(
                recipe);

        if (Pressed(input, PlatformKey.Left))
        {
            _desiredStockQuantity =
                Math.Max(
                    minimum,
                    current - step);
        }

        if (Pressed(input, PlatformKey.Right))
        {
            _desiredStockQuantity =
                current + step;
        }
    }

    private double ResolveDesiredStockQuantity(
        PlayerActionSnapshot? actions)
    {
        if (Mode !=
                PlayerActionPanelMode.Production ||
            actions?.Production is not
                PlayerProductionFacilityActionReadModel facility ||
            SelectedIndex < 0 ||
            SelectedIndex >= facility.Recipes.Count)
        {
            return 0.0;
        }

        return ResolveDesiredStockQuantity(
            facility.Recipes[
                SelectedIndex]);
    }

    private double ResolveDesiredStockQuantity(
        PlayerProductionRecipeActionReadModel recipe)
    {
        if (_desiredStockQuantity > 0.0)
        {
            return _desiredStockQuantity;
        }

        if (recipe.Outputs.Count == 0)
        {
            return 0.0;
        }

        PlayerActionResourceAmount output =
            recipe.Outputs[0];

        return Math.Max(
            output.RequiredQuantity,
            output.AvailableQuantity +
            output.RequiredQuantity * 5.0);
    }

    private int GetRowCount(
        PlayerActionSnapshot? actions) =>
        Mode switch
        {
            PlayerActionPanelMode.Construction =>
                actions?.Construction.Count ??
                0,
            PlayerActionPanelMode.Production =>
                actions?.Production is
                    PlayerProductionFacilityActionReadModel production
                    ? production.Recipes.Count +
                      production.Requests.Count
                    : 0,
            PlayerActionPanelMode.UnitProduction =>
                actions?.UnitProduction is
                    PlayerUnitProductionFacilityActionReadModel units
                    ? units.Units.Count +
                      units.Requests.Count
                    : 0,
            _ =>
                0
        };

    private static bool IsPointerInsidePanel(
        Vector2 pointer,
        in PlayerActionPanelView view,
        int rowCount,
        int viewportHeight)
    {
        float height =
            MathF.Min(
                viewportHeight -
                view.OriginY -
                12.0f,
                RowStartOffset +
                Math.Max(
                    rowCount,
                    1) *
                RowHeight +
                PanelBottomPadding);

        return pointer.X >= view.OriginX &&
               pointer.X <=
                   view.OriginX +
                   PanelWidth &&
               pointer.Y >= view.OriginY &&
               pointer.Y <=
                   view.OriginY +
                   Math.Max(
                       height,
                       RowStartOffset +
                       RowHeight);
    }

    private static bool TryResolvePointerRow(
        Vector2 pointer,
        in PlayerActionPanelView view,
        int rowCount,
        out int row)
    {
        float y =
            pointer.Y -
            (view.OriginY +
             RowStartOffset);

        if (y < 0.0f)
        {
            row = -1;
            return false;
        }

        row =
            (int)(y /
                  RowHeight);

        return row >= 0 &&
               row < rowCount;
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
        Mode =
            PlayerActionPanelMode.Closed;
        SelectedIndex = 0;
        Priority =
            ProductionPriority.Normal;
        ProductionMode =
            ProductionRequestMode.OneShot;
        PointerCaptured = false;
        _pendingRequest = null;
        _desiredStockQuantity = 0.0;
        _heldKeys.Clear();
        _leftWasDown = false;
    }

    private bool Pressed(
        InputState input,
        PlatformKey key)
    {
        bool down =
            input.IsKeyDown(
                key);
        bool held =
            _heldKeys.TryGetValue(
                key,
                out bool previous) &&
            previous;

        _heldKeys[key] =
            down;

        return down &&
               !held;
    }
}
