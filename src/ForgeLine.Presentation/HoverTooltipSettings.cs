namespace ForgeLine.Presentation;

public enum HoverTooltipTextSize : byte { Standard, Large, ExtraLarge }

/// <summary>Local presentation preferences; never part of simulation or replay state.</summary>
public sealed record HoverTooltipSettings
{
    public static HoverTooltipSettings Default { get; } = new();
    public int DelayMilliseconds { get; init; } = 175;
    public HoverTooltipTextSize TextSize { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public float TextScale => TextSize switch
    {
        HoverTooltipTextSize.Large => 1.15f,
        HoverTooltipTextSize.ExtraLarge => 1.3f,
        _ => 1
    };

    public void Validate()
    {
        if (DelayMilliseconds is < 0 or > 1000 || !Enum.IsDefined(TextSize))
            throw new ArgumentOutOfRangeException(nameof(HoverTooltipSettings));
    }
}
