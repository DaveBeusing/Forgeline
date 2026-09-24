namespace ForgeLine.Game;

public readonly record struct PlayerId(ulong Value)
{
    public static PlayerId None => default;

    public bool IsSpecified => Value != 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
