using System.Numerics;

namespace ForgeLine.Presentation;

internal enum PlayerActionDockControlKind : byte
{
    None = 0,
    Mode = 1,
    Item = 2,
    Activate = 3,
    Cancel = 4,
    CyclePrimary = 5,
    CycleSecondary = 6,
    Decrease = 7,
    Increase = 8,
    Page = 9,
    DecreaseMinimum = 10, IncreaseMinimum = 11, DecreaseMaximum = 12, IncreaseMaximum = 13
}

internal readonly record struct PlayerActionDockHitTarget(
    PlayerActionDockControlKind Kind,
    PlayerActionPanelMode Mode = PlayerActionPanelMode.Closed,
    int ItemIndex = -1);

internal static class PlayerActionDockInteractionLayout
{
    public static int GetItemCount(
        PlayerActionPanelMode mode,
        PlayerActionSnapshot? actions) =>
        mode switch
        {
            PlayerActionPanelMode.Construction =>
                actions?.Construction.Count ??
                0,
            PlayerActionPanelMode.Production =>
                actions?.Production is
                    PlayerProductionFacilityActionReadModel production
                    ? production.QueueRecipes.Count +
                      production.Requests.Count
                    : 0,
            PlayerActionPanelMode.UnitProduction =>
                actions?.UnitProduction is
                    PlayerUnitProductionFacilityActionReadModel units
                    ? units.Units.Count +
                      units.Requests.Count
                    : 0,
            PlayerActionPanelMode.Logistics =>
                actions?.BridgeRepairs.Count > 0 ? actions.BridgeRepairs.Count : actions?.ConveyorRouting is not null ? 3 : actions?.Logistics?.ItemCount ??
                0,
            PlayerActionPanelMode.Supply =>
                actions?.Supply is null
                    ? 0
                    : 3,
            PlayerActionPanelMode.Tactical =>
                actions?.Tactical is null
                    ? 0
                    : 11,
            PlayerActionPanelMode.Technology =>
                actions?.Technology.Count ??
                0,
            _ =>
                0
        };

    public static PlayerActionPanelMode ModeForButton(
        int index) =>
        index switch
        {
            0 =>
                PlayerActionPanelMode.Construction,
            1 =>
                PlayerActionPanelMode.Production,
            2 =>
                PlayerActionPanelMode.UnitProduction,
            3 =>
                PlayerActionPanelMode.Logistics,
            4 =>
                PlayerActionPanelMode.Supply,
            5 =>
                PlayerActionPanelMode.Tactical,
            6 =>
                PlayerActionPanelMode.Technology,
            _ =>
                PlayerActionPanelMode.Closed
        };

    public static PlayerActionDockControlKind FooterControl(
        int index) =>
        index switch
        {
            0 =>
                PlayerActionDockControlKind.Activate,
            1 =>
                PlayerActionDockControlKind.Cancel,
            2 =>
                PlayerActionDockControlKind.CyclePrimary,
            3 =>
                PlayerActionDockControlKind.CycleSecondary,
            4 =>
                PlayerActionDockControlKind.Decrease,
            5 =>
                PlayerActionDockControlKind.Increase,
            _ =>
                PlayerActionDockControlKind.None
        };
}
