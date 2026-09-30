using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateUnitAssetAuthoringTests
{
    private static readonly UnitAssetExpectation[] Units =
    [
        new(
            "rifle_squad",
            ["weapon_muzzle"]),
        new(
            "scout_vehicle",
            ["weapon_muzzle", "sensor_origin"]),
        new(
            "main_battle_tank",
            ["turret_pivot", "gun_pivot", "weapon_muzzle", "recoil_anchor"]),
        new(
            "self_propelled_artillery",
            ["gun_pivot", "weapon_muzzle", "recoil_anchor"]),
        new(
            "cargo_truck",
            ["cargo_load"]),
        new(
            "supply_truck",
            ["cargo_load", "supply_transfer"])
    ];

    [Fact]
    public void DirectorateVerticalSliceUnitFamiliesCompileWithLodsCollisionSocketsAndSymbols()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-directorate-units-" +
                Guid.NewGuid().ToString("N"));

        try
        {
            AssetCompilationResult result =
                AssetPipelineCompiler.Compile(
                    Path.Combine(
                        repositoryRoot,
                        "assets",
                        "source"),
                    runtimeRoot,
                    clean: true);

            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(
                        static diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Message}")));
            Assert.Equal(
                99,
                result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            foreach (UnitAssetExpectation unit in Units)
            {
                string primaryId =
                    $"unit.directorate.{unit.Name}";
                RuntimeAssetRecord primary =
                    catalog.Get(
                        AssetId.Parse(
                            primaryId));

                Assert.Equal(
                    RuntimeAssetType.Mesh,
                    primary.Type);
                Assert.Equal(
                    $"{primaryId}.collision",
                    primary.CollisionReference);
                Assert.Contains(
                    primary.Lods,
                    lod =>
                        lod.Level == 1 &&
                        lod.AssetId ==
                        $"{primaryId}.lod1");
                Assert.Contains(
                    primary.Lods,
                    lod =>
                        lod.Level == 2 &&
                        lod.AssetId ==
                        $"{primaryId}.lod2");

                foreach (string requiredSocket in
                         unit.RequiredSockets)
                {
                    Assert.Contains(
                        primary.Sockets,
                        socket =>
                            socket.Name ==
                            requiredSocket);
                }

                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod1")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod2")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.collision")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"material.directorate.unit.{unit.Name}")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"material.directorate.symbol.{unit.Name}")));
            }
        }
        finally
        {
            if (Directory.Exists(
                    runtimeRoot))
            {
                Directory.Delete(
                    runtimeRoot,
                    recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ForgeLine.sln")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test host.");
    }

    private sealed record UnitAssetExpectation(
        string Name,
        string[] RequiredSockets);
}
