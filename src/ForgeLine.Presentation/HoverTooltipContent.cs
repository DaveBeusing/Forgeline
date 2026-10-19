using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

internal readonly record struct HoverTooltipContent(
    string Title, string Role, string Status, string Hint,
    PlayerHoverSummary? World = null,
    IReadOnlyList<PlayerActionResourceAmount>? Costs = null,
    IReadOnlyList<PlayerActionResourceAmount>? Outputs = null,
    uint? ProductionTicks = null,
    string Requirement = "",
    IReadOnlyList<PlayerTechnologyPrerequisiteReadModel>? Prerequisites = null,
    string WorkStatus = "", string Explanation = "", UiSkinState VisualState = UiSkinState.Normal, bool WrapDetails = false,
    PlayerSelectionSummary? Selection = null, SelectedCombatGroup? Group = null,
    FormationTemplate Formation = default, PlayerCommandFeedback? Feedback = null);

internal static class HoverTooltipResolver
{
    public static HoverTooltipContent? Resolve(PresentationSnapshot snapshot, in HoverTooltipView view,
        in PlayerActionPanelView panel, SelectedCombatGroup? group = null, FormationTemplate formation = default)
    {
        if (!view.Ready || view.SessionId != snapshot.SessionId ||
            snapshot.PlayerExperience is not { IsMatchComplete: false }) return null;
        if (view.SelectionStatus)
        {
            var ring = panel.Ring;
            var experience = snapshot.PlayerExperience.Value;
            if (!ring.UsesRing || !ring.Open || !ring.Resolved || !ring.Geometry.IsValid || panel.SelectionPending ||
                ring.Session != snapshot.SessionId || ring.Revision != view.MenuRevision ||
                ring.SelectionRevision != view.SelectionRevision || experience.Tick != snapshot.Tick ||
                (ring.SingleOwned && (experience.Selection.Count != 1 || experience.Selection.PrimaryEntity != ring.Entity)) ||
                (ring.Global && experience.Selection.Count != 0) ||
                (ring.Group is not null && (experience.Selection.Count != ring.Group.SelectedCount ||
                    !ring.Group.MatchesSelection(snapshot.PlayerActions?.Tactical?.SelectedEntities)))) return null;
            return new(experience.Selection.Count == 0 ? "COMMANDS" : experience.Selection.DisplayName,
                SelectionInspectorHudModel.ResolveKindLabel(experience.Selection.Kind), "", "READ-ONLY STATUS",
                WrapDetails: true, Selection: experience.Selection,
                Group: ring.Group is not null && group?.Tick == snapshot.Tick && group.TotalCount == experience.Selection.Count ? group : null,
                Formation: formation, Feedback: experience.Feedback);
        }
        if (view.DockControl != 0) return ResolveDock(snapshot, view, panel);
        if (view.Contact.IsSpecified)
        {
            if (snapshot.Intelligence is not { } intelligence || intelligence.Tick != snapshot.Tick) return null;
            for (int i = 0; i < intelligence.Contacts.Count; i++)
            {
                var contact = intelligence.Contacts[i];
                if (contact.ContactKey == view.Contact && contact.IsCurrent && contact.State == IntelligenceState.Detected)
                    return new("UNKNOWN CONTACT", "DETECTED", "IDENTITY UNAVAILABLE", "");
            }
            return null;
        }
        if (snapshot.Hover is not { } world || world.Entity != view.Entity ||
            world.SessionId != snapshot.SessionId || world.Tick != snapshot.Tick) return null;
        bool visible = false;
        foreach (var instance in snapshot.Instances)
            if (instance.Entity == world.Entity && (instance.Visibility & RenderVisibilityMask.World) != 0)
            { visible = true; break; }
        if (!visible) return null;
        string role = world.Category switch
        {
            PlayerHoverCategory.Deposit => "RESOURCE DEPOSIT",
            PlayerHoverCategory.Construction => "CONSTRUCTION",
            PlayerHoverCategory.IdentifiedContact => "IDENTIFIED HOSTILE",
            _ => RtsUiIconCatalog.Get(world.RoleIcon).ShortLabel
        };
        string hint = world.OwnedDetails.Count == 1 ? "LEFT CLICK TO SELECT" : string.Empty;
        string explanation = (panel.Bindings ?? GameplayBindingRegistry.Default).Text(ProductionContextFeedback.Explain(world.OwnedDetails.Work.BlockReason));
        if (world.OwnedDetails.Count == 1 && snapshot.PlayerActions is { } selected && selected.SessionId == snapshot.SessionId && selected.Tick == snapshot.Tick)
        {
            if (selected.Logistics is { } logistics && logistics.Entity == world.Entity && logistics.Cargo is { } cargo)
                explanation = $"REGIONAL CARGO / {cargo.ResourceName} / ORDER {cargo.RequestedQuantity:0.##} / LOADED {cargo.CargoQuantity:0.##} / SOURCE {cargo.SourceName} / DESTINATION {cargo.DestinationName}; AUTOMATIC TARGET DEMAND ASSIGNS WORK. " + LogisticsStatusCatalog.ServiceDetails(logistics.Service);
            else if (selected.Supply is { } supply && supply.Entity == world.Entity)
                explanation = $"{LogisticsStatusCatalog.DriveTank(supply)}; {(supply.IsSupplyTruck ? "FIELD CARGO" : "PROVIDER STOCK")} FUEL {(supply.IsSupplyTruck ? supply.CargoFuel : supply.ProviderFuel):0.##} / AMMUNITION {(supply.IsSupplyTruck ? supply.CargoAmmunition : supply.ProviderAmmunition):0.##}. " + LogisticsStatusCatalog.ServiceDetails(supply.Service);
        }
        return new(world.DisplayName, role, world.ExtractionState, hint, world, Explanation: explanation,
            WrapDetails: world.OwnedDetails.Count == 1 && snapshot.PlayerActions is { } owned &&
                (owned.Logistics?.Entity == world.Entity || owned.Supply?.Entity == world.Entity));
    }

    private static HoverTooltipContent? ResolveDock(PresentationSnapshot snapshot, in HoverTooltipView view,
        in PlayerActionPanelView panel)
    {
        var control = (PlayerActionDockControlKind)view.DockControl;
        var mode = view.DockMode;
        if (control == PlayerActionDockControlKind.Page && panel.Ring.Group is { } group && view.DockIndex <= -99)
        {
            int groupControl = -100 - view.DockIndex;
            if (view.DockIndex == -99) return new($"GROUP {group.Count} / SELECTED {group.SelectedCount}", "SELECTION TOOLS",
                panel.Ring.VisibleMembers < group.Count ? "PART OF SELECTION VISIBLE" : "SELECTION VISIBLE",
                "VISIBILITY DOES NOT FILTER COMMAND MEMBERS");
            if (groupControl is < 0 or > 11) return null;
            int participants = group.Participants(groupControl);
            return new(GroupActionRingContext.Label(groupControl), "GROUP SELECTION",
                group.HasOperationalFacts && panel.Ring.GroupFactsCurrent ? $"ELIGIBLE {participants} / SELECTED {group.SelectedCount}" : "UPDATING GROUP DATA", !group.HasOperationalFacts || !panel.Ring.GroupFactsCurrent ? "WAIT FOR CURRENT GROUP DATA" : participants == 0 ? "NO ELIGIBLE MEMBERS" :
                groupControl < 8 ? "FILTER CURRENT SELECTION; SAVED GROUP UNCHANGED" : "CLICK TO REQUEST");
        }
        if (view.Contextual)
        {
            for (int i = 0; ContextualCommandModel.TryGet(snapshot, i, out var command,
                    ContextualCommandModel.HasPending(panel, snapshot), panel.SelectionPending, panel.Bindings); i++)
                if (command.Mode == mode && command.ItemIndex == view.DockIndex)
                    return new(command.Label, "SELECTION COMMAND", command.Availability.CanActivate
                        ? "AVAILABLE" : command.Availability.DisabledReason,
                        command.OpensMode ? command.Shortcut + " OPENS ADVANCED MODE" : (panel.Bindings ?? GameplayBindingRegistry.Default).Text("CLICK OR K / TAB / ENTER; WATCH COMMAND FEEDBACK"));
            return null;
        }
        if (control == PlayerActionDockControlKind.Mode)
            return new(PlayerActionDockHudModel.ResolveModeLabel(mode), "ACTION PANEL",
                PlayerActionDockHudModel.ResolveModeShortcut(mode, panel.Bindings), "CLICK TO OPEN / CLOSE");
        if (panel.SelectionPending || !panel.IsOpen || panel.Mode != mode || snapshot.PlayerActions is not { } actions ||
            actions.SessionId != snapshot.SessionId || actions.Tick != snapshot.Tick) return null;
        if (panel.Ring.Group is { } currentGroup && !currentGroup.MatchesSelection(actions.Tactical?.SelectedEntities))
            return new("GROUP ACTION", "CURRENT SELECTION", "UPDATING SELECTION", "");
        if (control == PlayerActionDockControlKind.Page)
            return new(panel.Ring.UsesRing ? "RING NAVIGATION" : "PAGE CARDS", "ACTION NAVIGATION", "CHANGES SELECTION", "CLICK / WHEEL / TAB");
        int index = view.DockIndex;
        if (control != PlayerActionDockControlKind.Item && index != panel.SelectedIndex) return null;
        if (control == PlayerActionDockControlKind.Item &&
            (index < 0 || index >= PlayerActionDockInteractionLayout.GetItemCount(mode, actions))) return null;
        var state = PlayerActionDockHudModel.ResolveItemState(mode, index, actions, ContextualCommandModel.HasPending(panel, snapshot));
        bool item = control == PlayerActionDockControlKind.Item;
        bool enabled = item ? state.CanActivate : PlayerActionDockHudModel.CanUseControl(control, mode, index, panel, actions);
        string title = item ? PlayerActionDockHudModel.ResolveItemTitle(mode, index, actions) :
            ResolveControlTitle(control, mode);
        if (title.Length == 0) return null;
        string status = enabled ? "AVAILABLE AT CAPTURED TICK" :
            !state.CanActivate && control is PlayerActionDockControlKind.Item or PlayerActionDockControlKind.Activate
                ? state.DisabledReason : "UNAVAILABLE IN CURRENT CONTEXT";
        string hint = !enabled ? string.Empty : item ? (panel.Ring.UsesRing ? "CLICK TO ACT / PARAMETERS TO EDIT" : "SELECT CARD / ENTER TO ACT") : "CLICK TO REQUEST";
        IReadOnlyList<PlayerActionResourceAmount>? costs = null, outputs = null;
        uint? ticks = null;
        string requirement = mode == PlayerActionPanelMode.Tactical && panel.Ring.Group is { } selectedGroup && actions.Tactical is { } tactical
              ? $"ELIGIBLE {(index is 5 or 6 ? tactical.Artillery.Count : tactical.CombatEligibleCount)} / SELECTED {selectedGroup.SelectedCount}; AUTHORITATIVE PARTIAL RESULTS APPLY" : string.Empty;
        IReadOnlyList<PlayerTechnologyPrerequisiteReadModel>? prerequisites = null;
        if ((item || control == PlayerActionDockControlKind.Activate) && index >= 0 &&
            index < PlayerActionDockInteractionLayout.GetItemCount(mode, actions))
        {
            switch (mode)
            {
                case PlayerActionPanelMode.Construction:
                    costs = actions.Construction[index].Costs;
                    requirement = actions.Construction[index].RequiresResourceDeposit ? "REQUIRES RESOURCE DEPOSIT" : string.Empty;
                    break;
                case PlayerActionPanelMode.Production when actions.Production is { } production && index < production.Recipes.Count:
                    costs = production.Recipes[index].Inputs;
                    outputs = production.Recipes[index].Outputs;
                    break;
                case PlayerActionPanelMode.UnitProduction when actions.UnitProduction is { } units && index < units.Units.Count:
                    costs = units.Units[index].Costs;
                    ticks = units.Units[index].ProductionTicks;
                    requirement = units.Units[index].TechnologyUnlocked ? string.Empty : "REQUIRES TECHNOLOGY";
                    break;
                case PlayerActionPanelMode.Logistics when actions.Logistics is { } logistics:
                    if (index >= logistics.Policies.Count)
                    {
                        var input = logistics.Requirements[index - logistics.Policies.Count];
                        costs = input.Inputs;
                        requirement = $"{panel.LogisticsSteps} ADDITIONAL STEP(S); ACTIVE RESERVED WORK IS SEPARATE; NO EXTRA BUFFER";
                        hint = "USE INPUTS PREPARES EDITOR; APPLY POLICY FOR EACH RESOURCE";
                    }
                    else
                    {
                        var stock = logistics.Policies[index];
                        requirement = (panel.StockPreview.Length > 0 ? panel.StockPreview + "; " : string.Empty) + LogisticsStatusCatalog.PolicyDetails(logistics, stock) +
                            (logistics.HasEnabledGraphNode ? "; NETWORK NODE ACTIVE; LOCAL ACCESS OBSERVED SEPARATELY" : "; NO ACTIVE NETWORK NODE");
                        status = $"{(stock.HasPolicy ? "POLICY SAVED" : "NO SAVED POLICY")} / SOURCE {(string.IsNullOrEmpty(stock.SourceName) ? "NOT ASSIGNED" : stock.SourceName)} / {(stock.AssignedTruck.IsValid ? "CARGO TRUCK ASSIGNED" : "NO CARGO TRUCK ASSIGNED")}";
                        hint = LogisticsStatusCatalog.Advice(stock.FailureReason);
                        if (stock.FailureReason == LogisticsTransportRequestFailureReason.NoTruckAvailable)
                        {
                            var fleet = logistics.CargoFleet;
                            requirement += $"; OWN CARGO {fleet.Total}, WORKING {fleet.Working}, BOUND {fleet.Bound}, EMPTY IDLE {fleet.EmptyIdle}, NO DRIVE FUEL {fleet.NoDriveFuel}; COUNTS OVERLAP, ROUTE ELIGIBILITY RECHECKED";
                        }
                    }
                    break;
                case PlayerActionPanelMode.Supply when actions.Supply is { } supply:
                    if (supply.IsStaticProvider)
                    {
                        requirement = supply.HasLocalProviderInventory ? $"LOCAL PROVIDER FUEL {supply.LocalProviderFuel:0.##} / AMMUNITION {supply.LocalProviderAmmunition:0.##}" : "LOCAL PROVIDER INVENTORY UNAVAILABLE";
                        hint = "LOGISTICS / APPLY POLICY REPLENISHES LOCAL PROVIDER STOCK"; break;
                    }
                    requirement = $"{LogisticsStatusCatalog.DriveTank(supply)}; {(supply.IsSupplyTruck ? "FIELD CARGO" : "PROVIDER STOCK")} FUEL {(supply.IsSupplyTruck ? supply.CargoFuel : supply.ProviderFuel):0.##} / AMMUNITION {(supply.IsSupplyTruck ? supply.CargoAmmunition : supply.ProviderAmmunition):0.##}";
                    hint = LogisticsStatusCatalog.ServiceDetails(supply.Service);
                    break;
                case PlayerActionPanelMode.Technology:
                    costs = actions.Technology[index].Costs;
                    requirement = actions.Technology[index].RequiredFacilityName;
                    prerequisites = actions.Technology[index].Prerequisites;
                    break;
            }
        }
        string workReason = mode switch
        {
            PlayerActionPanelMode.Production when actions.Production is { } processing => PlayerActionDockHudModel.ResolveProductionBlockLabel(processing.BlockReason),
            PlayerActionPanelMode.UnitProduction when actions.UnitProduction is { } factory => PlayerActionDockHudModel.ResolveUnitProductionBlockLabel(factory.BlockReason),
            _ => string.Empty
        };
        string explanation = (panel.Bindings ?? GameplayBindingRegistry.Default).Text(ProductionContextFeedback.Explain(enabled ? workReason : state.DisabledReason));
        if (mode == PlayerActionPanelMode.Logistics && actions.Logistics is { } logisticsHelp)
        {
            if (index >= 0 && index < logisticsHelp.Policies.Count)
                explanation = "MIN TRIGGERS REFILL USING STOCK + OUTSTANDING; TARGET REFILLS TO; MAX IS SAVED POLICY, NOT A HARD CAP. " +
                    LogisticsStatusCatalog.ServiceDetails(logisticsHelp.Policies[index].Service);
            else explanation = "CATALOG INPUTS; SHARED CAPACITY INCLUDES LOCAL STOCK AND OUTSTANDING DELIVERY DEMAND. PREVIEW CREATES NO ORDERS.";
            if (control is PlayerActionDockControlKind.CycleSecondary or PlayerActionDockControlKind.Increase or PlayerActionDockControlKind.Decrease)
                hint = index >= logisticsHelp.Policies.Count ? "ADJUST ADDITIONAL STEP COUNT WITH - / +" : panel.StockThresholdField switch
                {
                    PlayerStockThresholdField.Minimum => "MIN: REQUEST WHEN STOCK + OUTSTANDING FALLS BELOW THIS VALUE",
                    PlayerStockThresholdField.Maximum => "MAX: VALIDATED AND SAVED; DISPATCHER DOES NOT ENFORCE A HARD CAP",
                    _ => "TARGET: REFILL GOAL; ACTUAL SHARED STORAGE CAPACITY STILL APPLIES"
                };
        }
        return new(title, PlayerActionDockHudModel.ResolveModeLabel(mode), status, hint,
            Costs: costs, Outputs: outputs, ProductionTicks: ticks, Requirement: requirement, Prerequisites: prerequisites,
            WorkStatus: ProductionContextFeedback.WorkLabel(workReason),
            Explanation: explanation,
            VisualState: EconomicCardVisual.Resolve(mode, index, actions, ContextualCommandModel.HasPending(panel, snapshot)).State,
            WrapDetails: mode is PlayerActionPanelMode.Logistics or PlayerActionPanelMode.Supply);
    }

    private static string ResolveControlTitle(PlayerActionDockControlKind control, PlayerActionPanelMode mode)
    {
        if (PlayerActionDockHudModel.ResolveFooterLabel(control, mode).Length == 0) return string.Empty;
        return control switch
        {
            PlayerActionDockControlKind.Activate => "ACTIVATE SELECTED ACTION",
            PlayerActionDockControlKind.Cancel => "CANCEL SELECTED REQUEST",
            PlayerActionDockControlKind.CyclePrimary => "CYCLE PRIORITY",
            PlayerActionDockControlKind.CycleSecondary => mode switch
            {
                PlayerActionPanelMode.Production => "CYCLE PRODUCTION MODE",
                PlayerActionPanelMode.Supply => "TOGGLE AUTOMATIC RESUPPLY",
                _ => "CYCLE STOCK THRESHOLD FIELD"
            },
            PlayerActionDockControlKind.Decrease => "DECREASE SELECTED VALUE",
            PlayerActionDockControlKind.Increase => "INCREASE SELECTED VALUE",
            _ => string.Empty
        };
    }
}
