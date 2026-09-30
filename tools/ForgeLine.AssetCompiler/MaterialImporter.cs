using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class MaterialImporter
{
    public static ImportedAssetPayload Import(string path)
    {
        var json = File.ReadAllText(path);
        var options = RuntimeAssetCatalog.CreateJsonOptions();
        var definition = JsonSerializer.Deserialize<MaterialSourceDefinition>(json, options)
            ?? throw new InvalidDataException("Material definition is empty.");

        Validate(definition);

        var normalized = JsonSerializer.SerializeToUtf8Bytes(definition, options);
        return new ImportedAssetPayload(normalized, null, definition.ReferencedTextureIds);
    }

    public static IReadOnlyList<string> ReadDependencies(string path)
    {
        var json = File.ReadAllText(path);
        var definition = JsonSerializer.Deserialize<MaterialSourceDefinition>(
            json,
            RuntimeAssetCatalog.CreateJsonOptions())
            ?? throw new InvalidDataException("Material definition is empty.");

        Validate(definition);
        return definition.ReferencedTextureIds;
    }

    private static void Validate(MaterialSourceDefinition definition)
    {
        if (definition.BaseColorFactor.Length != 4 ||
            definition.BaseColorFactor.Any(static value => !float.IsFinite(value) || value < 0f))
        {
            throw new InvalidDataException("Material baseColorFactor must contain four finite non-negative values.");
        }

        if (!float.IsFinite(definition.MetallicFactor) ||
            definition.MetallicFactor < 0f ||
            definition.MetallicFactor > 1f)
        {
            throw new InvalidDataException("Material metallicFactor must be between 0 and 1.");
        }

        if (!float.IsFinite(definition.RoughnessFactor) ||
            definition.RoughnessFactor < 0f ||
            definition.RoughnessFactor > 1f)
        {
            throw new InvalidDataException("Material roughnessFactor must be between 0 and 1.");
        }

        foreach (var id in definition.ReferencedTextureIds)
        {
            _ = AssetId.Parse(id);
        }
    }
}
