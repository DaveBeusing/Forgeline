using System.Text;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>One bounded detail cache owned by the existing Operations surface.</summary>
internal sealed class NotificationDetailText
{
    private readonly string?[] _labels = new string?[5];
    private readonly string[] _lines = new string[6];
    private string _content = "";
    private int _page = -1;
    public int PageCount { get; private set; } = 1;
    private PlayerExperienceSnapshot _experience;
    private float _width, _size;
    private string _local = "";
    private ulong _session;
    public IReadOnlyList<string> Lines => _lines;
    public void Update(PresentationSnapshot snapshot, float width, float size, string local, int page = 0)
    {
        local ??= "";
        var experience = snapshot.PlayerExperience ?? default;
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        bool changed = _session != snapshot.SessionId.Value || _width != width || _size != size || _local != local ||
            _experience.Alerts != experience.Alerts || _experience.Feedback != experience.Feedback ||
            _experience.CriticalSupplyUnits != experience.CriticalSupplyUnits ||
            _experience.BlockedProductionFacilities != experience.BlockedProductionFacilities ||
            ResourcePowerHudModel.IsCommandFeedbackVisible(_experience) != ResourcePowerHudModel.IsCommandFeedbackVisible(experience);
        for (int i = 0; i < 5; i++)
        {
            string? label = alerts is not null && i < alerts.Alerts.Count ? alerts.Alerts[i].Label : null;
            changed |= _labels[i] != label; _labels[i] = label;
        }
        _experience = experience; _session = snapshot.SessionId.Value; _width = width; _size = size; _local = local;
        if (!changed && _page == page) return;
        if (changed)
        {
            var text = new StringBuilder();
            Span<char> buffer = stackalloc char[2048];
            int notifications = ResourcePowerHudModel.ResolveNotificationCount(experience);
            for (int i = 0; i < notifications; i++)
            {
                var item = ActionableAlertLayout.Ordered(experience, i);
                if (!item.Feedback && alerts is not null)
                {
                    string label = AlertLifecycleTracker.Label(item.Kind);
                    for (int a = 0; a < alerts.Alerts.Count; a++) if (alerts.Alerts[a].Identity.Kind == item.Kind) label = alerts.Alerts[a].Label;
                    text.AppendLine(label);
                }
                else text.AppendLine(NotificationText.Format(experience, alerts, item, buffer).ToString());
            }
            if (local.Length > 0) text.AppendLine(local);
            _content = text.Length > 0 ? text.ToString() : "NO CURRENT NOTIFICATIONS";
        }
        _page = page;
        int count = 0, offset = 0;
        while (offset < _content.Length)
        { _ = NotificationText.Line(_content.AsSpan(offset), MathF.Max(1, width), size, out int used); offset += used; count++; }
        PageCount = Math.Max(1, (count + 5) / 6);
        int first = Math.Max(0, page) % PageCount * 6;
        Array.Fill(_lines, ""); offset = 0;
        for (int i = 0; i < first + 6 && offset < _content.Length; i++)
        {
            var line = NotificationText.Line(_content.AsSpan(offset), MathF.Max(1, width), size, out int used);
            if (i >= first) _lines[i - first] = line.ToString();
            offset += used;
        }
    }
}
