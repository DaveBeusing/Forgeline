using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum PlayerActionPanelMode : byte
{
    Closed = 0,
    Construction = 1,
    Production = 2,
    UnitProduction = 3,
    Logistics = 4,
    Supply = 5,
    Tactical = 6
}

public enum PlayerStockThresholdField : byte
{
    Minimum = 0,
    Target = 1,
    Maximum = 2
}

public readonly record struct PlayerActionPanelView(
    PlayerActionPanelMode Mode,
    int SelectedIndex,
    ProductionPriority Priority,
    ProductionRequestMode ProductionMode,
    double DesiredStockQuantity,
    LogisticsStockPriority LogisticsPriority,
    PlayerStockThresholdField StockThresholdField,
    double StockMinimum,
    double StockTarget,
    double StockMaximum,
    bool AutomaticResupplyEnabled,
    double AutomaticFuelThreshold,
    double AutomaticAmmunitionThreshold,
    bool PointerCaptured,
    float OriginX,
    float OriginY,
    BattlefieldSupplyPriority SupplyPriority =
        BattlefieldSupplyPriority.Normal)
{
    public bool IsOpen =>
        Mode != PlayerActionPanelMode.Closed;
}

public sealed class PlayerActionPanelController
{
    private readonly Dictionary<PlatformKey, bool> _heldKeys = new();
    private bool _leftWasDown;
    private PlayerActionRequest? _pendingRequest;
    private SimulationSessionId _sessionId;
    private double _desiredStockQuantity;
    private ResourceId _stockResourceId;
    private double _stockMinimum;
    private double _stockTarget;
    private double _stockMaximum;
    private EntityId _supplyEntity;
    private double _automaticFuelThreshold;
    private double _automaticAmmunitionThreshold;
    private bool _automaticResupplyEnabled;
    private BattlefieldSupplyPriority _supplyPriority =
        BattlefieldSupplyPriority.Normal;

    public PlayerActionPanelMode Mode { get; private set; }

    public int SelectedIndex { get; private set; }

    public ProductionPriority Priority { get; private set; } =
        ProductionPriority.Normal;

    public ProductionRequestMode ProductionMode { get; private set; } =
        ProductionRequestMode.OneShot;

    public LogisticsStockPriority LogisticsPriority { get; private set; } =
        LogisticsStockPriority.Normal;

    public BattlefieldSupplyPriority SupplyPriority =>
        _supplyPriority;

    public PlayerStockThresholdField StockThresholdField { get; private set; } =
        PlayerStockThresholdField.Target;

    public bool PointerCaptured { get; private set; }

    public bool HasKeyboardFocus =>
        Mode != PlayerActionPanelMode.Closed;

    public void Update(
        InputState input,
        PresentationSnapshot? snapshot,
        int viewportWidth,
        int viewportHeight,
        uint dpi = 96,
        float uiScale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                viewportWidth,
                viewportHeight,
                dpi,
                uiScale);

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

        if (Pressed(input, PlatformKey.L))
        {
            ToggleMode(
                PlayerActionPanelMode.Logistics);
        }

        if (Pressed(input, PlatformKey.Y))
        {
            ToggleMode(
                PlayerActionPanelMode.Supply);
        }

        if (Pressed(input, PlatformKey.K))
        {
            ToggleMode(
                PlayerActionPanelMode.Tactical);
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
                _stockResourceId = ResourceId.None;
            }
            else
            {
                _ = Pressed(input, PlatformKey.Tab);
            }
        }

        SynchronizeStockEditor(actions);
        SynchronizeSupplyEditor(actions);

        if ((Mode is
                 PlayerActionPanelMode.Production or
                 PlayerActionPanelMode.UnitProduction) &&
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
        else if (Mode == PlayerActionPanelMode.Logistics &&
                 Pressed(input, PlatformKey.T))
        {
            LogisticsPriority =
                LogisticsPriority switch
                {
                    LogisticsStockPriority.Normal =>
                        LogisticsStockPriority.High,
                    LogisticsStockPriority.High =>
                        LogisticsStockPriority.Critical,
                    LogisticsStockPriority.Critical =>
                        LogisticsStockPriority.Low,
                    _ =>
                        LogisticsStockPriority.Normal
                };
        }
        else if (Mode == PlayerActionPanelMode.Supply &&
                 Pressed(input, PlatformKey.T))
        {
            _supplyPriority =
                _supplyPriority switch
                {
                    BattlefieldSupplyPriority.Normal =>
                        BattlefieldSupplyPriority.High,
                    BattlefieldSupplyPriority.High =>
                        BattlefieldSupplyPriority.Critical,
                    BattlefieldSupplyPriority.Critical =>
                        BattlefieldSupplyPriority.Low,
                    _ =>
                        BattlefieldSupplyPriority.Normal
                };
            QueueSupplyPriority(actions);
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
        else if (Mode == PlayerActionPanelMode.Logistics &&
                 Pressed(input, PlatformKey.M))
        {
            StockThresholdField =
                StockThresholdField switch
                {
                    PlayerStockThresholdField.Minimum =>
                        PlayerStockThresholdField.Target,
                    PlayerStockThresholdField.Target =>
                        PlayerStockThresholdField.Maximum,
                    _ =>
                        PlayerStockThresholdField.Minimum
                };
        }
        else if (Mode == PlayerActionPanelMode.Supply &&
                 Pressed(input, PlatformKey.M))
        {
            _automaticResupplyEnabled =
                !_automaticResupplyEnabled;
            QueueAutomaticResupplyPolicy(actions);
        }
        else
        {
            _ = Pressed(input, PlatformKey.M);
        }

        switch (Mode)
        {
            case PlayerActionPanelMode.Production:
                UpdateDesiredStockTarget(
                    input,
                    actions);
                break;

            case PlayerActionPanelMode.Logistics:
                UpdateStockThresholds(
                    input,
                    actions);
                break;

            case PlayerActionPanelMode.Supply:
                UpdateSupplyThresholds(
                    input,
                    actions);
                break;

            default:
                _ = Pressed(input, PlatformKey.Left);
                _ = Pressed(input, PlatformKey.Right);
                break;
        }

        PlayerActionPanelView view =
            CreateView(
                viewportWidth,
                viewportHeight,
                actions,
                dpi,
                uiScale);

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
                layout);

        if (PointerCaptured &&
            leftDown &&
            !_leftWasDown &&
            TryResolvePointerRow(
                input.PointerPosition,
                view,
                rowCount,
                layout,
                out int pointerRow))
        {
            SelectedIndex = pointerRow;
            _desiredStockQuantity = 0.0;
            ActivateSelected(actions);
            PointerCaptured = true;
        }

        bool activate =
            Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, PlatformKey.Enter);

        if (activate)
        {
            ActivateSelected(actions);
        }

        bool cancel =
            (Mode is
                 PlayerActionPanelMode.Production or
                 PlayerActionPanelMode.UnitProduction or
                 PlayerActionPanelMode.Logistics) &&
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
        PlayerActionSnapshot? actions,
        uint dpi = 96,
        float uiScale = 1.0f)
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                viewportWidth,
                viewportHeight,
                dpi,
                uiScale);

        return new PlayerActionPanelView(
            Mode,
            SelectedIndex,
            Priority,
            ProductionMode,
            ResolveDesiredStockQuantity(actions),
            LogisticsPriority,
            StockThresholdField,
            _stockMinimum,
            _stockTarget,
            _stockMaximum,
            _automaticResupplyEnabled,
            _automaticFuelThreshold,
            _automaticAmmunitionThreshold,
            PointerCaptured,
            layout.ActionDock.X,
            layout.ActionDock.Y,
            _supplyPriority);
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
        _stockResourceId = ResourceId.None;
        _supplyEntity = EntityId.Invalid;
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

            case PlayerActionPanelMode.Logistics:
                ActivateLogistics(
                    actions.Logistics);
                break;

            case PlayerActionPanelMode.Supply:
                ActivateSupply(
                    actions.Supply);
                break;

            case PlayerActionPanelMode.Tactical:
                ActivateTactical(
                    actions.Tactical);
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

    private void ActivateLogistics(
        PlayerLogisticsActionReadModel? logistics)
    {
        if (logistics is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= logistics.Policies.Count)
        {
            return;
        }

        PlayerStockPolicyActionReadModel policy =
            logistics.Policies[SelectedIndex];

        _pendingRequest =
            PlayerActionRequest.SetStockPolicy(
                logistics.Entity,
                policy.ResourceId,
                _stockMinimum,
                _stockTarget,
                _stockMaximum,
                LogisticsPriority,
                policy.Enabled);
    }

    private void ActivateSupply(
        PlayerSupplyActionReadModel? supply)
    {
        if (!supply.HasValue)
        {
            return;
        }

        if (SelectedIndex >= 2)
        {
            _pendingRequest =
                PlayerActionRequest.RequestResupply(
                    supply.Value.Entity);
            return;
        }

        QueueAutomaticResupplyPolicy(
            supply);
    }

    private void QueueSupplyPriority(
        PlayerActionSnapshot? actions)
    {
        if (!actions?.Supply.HasValue == true)
        {
            return;
        }

        _pendingRequest =
            PlayerActionRequest.SetSupplyPriority(
                actions!.Supply!.Value.Entity,
                _supplyPriority);
    }

    private void QueueAutomaticResupplyPolicy(
        PlayerActionSnapshot? actions) =>
        QueueAutomaticResupplyPolicy(
            actions?.Supply);

    private void QueueAutomaticResupplyPolicy(
        PlayerSupplyActionReadModel? supply)
    {
        if (!supply.HasValue)
        {
            return;
        }

        _pendingRequest =
            PlayerActionRequest.SetAutomaticResupplyPolicy(
                supply.Value.Entity,
                _automaticFuelThreshold,
                _automaticAmmunitionThreshold,
                _automaticResupplyEnabled);
    }

    private void ActivateTactical(
        PlayerTacticalActionReadModel? tactical)
    {
        if (tactical is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= 8)
        {
            return;
        }

        PlayerActionRequest request =
            SelectedIndex switch
            {
                0 =>
                    PlayerActionRequest.BeginAttackTargeting(
                        tactical.SelectedEntities),
                1 =>
                    PlayerActionRequest.BeginAttackMoveTargeting(
                        tactical.SelectedEntities),
                2 =>
                    PlayerActionRequest.StopCombat(
                        tactical.SelectedEntities),
                3 =>
                    PlayerActionRequest.HoldPosition(
                        tactical.SelectedEntities),
                4 =>
                    PlayerActionRequest.BeginRetreatTargeting(
                        tactical.SelectedEntities),
                5 =>
                    PlayerActionRequest.BeginFireMissionTargeting(
                        tactical.SelectedEntities),
                6 =>
                    PlayerActionRequest.CancelFireMission(
                        tactical.SelectedEntities),
                7 =>
                    PlayerActionRequest.RetreatToRecovery(
                        tactical.SelectedEntities,
                        FormationTemplate.Column),
                _ =>
                    default
            };

        if (request.Kind ==
            PlayerActionRequestKind.None)
        {
            return;
        }

        _pendingRequest = request;
        Close();
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

        if (Mode ==
                PlayerActionPanelMode.Logistics &&
            actions.Logistics is
                PlayerLogisticsActionReadModel logistics &&
            SelectedIndex >= 0 &&
            SelectedIndex < logistics.Policies.Count)
        {
            PlayerStockPolicyActionReadModel policy =
                logistics.Policies[SelectedIndex];

            if (policy.HasPolicy)
            {
                _pendingRequest =
                    PlayerActionRequest.RemoveStockPolicy(
                        policy.PolicyEntity);
            }
        }
    }

    private void SynchronizeStockEditor(
        PlayerActionSnapshot? actions)
    {
        if (Mode != PlayerActionPanelMode.Logistics ||
            actions?.Logistics is not
                PlayerLogisticsActionReadModel logistics ||
            SelectedIndex < 0 ||
            SelectedIndex >= logistics.Policies.Count)
        {
            _stockResourceId = ResourceId.None;
            return;
        }

        PlayerStockPolicyActionReadModel policy =
            logistics.Policies[SelectedIndex];

        if (_stockResourceId == policy.ResourceId)
        {
            return;
        }

        _stockResourceId = policy.ResourceId;
        _stockMinimum = policy.DesiredMinimum;
        _stockTarget = policy.DesiredTarget;
        _stockMaximum = policy.DesiredMaximum;
        LogisticsPriority = policy.Priority;
    }

    private void SynchronizeSupplyEditor(
        PlayerActionSnapshot? actions)
    {
        if (Mode != PlayerActionPanelMode.Supply ||
            !actions?.Supply.HasValue == true)
        {
            _supplyEntity = EntityId.Invalid;
            return;
        }

        PlayerSupplyActionReadModel supply =
            actions!.Supply!.Value;

        if (_supplyEntity == supply.Entity)
        {
            return;
        }

        _supplyEntity = supply.Entity;
        _automaticFuelThreshold =
            supply.AutomaticFuelThreshold;
        _automaticAmmunitionThreshold =
            supply.AutomaticAmmunitionThreshold;
        _automaticResupplyEnabled =
            supply.AutomaticEnabled;
        _supplyPriority =
            supply.Priority;
    }

    private void UpdateStockThresholds(
        InputState input,
        PlayerActionSnapshot? actions)
    {
        if (actions?.Logistics is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= actions.Logistics.Policies.Count)
        {
            _ = Pressed(input, PlatformKey.Left);
            _ = Pressed(input, PlatformKey.Right);
            return;
        }

        double step =
            Math.Max(
                1.0,
                Math.Ceiling(_stockTarget * 0.1));

        double delta = 0.0;
        if (Pressed(input, PlatformKey.Left))
        {
            delta = -step;
        }

        if (Pressed(input, PlatformKey.Right))
        {
            delta = step;
        }

        if (delta == 0.0)
        {
            return;
        }

        switch (StockThresholdField)
        {
            case PlayerStockThresholdField.Minimum:
                _stockMinimum =
                    Math.Clamp(
                        _stockMinimum + delta,
                        0.0,
                        _stockTarget);
                break;

            case PlayerStockThresholdField.Target:
                _stockTarget =
                    Math.Max(
                        1.0,
                        _stockTarget + delta);
                _stockMinimum =
                    Math.Min(
                        _stockMinimum,
                        _stockTarget);
                _stockMaximum =
                    Math.Max(
                        _stockMaximum,
                        _stockTarget);
                break;

            case PlayerStockThresholdField.Maximum:
                _stockMaximum =
                    Math.Max(
                        _stockTarget,
                        _stockMaximum + delta);
                break;
        }
    }

    private void UpdateSupplyThresholds(
        InputState input,
        PlayerActionSnapshot? actions)
    {
        if (!actions?.Supply.HasValue == true ||
            SelectedIndex > 1)
        {
            _ = Pressed(input, PlatformKey.Left);
            _ = Pressed(input, PlatformKey.Right);
            return;
        }

        double delta = 0.0;
        if (Pressed(input, PlatformKey.Left))
        {
            delta = -0.05;
        }

        if (Pressed(input, PlatformKey.Right))
        {
            delta = 0.05;
        }

        if (delta == 0.0)
        {
            return;
        }

        if (SelectedIndex == 0)
        {
            _automaticFuelThreshold =
                Math.Clamp(
                    _automaticFuelThreshold + delta,
                    0.0,
                    1.0);
        }
        else
        {
            _automaticAmmunitionThreshold =
                Math.Clamp(
                    _automaticAmmunitionThreshold + delta,
                    0.0,
                    1.0);
        }
    }

    private void UpdateDesiredStockTarget(
        InputState input,
        PlayerActionSnapshot? actions)
    {
        if (ProductionMode !=
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
            PlayerActionPanelMode.Logistics =>
                actions?.Logistics?.Policies.Count ??
                0,
            PlayerActionPanelMode.Supply =>
                actions?.Supply is null
                    ? 0
                    : 3,
            PlayerActionPanelMode.Tactical =>
                actions?.Tactical is null
                    ? 0
                    : 8,
            _ =>
                0
        };

    private static bool IsPointerInsidePanel(
        Vector2 pointer,
        in PlayerActionPanelView view,
        int rowCount,
        in GameplayHudLayout layout)
    {
        float height =
            MathF.Min(
                MathF.Max(
                    0.0f,
                    layout.SafeArea.Bottom -
                    view.OriginY),
                layout.ActionRowStartOffset +
                Math.Max(
                    rowCount,
                    1) *
                layout.ActionRowHeight +
                layout.ActionBottomPadding);

        return pointer.X >= view.OriginX &&
               pointer.X <=
                   view.OriginX +
                   layout.ActionDock.Width &&
               pointer.Y >= view.OriginY &&
               pointer.Y <=
                   view.OriginY +
                   Math.Max(
                       height,
                       layout.ActionRowStartOffset +
                       layout.ActionRowHeight);
    }

    private static bool TryResolvePointerRow(
        Vector2 pointer,
        in PlayerActionPanelView view,
        int rowCount,
        in GameplayHudLayout layout,
        out int row)
    {
        float y =
            pointer.Y -
            (view.OriginY +
             layout.ActionRowStartOffset);

        if (y < 0.0f)
        {
            row = -1;
            return false;
        }

        row =
            (int)(y /
                  layout.ActionRowHeight);

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
        LogisticsPriority =
            LogisticsStockPriority.Normal;
        _supplyPriority =
            BattlefieldSupplyPriority.Normal;
        StockThresholdField =
            PlayerStockThresholdField.Target;
        _stockResourceId = ResourceId.None;
        _supplyEntity = EntityId.Invalid;
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
