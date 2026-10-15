using ForgeLine.Assets;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;

namespace ForgeLine.Presentation;

/// <summary>Copied work state is independent from command availability. No retained entity targets.</summary>
internal readonly record struct EconomicCardVisual(UiSkinState State, string Label, double? Progress = null)
{
    internal static EconomicCardVisual Resolve(PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions, bool pending)
    {
        if (actions is null || index < 0) return new(UiSkinState.Disabled, "NO DATA");
        // An in-flight command never declares work started or completed.
        if (pending || actions.PendingCommandCount > 0) return new(UiSkinState.Pending, "REQUEST PENDING");
        if (mode == PlayerActionPanelMode.Production && actions.Production is { } p && index >= p.Recipes.Count && index < p.Recipes.Count + p.Requests.Count)
        {
            var job = p.Requests[index - p.Recipes.Count];
            if (job.Paused) return new(UiSkinState.Warning, "QUEUED / PAUSED");
            if (!job.IsActive) return new(UiSkinState.Normal, "QUEUED");
            if (p.BlockReason != ProductionBlockReason.None)
                return new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveProductionBlockLabel(p.BlockReason), p.Progress);
            return p.Status == ProductionStatus.Running ? new(UiSkinState.Selected, "STARTED", p.Progress) : new(UiSkinState.Normal, "QUEUED", p.Progress);
        }
        if (mode == PlayerActionPanelMode.UnitProduction && actions.UnitProduction is { } u && index >= u.Units.Count && index < u.Units.Count + u.Requests.Count)
        {
            var job = u.Requests[index - u.Units.Count];
            if (job.Paused) return new(UiSkinState.Warning, "QUEUED / PAUSED");
            if (!job.IsActive) return new(UiSkinState.Normal, "QUEUED");
            if (u.BlockReason != UnitProductionBlockReason.None)
                return new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveUnitProductionBlockLabel(u.BlockReason), u.Progress);
            return u.Status == UnitProductionStatus.Running ? new(UiSkinState.Selected, "STARTED", u.Progress) : new(UiSkinState.Normal, "QUEUED", u.Progress);
        }
        if (mode == PlayerActionPanelMode.Technology && index < actions.Technology.Count)
        {
            var t = actions.Technology[index];
            return t.State switch
            {
                PlayerTechnologyState.Completed => new(UiSkinState.Accepted, "COMPLETED", 1),
                PlayerTechnologyState.Researching => new(UiSkinState.Selected, "RESEARCHING", t.Progress),
                PlayerTechnologyState.Locked or PlayerTechnologyState.Blocked => new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveTechnologyBlockLabel(t.BlockReason), t.ActiveRequest.IsValid ? t.Progress : null),
                _ => new(UiSkinState.Normal, "AVAILABLE")
            };
        }
        if (mode == PlayerActionPanelMode.Logistics && actions.Logistics is { } logistics && index < logistics.Policies.Count)
        {
            var stock = logistics.Policies[index];
            if (stock.FailureReason != LogisticsTransportRequestFailureReason.None)
                return new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveTransportFailureLabel(stock.FailureReason));
            if (stock.BottleneckReason != LogisticsBottleneckReason.None)
                return new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveBottleneckLabel(stock.BottleneckReason));
        }
        if (mode == PlayerActionPanelMode.Supply && actions.Supply is { } supply)
        {
            if (index == 2 && supply.ProviderState is PlayerSupplyProviderState.Empty or PlayerSupplyProviderState.Blocked)
                return new(UiSkinState.Warning, PlayerActionDockHudModel.ResolveSupplyProviderStateLabel(supply.ProviderState));
            if (index == 0 && supply.FuelFraction <= 0) return new(UiSkinState.Warning, "FUEL EMPTY");
            if (index == 1 && supply.AmmunitionFraction <= 0) return new(UiSkinState.Warning, "AMMUNITION EMPTY");
        }
        var availability = PlayerActionDockHudModel.ResolveItemState(mode, index, actions);
        return availability.CanActivate ? new(UiSkinState.Normal, "") : new(UiSkinState.Disabled, availability.DisabledReason);
    }

    internal static (PlayerSelectionKind Kind, uint Id) Portrait(PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions)
    {
        if (actions is null || index < 0) return default;
        if (mode == PlayerActionPanelMode.Construction && index < actions.Construction.Count)
            return (PlayerSelectionKind.Building, actions.Construction[index].BuildingId.Value);
        if (mode == PlayerActionPanelMode.UnitProduction && actions.UnitProduction is { } u)
        {
            if (index < u.Units.Count) return (PlayerSelectionKind.Unit, u.Units[index].UnitId.Value);
            if (index < u.Units.Count + u.Requests.Count) return (PlayerSelectionKind.Unit, u.Requests[index - u.Units.Count].UnitId.Value);
        }
        return default;
    }
    internal static RtsUiIcon Icon(PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions)
    {
        if (actions is null || index < 0) return RtsUiIcon.StatusAlert;
        // Explicit TechnologyId bindings from artwork.catalog.json. Unknown future identities stay neutral.
        if (mode == PlayerActionPanelMode.Technology && index < actions.Technology.Count)
            return actions.Technology[index].TechnologyId.Value switch
            {
                1 => RtsUiIcon.BuildingProcessing, 2 => RtsUiIcon.UnitLogistics,
                3 => RtsUiIcon.UnitArmor, 4 => RtsUiIcon.UnitReconnaissance, _ => RtsUiIcon.StatusAlert
            };
        return PlayerActionDockHudModel.ResolveItemIcon(mode, index, actions);
    }
}
