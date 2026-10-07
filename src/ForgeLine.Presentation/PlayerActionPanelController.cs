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
    UnitProduction = 3,
    Logistics = 4,
    Supply = 5,
    Tactical = 6,
    Technology = 7
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
        BattlefieldSupplyPriority.Normal,
    int HoveredIndex = -1,
    bool PointerPressed = false)
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
    private int _hoveredIndex = -1;
    private bool _pointerPressed;

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

    public int HoveredIndex =>
        _hoveredIndex;

    public bool PointerPressed =>
        _pointerPressed;

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

        if (snapshot?.PlayerExperience is
                PlayerExperienceSnapshot experience &&
            experience.IsMatchComplete)
        {
            CloseForTerminal(
                input);
            return;
        }

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

        if (Pressed(input, PlatformKey.H))
        {
            ToggleMode(
                PlayerActionPanelMode.Technology);
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

        int itemCount =
            PlayerActionDockInteractionLayout.GetItemCount(
                Mode,
                actions);

        if (itemCount == 0)
        {
            SelectedIndex = 0;
        }
        else
        {
            SelectedIndex =
                Math.Clamp(
                    SelectedIndex,
                    0,
                    itemCount - 1);

            if (Pressed(input, PlatformKey.Tab))
            {
                SelectedIndex =
                    (SelectedIndex + 1) %
                    itemCount;
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

        if (Pressed(input, PlatformKey.T))
        {
            CyclePrimarySetting(
                actions);
        }

        if (Pressed(input, PlatformKey.M))
        {
            CycleSecondarySetting(
                actions);
        }

        int adjustment = 0;
        if (Pressed(input, PlatformKey.Left))
        {
            adjustment--;
        }

        if (Pressed(input, PlatformKey.Right))
        {
            adjustment++;
        }

        if (adjustment != 0)
        {
            AdjustCurrentSetting(
                actions,
                adjustment);
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
            input.HasPointerPosition &&
            PlayerActionDockInteractionLayout.CapturesPointer(
                input.PointerPosition,
                layout,
                view.IsOpen);

        PlayerActionDockHitTarget hit =
            default;
        bool hasHit =
            PointerCaptured &&
            PlayerActionDockInteractionLayout.TryHit(
                input.PointerPosition,
                layout,
                view.IsOpen,
                itemCount,
                out hit);
        _hoveredIndex =
            hasHit &&
            hit.Kind ==
                PlayerActionDockControlKind.Item
                ? hit.ItemIndex
                : -1;
        _pointerPressed =
            hasHit &&
            leftDown;

        if (hasHit &&
            leftDown &&
            !_leftWasDown)
        {
            HandlePointerHit(
                hit,
                actions,
                view);
            PointerCaptured = true;
        }

        bool activate =
            Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, PlatformKey.Enter);

        if (activate &&
            PlayerActionDockHudModel.ResolveItemState(
                Mode,
                SelectedIndex,
                actions).CanActivate)
        {
            ActivateSelected(actions);
        }

        bool cancel =
            (Mode is
                 PlayerActionPanelMode.Production or
                 PlayerActionPanelMode.UnitProduction or
                 PlayerActionPanelMode.Logistics or
                 PlayerActionPanelMode.Technology) &&
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
            _supplyPriority,
            _hoveredIndex,
            _pointerPressed);
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
        _hoveredIndex = -1;
        _pointerPressed = false;
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
        _hoveredIndex = -1;
        _pointerPressed = false;
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

            case PlayerActionPanelMode.Technology:
                ActivateTechnology(
                    actions.Technology);
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

    private void ActivateTechnology(
        IReadOnlyList<PlayerTechnologyActionReadModel> technologies)
    {
        if (SelectedIndex < 0 ||
            SelectedIndex >= technologies.Count)
        {
            return;
        }

        PlayerTechnologyActionReadModel technology =
            technologies[SelectedIndex];

        if (!technology.CanStart ||
            !technology.Facility.IsValid ||
            !technology.SourceInventory.IsValid)
        {
            return;
        }

        _pendingRequest =
            PlayerActionRequest.StartTechnologyResearch(
                technology.TechnologyId,
                technology.Facility,
                technology.SourceInventory);
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
                PlayerActionPanelMode.Technology &&
            SelectedIndex >= 0 &&
            SelectedIndex < actions.Technology.Count)
        {
            PlayerTechnologyActionReadModel technology =
                actions.Technology[SelectedIndex];

            if (technology.CanCancel)
            {
                _pendingRequest =
                    PlayerActionRequest.CancelTechnologyResearch(
                        technology.ActiveRequest);
            }

            return;
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

    private void HandlePointerHit(
        in PlayerActionDockHitTarget hit,
        PlayerActionSnapshot? actions,
        in PlayerActionPanelView panel)
    {
        switch (hit.Kind)
        {
            case PlayerActionDockControlKind.Mode:
                ToggleMode(
                    hit.Mode);
                break;

            case PlayerActionDockControlKind.Item:
                SelectedIndex =
                    hit.ItemIndex;
                _desiredStockQuantity = 0.0;
                _stockResourceId = ResourceId.None;
                SynchronizeStockEditor(
                    actions);
                SynchronizeSupplyEditor(
                    actions);
                break;

            case PlayerActionDockControlKind.Activate:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.Activate,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    ActivateSelected(
                    actions);
                }

                break;

            case PlayerActionDockControlKind.Cancel:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.Cancel,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    CancelSelected(
                    actions);
                }

                break;

            case PlayerActionDockControlKind.CyclePrimary:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.CyclePrimary,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    CyclePrimarySetting(
                    actions);
                }

                break;

            case PlayerActionDockControlKind.CycleSecondary:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.CycleSecondary,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    CycleSecondarySetting(
                    actions);
                }

                break;

            case PlayerActionDockControlKind.Decrease:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.Decrease,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    AdjustCurrentSetting(
                    actions,
                    -1);
                }

                break;

            case PlayerActionDockControlKind.Increase:
                if (PlayerActionDockHudModel.CanUseControl(
                        PlayerActionDockControlKind.Increase,
                        Mode,
                        SelectedIndex,
                        panel,
                        actions))
                {
                    AdjustCurrentSetting(
                    actions,
                    1);
                }

                break;
        }
    }

    private void CyclePrimarySetting(
        PlayerActionSnapshot? actions)
    {
        if (Mode is
            PlayerActionPanelMode.Production or
            PlayerActionPanelMode.UnitProduction)
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
            return;
        }

        if (Mode ==
            PlayerActionPanelMode.Logistics)
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
            return;
        }

        if (Mode ==
            PlayerActionPanelMode.Supply)
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
            QueueSupplyPriority(
                actions);
        }
    }

    private void CycleSecondarySetting(
        PlayerActionSnapshot? actions)
    {
        if (Mode ==
            PlayerActionPanelMode.Production)
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
            return;
        }

        if (Mode ==
            PlayerActionPanelMode.Logistics)
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
            return;
        }

        if (Mode ==
            PlayerActionPanelMode.Supply)
        {
            _automaticResupplyEnabled =
                !_automaticResupplyEnabled;
            QueueAutomaticResupplyPolicy(
                actions);
        }
    }

    private void AdjustCurrentSetting(
        PlayerActionSnapshot? actions,
        int direction)
    {
        if (direction == 0)
        {
            return;
        }

        switch (Mode)
        {
            case PlayerActionPanelMode.Production:
                AdjustDesiredStockTarget(
                    actions,
                    direction);
                break;

            case PlayerActionPanelMode.Logistics:
                AdjustStockThreshold(
                    actions,
                    direction);
                break;

            case PlayerActionPanelMode.Supply:
                AdjustSupplyThreshold(
                    actions,
                    direction);
                break;
        }
    }

    private void AdjustDesiredStockTarget(
        PlayerActionSnapshot? actions,
        int direction)
    {
        if (ProductionMode !=
                ProductionRequestMode.DesiredStock ||
            actions?.Production is not
                PlayerProductionFacilityActionReadModel facility ||
            SelectedIndex < 0 ||
            SelectedIndex >= facility.Recipes.Count)
        {
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
        double current =
            ResolveDesiredStockQuantity(
                recipe);

        _desiredStockQuantity =
            Math.Max(
                step,
                current +
                direction *
                step);
    }

    private void AdjustStockThreshold(
        PlayerActionSnapshot? actions,
        int direction)
    {
        if (actions?.Logistics is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= actions.Logistics.Policies.Count)
        {
            return;
        }

        double step =
            Math.Max(
                1.0,
                Math.Ceiling(
                    _stockTarget *
                    0.1));
        double delta =
            direction *
            step;

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

    private void AdjustSupplyThreshold(
        PlayerActionSnapshot? actions,
        int direction)
    {
        if (!actions?.Supply.HasValue == true ||
            SelectedIndex > 1)
        {
            return;
        }

        double delta =
            direction *
            0.05;

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

    private void CloseForTerminal(
        InputState input)
    {
        Close();
        _pendingRequest = null;
        _stockResourceId = ResourceId.None;
        _supplyEntity = EntityId.Invalid;
        _leftWasDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);

        _ = Pressed(input, PlatformKey.B);
        _ = Pressed(input, PlatformKey.P);
        _ = Pressed(input, PlatformKey.U);
        _ = Pressed(input, PlatformKey.L);
        _ = Pressed(input, PlatformKey.Y);
        _ = Pressed(input, PlatformKey.K);
        _ = Pressed(input, PlatformKey.H);
        _ = Pressed(input, PlatformKey.Escape);
        _ = Pressed(input, PlatformKey.Tab);
        _ = Pressed(input, PlatformKey.T);
        _ = Pressed(input, PlatformKey.M);
        _ = Pressed(input, PlatformKey.Left);
        _ = Pressed(input, PlatformKey.Right);
        _ = Pressed(input, PlatformKey.Enter);
        _ = Pressed(input, PlatformKey.C);
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
