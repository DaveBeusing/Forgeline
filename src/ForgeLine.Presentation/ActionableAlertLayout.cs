using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public readonly record struct NotificationCardLayout(AlertNotification Item, HudRect Bounds, HudRect Text,
    HudRect Action, HudRect Details, bool Truncated, ulong Revision = 0, AlertIdentity Identity = default,
    HudRect ExpansionBounds = default, bool ExpansionActive = false)
{
    public HudRect InteractiveBounds => !ExpansionActive || ExpansionBounds.IsEmpty ? Bounds :
        Bounds with { Height = ExpansionBounds.Bottom - Bounds.Y };
}

public readonly record struct NotificationStackLayout(HudRect Bounds, float FontSize, int Count,
    NotificationCardLayout First = default, NotificationCardLayout Second = default,
    NotificationCardLayout Third = default, NotificationCardLayout Overflow = default, int Revision = 0)
{
    public HudRect PaintedBounds => Count==0 ? default : Bounds with {Height=Card(Count-1).InteractiveBounds.Bottom-Bounds.Y};
    public NotificationCardLayout Card(int index) => index >= 0 && index < Count ? index switch
    { 0 => First, 1 => Second, 2 => Third, _ => Overflow } : default;
    public int Hit(Vector2 point)
    { for (int i = 0; i < Count; i++) if (Card(i).InteractiveBounds.Contains(point)) return i; return -1; }
    public bool Contains(Vector2 point) => Hit(point) >= 0;
}

/// <summary>Bounded copied notifications; the same card geometry drives rendering and input.</summary>
public static class ActionableAlertLayout
{
    public const int ContractVersion = 5;
    public const int MaximumCards = 3;
    public static float Scale(in GameplayHudLayout layout) => MathF.Max(1, layout.NotificationScale);
    public static NotificationStackLayout Resolve(PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        bool expanded = false, string localFeedback = "", NotificationExpansion expansion = default) =>
        snapshot?.PlayerExperience is { } experience && experience.Tick == snapshot.Tick
            ? Resolve(experience, layout, expanded, ActionableAlertSnapshot.Resolve(snapshot), localFeedback, expansion) : default;
    public static NotificationStackLayout Resolve(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout,
        bool expanded = false, ActionableAlertSnapshot? alerts = null, string localFeedback = "",
        NotificationExpansion expansion = default)
    {
        localFeedback ??= "";
        var bounds = layout.NotificationBounds(expanded);
        float scale = Scale(layout), font = 14 * scale, padding = 4 * scale, gap = 4 * scale;
        float line = UiFontMetrics.Default.Description.LineHeight / UiFontMetrics.Default.Description.PixelSize * font;
        float summary = line + 2 * padding;
        int total = ResourcePowerHudModel.ResolveNotificationCount(experience) + (localFeedback.Length > 0 ? 1 : 0);
        if (bounds.IsEmpty || total == 0 || bounds.Width < 64 || bounds.Height < summary) return new(bounds, font, 0);
        Span<NotificationCardLayout> cards = stackalloc NotificationCardLayout[4];
        cards.Clear();
        var revision = new HashCode();
        revision.Add(experience.Player); revision.Add(experience.Alerts); revision.Add(experience.Feedback);
        revision.Add(experience.CriticalSupplyUnits); revision.Add(experience.BlockedProductionFacilities);
        revision.Add(localFeedback);
        if (alerts is not null)
            for (int a = 0; a < alerts.Alerts.Count; a++) revision.Add(alerts.Alerts[a]);
        Span<char> text = stackalloc char[2048];
        float y = bounds.Y;
        int count = 0, consumed = 0;
        // Reserve the regular rows first: expanding one card must never make a
        // subsequent actionable row or overflow card disappear.
        int normalRows = Math.Min(total, MaximumCards) + (total > MaximumCards ? 1 : 0);
        float freeHeight = MathF.Max(0, bounds.Height -
            (normalRows * summary + MathF.Max(0, normalRows - 1) * gap));
        float detailBudget = MathF.Min(126 * scale, freeHeight);
        for (int i = 0; i < Math.Min(total, MaximumCards); i++)
        {
            var item = i == ResourcePowerHudModel.ResolveNotificationCount(experience) ? new AlertNotification(default, Local: true) : Ordered(experience, i);
            var value = NotificationText.Format(experience, alerts, item, text, localFeedback);
            float height = summary;
            float rail = 5 * scale, icon = font, controlWidth = MathF.Min(86 * scale, MathF.Max(28 * scale, bounds.Width * .23f));
            float textStart = bounds.X + rail + padding + icon + gap;
            float available = MathF.Max(1, bounds.Right - padding - textStart);
            bool actionRequired = !item.Feedback && !item.Local;
            float reserved = actionRequired ? controlWidth + gap : 0;
            float textWidth = MathF.Max(1, available - reserved);
            bool truncated = NotificationText.Truncated(value, textWidth, font);
            bool separateDetails = truncated && actionRequired &&
                available >= 2 * (controlWidth + gap) + 16 * scale;
            if (separateDetails)
                textWidth = MathF.Max(1, available - 2 * (controlWidth + gap));
            else if (truncated)
                textWidth = MathF.Max(1, available - controlWidth - gap);
            truncated = NotificationText.Truncated(value, textWidth, font);
            float required = height + (i + 1 < total ? gap + summary : 0);
            if (y + required > bounds.Bottom) break;
            var rect = new HudRect(bounds.X, y, bounds.Width, height);
            var content = new HudRect(textStart, rect.Y + (height - line) * .5f, textWidth, line);
            var action = actionRequired || truncated
                ? new HudRect(rect.Right - padding - controlWidth, content.Y, controlWidth, line) : default;
            var details = truncated && actionRequired
                ? separateDetails ? new HudRect(action.X - gap - controlWidth, content.Y, controlWidth, line)
                    : action
                : default;
            AlertIdentity identity = default;
            if (alerts is not null && !item.Feedback && !item.Local)
                for (int a = 0; a < alerts.Alerts.Count; a++)
                    if (alerts.Alerts[a].Identity.Kind == item.Kind)
                    { identity = alerts.Alerts[a].Identity; break; }
            ulong textRevision = NotificationText.Revision(value);
            HudRect expandedBounds = default;
            if (expansion.Open && item.Hidden == 0 && expansion.Item == item &&
                expansion.Identity == identity && expansion.Revision == textRevision &&
                detailBudget >= line + 2 * padding)
            {
                float visibleHeight = detailBudget * Math.Clamp(expansion.Progress, 0, 1);
                if (visibleHeight > 0)
                    expandedBounds = new HudRect(rect.X, rect.Bottom, rect.Width, visibleHeight);
            }
            cards[count++] = new(item, rect, content, action, details, truncated,
                textRevision, identity, expandedBounds, expansion.Open);
            consumed++; y += height + (expandedBounds.IsEmpty ? 0 : expandedBounds.Height) + gap;
        }
        if (consumed < total && y + summary <= bounds.Bottom)
        {
            var rect = new HudRect(bounds.X, y, bounds.Width, summary);
            var representative = consumed == ResourcePowerHudModel.ResolveNotificationCount(experience)
                ? new AlertNotification(default, Local: true) : Ordered(experience, consumed);
            for (int i = consumed + 1; i < total; i++)
            {
                var candidate = i == ResourcePowerHudModel.ResolveNotificationCount(experience)
                    ? new AlertNotification(default, Local: true) : Ordered(experience, i);
                if (NotificationText.Severity(experience, candidate) > NotificationText.Severity(experience, representative)) representative = candidate;
            }
            cards[count++] = new(representative with { Hidden = total - consumed }, rect,
                new(rect.X + 5 * scale + padding + font + gap, rect.Y + (rect.Height - line) * .5f,
                    MathF.Max(1, rect.Width - 5 * scale - 2 * padding - font - gap), line), rect, rect, false);
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
        if (item.Hidden > 0) { var severity = Severity(experience, item);
            if (severity == HudAlertSeverity.Critical) b.Append("CRITICAL / ");
            else if (severity == HudAlertSeverity.Warning) b.Append("WARNING / ");
            b.Append("MORE / "); b.Append(item.Hidden); b.Append(" MESSAGES"); }
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
    public static HudAlertSeverity Severity(in PlayerExperienceSnapshot experience, in AlertNotification item) =>
        item.Local ? HudAlertSeverity.Information : item.Feedback ? experience.Feedback.State switch
        { PlayerCommandFeedbackState.Rejected => HudAlertSeverity.Critical, PlayerCommandFeedbackState.Partial => HudAlertSeverity.Warning, _ => HudAlertSeverity.Information }
        : ResourcePowerHudModel.ResolveAlertSeverity(item.Kind);

    public static bool Truncated(ReadOnlySpan<char> text, float width, float size)
    {
        if (text.IndexOfAny('\r', '\n') >= 0) return true;
        return UiFontMetrics.Default.FitPrefix(text, MathF.Max(1, width), size, false) < text.Length;
    }

    public static ReadOnlySpan<char> Ellipsized(ReadOnlySpan<char> text, float width, float size, Span<char> output)
    {
        int newline = text.IndexOfAny('\r', '\n');
        var first = newline >= 0 ? text[..newline] : text;
        bool clipped = newline >= 0 || UiFontMetrics.Default.FitPrefix(first, MathF.Max(1, width), size, false) < first.Length;
        float allowance = clipped ? MathF.Max(0, width - UiFontMetrics.Default.Measure("…", size)) : width;
        int count = Math.Min(output.Length - (clipped ? 1 : 0), UiFontMetrics.Default.FitPrefix(first, allowance, size, false));
        first[..count].CopyTo(output);
        if (clipped) output[count++] = '…';
        return output[..count];
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
