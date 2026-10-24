using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayVisualAssetContractTests
{
    [Theory]
    [InlineData("material.directorate.building.command_core", RuntimeAssetType.Material)]
    [InlineData("material.infrastructure.directorate.conveyor", RuntimeAssetType.Material)]
    [InlineData("infrastructure.directorate.conveyor.straight_standard.idle_no_input", RuntimeAssetType.Mesh)]
    [InlineData("infrastructure.directorate.conveyor.curve_90.right.flowing.lod2", RuntimeAssetType.Mesh)]
    [InlineData("infrastructure.directorate.conveyor.support_light.tier1.structural_intact", RuntimeAssetType.Mesh)]
    public void RejectsInternallyConsistentOlderCatalogMissingRendererSelectedVisual(string missing, RuntimeAssetType type)
    {
        var requirements = GameplayVisualAssetContract.RequiredAssets();
        Assert.Equal(type, requirements[missing]);
        WithCatalog(requirements.Where(x => x.Key != missing).Select(Record), catalog =>
        {
            var error = Assert.Throws<InvalidDataException>(() => GameplayVisualAssetContract.Validate(catalog, TestContext.Current.CancellationToken));
            Assert.Contains(missing, error.Message);
            Assert.Contains("Start-DevelopmentClient.ps1", error.Message);
        });
    }

    [Fact]
    public void RejectsWrongTypeEvenWhenTheSelectedIdExists()
    {
        const string id = "material.directorate.building.command_core";
        WithCatalog(GameplayVisualAssetContract.RequiredAssets().Select(pair =>
            Record(pair.Key == id ? new(pair.Key, RuntimeAssetType.Mesh) : pair)), catalog =>
            Assert.Contains("expected Material, found Mesh",
                Assert.Throws<InvalidDataException>(() => GameplayVisualAssetContract.Validate(catalog, TestContext.Current.CancellationToken)).Message));
    }

    [Fact]
    public void AcceptsCompleteCatalogAndHonorsCancellation()
    {
        WithCatalog(GameplayVisualAssetContract.RequiredAssets().Select(Record), catalog =>
        {
            GameplayVisualAssetContract.Validate(catalog, TestContext.Current.CancellationToken);
            Assert.Throws<OperationCanceledException>(() =>
                GameplayVisualAssetContract.Validate(catalog, new CancellationToken(true)));
        });
    }

    [Fact]
    public void EveryRendererSelectedBuildingAndConveyorVariantHasAnAuthoredAsset()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgeLine.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var authored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(directory.FullName, "assets", "source"),
            "*.asset.json", SearchOption.AllDirectories))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            authored.Add(root.GetProperty("id").GetString()!, root.GetProperty("type").GetString()!);
        }
        foreach (var (id, type) in GameplayVisualAssetContract.RequiredAssets())
        {
            Assert.True(authored.TryGetValue(id, out var actual), $"Missing authoring definition: {id}");
            Assert.Equal(type.ToString(), actual, ignoreCase: true);
        }
    }

    private static RuntimeAssetRecord Record(KeyValuePair<string, RuntimeAssetType> pair) => new()
    {
        Id = pair.Key, Type = pair.Value, SourcePath = "source", RuntimePath = pair.Key + ".flasset",
        SourceHash = new('a', 64), BuildHash = new('b', 64), CompilerVersion = "test", RuntimeVersion = 4
    };

    private static void WithCatalog(IEnumerable<RuntimeAssetRecord> records, Action<RuntimeAssetCatalog> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "gameplay-visual-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(
                new RuntimeAssetManifest { CompilerVersion = "test", Assets = records.ToArray() },
                RuntimeAssetCatalog.CreateJsonOptions()));
            test(RuntimeAssetCatalog.Load(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
