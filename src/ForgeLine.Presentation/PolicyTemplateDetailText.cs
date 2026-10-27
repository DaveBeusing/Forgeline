using System.Text;

namespace ForgeLine.Presentation;

/// <summary>Measured bounded pages show complete before/after, costs and each target result.</summary>
internal sealed class PolicyTemplateDetailText
{
    private readonly string[] _lines = new string[6];
    private PolicyTemplatePanelView _view;
    private float _width, _size;
    private bool _initialized;
    internal IReadOnlyList<string> Lines => _lines;
    internal int PageCount { get; private set; } = 1;
    internal int Page { get; private set; }
    internal void Update(PolicyTemplatePanelView view, float width, float size)
    {
        if (_initialized && _view == view && _width == width && _size == size) return;
        _initialized = true; _view = view; _width = width; _size = size;
        var text = new StringBuilder();
        text.AppendLine(view.Feedback);
        text.Append("SLOT ").Append(view.Slot + 1).Append(view.Dirty ? " / LOCAL DRAFT" :
            view.Saved ? " / SAVED LOCALLY" : " / EMPTY").Append(" / ").AppendLine(view.Draft.Label);
        if (view.Rows.Count == 0)
            text.AppendLine("Select one source and COPY SOURCE repeatedly to choose its resource or recipe. Adjust fields, SAVE SLOT, select target facilities, PREVIEW, then APPLY PREVIEW. Saving alone sends no command.");
        else
            for (int i = 0; i < view.Rows.Count; i++)
            {
                var row = view.Rows[i];
                text.Append("TARGET ").Append(i + 1).Append('/').Append(view.Rows.Count).Append(' ')
                    .Append(row.Name).Append(" / ").AppendLine(row.State.ToString());
                text.Append("BEFORE: ").AppendLine(row.Before);
                text.Append("AFTER: ").AppendLine(row.After);
                text.AppendLine(row.Reason.Length == 0 ? "READY / NOTHING APPLIED BY PREVIEW" : row.Reason);
            }
        if (view.Omitted > 0) text.Append(view.Omitted).AppendLine(" selected targets omitted: maximum 32. They receive no command.");
        text.AppendLine("Queue confirmation is not manufactured stock or delivered cargo. Existing queues, active rules and manually disabled policies are preserved. Rejections are never automatically retried.");
        string content = text.ToString();
        int count = 0, offset = 0;
        while (offset < content.Length)
        {
            _ = NotificationText.Line(content.AsSpan(offset), MathF.Max(1, width), size, out int used, bold: true);
            if (used <= 0) break;
            offset += used; count++;
        }
        PageCount = Math.Max(1, (count + _lines.Length - 1) / _lines.Length);
        Page = view.DetailPage % PageCount;
        Array.Fill(_lines, string.Empty); offset = 0;
        for (int i = 0; i < (Page + 1) * _lines.Length && offset < content.Length; i++)
        {
            var line = NotificationText.Line(content.AsSpan(offset), MathF.Max(1, width), size, out int used, bold: true);
            if (used <= 0) break;
            if (i >= Page * _lines.Length) _lines[i - Page * _lines.Length] = line.ToString();
            offset += used;
        }
    }
}
