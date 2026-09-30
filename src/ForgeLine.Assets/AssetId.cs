namespace ForgeLine.Assets;

public readonly record struct AssetId
{
    private readonly string? value;

    public AssetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!IsValid(value))
        {
            throw new ArgumentException(
                "Asset IDs must be lowercase dot-separated identifiers containing only a-z, 0-9, '_' or '-'.",
                nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;

    public static AssetId Parse(string value) => new(value);

    public static bool TryParse(string? value, out AssetId assetId)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsValid(value))
        {
            assetId = new AssetId(value);
            return true;
        }

        assetId = default;
        return false;
    }

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        if (value.Length < 3 || value[0] == '.' || value[^1] == '.' || !value.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var previousWasDot = false;
        foreach (var character in value)
        {
            if (character == '.')
            {
                if (previousWasDot)
                {
                    return false;
                }

                previousWasDot = true;
                continue;
            }

            previousWasDot = false;
            if ((character < 'a' || character > 'z') &&
                (character < '0' || character > '9') &&
                character != '_' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }
}
