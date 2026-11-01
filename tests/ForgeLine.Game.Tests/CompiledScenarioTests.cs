using System.Security.Cryptography;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CompiledScenarioTests
{
    [Fact]
    public void ProductiveCompiledStartHasCurrentCheckpointsAndUnchangedGeography()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026, enableDebugCapture: true), TestContext.Current.CancellationToken);
        Assert.Equal(ScenarioRegistry.CentralDivideDescriptor.Identity, runtime.RuntimeSettings.Composition.Identity);
        BattlefieldMapArtifact.Capture(CentralDivideBattlefield.Create()).ValidateMatches(runtime.Battlefield);
        Assert.Equal(20, MatchAuthoritativeSnapshot.CurrentSchemaVersion);
        using var independent = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026, enableDebugCapture: true), TestContext.Current.CancellationToken);
        string initial = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        Assert.Equal(initial, MatchAuthoritativeSnapshot.Capture(independent).ComputeSha256());
        runtime.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        independent.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        string afterTwenty = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        Assert.Equal(afterTwenty, MatchAuthoritativeSnapshot.Capture(independent).ComputeSha256());
        Assert.NotEqual("E8A27383CE21EFE848672254CAA4D2E1F7ECD9912E59F174C6762E9884E0324C", initial);
        Assert.NotEqual("14D58A81A68CC2437CA714001BB323859E04A9F4D759D77BBF16F614A2606E99", afterTwenty);
        // Schema 20: independently captured with the same Validation recipe and no presentation observer.
        Assert.Equal("A5215C615B70EF19EE0D64DCF994C2EF4526E4CF8296AE6FCF26401D276CFAC5", initial);
        Assert.Equal("822EA719E5A002667DE4B98E014D329FFA8C7E0912E60DAF41E6F3D0FE3A2CB3", afterTwenty);
        var report = BattlefieldOperationalGeographyValidator.Validate(runtime.Battlefield, runtime.Terrain,
            new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = 0.5f },
            new NavigationSectorSettings { SectorSizeCells = 8 });
        Assert.Equal(71, report.ReachabilityChecks);
        Assert.Equal(2, report.AlternateNavigationChecks);
        Assert.Equal(2, report.AlternateRoadChecks);
    }

    [Fact]
    public void RegisteredArtifactValueChangesActualResourceState()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var artifact = BattlefieldMapArtifact.Deserialize(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "maps", ScenarioRegistry.CentralDivideMapPath)));
            artifact.Resources[0] = artifact.Resources[0] with { TotalQuantity = 12345 };
            artifact = artifact with { MapRevision = 2 };
            byte[] bytes = artifact.Serialize();
            string digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var descriptor = ScenarioRegistry.CentralDivideDescriptor with
            {
                Key = "test.compiled-resource.v1",
                Identity = ScenarioRegistry.CentralDivideDescriptor.Identity with { MapSha256 = digest, MapRevision = 2 }
            };
            string path = Path.Combine(root, descriptor.MapPath);
            File.WriteAllBytes(path, bytes);
            File.WriteAllText(path + ".sha256", digest);
            var registry = new ScenarioRegistry(root, [descriptor]);
            var composition = registry.Resolve(descriptor.Key);
            using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation) with { Composition = composition }, TestContext.Current.CancellationToken);
            Assert.Equal(12345, runtime.Simulation.Entities.GetComponent<ResourceDeposit>(runtime.BattlefieldRuntime.ResourceEntities[0]).RemainingQuantity);
            Assert.Equal(descriptor.Identity, runtime.RuntimeSettings.Composition.Identity);
            File.AppendAllText(path, " ");
            Assert.Throws<InvalidOperationException>(() => registry.Resolve(descriptor.Key));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MissingTamperedUnknownAndEscapingMapsFailExplicitly()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-scenario-" + Guid.NewGuid().ToString("N"));
        var registry = new ScenarioRegistry(root, [ScenarioRegistry.CentralDivideDescriptor]);
        Assert.Throws<DirectoryNotFoundException>(() => registry.Resolve(CentralDivideScenario.CompositionKey));
        Assert.False(Assert.Single(registry.Inspect()).Available);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("unknown"));
        Assert.Throws<ArgumentException>(() => new ScenarioRegistry(root, [ScenarioRegistry.CentralDivideDescriptor with { MapPath = "../escape.json" }]));
        Assert.Throws<ArgumentException>(() => new ScenarioRegistry(root, [ScenarioRegistry.CentralDivideDescriptor with { MapPath = Path.GetFullPath("escape.json") }]));
        Assert.Throws<ArgumentException>(() => new ScenarioRegistry(root, [ScenarioRegistry.CentralDivideDescriptor, ScenarioRegistry.CentralDivideDescriptor]));
    }
}
