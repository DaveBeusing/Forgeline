using System.Buffers;
using System.Text;
using System.Text.Json;

namespace ForgeLine.Presentation;

public enum UiTextRole { CommandTitle, MenuItem, SectionHeading, Telemetry, Numeric, Warning, Tooltip }
public readonly record struct UiTextStyle(float Size, bool Bold);
public static class UiTypography
{
    public static UiTextStyle Style(UiTextRole role) => role switch
    {
        UiTextRole.CommandTitle => new(32, true),
        UiTextRole.MenuItem => new(22, false),
        UiTextRole.SectionHeading => new(16, true),
        UiTextRole.Numeric => new(14, true),
        UiTextRole.Warning => new(14, true),
        UiTextRole.Tooltip => new(14, false),
        _ => new(14, false)
    };
}

public sealed class UiFontMetrics
{
    private static readonly JsonSerializerOptions s_json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<UiFontMetrics> s_default = new(Load);
    private readonly Dictionary<long, UiFontGlyph> _glyphs = [];
    private readonly Dictionary<(int Face, int Left, int Right), float> _kerning = [];
    private readonly Dictionary<(int Base, int Mark), int> _compositions = [];
    public static UiFontMetrics Default => s_default.Value;
    public UiFontDescription Description { get; }
    public int GlyphCount => _glyphs.Count;
    private UiFontMetrics(UiFontDescription description)
    {
        Description = description;
        if (description.Version != 1 || description.Width is < 1 or > 4096 || description.Height is < 1 or > 4096 ||
            description.PixelSize != 48 || description.Glyphs.Length is < 1 or > 8192 || description.Kerning.Length > 65536 ||
            !float.IsFinite(description.Ascent) || !float.IsFinite(description.LineHeight) || description.LineHeight <= 0)
            throw new InvalidDataException("Invalid bundled font contract.");
        foreach (var glyph in description.Glyphs)
        {
            if (glyph.Face is < 0 or > 1 || !Rune.IsValid(glyph.Codepoint) || glyph.X < 0 || glyph.Y < 0 ||
                glyph.Width < 0 || glyph.Height < 0 || glyph.X + glyph.Width > description.Width || glyph.Y + glyph.Height > description.Height ||
                !float.IsFinite(glyph.Advance) || glyph.Advance is < 0 or > 256 || !float.IsFinite(glyph.Left) || !float.IsFinite(glyph.Top) ||
                !_glyphs.TryAdd(Key(glyph.Face, glyph.Codepoint), glyph)) throw new InvalidDataException("Invalid bundled glyph.");
        }
        foreach (var pair in description.Kerning)
        {
            if (!float.IsFinite(pair.Adjustment) || MathF.Abs(pair.Adjustment) > 128 || pair.Face is < 0 or > 1 ||
                !_kerning.TryAdd((pair.Face, pair.Left, pair.Right), pair.Adjustment)) throw new InvalidDataException("Invalid bundled kerning.");
        }
        foreach (var pair in description.Compositions) _compositions[(pair.Base, pair.Mark)] = pair.Composed;
        if (!Contains(0xfffd) || !Contains(0xfffd, true)) throw new InvalidDataException("Missing replacement glyph.");
    }
    private static long Key(int face, int codepoint) => ((long)face << 32) | (uint)codepoint;
    public bool Contains(int codepoint, bool bold = false) => _glyphs.ContainsKey(Key(bold ? 1 : 0, codepoint));
    internal UiFontGlyph Glyph(int codepoint, bool bold, out bool missing)
    {
        missing = !_glyphs.TryGetValue(Key(bold ? 1 : 0, codepoint), out var glyph);
        return missing ? _glyphs[Key(bold ? 1 : 0, 0xfffd)] : glyph!;
    }
    internal float Kern(int left, int right, bool bold) => _kerning.GetValueOrDefault((bold ? 1 : 0, left, right));
    internal int Next(ReadOnlySpan<char> text, ref int index)
    {
        var result = Rune.DecodeFromUtf16(text[index..], out Rune rune, out int consumed);
        int cp = result == OperationStatus.Done ? rune.Value : 0xfffd;
        index += result == OperationStatus.Done ? consumed : 1;
        if (index < text.Length && _compositions.TryGetValue((cp, text[index]), out int composed)) { index++; cp = composed; }
        return cp;
    }
    public float Measure(ReadOnlySpan<char> text, float size, bool bold = false)
    {
        ValidateSize(size);
        float current = 0, maximum = 0; int previous = 0;
        for (int index = 0; index < text.Length;)
        {
            int cp = Next(text, ref index);
            if (cp == '\r') continue;
            if (cp == '\n') { maximum = MathF.Max(maximum, current); current = 0; previous = 0; continue; }
            if (cp == '\t') cp = ' ';
            var glyph = Glyph(cp, bold, out _);
            current += (Kern(previous, glyph.Codepoint, bold) + glyph.Advance) * size / Description.PixelSize;
            previous = glyph.Codepoint;
        }
        return MathF.Max(maximum, current);
    }
    // Logical advances plus ink overhang, in coordinates relative to the line top.
    internal HudRect MeasureLineBounds(ReadOnlySpan<char> text, float size, bool bold = false)
    {
        ValidateSize(size);
        float scale = size / Description.PixelSize;
        float cursor = 0, left = 0, right = 0, top = 0, bottom = Description.LineHeight * scale;
        int previous = 0;
        for (int index = 0; index < text.Length;)
        {
            int cp = Next(text, ref index);
            if (cp == '\r') continue;
            if (cp == '\n') break;
            if (cp == '\t') cp = ' ';
            var glyph = Glyph(cp, bold, out _);
            cursor += Kern(previous, glyph.Codepoint, bold) * scale;
            left = MathF.Min(left, cursor + glyph.Left * scale);
            right = MathF.Max(right, cursor + (glyph.Left + glyph.Width) * scale);
            top = MathF.Min(top, (Description.Ascent + glyph.Top) * scale);
            bottom = MathF.Max(bottom, (Description.Ascent + glyph.Top + glyph.Height) * scale);
            cursor += glyph.Advance * scale;
            right = MathF.Max(right, cursor);
            previous = glyph.Codepoint;
        }
        return new(left, top, right - left, bottom - top);
    }

    internal int FitPrefix(ReadOnlySpan<char> text, float width, float size, bool bold)
    {
        float current = 0; int previous = 0, index = 0;
        while (index < text.Length)
        {
            int start = index, cp = Next(text, ref index);
            if (cp == '\r') continue;
            if (cp == '\n') return start;
            if (cp == '\t') cp = ' ';
            var glyph = Glyph(cp, bold, out _);
            float next = current + (Kern(previous, glyph.Codepoint, bold) + glyph.Advance) * size / Description.PixelSize;
            if (next > width) return start;
            current = next; previous = glyph.Codepoint;
        }
        return index;
    }
    public string Fit(string text, float width, float size, bool bold = false)
    {
        ArgumentNullException.ThrowIfNull(text); ValidateSize(size);
        if (!float.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (Measure(text, size, bold) <= width) return text;
        float ellipsis = Measure("\u2026", size, bold);
        if (ellipsis > width) return string.Empty;
        return string.Concat(text.AsSpan(0, FitPrefix(text, width - ellipsis, size, bold)), "\u2026");
    }
    public string[] Wrap(string text, float width, float size, bool bold = false)
    {
        ArgumentNullException.ThrowIfNull(text); ValidateSize(size);
        if (!float.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            if (lines.Count >= 256) break;
            int offset = 0;
            if (paragraph.Length == 0) { lines.Add(string.Empty); continue; }
            while (offset < paragraph.Length && lines.Count < 256)
            {
                ReadOnlySpan<char> remaining = paragraph.AsSpan(offset);
                int count = FitPrefix(remaining, width, size, bold);
                if (count == 0)
                {
                    int next = 0; _ = Next(remaining, ref next);
                    lines.Add(string.Empty); offset += next; continue;
                }
                if (count < remaining.Length) { int space = remaining[..count].LastIndexOf(' '); if (space > 0) count = space; }
                lines.Add(remaining[..count].ToString()); offset += count;
                while (offset < paragraph.Length && paragraph[offset] == ' ') offset++;
            }
        }
        return lines.ToArray();
    }
    internal static void ValidateSize(float size)
    { if (!float.IsFinite(size) || size is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(size)); }
    private static UiFontMetrics Load()
    {
        using var stream = typeof(UiFontMetrics).Assembly.GetManifestResourceStream("ForgeLine.UiFont.Metrics") ?? throw new InvalidDataException("Bundled font metrics missing.");
        return new(JsonSerializer.Deserialize<UiFontDescription>(stream, s_json) ?? throw new InvalidDataException("Bundled font metrics empty."));
    }
}
public sealed record UiFontDescription(int Version, string ResourceId, int Width, int Height, int PixelSize, float Ascent,
    float LineHeight, string AlphaSha256, UiFontGlyph[] Glyphs, UiFontKerning[] Kerning, UiFontComposition[] Compositions);
public sealed record UiFontGlyph(int Face, int Codepoint, int X, int Y, int Width, int Height, float Left, float Top, float Advance);
public sealed record UiFontKerning(int Face, int Left, int Right, float Adjustment);
public sealed record UiFontComposition(int Base, int Mark, int Composed);
