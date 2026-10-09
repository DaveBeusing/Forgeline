using ForgeLine.Game;
using ForgeLine.Intelligence;

namespace ForgeLine.Presentation;

internal readonly record struct HoverTooltipContent(
    string Title, string Role, string Status, string Hint,
    PlayerHoverSummary? World = null,
    IReadOnlyList<PlayerActionResourceAmount>? Costs = null,
    IReadOnlyList<PlayerActionResourceAmount>? Outputs = null,
    uint? ProductionTicks = null,
    string Requirement = "",
    IReadOnlyList<PlayerTechnologyPrerequisiteReadModel>? Prerequisites = null);

internal static class HoverTooltipResolver
{
    public static HoverTooltipContent? Resolve(PresentationSnapshot snapshot, in HoverTooltipView view,
        in PlayerActionPanelView panel)
    {
        if (!view.Ready || view.SessionId != snapshot.SessionId ||
            snapshot.PlayerExperience is not { IsMatchComplete: false }) return null;
        if (view.DockControl != 0) return ResolveDock(snapshot, view, panel);
        if (view.Contact.IsSpecified)
        {
            if (snapshot.Intelligence is not { } intelligence || intelligence.Tick != snapshot.Tick) return null;
            foreach (var contact in intelligence.Contacts)
                if (contact.ContactKey == view.Contact && contact.IsCurrent && contact.State == IntelligenceState.Detected)
                    return new("UNKNOWN CONTACT", "DETECTED", "IDENTITY UNAVAILABLE", "");
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
        return new(world.DisplayName, role, world.ExtractionState, hint, world);
    }

    private static HoverTooltipContent? ResolveDock(PresentationSnapshot snapshot, in HoverTooltipView view,
        in PlayerActionPanelView panel)
    {
        var control = (PlayerActionDockControlKind)view.DockControl;
        var mode = view.DockMode;
        if (control == PlayerActionDockControlKind.Mode)
            return new(PlayerActionDockHudModel.ResolveModeLabel(mode), "ACTION PANEL",
                PlayerActionDockHudModel.ResolveModeShortcut(mode), "CLICK TO OPEN / CLOSE");
        if (!panel.IsOpen || panel.Mode != mode || snapshot.PlayerActions is not { } actions ||
            actions.SessionId != snapshot.SessionId || actions.Tick != snapshot.Tick) return null;
        int index = view.DockIndex;
        if (control != PlayerActionDockControlKind.Item && index != panel.SelectedIndex) return null;
        if (control == PlayerActionDockControlKind.Item &&
            (index < 0 || index >= PlayerActionDockInteractionLayout.GetItemCount(mode, actions))) return null;
        var state = PlayerActionDockHudModel.ResolveItemState(mode, index, actions);
        bool item = control == PlayerActionDockControlKind.Item;
        bool enabled = item ? state.CanActivate : PlayerActionDockHudModel.CanUseControl(control, mode, index, panel, actions);
        string title = item ? PlayerActionDockHudModel.ResolveItemTitle(mode, index, actions) :
            ResolveControlTitle(control, mode);
        if (title.Length == 0) return null;
        string status = enabled ? "AVAILABLE AT CAPTURED TICK" :
            !state.CanActivate && control is PlayerActionDockControlKind.Item or PlayerActionDockControlKind.Activate
                ? state.DisabledReason : "UNAVAILABLE IN CURRENT CONTEXT";
        string hint = !enabled ? string.Empty : item ? "SELECT CARD / ENTER TO ACT" : "CLICK TO REQUEST";
        IReadOnlyList<PlayerActionResourceAmount>? costs = null, outputs = null;
        uint? ticks = null;
        string requirement = string.Empty;
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
                case PlayerActionPanelMode.Technology:
                    costs = actions.Technology[index].Costs;
                    requirement = actions.Technology[index].RequiredFacilityName;
                    prerequisites = actions.Technology[index].Prerequisites;
                    break;
            }
        }
        return new(title, PlayerActionDockHudModel.ResolveModeLabel(mode), status, hint,
            Costs: costs, Outputs: outputs, ProductionTicks: ticks, Requirement: requirement, Prerequisites: prerequisites);
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
