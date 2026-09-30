using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateBuildingAssetAuthoringTests
{
    private static readonly string[] BuildingFamilies =
    [
        "command_core",
        "extractor",
        "smelter",
        "electronics_plant",
        "fuel_refinery",
        "vehicle_factory",
        "storage_depot",
        "supply_depot",
        "power_plant"
    ];

    [Fact]
    public void DirectorateBuildingsAndInfrastructureCompileWithStableRuntimeContracts()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-directorate-buildings-" +
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
                210,
                result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            foreach (string family in
                     BuildingFamilies)
            {
                string primaryId =
                    $"building.directorate.{family}";
                RuntimeAssetRecord primary =
                    catalog.Get(
                        AssetId.Parse(
                            primaryId));

                Assert.Equal(
                    RuntimeAssetType.Mesh,
                    primary.Type);
                Assert.Equal(
                    "building.directorate.module.collision_box",
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
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod1")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod2")));
            }

            string[] sharedIds =
            [
                "building.directorate.module.foundation",
                "building.directorate.module.structural_frame",
                "building.directorate.module.partial_shell",
                "building.directorate.module.state_idle",
                "building.directorate.module.state_unpowered",
                "building.directorate.module.state_damaged",
                "building.directorate.module.state_critical",
                "building.directorate.module.destroyed",
                "building.directorate.module.collision_box",
                "material.directorate.building.structural",
                "material.directorate.building.unpowered",
                "material.directorate.building.damaged",
                "material.directorate.building.critical",
                "material.directorate.building.destroyed",
                "material.directorate.symbol.building.command",
                "material.directorate.symbol.building.extraction",
                "material.directorate.symbol.building.processing",
                "material.directorate.symbol.building.factory",
                "material.directorate.symbol.building.storage",
                "material.directorate.symbol.building.supply",
                "material.directorate.symbol.building.power",
                "infrastructure.directorate.road.straight",
                "infrastructure.directorate.road.curve_short",
                "infrastructure.directorate.road.curve_long",
                "infrastructure.directorate.road.junction_t",
                "infrastructure.directorate.road.junction_cross",
                "infrastructure.directorate.road.yard_transition",
                "infrastructure.directorate.road.shoulder",
                "infrastructure.directorate.road.damaged",
                "infrastructure.directorate.road.destroyed",
                "infrastructure.directorate.bridge.road.intact",
                "infrastructure.directorate.bridge.road.damaged",
                "infrastructure.directorate.bridge.road.destroyed",
                "material.directorate.infrastructure.road",
                "material.directorate.infrastructure.bridge",
                "material.directorate.symbol.infrastructure.road",
                "material.directorate.symbol.infrastructure.bridge"
            ];

            foreach (string id in sharedIds)
            {
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            id)),
                    $"Compiled catalog is missing '{id}'.");
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
}
