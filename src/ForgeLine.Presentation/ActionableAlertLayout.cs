using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct NotificationCardLayout(AlertNotification Item, HudRect Bounds, HudRect Text,
    HudRect Action, HudRect Details, bool Truncated, ulong Revision = 0);

public readonly record struct NotificationStackLayout(HudRect Bounds, float FontSize, int Count,
    NotificationCardLayout First = default, NotificationCardLayout Second = default,
    NotificationCardLayout Third = default, NotificationCardLayout Overflow = default, int Revision = 0)
{
    public NotificationCardLayout Card(int index) => index >= 0 && index < Count ? index switch
    { 0 => First, 1 => Second, 2 => Third, _ => Overflow } : default;
    public int Hit(Vector2 point)
    { for (int i = 0; i < Count; i++) if (Card(i).Bounds.Contains(point)) return i; return -1; }
    public bool Contains(Vector2 point) => Hit(point) >= 0;
}

/// <summary>Bounded copied notifications; the same card geometry drives rendering and input.</summary>
public static class ActionableAlertLayout
{
    public const int ContractVersion = 3;
    public const int MaximumCards = 3;
    public static float Scale(in GameplayHudLayout layout) => Math.Clamp(layout.DockContentScale, 1, 1.5f);
    public static NotificationStackLayout Resolve(PresentationSnapshot? snapshot, in GameplayHudLayout layout, bool expanded = false, string localFeedback = "") =>
        snapshot?.PlayerExperience is { } experience && experience.Tick == snapshot.Tick
            ? Resolve(experience, layout, expanded, ActionableAlertSnapshot.Resolve(snapshot), localFeedback) : default;
    public static NotificationStackLayout Resolve(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout,
        bool expanded = false, ActionableAlertSnapshot? alerts = null, string localFeedback = "")
    {
        localFeedback ??= "";
        var bounds = layout.NotificationBounds(expanded);
        float font = 14 * Scale(layout), padding = 4 * Scale(layout), gap = 4 * Scale(layout);
        float line = UiFontMetrics.Default.Description.LineHeight / UiFontMetrics.Default.Description.PixelSize * font;
        float summary = line + 2 * padding;
        int total = ResourcePowerHudModel.ResolveNotificationCount(experience) + (localFeedback.Length > 0 ? 1 : 0);
        if (bounds.IsEmpty || total == 0 || bounds.Width < 64 || bounds.Height < summary) return new(bounds, font, 0);
        Span<NotificationCardLayout> cards = stackalloc NotificationCardLayout[4];
        cards.Clear();
        var revision = new HashCode();
        revision.Add(experience.Player); revision.Add(experience.Feedback);
        if (alerts is not null)
            for (int a = 0; a < alerts.Alerts.Count; a++) revision.Add(alerts.Alerts[a]);
        Span<char> text = stackalloc char[2048];
        float y = bounds.Y;
        int count = 0, consumed = 0;
        for (int i = 0; i < Math.Min(total, MaximumCards); i++)
        {
            var item = i == ResourcePowerHudModel.ResolveNotificationCount(experience) ? new AlertNotification(default, Local: true) : Ordered(experience, i);
            var value = NotificationText.Format(experience, alerts, item, text, localFeedback);
            float textWidth = MathF.Max(1, bounds.Width - 32 * Scale(layout) - 2 * padding);
            int offset = 0, lines = 0;
            while (offset < value.Length && lines < 2)
            { _ = NotificationText.Line(value[offset..], textWidth, font, out int used); offset += used; lines++; }
            bool truncated = offset < value.Length || value.Length == text.Length;
            float actionHeight = (item.Feedback || item.Local) && !truncated ? 0 : line;
            float height = Math.Max(1, lines) * line + actionHeight + 2 * padding;
            bool moreAfter = i + 1 < total;
            float required = height + (moreAfter ? gap + summary : 0);
            if (y + required > bounds.Bottom) break;
            var rect = new HudRect(bounds.X, y, bounds.Width, height);
            var content = new HudRect(rect.X + 28 * Scale(layout), rect.Y + padding, textWidth, lines * line);
            var action = actionHeight > 0 ? new HudRect(rect.Right - MathF.Min(90 * Scale(layout), rect.Width * .35f),
                rect.Bottom - padding - line, MathF.Min(86 * Scale(layout), rect.Width * .33f), line) : default;
            var details = truncated && !item.Feedback && !item.Local ? new HudRect(rect.X + padding, action.Y, MathF.Min(86 * Scale(layout), rect.Width * .33f), line) : default;
            cards[count++] = new(item, rect, content, action, details, truncated, NotificationText.Revision(value));
            consumed++; y += height + gap;
        }
        if (consumed < total && y + summary <= bounds.Bottom)
        {
            var rect = new HudRect(bounds.X, y, bounds.Width, summary);
            cards[count++] = new(new(default, Hidden: total - consumed), rect,
                new(rect.X + padding, rect.Y + padding, rect.Width - 2 * padding, line), rect, rect, false);
        }
        return new(bounds, font, count, cards[0], cards[1], cards[2], cards[3], revision.ToHashCode());
    }
    internal static AlertNotification Ordered(in PlayerExperienceSnapshot experience, int index)
    {
        int ordinal = 0;
        for (int slot = 0; slot < 6; slot++)
        {
            bool feedback = slot == 3;
            var kind = feedback ? default : AlertLifecycleTracker.Kind(slot < 3 ? slot : slot - 1);
            if (feedback ? !ResourcePowerHudModel.IsCommandFeedbackVisible(experience) : (experience.Alerts & kind) == 0) continue;
            if (ordinal++ == index) return new(kind, feedback);
        }
        return default;
    }
    public static bool Contains(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout, Vector2 point, bool expanded = false) =>
        Resolve(experience, layout, expanded).Contains(point);

}

internal static class NotificationText
{
    public static ReadOnlySpan<char> Format(in PlayerExperienceSnapshot experience, ActionableAlertSnapshot? alerts,
        in AlertNotification item, Span<char> buffer, string localFeedback = "")
    {
        var b = new ResourcePowerHudRenderer.HudTextBuilder(buffer);
        if (item.Hidden > 0) { b.Append("MORE / "); b.Append(item.Hidden); b.Append(" MESSAGES"); }
        else if (item.Local) b.Append(localFeedback);
        else if (item.Feedback)
        {
            b.Append(experience.Feedback.State switch { PlayerCommandFeedbackState.Accepted => "ACCEPTED", PlayerCommandFeedbackState.Partial => "PARTIAL", _ => "REJECTED" });
            b.Append(" / "); b.Append(ResourcePowerHudRenderer.CommandLabel(experience.Feedback.Kind));
            b.Append(" "); b.Append(experience.Feedback.AcceptedTargets); b.Append("/"); b.Append(experience.Feedback.RejectedTargets);
            b.Append(" "); b.Append(ResourcePowerHudRenderer.ResolveFailureLabel(experience.Feedback));
        }
        else
        {
            b.Append(ResourcePowerHudModel.ResolveAlertSeverity(item.Kind) == HudAlertSeverity.Critical ? "CRITICAL / " : "WARNING / ");
            string label = AlertLifecycleTracker.Label(item.Kind);
            if (alerts is not null)
                for (int i = 0; i < alerts.Alerts.Count; i++)
                    if (alerts.Alerts[i].Identity.Kind == item.Kind) { label = alerts.Alerts[i].Label; break; }
            b.Append(label);
            int count = item.Kind == PlayerAlertState.SupplyCritical ? experience.CriticalSupplyUnits :
                item.Kind == PlayerAlertState.ProductionBlocked ? experience.BlockedProductionFacilities : 0;
            if (count > 0) { b.Append(" / "); b.Append(count); }
        }
        return b.Written;
    }
    public static ulong Revision(ReadOnlySpan<char> text)
    { ulong hash = 14695981039346656037; foreach (char c in text) hash = unchecked((hash ^ c) * 1099511628211); return hash; }

    public static ReadOnlySpan<char> Line(ReadOnlySpan<char> text, float width, float size, out int consumed)
    {
        int newline = text.IndexOf('\n');
        var paragraph = newline < 0 ? text : text[..newline];
        int length = UiFontMetrics.Default.FitPrefix(paragraph, width, size, false);
        if (length < paragraph.Length) { int space = paragraph[..length].LastIndexOf(' '); if (space > 0) length = space; }
        consumed = length;
        if (consumed == 0 && paragraph.Length > 0) { int next = 0; _ = UiFontMetrics.Default.Next(paragraph, ref next); consumed = next; }
        while (consumed < text.Length && text[consumed] is ' ' or '\r') consumed++;
        if (consumed < text.Length && text[consumed] == '\n') consumed++;
        return paragraph[..length];
    }
}
