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
    Increase = 8
}

internal readonly record struct PlayerActionDockHitTarget(
    PlayerActionDockControlKind Kind,
    PlayerActionPanelMode Mode = PlayerActionPanelMode.Closed,
    int ItemIndex = -1);

internal static class PlayerActionDockInteractionLayout
{
    public const int ModeButtonCount = 6;
    public const int FooterButtonCount = 6;

    public static float ModeBarHeight(
        in GameplayHudLayout layout) =>
        36.0f *
        layout.Scale;

    public static float HeaderHeight(
        in GameplayHudLayout layout) =>
        48.0f *
        layout.Scale;

    public static float FooterHeight(
        in GameplayHudLayout layout) =>
        32.0f *
        layout.Scale;

    public static int Columns(
        in GameplayHudLayout layout)
    {
        if (layout.ActionDock.Width >=
            420.0f *
            layout.Scale)
        {
            return 3;
        }

        return layout.ActionDock.Width >=
            260.0f *
            layout.Scale
            ? 2
            : 1;
    }

    public static float CardHeight(
        in GameplayHudLayout layout) =>
        42.0f *
        layout.Scale;

    public static HudRect GetModeBarRect(
        in GameplayHudLayout layout) =>
        new(
            layout.ActionDock.X,
            layout.ActionDock.Y,
            layout.ActionDock.Width,
            MathF.Min(
                ModeBarHeight(
                    layout),
                layout.ActionDock.Height));

    public static HudRect GetHeaderRect(
        in GameplayHudLayout layout)
    {
        HudRect modeBar =
            GetModeBarRect(
                layout);
        float height =
            MathF.Min(
                HeaderHeight(
                    layout),
                MathF.Max(
                    0.0f,
                    layout.ActionDock.Bottom -
                    modeBar.Bottom));

        return new HudRect(
            layout.ActionDock.X,
            modeBar.Bottom,
            layout.ActionDock.Width,
            height);
    }

    public static HudRect GetFooterRect(
        in GameplayHudLayout layout)
    {
        float height =
            MathF.Min(
                FooterHeight(
                    layout),
                layout.ActionDock.Height);

        return new HudRect(
            layout.ActionDock.X,
            layout.ActionDock.Bottom -
                height,
            layout.ActionDock.Width,
            height);
    }

    public static HudRect GetCardsRect(
        in GameplayHudLayout layout)
    {
        HudRect header =
            GetHeaderRect(
                layout);
        HudRect footer =
            GetFooterRect(
                layout);
        float gap =
            5.0f *
            layout.Scale;
        float top =
            header.Bottom +
            gap;
        float bottom =
            MathF.Max(
                top,
                footer.Y -
                gap);

        return new HudRect(
            layout.ActionDock.X +
                gap,
            top,
            MathF.Max(
                0.0f,
                layout.ActionDock.Width -
                    gap *
                    2.0f),
            MathF.Max(
                0.0f,
                bottom -
                    top));
    }

    public static HudRect GetModeButtonRect(
        in GameplayHudLayout layout,
        int index)
    {
        if (index < 0 ||
            index >= ModeButtonCount)
        {
            return default;
        }

        HudRect bar =
            GetModeBarRect(
                layout);
        float gap =
            3.0f *
            layout.Scale;
        float totalGap =
            gap *
            (ModeButtonCount - 1);
        float width =
            MathF.Max(
                0.0f,
                (bar.Width -
                 totalGap) /
                ModeButtonCount);

        return new HudRect(
            bar.X +
                index *
                (width + gap),
            bar.Y,
            width,
            bar.Height);
    }

    public static HudRect GetCardRect(
        in GameplayHudLayout layout,
        int index)
    {
        if (index < 0)
        {
            return default;
        }

        HudRect cards =
            GetCardsRect(
                layout);
        int columns =
            Columns(
                layout);
        float gap =
            4.0f *
            layout.Scale;
        float width =
            MathF.Max(
                0.0f,
                (cards.Width -
                 gap *
                 (columns - 1)) /
                columns);
        float height =
            CardHeight(
                layout);
        int row =
            index /
            columns;
        int column =
            index %
            columns;
        HudRect rect =
            new(
                cards.X +
                    column *
                    (width + gap),
                cards.Y +
                    row *
                    (height + gap),
                width,
                height);

        return rect.Bottom <=
            cards.Bottom +
            0.01f
            ? rect
            : default;
    }

    public static int MaximumVisibleItems(
        in GameplayHudLayout layout)
    {
        HudRect cards =
            GetCardsRect(
                layout);
        float gap =
            4.0f *
            layout.Scale;
        float step =
            CardHeight(
                layout) +
            gap;

        if (cards.IsEmpty ||
            step <= 0.0f)
        {
            return 0;
        }

        int rows =
            Math.Max(
                0,
                (int)MathF.Floor(
                    (cards.Height + gap) /
                    step));

        return rows *
            Columns(
                layout);
    }

    public static HudRect GetFooterButtonRect(
        in GameplayHudLayout layout,
        int index)
    {
        if (index < 0 ||
            index >= FooterButtonCount)
        {
            return default;
        }

        HudRect footer =
            GetFooterRect(
                layout);
        float gap =
            3.0f *
            layout.Scale;
        float totalGap =
            gap *
            (FooterButtonCount - 1);
        float width =
            MathF.Max(
                0.0f,
                (footer.Width -
                 totalGap) /
                FooterButtonCount);

        return new HudRect(
            footer.X +
                index *
                (width + gap),
            footer.Y,
            width,
            footer.Height);
    }

    public static bool CapturesPointer(
        Vector2 pointer,
        in GameplayHudLayout layout,
        bool isOpen)
    {
        if (layout.ActionDock.IsEmpty)
        {
            return false;
        }

        return isOpen
            ? layout.ActionDock.Contains(
                pointer)
            : GetModeBarRect(
                layout).Contains(
                    pointer);
    }

    public static bool TryHit(
        Vector2 pointer,
        in GameplayHudLayout layout,
        bool isOpen,
        int itemCount,
        out PlayerActionDockHitTarget target)
    {
        for (int index = 0;
             index < ModeButtonCount;
             index++)
        {
            if (!GetModeButtonRect(
                    layout,
                    index).Contains(
                    pointer))
            {
                continue;
            }

            target =
                new PlayerActionDockHitTarget(
                    PlayerActionDockControlKind.Mode,
                    ModeForButton(
                        index));
            return true;
        }

        if (!isOpen)
        {
            target = default;
            return false;
        }

        int visibleItems =
            Math.Min(
                Math.Max(
                    itemCount,
                    0),
                MaximumVisibleItems(
                    layout));

        for (int index = 0;
             index < visibleItems;
             index++)
        {
            if (!GetCardRect(
                    layout,
                    index).Contains(
                    pointer))
            {
                continue;
            }

            target =
                new PlayerActionDockHitTarget(
                    PlayerActionDockControlKind.Item,
                    ItemIndex:
                        index);
            return true;
        }

        for (int index = 0;
             index < FooterButtonCount;
             index++)
        {
            if (!GetFooterButtonRect(
                    layout,
                    index).Contains(
                    pointer))
            {
                continue;
            }

            target =
                new PlayerActionDockHitTarget(
                    FooterControl(
                        index));
            return true;
        }

        target = default;
        return false;
    }

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
