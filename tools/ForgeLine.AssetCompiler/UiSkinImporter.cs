using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class UiSkinImporter
{
    internal sealed record Source(int SchemaVersion, UiSkinPage[] Pages, UiSkinSprite[] Sprites);
    internal static RuntimeUiSkinData Read(string path)
    {
        if (new FileInfo(path).Length > RuntimeUiSkinData.MaximumPayloadBytes)
            throw new InvalidDataException("UI source metadata exceeds its limit.");
        var source = JsonSerializer.Deserialize<Source>(File.ReadAllText(path), RuntimeAssetCatalog.CreateJsonOptions())
            ?? throw new InvalidDataException("Missing UI source metadata.");
        if (source.SchemaVersion != RuntimeUiSkinData.PayloadVersion || source.Pages is null || source.Sprites is null)
            throw new InvalidDataException("Unsupported UI source contract.");
        return new(source.Pages, source.Sprites);
    }
    internal static ImportedAssetPayload Import(string path, IReadOnlyDictionary<AssetId, RuntimeAssetRecord> records, string runtimeRoot)
    {
        var data = Read(path);
        foreach (var page in data.Pages)
        {
            if (!records.TryGetValue(AssetId.Parse(page.Id), out var record) || record.Type != RuntimeAssetType.Texture)
                throw new InvalidDataException($"UI page '{page.Id}' is not a compiled texture dependency.");
            var content = RuntimeAssetFile.Read(Path.Combine(runtimeRoot, record.RuntimePath), RuntimeAssetType.Texture, record.RuntimeBytes, record.RuntimeHash);
            RuntimeUiSkinData.ValidatePage(page, RuntimeTextureData.FromPayload(content.Payload));
        }
        return new(data.ToPayload(), null, data.Pages.Select(p => p.Id).ToArray());
    }
}
