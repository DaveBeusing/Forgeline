using System.Text;
using System.Text.Json.Nodes;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GeneratedMapTests
{
    private static readonly string Assets = new('a', 64);
    private static readonly Lazy<GeneratedMapPackage> Fixture = new(() => GeneratedMapCompiler.Compile(new(), Assets));
    private static GeneratedMapPackage Map => Fixture.Value;

    [Fact]
    public void IndependentRunsHaveIdenticalCanonicalPackagesAndDecoration()
    {
        var second = GeneratedMapCompiler.Compile(new(), Assets, TestContext.Current.CancellationToken);
        Assert.Equal(Map.GetManifestBytes(), second.GetManifestBytes());
        Assert.Equal(Map.Manifest.GameplaySha256, second.Manifest.GameplaySha256);
        Assert.Equal(Map.Manifest.Terrain, second.Manifest.Terrain);
        Assert.Equal(Map.Manifest.DecorationSha256, second.Manifest.DecorationSha256);
        Assert.Equal(Map.GenerateDecoration(new(4, 5)).GetCanonicalBytes(), second.GenerateDecoration(new(4, 5)).GetCanonicalBytes());
        Assert.Equal(0, Map.Audit.ErrorCount);
        Assert.Equal(0, Map.Audit.WarningCount);
        Assert.Empty(Map.Definition.WorldObjects);
        Assert.NotEmpty(Map.Definition.TacticalAreas.Areas);
        Assert.True(Map.Geography.TerrainElevationRangeMeters >= 20);
    }

    [Fact]
    public void VisualChangesPreserveGameplayAndTerrainIdentity()
    {
        var visual = GeneratedMapCompiler.Compile(new() { ScatterDensityPermille = 100 }, new string('b', 64), TestContext.Current.CancellationToken);
        Assert.Equal(Map.Manifest.GameplaySha256, visual.Manifest.GameplaySha256);
        Assert.Equal(Map.Manifest.Terrain, visual.Manifest.Terrain);
        Assert.NotEqual(Map.Manifest.DecorationSha256, visual.Manifest.DecorationSha256);
        Assert.NotEqual(Map.Manifest.VisualSha256, visual.Manifest.VisualSha256);
        Assert.NotEqual(Map.ManifestSha256, visual.ManifestSha256);
    }

    [Fact]
    public void SeedAndTerrainChangesChangeGameplayIdentity()
    {
        var original = new GeneratedMapRecipe();
        var changedSeed = GeneratedMapCompiler.Compile(original with
        { Terrain = original.Terrain with { Generation = original.Terrain.Generation with { Seed = 18 } } }, Assets, TestContext.Current.CancellationToken);
        var changedTerrain = GeneratedMapCompiler.Compile(original with
        { Terrain = original.Terrain with { BaseHeightMillimeters = 21_000 } }, Assets, TestContext.Current.CancellationToken);
        Assert.NotEqual(Map.Manifest.GameplaySha256, changedSeed.Manifest.GameplaySha256);
        Assert.NotEqual(Map.Manifest.GameplaySha256, changedTerrain.Manifest.GameplaySha256);
    }

    [Theory]
    [InlineData(GeneratedMapPackage.GeographyFileName)]
    [InlineData(GeneratedMapPackage.TerrainFileName)]
    public void TamperedOrMissingSidecarsFail(string name)
    {
        using var directory = Export();
        string path = Path.Combine(directory.Path, name);
        var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        Assert.Contains("integrity", Assert.Throws<InvalidDataException>(() => Load(directory)).Message);
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => Load(directory));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("generator")]
    [InlineData("profile")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("size")]
    [InlineData("rehashed-sidecar")]
    [InlineData("forged-hash")]
    public void UntrustedHeaderAndRehashedContentCannotIntroduceGeography(string mutation)
    {
        using var directory = Export();
        string path = Path.Combine(directory.Path, GeneratedMapPackage.ManifestFileName);
        var header = JsonNode.Parse(File.ReadAllBytes(path))!;
        switch (mutation)
        {
            case "version": header["FormatVersion"] = 99; break;
            case "generator": header["Recipe"]!["GeneratorVersion"] = 99; break;
            case "profile": header["Recipe"]!["TacticalProfileId"] = "foreign.script.v1"; break;
            case "unknown": header["Executable"] = "foreign.dll"; break;
            case "size": header["Recipe"]!["Terrain"]!["Generation"]!["WidthInChunks"] = int.MaxValue; break;
            case "forged-hash": header["GameplaySha256"] = new string('0', 64); break;
            case "rehashed-sidecar":
                string geometry = Path.Combine(directory.Path, GeneratedMapPackage.GeographyFileName);
                var bytes = File.ReadAllBytes(geometry); bytes[0] ^= 1; File.WriteAllBytes(geometry, bytes);
                header["Geography"]!["Sha256"] = GeneratedMapCompiler.Hash(bytes); break;
        }
        string json = header.ToJsonString();
        if (mutation == "duplicate") json = "{\"FormatVersion\":1," + json[1..];
        File.WriteAllText(path, json, new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => Load(directory));
    }

    [Fact]
    public void AssetConflictExpectedIdentityAndInputBudgetsFailBeforeReconstruction()
    {
        using var directory = Export();
        Assert.Contains("asset identity", Assert.Throws<InvalidDataException>(() => GeneratedMapPackage.Load(directory.Path, new string('b', 64), cancellationToken: TestContext.Current.CancellationToken)).Message);
        Assert.Contains("manifest integrity", Assert.Throws<InvalidDataException>(() => GeneratedMapPackage.Load(directory.Path, Assets, new string('0', 64), TestContext.Current.CancellationToken)).Message);
        File.WriteAllBytes(Path.Combine(directory.Path, GeneratedMapPackage.ManifestFileName), new byte[GeneratedMapPackage.MaximumManifestBytes + 1]);
        Assert.Contains("budget", Assert.Throws<InvalidDataException>(() => Load(directory)).Message);
    }

    [Fact]
    public void LoadedPackageRoundTripsSaveAndReplayWithExactIdentity()
    {
        using var directory = Export();
        var loaded = GeneratedMapPackage.Load(directory.Path, Assets, Map.ManifestSha256, TestContext.Current.CancellationToken);
        MatchComposition Resolve(string key) => key == loaded.CompositionKey ? loaded.CreateComposition() : throw new InvalidOperationException("Unknown map.");
        using var source = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,
            2026, enableDebugCapture: true, composition: loaded.CreateComposition()), TestContext.Current.CancellationToken);
        source.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(source)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(source)));
        Assert.Equal(loaded.ManifestSha256, save.Configuration.Identity!.Generated!.ManifestSha256);
        using var restored = MatchPersistenceService.Restore(save, Resolve);
        using var playback = MatchPersistenceService.PlayReplay(replay, Resolve);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        Assert.Throws<InvalidOperationException>(() => ScenarioRegistry.Default.Resolve(loaded.CompositionKey));
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save));
        var noIdentity = save with { Configuration = save.Configuration with { Identity = null } };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(noIdentity, Resolve));
        var forged = save with { Configuration = save.Configuration with { Identity = save.Configuration.Identity with
            { Generated = save.Configuration.Identity.Generated with { ManifestSha256 = new string('0', 64) } } } };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(forged, Resolve));
        var wrongVersion = save with { Configuration = save.Configuration with { Identity = save.Configuration.Identity with
            { Generated = save.Configuration.Identity.Generated with { GeneratorVersion = 99 } } } };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(wrongVersion, Resolve));
    }

    [Fact]
    public void CallerMutationCannotChangeRetainedPackageAndExportNeverOverwrites()
    {
        var map = Map.Definition;
        ((BattlefieldStartPosition[])map.Starts)[0] = default;
        map.TerrainVisual.MaterialAssetIds[0] = "changed";
        var terrain = Map.CreateTerrain();
        ((TerrainChunk[])terrain.Chunks)[0] = null!;
        Assert.True(Map.Definition.Starts[0].Player.IsSpecified);
        Assert.NotEqual("changed", Map.Definition.TerrainVisual.MaterialAssetIds[0]);
        Assert.NotNull(Map.CreateTerrain().Chunks[0]);
        using var directory = Export();
        Assert.Throws<IOException>(() => Map.Write(directory.Path));
        Assert.Equal(Map.GetManifestBytes(), File.ReadAllBytes(Path.Combine(directory.Path, GeneratedMapPackage.ManifestFileName)));
    }

    private static GeneratedMapPackage Load(DirectoryLease directory) => GeneratedMapPackage.Load(directory.Path, Assets, cancellationToken: TestContext.Current.CancellationToken);
    private static DirectoryLease Export()
    {
        var directory = new DirectoryLease(); Map.Write(directory.Path); return directory;
    }
    private sealed class DirectoryLease : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgeline-map-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
