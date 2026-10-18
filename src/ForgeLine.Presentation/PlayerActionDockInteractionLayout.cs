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
    Page = 9
}

internal readonly record struct PlayerActionDockHitTarget(
    PlayerActionDockControlKind Kind,
    PlayerActionPanelMode Mode = PlayerActionPanelMode.Closed,
    int ItemIndex = -1);

internal static class PlayerActionDockInteractionLayout
{
    public const int ModeButtonCount = 7;
    public const int FooterButtonCount = 6;

    // Fit dock content inside its existing region, independently of the surrounding HUD.
    public static float Scale(in GameplayHudLayout layout) =>
        MathF.Max(0, MathF.Min(layout.Scale, MathF.Min(layout.ActionDock.Width / 520, layout.ActionDock.Height / 230)));

    public static bool Compact(in GameplayHudLayout layout) => layout.ActionDock.Height < 280 * Scale(layout);
    public static float ModeBarHeight(in GameplayHudLayout layout) => (Compact(layout) ? 42 : 46) * Scale(layout);
    public static float HeaderHeight(in GameplayHudLayout layout) => (Compact(layout) ? 36 : 64) * Scale(layout);
    public static float FooterHeight(in GameplayHudLayout layout) => (Compact(layout) ? 76 : 84) * Scale(layout);
    public static float CardHeight(in GameplayHudLayout layout) => (Compact(layout) ? 52 : 60) * Scale(layout);
    public static int Columns(in GameplayHudLayout layout) => layout.ActionDock.Width >= 480 * Scale(layout) ? 2 : 1;
    public static HudRect GetModeBarRect(in GameplayHudLayout layout) =>
        new(layout.ActionDock.X, layout.ActionDock.Y, layout.ActionDock.Width, ModeBarHeight(layout));
    public static HudRect GetCollapsedRect(in GameplayHudLayout layout) =>
        GetModeBarRect(layout) with { Height = MathF.Min(layout.ActionDock.Height, 68 * Scale(layout)) };
    public static HudRect GetHeaderRect(in GameplayHudLayout layout) =>
        new(layout.ActionDock.X, GetModeBarRect(layout).Bottom, layout.ActionDock.Width, HeaderHeight(layout));
    public static HudRect GetFooterRect(in GameplayHudLayout layout) =>
        new(layout.ActionDock.X, layout.ActionDock.Bottom - FooterHeight(layout), layout.ActionDock.Width, FooterHeight(layout));
    public static HudRect GetCardsRect(in GameplayHudLayout layout)
    {
        float gap = 5 * Scale(layout);
        float top = GetHeaderRect(layout).Bottom + gap;
        return new(layout.ActionDock.X + gap, top, MathF.Max(0, layout.ActionDock.Width - 2 * gap),
            MathF.Max(0, GetFooterRect(layout).Y - gap - top));
    }
    public static HudRect GetModeButtonRect(in GameplayHudLayout layout, int index)
    {
        if (index is < 0 or >= ModeButtonCount) return default;
        var bar = GetModeBarRect(layout); float gap = 3 * Scale(layout);
        float width = MathF.Max(0, (bar.Width - gap * (ModeButtonCount - 1)) / ModeButtonCount);
        return new(bar.X + index * (width + gap), bar.Y, width, bar.Height);
    }
    public static int MaximumVisibleItems(in GameplayHudLayout layout)
    {
        var cards = GetCardsRect(layout); float gap = 4 * Scale(layout);
        float step = CardHeight(layout) + gap;
        return cards.IsEmpty || step <= 0 ? 0 : (int)((cards.Height + gap) / step) * Columns(layout);
    }
    public static int FirstVisibleItem(in GameplayHudLayout layout, int selectedIndex, int itemCount)
    {
        int capacity = MaximumVisibleItems(layout);
        return capacity <= 0 || itemCount <= 0 ? 0 : Math.Clamp(selectedIndex, 0, itemCount - 1) / capacity * capacity;
    }
    public static HudRect GetCardRect(in GameplayHudLayout layout, int index, int firstVisible = 0)
    {
        int local = index - firstVisible; int columns = Columns(layout);
        if (local < 0 || local >= MaximumVisibleItems(layout)) return default;
        var cards = GetCardsRect(layout); float gap = 4 * Scale(layout);
        float width = MathF.Max(0, (cards.Width - gap * (columns - 1)) / columns);
        return new(cards.X + local % columns * (width + gap),
            cards.Y + local / columns * (CardHeight(layout) + gap), width, CardHeight(layout));
    }
    public static HudRect GetPageButtonRect(in GameplayHudLayout layout, int index)
    {
        if (index is < 0 or > 1) return default;
        var header = GetHeaderRect(layout); float scale = Scale(layout);
        return new(header.Right - (50 - index * 24) * scale, header.Y + 4 * scale, 22 * scale, 22 * scale);
    }
    public static HudRect GetFooterButtonRect(in GameplayHudLayout layout, int index)
    {
        if (index is < 0 or >= FooterButtonCount) return default;
        var footer = GetFooterRect(layout); float gap = 3 * Scale(layout);
        float width = (footer.Width - 2 * gap) / 3;
        float height = (footer.Height - gap) / 2;
        return new(footer.X + index % 3 * (width + gap), footer.Y + index / 3 * (height + gap), width, height);
    }
    public static bool CapturesPointer(Vector2 pointer, in GameplayHudLayout layout, bool isOpen, int itemCount = 0, int selectedIndex = 0, PlayerActionPanelMode mode = PlayerActionPanelMode.Production,
        PlayerActionSnapshot? actions = null) =>
        TryHit(pointer, layout, isOpen, itemCount, out _, selectedIndex, mode, actions);

    public static bool TryHit(Vector2 pointer, in GameplayHudLayout layout, bool isOpen, int itemCount,
        out PlayerActionDockHitTarget target, int selectedIndex = 0,
        PlayerActionPanelMode mode = PlayerActionPanelMode.Production, PlayerActionSnapshot? actions = null)
    {
        for (int i = 0; i < ModeButtonCount; i++)
            if (GetModeButtonRect(layout, i).Contains(pointer))
            { target = new(PlayerActionDockControlKind.Mode, ModeForButton(i)); return true; }
        if (!isOpen) { target = default; return false; }
        int first = FirstVisibleItem(layout, selectedIndex, itemCount);
        int capacity = MaximumVisibleItems(layout);
        if (capacity > 0 && itemCount > capacity)
            for (int i = 0; i < 2; i++)
            {
                int destination = i == 0 ? first - capacity : first + capacity;
                if (destination >= 0 && destination < itemCount && GetPageButtonRect(layout, i).Contains(pointer))
                { target = new(PlayerActionDockControlKind.Page, ItemIndex: destination); return true; }
            }
        int end = Math.Min(Math.Max(0, itemCount), first + MaximumVisibleItems(layout));
        for (int i = first; i < end; i++)
            if (GetCardRect(layout, i, first).Contains(pointer))
            { target = new(PlayerActionDockControlKind.Item, ItemIndex: i); return true; }
        for (int i = 0; i < FooterButtonCount; i++)
            if ((i == 0 ? PlayerActionDockHudModel.ResolvePrimaryActionLabel(mode, selectedIndex, actions) :
                PlayerActionDockHudModel.ResolveFooterLabel(FooterControl(i), mode)).Length > 0 &&
                GetFooterButtonRect(layout, i).Contains(pointer))
            { target = new(FooterControl(i)); return true; }
        target = default; return false;
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
                actions?.Logistics?.ItemCount ??
                0,
            PlayerActionPanelMode.Supply =>
                actions?.Supply is null
                    ? 0
                    : 3,
            PlayerActionPanelMode.Tactical =>
                actions?.Tactical is null
                    ? 0
                    : 8,
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
