using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal readonly record struct ContextualCommand(
    PlayerActionPanelMode Mode, int ItemIndex, string Label, string Shortcut,
    PlayerActionDockItemState Availability)
{
    public bool OpensMode => ItemIndex < 0;
}

internal static class ContextualCommandModel
{
    public static PlayerActionSnapshot? ResolveActions(PresentationSnapshot? snapshot) =>
        PlayerActionDockHudModel.ResolveActions(snapshot) is { } actions &&
        snapshot!.PlayerExperience is { IsMatchComplete: false } experience && experience.Tick == snapshot.Tick
            ? actions : null;

    public static string Status(PresentationSnapshot? snapshot)
    {
        if (snapshot?.PlayerExperience is { IsMatchComplete: true }) return "MATCH COMPLETE";
        if (ResolveActions(snapshot) is null) return "WAITING FOR COMMAND DATA";
        if (snapshot!.PlayerExperience!.Value.Selection.Count == 0) return "SELECT UNIT OR BUILDING";
        return snapshot.PlayerExperience.Value.Selection.Kind == PlayerSelectionKind.Mixed
            ? "MIXED SELECTION - ELIGIBLE UNITS ONLY" : "SELECTION COMMANDS";
    }

    public static int Count(PresentationSnapshot? snapshot)
    {
        int count = 0;
        while (TryGet(snapshot, count, out _)) count++;
        return count;
    }

    public static bool TryGet(PresentationSnapshot? snapshot, int index, out ContextualCommand command)
    {
        command = default;
        var actions = ResolveActions(snapshot);
        if (index < 0 || actions is null || snapshot!.PlayerExperience is not { } experience ||
            experience.Selection.Count == 0) return false;

        if (actions.Tactical is { CombatEligibleCount: > 0 })
        {
            if (index >= 6) return false;
            command = new(PlayerActionPanelMode.Tactical, index,
                PlayerActionDockHudModel.ResolveItemTitle(PlayerActionPanelMode.Tactical, index, actions),
                "K / TAB / ENTER",
                actions.PendingCommandCount > 0 ? PlayerActionDockItemState.Disabled("REQUEST PENDING") :
                    PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Tactical, index, actions));
            return true;
        }

        PlayerActionPanelMode mode = PlayerActionPanelMode.Closed;
        if (experience.Selection.CommonBuildingId == BuildingIds.CommandCore && actions.Construction.Count > 0 && index-- == 0)
            mode = PlayerActionPanelMode.Construction;
        else if (actions.UnitProduction is not null && index-- == 0) mode = PlayerActionPanelMode.UnitProduction;
        else if (actions.Production is not null && index-- == 0) mode = PlayerActionPanelMode.Production;
        else if (actions.Logistics is not null && index-- == 0) mode = PlayerActionPanelMode.Logistics;
        else if (actions.Supply is not null && index-- == 0) mode = PlayerActionPanelMode.Supply;
        if (mode == PlayerActionPanelMode.Closed) return false;
        command = new(mode, -1, PlayerActionDockHudModel.ResolveModeLabel(mode),
            PlayerActionDockHudModel.ResolveModeShortcut(mode), PlayerActionDockItemState.Enabled);
        return true;
    }

    public static float TextScale(in GameplayHudLayout layout) =>
        MathF.Min(layout.Scale, MathF.Min(layout.PrimaryCommands.Width / 360, layout.PrimaryCommands.Height / 112));

    public static HudRect Button(in GameplayHudLayout layout, int index)
    {
        if (index < 0 || index >= 6 || layout.PrimaryCommands.IsEmpty) return default;
        HudRect region = layout.PrimaryCommands;
        float scale = TextScale(layout);
        float gap = 4 * scale;
        float width = (region.Width - 4 * gap) / 3;
        return new(region.X + gap + index % 3 * (width + gap),
            region.Y + 24 * scale + index / 3 * 34 * scale, width, 30 * scale);
    }

    public static bool TryHit(Vector2 pointer, PresentationSnapshot? snapshot,
        in GameplayHudLayout layout, out ContextualCommand command)
    {
        for (int i = 0; TryGet(snapshot, i, out command); i++)
            if (Button(layout, i).Contains(pointer)) return true;
        command = default;
        return false;
    }
}
