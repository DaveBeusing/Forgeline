namespace ForgeLine.Presentation;

/// <summary>Validates presentation resources before startup playback.</summary>
public static class SplashAssetPreflight
{
    public static SplashDefinition? Prepare(
        SplashDefinition definition,
        Func<string, bool> assetExists,
        Action<string>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(assetExists);
        definition.Validate();

        var available = new List<SplashLayerDefinition>(definition.Layers.Count);
        foreach (SplashLayerDefinition layer in definition.Layers)
        {
            bool exists;
            try
            {
                exists = assetExists(layer.AssetId);
            }
            catch (Exception error)
            {
                diagnostic?.Invoke($"Splash asset lookup failed for '{layer.AssetId}': {error.Message}");
                exists = false;
            }

            if (exists)
            {
                available.Add(layer);
                continue;
            }

            bool required = IsRequiredBrandLayer(layer.Id);
            diagnostic?.Invoke(
                $"Splash {(required ? "required" : "optional")} asset is missing: '{layer.AssetId}'.");

            if (required)
            {
                return null;
            }
        }

        return available.Count == 0
            ? null
            : definition with { Layers = available.ToArray() };
    }

    private static bool IsRequiredBrandLayer(string id) =>
        string.Equals(id, "monogram", StringComparison.Ordinal) ||
        string.Equals(id, "wordmark", StringComparison.Ordinal) ||
        string.Equals(id, "subtitle", StringComparison.Ordinal);
}
