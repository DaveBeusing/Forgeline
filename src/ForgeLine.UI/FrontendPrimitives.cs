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
        float userScale = 1f)
    {
        float scale =
            ResolveScale(
                viewportWidth,
                viewportHeight,
                userScale);
        float contentWidth =
            ReferenceWidth * scale;
        float contentHeight =
            ReferenceHeight * scale;

        return new FrontendLayout(
            scale,
            MathF.Max(0f, (viewportWidth - contentWidth) * 0.5f),
            MathF.Max(0f, (viewportHeight - contentHeight) * 0.5f),
            contentWidth,
            contentHeight);
    }

    public static string FitText(
        string value,
        int maximumLength = MaximumDetailTextLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 4);

        return value.Length <= maximumLength
            ? value
            : string.Concat(
                value.AsSpan(0, maximumLength - 3),
                "...");
    }

    public static float ResolveScale(float viewportWidth, float viewportHeight, float userScale = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(viewportWidth, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(viewportHeight, 0f);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(userScale, 0f);

        var viewportScale = MathF.Min(viewportWidth / ReferenceWidth, viewportHeight / ReferenceHeight);
        return Math.Clamp(viewportScale * userScale, MinimumScale, MaximumScale);
    }
}

public readonly record struct FrontendLayout(
    float Scale,
    float OffsetX,
    float OffsetY,
    float ContentWidth,
    float ContentHeight);

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
