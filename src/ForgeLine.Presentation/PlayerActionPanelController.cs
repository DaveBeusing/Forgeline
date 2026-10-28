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
    bool PointerPressed = false,
    bool ContextualPending = false,
    SimulationTick? ContextualActivationTick = null,
    SimulationSessionId ContextualSessionId = default,
    bool SelectionPending = false, GameplayBindingRegistry? Bindings = null, int ContextualHoveredIndex = -1, bool ContextualPressed = false)
{
    public UiMotionView? Motion { get; init; }
    public int LogisticsSteps { get; init; } = 1;
    public string StockPreview { get; init; } = string.Empty;
    public bool StockConflict { get; init; }
    public ObjectActionRingView Ring { get; init; }
    public bool DockExpanded => IsOpen && !Ring.UsesRing;
    public bool IsOpen =>
        Mode != PlayerActionPanelMode.Closed;
}

public sealed partial class PlayerActionPanelController
{
    private readonly ulong[] _keySequences = new ulong[Enum.GetValues<PlatformKey>().Length];
    private readonly GameplayBindingRegistry _bindings;
    public PlayerActionPanelController(GameplayBindingRegistry? bindings = null) { _bindings = bindings ?? GameplayBindingRegistry.Default; }
    private readonly Dictionary<PlatformKey, bool> _heldKeys = new();
    private bool _leftWasDown;
    private PlayerActionRequest? _pendingRequest;
    private SimulationSessionId _sessionId;
    private double _desiredStockQuantity;
    private ResourceId _stockResourceId;
    private EntityId _stockEntity;
    private ProductionStockProposal? _stockProposal;
    private string _stockPreviewLabel = string.Empty;
    private bool _stockResourceFromProposal;
    private StockPolicyBaseline? _stockBaseline;
    private bool _stockDraftStale;
    private readonly record struct StockPolicyBaseline(EntityId Policy, double Minimum, double Target,
        double Maximum, LogisticsStockPriority Priority, bool Enabled)
    {
        internal static StockPolicyBaseline From(in PlayerStockPolicyActionReadModel policy) =>
            new(policy.PolicyEntity, policy.DesiredMinimum, policy.DesiredTarget, policy.DesiredMaximum, policy.Priority, policy.Enabled);
    }
    private int _logisticsSteps = 1;
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
    private int _contextualHoveredIndex = -1;
    private bool _contextualPressed;
    private TimeSpan _feedbackRemaining;
    public string Feedback { get; private set; } = string.Empty;
    public void ShowDemolitionFeedback(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Feedback = message;
        _feedbackRemaining = TimeSpan.FromSeconds(5);
    }
    public void AdvanceFeedback(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _feedbackRemaining -= elapsed;
        if (_feedbackRemaining <= TimeSpan.Zero) Feedback = string.Empty;
    }
    private bool _pointerPressed;
    private GameplayHudLayout _lastPointerLayout;
    private bool _hasPointerLayout;
    private ulong _leftPressSequence;
    private SimulationTick? _contextualActivationTick;
    private SelectionSet? _currentSelection;

    public PlayerActionPanelMode Mode { get; private set; }
    public bool DockExpanded => Mode != PlayerActionPanelMode.Closed && !_ringContext;
    public bool HasOpenMenu => Mode != PlayerActionPanelMode.Closed || _ringOpen;

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

    // Dock shortcuts consume their mapped keys; the panel has no free-text editor.
    public bool HasKeyboardFocus => PointerCaptured && Mode != PlayerActionPanelMode.Closed;

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
        float uiScale = 1.0f,
        SelectionSet? currentSelection = null,
        bool globalShortcutsBlocked = false)
    {
        _currentSelection = currentSelection;
        _contextualHoveredIndex = -1;
        _contextualPressed = false;
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(viewportHeight);

        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                viewportWidth,
                viewportHeight,
                dpi,
                uiScale);

        bool displayChanged = _hasPointerLayout && _lastPointerLayout != layout;
        _lastPointerLayout = layout;
        _hasPointerLayout = true;
        ulong pressSequence = input.MousePressSequence(PlatformMouseButton.Left);
        bool freshPress = pressSequence != _leftPressSequence;
        _leftPressSequence = pressSequence;
        if (input.FocusLostThisFrame && !input.HasPointerPosition)
        {
            if (_ringContext) Close();
            Feedback = string.Empty;
            _pendingRequest = null;
            PointerCaptured = false;
            _hoveredIndex = -1;
            _pointerPressed = false;
            _leftWasDown = false;
            _heldKeys.Clear();
            return;
        }

        bool sessionChanged = _sessionId.IsSpecified && snapshot?.SessionId != _sessionId;
        if (_contextualActivationTick != snapshot?.Tick) _contextualActivationTick = null;
        SynchronizeSession(
            snapshot?.SessionId ??
            SimulationSessionId.None);

        PlayerActionSnapshot? actions =
            PlayerActionDockHudModel.ResolveActions(snapshot);
        if (!PlayerActionDockHudModel.MatchesSelection(actions, currentSelection) ||
            _ringGroup is not null && !_ringGroup.MatchesSelection(actions?.Tactical?.SelectedEntities)) actions = null;

        if (snapshot?.PlayerExperience is
                PlayerExperienceSnapshot experience &&
            experience.IsMatchComplete)
        {
            CloseForTerminal(
                input);
            return;
        }

        if (displayChanged || sessionChanged)
        {
            CloseRing();
            _pendingRequest = null;
            input.SuppressHeldInput();
            for (int i = 0; i < _keySequences.Length; i++) _keySequences[i] = input.KeyPressSequence((PlatformKey)i);
            _hoveredIndex = -1; _pointerPressed = false;
            return;
        }

        _globalAnchorValid = input.HasPointerPosition && layout.SafeArea.Contains(input.PointerPosition);
        if (_ringGlobal && _ringOpen && !_globalAnchorValid)
        {
            Close();
            return;
        }
        if (_ringGlobal && !_ringOpen && _globalAnchorValid)
            _globalAnchor = input.PointerPosition;

        SynchronizeRingParameter(actions);
        if (!globalShortcutsBlocked) UpdateRadialPaging(input, actions);

        if (Pressed(input, _bindings.Key(GameplayAction.Build)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.Construction);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Process)) && !globalShortcutsBlocked &&
            actions?.Production is not { IsAutomatic: true })
        {
            ToggleMode(
                PlayerActionPanelMode.Production);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Units)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.UnitProduction);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Logistics)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.Logistics);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Supply)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.Supply);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Combat)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.Tactical);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Technology)) && !globalShortcutsBlocked)
        {
            ToggleMode(
                PlayerActionPanelMode.Technology);
        }

        if (Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, PlatformKey.Escape))
        {
            Close();
            Feedback = "CANCELLED - PANEL CLOSED";
            _feedbackRemaining = TimeSpan.FromSeconds(3);
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

            if (Pressed(input, _bindings.Key(GameplayAction.NextItem)))
            {
                SelectedIndex =
                    (SelectedIndex + 1) %
                    itemCount;
                _desiredStockQuantity = 0.0;
                _stockResourceId = ResourceId.None;
                _logisticsSteps = 1;
                _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions);
                if (_ringContext) { _ringPage = SelectedIndex / 4; _ringRevision++; _presentedRing = default; }
            }
            else
            {
                _ = Pressed(input, _bindings.Key(GameplayAction.NextItem));
            }
        }

        if (Mode != PlayerActionPanelMode.Closed && input.HasPointerPosition &&
            ContainsControl(input.PointerPosition, snapshot, layout) && input.WheelDelta != 0 && itemCount > 0)
        {
            int capacity = 4;
            SelectedIndex = Math.Clamp(SelectedIndex + (input.WheelDelta > 0 ? -capacity : capacity), 0, itemCount - 1);
            _desiredStockQuantity = 0.0;
            _stockResourceId = ResourceId.None;
            _logisticsSteps = 1;
            if (_ringContext) { _ringPage = SelectedIndex / 4; _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions); _ringRevision++; _presentedRing = default; }
        }
        SynchronizeStockEditor(actions);
        SynchronizeSupplyEditor(actions);
        bool requestPending = actions?.PendingCommandCount > 0 ||
            ContextualCommandModel.HasPending(CreateView(viewportWidth, viewportHeight, actions, dpi, uiScale), snapshot);

        if (Pressed(input, _bindings.Key(GameplayAction.PrimarySetting)) && !requestPending)
        {
            CyclePrimarySetting(
                actions);
        }

        if (Pressed(input, _bindings.Key(GameplayAction.SecondarySetting)) && !requestPending)
        {
            CycleSecondarySetting(
                actions);
        }

        int adjustment = 0;
        if (Pressed(input, _bindings.Key(GameplayAction.Decrease)))
        {
            adjustment--;
        }

        if (Pressed(input, _bindings.Key(GameplayAction.Increase)))
        {
            adjustment++;
        }

        if (adjustment != 0 && !requestPending)
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

        if (_ringContext)
        {
            UpdateRingPointer(input, snapshot, actions, layout, view, freshPress);
        }
        else PointerCaptured = false;

        bool activate =
            Mode != PlayerActionPanelMode.Closed &&
            Pressed(input, _bindings.Key(GameplayAction.Activate));

        if (activate && !requestPending && Mode == PlayerActionPanelMode.UnitProduction &&
            actions?.UnitProduction is { } lockedProduction && SelectedIndex >= 0 &&
            SelectedIndex < lockedProduction.Units.Count && !lockedProduction.Units[SelectedIndex].TechnologyUnlocked)
        {
            TechnologyCapabilityId capability = lockedProduction.Units[SelectedIndex].RequiredTechnologyCapability;
            for (int index = 0; index < actions.Technology.Count; index++)
                if (actions.Technology[index].Unlocks.Contains(capability))
                {
                    ToggleMode(PlayerActionPanelMode.Technology);
                    if (Mode == PlayerActionPanelMode.Technology) SelectedIndex = index;
                    return;
                }
        }

        if (activate &&
            PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Activate,
                Mode, SelectedIndex, view, actions))
        {
            ActivateSelected(actions);
            if (_ringContext && _pendingRequest.HasValue) _contextualActivationTick = snapshot?.Tick;
        }

        bool cancel =
            (Mode is
                 PlayerActionPanelMode.Production or
                 PlayerActionPanelMode.UnitProduction or
                 PlayerActionPanelMode.Logistics or
                 PlayerActionPanelMode.Technology) &&
            Pressed(input, _bindings.Key(GameplayAction.CancelJob));

        if (cancel && !requestPending)
        {
            CancelSelected(actions);
            if (_ringContext && _pendingRequest.HasValue) _contextualActivationTick = snapshot?.Tick;
        }
        else
        {
            _ = Pressed(input, _bindings.Key(GameplayAction.CancelJob));
        }

        UpdateTacticalShortcuts(input, actions, globalShortcutsBlocked);
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
            layout.SafeArea.X,
            layout.SafeArea.Y,
            _supplyPriority,
            _hoveredIndex,
            _pointerPressed,
            _contextualActivationTick.HasValue,
            _contextualActivationTick,
            _sessionId,
            !PlayerActionDockHudModel.MatchesSelection(actions, _currentSelection), _bindings, _contextualHoveredIndex, _contextualPressed)
        { Ring = BuildRing(actions), LogisticsSteps = _logisticsSteps,
            StockConflict = _stockDraftStale,
            StockPreview = _stockDraftStale ? "POLICY CHANGED / CURRENT VALUES LOADED / REVIEW BEFORE APPLY" :
                _stockProposal is not null && _stockResourceFromProposal ? _stockPreviewLabel : string.Empty };
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

    public bool ContainsControl(System.Numerics.Vector2 point, PresentationSnapshot? snapshot, in GameplayHudLayout layout)
    {
        return _ringContext && _presentedRing.TryHit(point, out _);
    }

    public void DismissTransient(InputState input, bool preserveSelectionGesture = false)
    {
        if (!preserveSelectionGesture) { _selectionOpenArmed = false; _reopenSelection = false; }
        Close();
        _pendingRequest = null;
        _contextualActivationTick = null;
        _contextualHoveredIndex = -1; _contextualPressed = false;
        _leftPressSequence = input.MousePressSequence(PlatformMouseButton.Left);
        _leftWasDown = input.IsMouseButtonDown(PlatformMouseButton.Left);
    }

    public void Close()
    {
        CloseRing();
        Feedback = string.Empty;
        _feedbackRemaining = default;
        Mode =
            PlayerActionPanelMode.Closed;
        SelectedIndex = 0;
        PointerCaptured = false;
        _hoveredIndex = -1;
        _pointerPressed = false;
        _desiredStockQuantity = 0.0;
        _stockProposal = null; _stockEntity = default; _logisticsSteps = 1;
    }

    public void OpenOperationsControls(PlayerActionPanelMode mode)
    {
        if (mode is not (PlayerActionPanelMode.Production or PlayerActionPanelMode.UnitProduction or PlayerActionPanelMode.Logistics or PlayerActionPanelMode.Supply)) return;
        Close();
        _pendingRequest = null;
        _contextualActivationTick = null;
        ToggleMode(mode);
    }

    private void ToggleMode(
        PlayerActionPanelMode requested)
    {
        if (_ringGlobal && !_ringOpen && !_globalAnchorValid)
        {
            Feedback = "MOVE POINTER INTO PLAY AREA";
            _feedbackRemaining = TimeSpan.FromSeconds(3);
            return;
        }
        Feedback = string.Empty;
        Mode =
            Mode == requested
                ? PlayerActionPanelMode.Closed
                : requested;
        SelectedIndex = 0;
        _hoveredIndex = -1;
        _pointerPressed = false;
        _desiredStockQuantity = 0.0;
        _stockResourceId = ResourceId.None;
        _stockProposal = null; _stockEntity = default; _logisticsSteps = 1;
        _supplyEntity = EntityId.Invalid;
        if (_ringContext) { _ringOpen = Mode != PlayerActionPanelMode.Closed; _ringLevel = ActionRingLevel.Items; _ringRevision++; _ringPage = 0;
            _presentedRing = default; _ringArmed = false; _itemSlotsPage = -1; }
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
                ActivateLogistics(actions);
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
            facility.QueueRecipes.Count)
        {
            PlayerProductionRecipeActionReadModel recipe =
                facility.QueueRecipes[
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
            facility.QueueRecipes.Count;

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

    private void ActivateLogistics(PlayerActionSnapshot actions)
    {
        if (actions.BridgeRepairs.Count > 0)
        {
            if (SelectedIndex >= 0 && SelectedIndex < actions.BridgeRepairs.Count &&
                actions.BridgeRepairs[SelectedIndex] is var repair && repair.Failure == BridgeRepairFailure.None)
                _pendingRequest = PlayerActionRequest.RepairBridge(repair.Engineer, repair.Bridge, repair.Source, repair.Revision);
            return;
        }
        if (actions.ConveyorRouting is { } routing)
        {
            if (SelectedIndex is >= 0 and < 3 && routing.Failure(SelectedIndex) == ConveyorRoutingFailure.None)
                _pendingRequest = PlayerActionRequest.SetConveyorRouting(routing.Entity,
                    (ConveyorRoutingPreference)SelectedIndex, routing.Revision);
            return;
        }
        var logistics = actions.Logistics;
        if (logistics is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= logistics.ItemCount)
        {
            return;
        }
        if (SelectedIndex >= logistics.OrderStartIndex) return;
        if (SelectedIndex >= logistics.Policies.Count)
        {
            var requirement = logistics.Requirements[SelectedIndex - logistics.Policies.Count];
            var proposal = ProductionStockProposal.Create(logistics, requirement, _logisticsSteps);
            Feedback = proposal.Reason;
            _feedbackRemaining = TimeSpan.FromSeconds(8);
            if (!proposal.FitsCapacity) return;
            _stockProposal = proposal;
            _stockPreviewLabel = $"INPUT DRAFT / {proposal.Steps} ADDITIONAL STEP(S) / NO EXTRA BUFFER";
            _stockEntity = logistics.Entity;
            SelectedIndex = logistics.Policies.ToList().FindIndex(p => p.ResourceId == proposal.Thresholds[0].Resource);
            _stockResourceId = ResourceId.None;
            SynchronizeStockEditor(actions);
            _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions);
            _ringPage = SelectedIndex / 4; _itemSlotsPage = -1;
            return;
        }

        PlayerStockPolicyActionReadModel policy = logistics.Policies[SelectedIndex];
        if (logistics.IsCommandCore && _ringContext && _ringLevel != ActionRingLevel.Parameters)
        {
            _ringLevel = ActionRingLevel.Parameters;
            _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions);
            _ringRevision++; _presentedRing = default; _ringArmed = false;
            StockThresholdField = PlayerStockThresholdField.Minimum;
            return;
        }
        if (!policy.HasPolicy && logistics.InventoryCapacity > 0 &&
            logistics.InventoryCapacity < LogisticsQuantityEditor.Minimum)
        {
            Feedback = "INVENTORY CAPACITY BELOW 50";
            _feedbackRemaining = TimeSpan.FromSeconds(4);
            return;
        }
        if (_stockDraftStale)
        {
            _stockDraftStale = false;
            Feedback = "CURRENT POLICY REVIEWED / APPLY AGAIN TO SAVE";
            _feedbackRemaining = TimeSpan.FromSeconds(8);
            return;
        }
        _pendingRequest =
            PlayerActionRequest.SetStockPolicy(
                logistics.Entity,
                policy.ResourceId,
                _stockMinimum,
                logistics.IsCommandCore ? _stockMaximum : _stockTarget,
                _stockMaximum,
                LogisticsPriority,
                logistics.IsCommandCore || policy.Enabled);
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
        if (actions?.Supply is not PlayerSupplyActionReadModel supply)
        {
            return;
        }

        _pendingRequest =
            PlayerActionRequest.SetSupplyPriority(
                supply.Entity,
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

    private void UpdateTacticalShortcuts(InputState input, PlayerActionSnapshot? actions, bool blocked)
    {
        int requested = -1;
        for (int index = 0; index < 8; index++)
        {
            PlatformKey key = _bindings.Key(GameplayBindingRegistry.TacticalAction(index));
            if (Pressed(input, key) && requested < 0 &&
                !input.WasKeyPressedWithShift(key) && !input.WasKeyPressedWithControl(key)) requested = index;
        }
        if (requested < 0 || blocked || input.FocusLostThisFrame || _pendingRequest.HasValue ||
            input.IsKeyDown(PlatformKey.LeftControl) || input.IsKeyDown(PlatformKey.RightControl) ||
            input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift)) return;
        var state = PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Tactical, requested, actions);
        if (!state.CanActivate)
        {
            Feedback = state.DisabledReason;
            _feedbackRemaining = TimeSpan.FromSeconds(3);
            return;
        }
        ActivateTactical(actions?.Tactical, requested);
    }

    private void ActivateTactical(
        PlayerTacticalActionReadModel? tactical, int? itemIndex = null)
    {
        int index = itemIndex ?? SelectedIndex;
        if (tactical is null ||
            index < 0 ||
            index >= 8)
        {
            return;
        }

        PlayerActionRequest request =
            index switch
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
                production.QueueRecipes.Count;

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

        if (Mode == PlayerActionPanelMode.Logistics &&
            actions.Logistics is { SelectedConvoy: { } selectedConvoy } &&
            SelectedIndex == 0)
        {
            _pendingRequest =
                PlayerActionRequest.DissolveCargoConvoy(selectedConvoy.Group);
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
        if (actions?.Logistics is null)
        { _stockProposal = null; _stockEntity = default; _stockPreviewLabel = string.Empty; }
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

        if (_stockEntity != logistics.Entity)
        {
            _stockEntity = logistics.Entity; _stockProposal = null;
            _stockResourceId = ResourceId.None; _stockBaseline = null; _stockDraftStale = false;
        }
        var baseline = StockPolicyBaseline.From(policy);
        bool sameResource = _stockResourceId == policy.ResourceId;
        if (sameResource && _stockBaseline == baseline) return;
        if (sameResource && _stockBaseline is { } previous)
        {
            bool dirty = _stockMinimum != previous.Minimum ||
                _stockTarget != (logistics.IsCommandCore ? previous.Maximum : previous.Target) ||
                _stockMaximum != previous.Maximum || LogisticsPriority != previous.Priority ||
                _stockResourceFromProposal;
            bool acceptedDraft = policy.HasPolicy && policy.Enabled == (logistics.IsCommandCore || previous.Enabled) &&
                _stockMinimum == policy.DesiredMinimum &&
                _stockTarget == policy.DesiredTarget &&
                _stockMaximum == policy.DesiredMaximum && LogisticsPriority == policy.Priority;
            if (dirty && !acceptedDraft)
            {
                _stockDraftStale = true;
                _stockProposal = null;
                Feedback = "POLICY CHANGED / CURRENT VALUES LOADED / REVIEW BEFORE APPLY";
                _feedbackRemaining = TimeSpan.FromSeconds(8);
            }
        }
        else _stockDraftStale = false;
        _stockBaseline = baseline;
        _stockResourceId = policy.ResourceId;
        _stockResourceFromProposal = false;
        _stockMinimum = policy.DesiredMinimum;
        _stockTarget = logistics.IsCommandCore ? policy.DesiredMaximum : policy.DesiredTarget;
        if (logistics.IsCommandCore && StockThresholdField == PlayerStockThresholdField.Target)
            StockThresholdField = PlayerStockThresholdField.Minimum;
        _stockMaximum = policy.DesiredMaximum;
        LogisticsPriority = policy.Priority;
        if (_stockProposal is not null)
            foreach (var threshold in _stockProposal.Thresholds)
                if (threshold.Resource == policy.ResourceId)
                { _stockMinimum = threshold.Minimum; _stockTarget = threshold.Target; _stockMaximum = threshold.Maximum; _stockResourceFromProposal = true; break; }
    }

    private void SynchronizeSupplyEditor(
        PlayerActionSnapshot? actions)
    {
        if (Mode != PlayerActionPanelMode.Supply ||
            actions?.Supply is not PlayerSupplyActionReadModel supply)
        {
            _supplyEntity = EntityId.Invalid;
            return;
        }

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
            SelectedIndex >= facility.QueueRecipes.Count)
        {
            return 0.0;
        }

        return ResolveDesiredStockQuantity(
            facility.QueueRecipes[
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

            case PlayerActionDockControlKind.Page:
            case PlayerActionDockControlKind.Item:
                if (SelectedIndex != hit.ItemIndex) _logisticsSteps = 1;
                SelectedIndex =
                    hit.ItemIndex;
                _desiredStockQuantity = 0.0;
                _stockResourceId = ResourceId.None;
                SynchronizeStockEditor(
                    actions);
                SynchronizeSupplyEditor(
                    actions);
                if (hit.Kind == PlayerActionDockControlKind.Item &&
                    PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Activate,
                        Mode, SelectedIndex, panel, actions))
                    ActivateSelected(actions);
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

            case PlayerActionDockControlKind.DecreaseMinimum:
            case PlayerActionDockControlKind.IncreaseMinimum:
            case PlayerActionDockControlKind.DecreaseMaximum:
            case PlayerActionDockControlKind.IncreaseMaximum:
                if (PlayerActionDockHudModel.CanUseControl(hit.Kind, Mode, SelectedIndex, panel, actions))
                {
                    StockThresholdField = hit.Kind is PlayerActionDockControlKind.DecreaseMinimum or PlayerActionDockControlKind.IncreaseMinimum
                        ? PlayerStockThresholdField.Minimum : PlayerStockThresholdField.Maximum;
                    AdjustStockThreshold(actions, hit.Kind is PlayerActionDockControlKind.IncreaseMinimum or PlayerActionDockControlKind.IncreaseMaximum ? 1 : -1);
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
        if (Mode == PlayerActionPanelMode.Logistics && actions?.Logistics is { } orders &&
            SelectedIndex >= orders.OrderStartIndex) return;
        if (Mode == PlayerActionPanelMode.Supply && actions?.Supply is { IsStaticProvider: true }) return;
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
        if (Mode == PlayerActionPanelMode.Logistics && actions?.Logistics is { } orders &&
            SelectedIndex >= orders.OrderStartIndex) return;
        if (Mode == PlayerActionPanelMode.Supply && actions?.Supply is { IsStaticProvider: true }) return;
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
            if (actions?.Logistics is { IsCommandCore: true })
            {
                StockThresholdField = StockThresholdField == PlayerStockThresholdField.Minimum
                    ? PlayerStockThresholdField.Maximum : PlayerStockThresholdField.Minimum;
                return;
            }
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
        if (Mode == PlayerActionPanelMode.Supply && actions?.Supply is { IsStaticProvider: true }) return;
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
            SelectedIndex >= facility.QueueRecipes.Count)
        {
            return;
        }

        PlayerProductionRecipeActionReadModel recipe =
            facility.QueueRecipes[
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
        if (actions?.Logistics is { } logistics && SelectedIndex >= logistics.Policies.Count && SelectedIndex < logistics.OrderStartIndex)
        { _logisticsSteps = Math.Clamp(_logisticsSteps + direction, 1, 100); return; }
        if (actions?.Logistics is null ||
            SelectedIndex < 0 ||
            SelectedIndex >= actions.Logistics.Policies.Count)
        {
            return;
        }

        bool band = actions.Logistics.IsCommandCore;
        var decision = band
            ? LogisticsQuantityEditor.DecideBand(StockThresholdField, _stockMinimum, _stockMaximum,
                actions.Logistics.InventoryCapacity, direction)
            : LogisticsQuantityEditor.DecideField(StockThresholdField, _stockMinimum, _stockTarget, _stockMaximum,
                actions.Logistics.InventoryCapacity, direction);
        if (!decision.Allowed)
        {
            Feedback = decision.Reason;
            _feedbackRemaining = TimeSpan.FromSeconds(3);
            return;
        }
        switch (StockThresholdField)
        {
            case PlayerStockThresholdField.Minimum: _stockMinimum = decision.Next; break;
            case PlayerStockThresholdField.Target: _stockTarget = decision.Next; break;
            case PlayerStockThresholdField.Maximum: _stockMaximum = decision.Next; break;
        }
        if (band) _stockTarget = _stockMaximum;
    }

    private void AdjustSupplyThreshold(
        PlayerActionSnapshot? actions,
        int direction)
    {
        if (actions?.Supply is null ||
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
        _stockProposal = null; _stockEntity = default; _logisticsSteps = 1;
        _supplyEntity = EntityId.Invalid;
        _leftWasDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);

        _ = Pressed(input, _bindings.Key(GameplayAction.Build));
        _ = Pressed(input, _bindings.Key(GameplayAction.Process));
        _ = Pressed(input, _bindings.Key(GameplayAction.Units));
        _ = Pressed(input, _bindings.Key(GameplayAction.Logistics));
        _ = Pressed(input, _bindings.Key(GameplayAction.Supply));
        _ = Pressed(input, _bindings.Key(GameplayAction.Combat));
        _ = Pressed(input, _bindings.Key(GameplayAction.Technology));
        _ = Pressed(input, PlatformKey.Escape);
        _ = Pressed(input, _bindings.Key(GameplayAction.NextItem));
        _ = Pressed(input, _bindings.Key(GameplayAction.PrimarySetting));
        _ = Pressed(input, _bindings.Key(GameplayAction.SecondarySetting));
        _ = Pressed(input, _bindings.Key(GameplayAction.Decrease));
        _ = Pressed(input, _bindings.Key(GameplayAction.Increase));
        _ = Pressed(input, _bindings.Key(GameplayAction.Activate));
        _ = Pressed(input, _bindings.Key(GameplayAction.CancelJob));
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
        Feedback = string.Empty;
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
        _stockProposal = null; _stockEntity = default; _logisticsSteps = 1;
        _supplyEntity = EntityId.Invalid;
        PointerCaptured = false;
        _pendingRequest = null;
        _contextualActivationTick = null;
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

        ulong sequence = input.KeyPressSequence(key);
        bool shortPress = input.WasKeyPressed(key) && sequence != _keySequences[(int)key];
        _keySequences[(int)key] = sequence;
        return down && !held || shortPress;
    }
}
