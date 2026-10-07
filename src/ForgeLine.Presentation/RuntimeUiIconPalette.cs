using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

internal sealed class RuntimeUiIconPalette
{
    private readonly RuntimeAssetCatalog _catalog;
    private readonly Dictionary<string, Vector4> _colors =
        new(StringComparer.Ordinal);

    public RuntimeUiIconPalette(
        RuntimeAssetCatalog catalog)
    {
        _catalog =
            catalog ??
            throw new ArgumentNullException(nameof(catalog));
    }

    public bool TryResolve(
        string rawAssetId,
        out Vector4 color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            rawAssetId);

        if (_colors.TryGetValue(
                rawAssetId,
                out color))
        {
            return true;
        }

        AssetId id =
            AssetId.Parse(
                rawAssetId);

        if (!_catalog.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type !=
                RuntimeAssetType.Material)
        {
            color = default;
            return false;
        }

        RuntimeAssetContent content =
            _catalog.Read(
                id);

        using JsonDocument document =
            JsonDocument.Parse(
                content.Payload);

        if (!document.RootElement.TryGetProperty(
                "baseColorFactor",
                out JsonElement factor) ||
            factor.ValueKind !=
                JsonValueKind.Array ||
            factor.GetArrayLength() != 4)
        {
            color =
                Vector4.One;
        }
        else
        {
            color =
                new Vector4(
                    factor[0].GetSingle(),
                    factor[1].GetSingle(),
                    factor[2].GetSingle(),
                    factor[3].GetSingle());
        }

        _colors.Add(
            rawAssetId,
            color);
        return true;
    }
}
