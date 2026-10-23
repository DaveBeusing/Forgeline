using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

internal sealed record ProductionSourceAsset(string Id, RuntimeAssetType Type, string DefinitionPath);

/// <summary>Compare declared source membership with output, independently of compiler discovery.</summary>
internal static class ProductionAssetCatalogContract
{
    internal static IReadOnlyList<ProductionSourceAsset> ReadSources(string sourceRoot)
    {
        var expected = new List<ProductionSourceAsset>();
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.asset.json", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement definition = json.RootElement;
            string id = definition.GetProperty("id").GetString()!;
            Assert.True(AssetId.TryParse(id, out _), $"Source definition '{path}' has invalid ID '{id}'.");
            string typeName = definition.GetProperty("type").GetString()!;
            Assert.True(Enum.TryParse(typeName, ignoreCase: true, out RuntimeAssetType type) && Enum.IsDefined(type),
                $"Source definition '{path}' has unsupported type '{typeName}'.");
            expected.Add(new(id, type, Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')));
        }
        Assert.NotEmpty(expected);
        AssertUniqueSources(expected);
        return expected;
    }

    internal static void AssertMatches(IReadOnlyList<ProductionSourceAsset> expected,
        RuntimeAssetManifest manifest, AssetCompilationResult result)
    {
        Assert.True(result.Success, string.Join(Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        Assert.NotEmpty(expected);
        AssertUniqueSources(expected);

        string[] duplicateRuntimeIds = manifest.Assets.GroupBy(static record => record.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1).Select(static group => group.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.True(duplicateRuntimeIds.Length == 0,
            "Duplicate runtime asset IDs: " + string.Join(", ", duplicateRuntimeIds));

        var declarations = expected.ToDictionary(static asset => asset.Id, StringComparer.Ordinal);
        var records = manifest.Assets.ToDictionary(static record => record.Id, StringComparer.Ordinal);
        string[] missing = declarations.Keys.Except(records.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] unexpected = records.Keys.Except(declarations.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0 && unexpected.Length == 0,
            $"Source/runtime catalog membership differs. Missing: [{string.Join(", ", missing)}]. " +
            $"Unexpected: [{string.Join(", ", unexpected)}].");

        foreach (ProductionSourceAsset declaration in expected)
        {
            RuntimeAssetRecord record = records[declaration.Id];
            Assert.True(declaration.Type == record.Type,
                $"Asset '{declaration.Id}' from '{declaration.DefinitionPath}' declares {declaration.Type}, " +
                $"but its runtime record is {record.Type}.");
        }

        Assert.Equal(expected.Count, result.CompiledCount + result.SkippedCount);
        Assert.Equal(expected.Count, manifest.Assets.Count);
    }

    private static void AssertUniqueSources(IReadOnlyList<ProductionSourceAsset> expected)
    {
        string[] duplicates = expected.GroupBy(static asset => asset.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => $"{group.Key}: {string.Join(", ", group.Select(static asset => asset.DefinitionPath))}")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(duplicates.Length == 0,
            "Duplicate source asset IDs: " + string.Join("; ", duplicates));
    }
}
