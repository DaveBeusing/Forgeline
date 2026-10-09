using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class MaterialImporter
{
    public static ImportedAssetPayload Import(string path)
    {
        MaterialSourceDefinition definition =
            ReadDefinition(
                path);
        var options =
            RuntimeAssetCatalog.CreateJsonOptions();
        byte[] normalized =
            JsonSerializer.SerializeToUtf8Bytes(
                definition,
                options);

        return new ImportedAssetPayload(
            normalized,
            null,
            definition.ReferencedTextureIds);
    }

    public static IReadOnlyList<string> ReadDependencies(
        string path) =>
        ReadDefinition(
            path)
        .ReferencedTextureIds;

    public static MaterialSourceDefinition ReadDefinition(
        string path)
    {
        string json =
            File.ReadAllText(
                path);
        MaterialSourceDefinition definition =
            JsonSerializer.Deserialize<MaterialSourceDefinition>(
                json,
                RuntimeAssetCatalog.CreateJsonOptions())
            ?? throw new InvalidDataException(
                "Material definition is empty.");

        Validate(
            definition);
        return definition;
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

        if (!float.IsFinite(definition.EmissiveMultiplier) ||
            definition.EmissiveMultiplier < 0f)
        {
            throw new InvalidDataException(
                "Material emissiveMultiplier must be finite and non-negative.");
        }

        if (definition.UvScale.Length != 2 ||
            definition.UvScale.Any(static value => !float.IsFinite(value) || value <= 0f))
        {
            throw new InvalidDataException(
                "Material uvScale must contain two finite positive values.");
        }

        foreach (var id in definition.ReferencedTextureIds)
        {
            _ = AssetId.Parse(id);
        }

        if (definition.UvOffset.Length != 2 ||
            definition.UvOffset.Any(static value => !float.IsFinite(value)))
        {
            throw new InvalidDataException("Material uvOffset must contain two finite values.");
        }
    }
}
