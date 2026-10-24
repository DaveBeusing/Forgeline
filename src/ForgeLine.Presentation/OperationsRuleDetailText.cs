using System.Text;
using System.Globalization;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>Bounded measured pages retain full rule values and feedback on narrow Operations panels.</summary>
internal sealed class OperationsRuleDetailText
{
    private readonly string[] _lines = new string[6];
    private OperationsRulePanelView _view;
    private float _width, _size;
    private bool _initialized;
    internal IReadOnlyList<string> Lines => _lines;
    internal int PageCount { get; private set; } = 1;
    internal int Page { get; private set; }

    internal void Update(in OperationsRulePanelView view, float width, float size)
    {
        if (_initialized && _view == view && _width == width && _size == size) return;
        _initialized = true; _view = view; _width = width; _size = size;
        var text = new StringBuilder();
        if (view.Feedback.Length > 0) text.AppendLine(view.Feedback);
        if (view.Data is null) text.AppendLine("Select exactly one owned processing facility in the world, then open Operations Rules.");
        else
        {
            if (view.Observed is { } row)
            {
                text.Append(row.Enabled ? row.Result == OperationsRuleResult.ManualOverride ? "SUSPENDED / MANUAL" : "ENABLED" : "DISABLED")
                    .Append(" / ").AppendLine(row.Result.ToString());
                text.Append("EVALUATED TICK ").Append(row.LastEvaluated.Value).Append(" / LAST SUCCESS ").AppendLine(row.LastFired.Value.ToString(CultureInfo.InvariantCulture));
                text.Append("ATTEMPTS ").Append(row.AttemptCount).Append(" / REMAINING ")
                    .Append(Math.Max(0, OperationsRuleSystem.MaximumActionsPerRule - row.AttemptCount))
                    .Append(" / ACTIONS ").Append(row.ActionCount).Append(" / LAST ATTEMPT ").AppendLine(row.LastAttempt.Value.ToString(CultureInfo.InvariantCulture));
                if (row.ManagedProductionRequest.IsValid)
                {
                    string name = "CURRENT REQUEST UNAVAILABLE";
                    foreach (var request in view.Data.ManagedRequests)
                        if (request.RequestEntity == row.ManagedProductionRequest) name = request.DisplayName;
                    text.Append("RULE-MANAGED PAUSE: ").AppendLine(name);
                }
                else text.AppendLine("NO RULE-MANAGED PAUSE");
            }
            else text.AppendLine("NEW LOCAL DRAFT / EXPECTED REVISION 0");
            var d = view.Draft;
            if (d.Template == OperationsRuleTemplate.EnsureInputStockPolicy)
            {
                string resource = "NOT IN CURRENT RECIPE";
                foreach (var input in view.Data.Inputs) if (input.ResourceId == d.Resource) resource = input.DisplayName;
                text.Append("INPUT RESOURCE: ").AppendLine(resource);
            }
            text.AppendLine(FormattableString.Invariant($"DRAFT MIN {d.Minimum:G} / TARGET {d.Target:G} / MAX {d.Maximum:G}"));
            text.AppendLine(FormattableString.Invariant($"PRIORITY {d.Priority} / COOLDOWN {d.CooldownTicks} TICKS ({d.CooldownTicks * view.Data.TickDuration.TotalSeconds:0.###}s)"));
            text.AppendLine(d.Template == OperationsRuleTemplate.EnsureInputStockPolicy
                ? "INPUT STOCK: after missing recipe input, create one absent stock policy. Existing enabled or manually disabled policies win. This confirms no reservation, loading or delivery. A removed policy is not automatically recreated."
                : "OUTPUT RECOVERY: pause the exact active request only while output is full; resume it when real output capacity returns. Manual pause or resume suspends automation. Disable/remove releases only a rule-owned pause.");
            text.AppendLine("Edits are local until APPLY. ENABLE explicitly rearms the rule. Limits: 32/player, 8/facility, 16 attempts/revision. Managed recovery can complete at the attempt limit.");
        }
        string content = text.ToString();
        int count = 0, offset = 0;
        while (offset < content.Length)
        { _ = NotificationText.Line(content.AsSpan(offset), MathF.Max(1, width), size, out int used); offset += used; count++; }
        PageCount = Math.Max(1, (count + _lines.Length - 1) / _lines.Length);
        Page = Math.Max(0, view.DetailPage) % PageCount;
        Array.Fill(_lines, string.Empty); offset = 0;
        for (int i = 0; i < (Page + 1) * _lines.Length && offset < content.Length; i++)
        {
            var line = NotificationText.Line(content.AsSpan(offset), MathF.Max(1, width), size, out int used);
            if (i >= Page * _lines.Length) _lines[i - Page * _lines.Length] = line.ToString();
            offset += used;
        }
    }
}
