using ForgeLine.Presentation;

namespace ForgeLine.UI;

public static class FrontendDesign
{
    public const float ReferenceWidth = 1920f;
    public const float ReferenceHeight = 1080f;
    public const float MinimumScale = 0.75f;
    public const float MaximumScale = 2.0f;
    public const float OuterMargin = 48f;
    public const float PanelGap = 24f;
    public const float ControlHeight = 56f;
    public const float FocusStroke = 2f;
    public const int MaximumVisibleDetailRows = 7;
    public const int MaximumDetailTextLength = 34;

    public static FrontendLayout ResolveLayout(
        float viewportWidth,
        float viewportHeight,
        float userScale = 1f, float dpi = 96f, bool controls = false)
    {
        var geometry = FrontendGeometry.Resolve(viewportWidth, viewportHeight, userScale, dpi, controls);
        return new FrontendLayout(geometry.Scale, 0, 0, viewportWidth, viewportHeight) { Geometry = geometry };
    }

    public static string FitText(
        string value,
        int maximumLength = MaximumDetailTextLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 4);

        return FitText(value, maximumLength * 13f, UiTypography.Style(UiTextRole.MenuItem).Size);
    }

    public static string FitText(string value, float maximumWidth, float fontSize, bool bold = false)
        => UiFontMetrics.Default.Fit(value, maximumWidth, fontSize, bold);

    public static float ResolveScale(float viewportWidth, float viewportHeight, float userScale = 1f)
    {
        return FrontendGeometry.Resolve(viewportWidth, viewportHeight, userScale).Scale;
    }
}

public readonly record struct FrontendLayout(
    float Scale,
    float OffsetX,
    float OffsetY,
    float ContentWidth,
    float ContentHeight)
{
    public FrontendGeometry Geometry { get; init; }
}

public readonly record struct FrontendRect(float X, float Y, float Width, float Height)
{
    public FrontendRect Scale(float scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0f);

        return new FrontendRect(X * scale, Y * scale, Width * scale, Height * scale);
    }
}

public sealed class FrontendFocusModel
{
    private readonly string[] _focusOrder;

    public FrontendFocusModel(IEnumerable<string> focusOrder)
    {
        ArgumentNullException.ThrowIfNull(focusOrder);

        _focusOrder = focusOrder
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (_focusOrder.Length == 0)
        {
            throw new ArgumentException("At least one focus target is required.", nameof(focusOrder));
        }
    }

    public int FocusedIndex { get; private set; }

    public string FocusedId => _focusOrder[FocusedIndex];

    public string MoveNext()
    {
        FocusedIndex = (FocusedIndex + 1) % _focusOrder.Length;
        return FocusedId;
    }

    public string MovePrevious()
    {
        FocusedIndex = (FocusedIndex - 1 + _focusOrder.Length) % _focusOrder.Length;
        return FocusedId;
    }

    public bool TryFocus(string id)
    {
        var index = Array.IndexOf(_focusOrder, id);
        if (index < 0)
        {
            return false;
        }

        FocusedIndex = index;
        return true;
    }
}
