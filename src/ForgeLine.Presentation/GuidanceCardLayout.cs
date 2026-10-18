using System.Text;

namespace ForgeLine.Presentation;

public static class GuidanceCardLayout
{
    public const float Header = 110;
    public const float Footer = 76;
    public static float TextSize(in GameplayHudLayout layout) => Math.Clamp(16 * MathF.Min(layout.Scale, 1.25f), 14, 20);
    public static int BodyRows(in GameplayHudLayout layout) => Math.Max(1,
        (int)((GameplayGuidanceLayout.Resolve(layout, true).Height - Header - Footer) / (TextSize(layout) * 1.4f)));
    public static HudRect Control(in OnboardingPresentation state, int index)
    {
        if (!state.Detailed || index is < 0 or > 5) return default;
        float width = (state.Panel.Width - 24) / 3;
        var rect = new HudRect(state.Panel.X + 8 + index % 3 * (width + 4),
            state.Panel.Bottom - Footer + index / 3 * 32, width, 28);
        return OnboardingPresentation.Intersect(rect, state.Clip);
    }
    public static GuidanceNavigation Action(int index) => index switch
    {
        0 => GuidanceNavigation.PreviousSection, 1 => GuidanceNavigation.NextSection, 2 => GuidanceNavigation.Read,
        3 => GuidanceNavigation.PreviousPage, 4 => GuidanceNavigation.NextPage, 5 => GuidanceNavigation.Later, _ => GuidanceNavigation.None
    };
    public static string Label(int index) => index switch { 0 => "PREV", 1 => "NEXT", 2 => "READ", 3 => "PAGE <", 4 => "PAGE >", 5 => "LATER", _ => "MINIMIZE" };
    internal static IReadOnlyList<string> Pages(string body, in GameplayHudLayout layout)
    {
        var rect = GameplayGuidanceLayout.Resolve(layout, true);
        float width = MathF.Max(40, rect.Width - 20);
        int rows = BodyRows(layout), count = 0;
        var pages = new List<string>();
        var page = new StringBuilder();
        ReadOnlySpan<char> rest = body.AsSpan();
        while (!rest.IsEmpty)
        {
            int length = UiFontMetrics.Default.FitPrefix(rest, width - 2, TextSize(layout), false);
            length = Math.Max(1, length);
            if (length < rest.Length)
            {
                int word = rest[..length].LastIndexOf(' ');
                if (word > 0) length = word;
            }
            if (count > 0) page.Append('\n');
            page.Append(rest[..length]);
            rest = rest[length..].TrimStart();
            if (++count == rows || rest.IsEmpty) { pages.Add(page.ToString()); page.Clear(); count = 0; }
        }
        return pages.AsReadOnly();
    }
}
