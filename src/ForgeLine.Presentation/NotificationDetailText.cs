using System.Text;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>One bounded detail cache owned by the existing Operations surface.</summary>
internal sealed class NotificationDetailText
{
    private readonly string?[] _labels = new string?[6];
    private CargoFleetCapacityObservation? _fleet;
    private readonly string[] _lines = new string[6];
    private string _content = "";
    private int _page = -1, _pageSize;
    public int PageCount { get; private set; } = 1;
    private PlayerExperienceSnapshot _experience;
    private float _width, _size;
    private string _local = "";
    private ulong _session;
    private OperationsCauseSnapshot? _causes;
    public IReadOnlyList<string> Lines => _lines;
    public void Update(PresentationSnapshot snapshot, float width, float size, string local, int page = 0, int pageSize = 6)
    {
        local ??= "";
        pageSize = Math.Clamp(pageSize, 1, 6);
        var experience = snapshot.PlayerExperience ?? default;
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        var causes = OperationsCauseSnapshot.Resolve(snapshot);
        bool causesChanged = !OperationsCauseSnapshot.SameMembership(_causes, causes);
        _causes = causes;
        bool changed = causesChanged || _session != snapshot.SessionId.Value || _width != width || _size != size || _local != local ||
            _experience.Alerts != experience.Alerts || _experience.Feedback != experience.Feedback ||
            _experience.CriticalSupplyUnits != experience.CriticalSupplyUnits ||
            _experience.BlockedProductionFacilities != experience.BlockedProductionFacilities ||
            ResourcePowerHudModel.IsCommandFeedbackVisible(_experience) != ResourcePowerHudModel.IsCommandFeedbackVisible(experience);
        CargoFleetCapacityObservation? currentFleet = null;
        if (alerts is not null)
            for (int i = 0; i < alerts.Alerts.Count; i++)
                if (alerts.Alerts[i].Identity.Kind == PlayerAlertState.CargoFleetShortage)
                    currentFleet = alerts.Alerts[i].Fleet;
        changed |= currentFleet != _fleet;
        _fleet = currentFleet;
        for (int i = 0; i < 6; i++)
        {
            string? label = alerts is not null && i < alerts.Alerts.Count ? alerts.Alerts[i].Label : null;
            changed |= _labels[i] != label; _labels[i] = label;
        }
        _experience = experience; _session = snapshot.SessionId.Value; _width = width; _size = size; _local = local;
        if (!changed && _page == page && _pageSize == pageSize) return;
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
                    text.Append(ResourcePowerHudModel.ResolveAlertSeverity(item.Kind) == HudAlertSeverity.Critical ? "CRITICAL / " : "WARNING / ");
                    text.Append(label);
                    if (item.Kind == PlayerAlertState.CargoFleetShortage && _fleet is { } fleet)
                    {
                        text.Append(" / BACKLOG ").Append(fleet.BacklogRequests);
                        text.Append(" ACTIVE ").Append(fleet.ActiveTrucks);
                        text.Append(" IDLE ").Append(fleet.IdleTrucks);
                        text.Append(" EFFECTIVE ").Append(fleet.EffectiveTrucks);
                    }
                    int affected = item.Kind == PlayerAlertState.SupplyCritical ? experience.CriticalSupplyUnits :
                        item.Kind == PlayerAlertState.ProductionBlocked ? experience.BlockedProductionFacilities : 0;
                    if (affected > 0) text.Append(" / ").Append(affected);
                    text.AppendLine();
                }
                else text.AppendLine(NotificationText.Format(experience, alerts, item, buffer).ToString());
            }
            if (causes is not null)
            {
                foreach (var group in causes.Groups)
                {
                    text.Append("SHARED CAUSE / ").Append(OperationsDiagnosisSnapshot.Label(group.Reason));
                    if (group.Network.IsSpecified) text.Append(" / POWER NETWORK ").Append(group.Network);
                    if (group.Cause.IsValid) text.Append(" / CAUSE ").Append(group.Cause);
                    if (group.Resource.IsSpecified) text.Append(" / RESOURCE ").Append(group.Resource);
                    text.Append(" / ").Append(group.Members.Count).AppendLine(" OBSERVED FACILITIES");
                    foreach (var member in group.Members) text.Append("FACILITY ").AppendLine(member.ToString());
                }
                text.AppendLine(causes.Limited ? "CAUSE CAPTURE LIMITED / OTHER CAUSES MAY BE UNOBSERVED" : "CAUSES FROM CURRENT OWNED OBSERVATIONS / NO COMPLETE-BASE CLAIM");
                if (causes.Groups.Count > 0) text.AppendLine("FACILITIES: USE FOCUS, TRACE NEXT AND OPEN EXISTING CONTROLS");
            }
            if (local.Length > 0) text.AppendLine(local);
            _content = text.Length > 0 ? text.ToString() : "NO CURRENT NOTIFICATIONS";
        }
        _page = page; _pageSize = pageSize;
        int count = 0, offset = 0;
        while (offset < _content.Length)
        { _ = NotificationText.Line(_content.AsSpan(offset), MathF.Max(1, width), size, out int used); offset += used; count++; }
        PageCount = Math.Max(1, (count + pageSize - 1) / pageSize);
        int first = Math.Max(0, page) % PageCount * pageSize;
        Array.Fill(_lines, ""); offset = 0;
        for (int i = 0; i < first + pageSize && offset < _content.Length; i++)
        {
            var line = NotificationText.Line(_content.AsSpan(offset), MathF.Max(1, width), size, out int used);
            if (i >= first) _lines[i - first] = line.ToString();
            offset += used;
        }
    }
}
