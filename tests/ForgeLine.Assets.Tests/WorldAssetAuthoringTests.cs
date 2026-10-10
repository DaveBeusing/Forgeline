using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class WorldAssetAuthoringTests
{
    [Fact]
    public void VerticalSliceWorldAssetsCompileThroughRuntimePipeline()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string sourceRoot =
            Path.Combine(
                repositoryRoot,
                "assets",
                "source");
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-world-assets-" +
                Guid.NewGuid().ToString("N"));

        try
        {
            AssetCompilationResult result =
                ProductionAssetFixture.CompileTo(runtimeRoot);

            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(
                        static diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Message}")));
            Assert.Equal(341, result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            string[] requiredIds =
            [
                "material.world.terrain.grass_ground",
                "material.world.terrain.dirt",
                "material.world.terrain.mud",
                "material.world.terrain.rock",
                "material.world.terrain.gravel",
                "material.world.terrain.industrial_ground",
                "material.world.terrain.concrete",
                "material.world.terrain.scorched",
                "material.world.decal.tire_tracks",
                "material.world.decal.tracked_vehicle_marks",
                "material.world.decal.road_wear",
                "material.world.decal.oil_stain",
                "material.world.decal.blast_mark",
                "material.world.decal.shell_impact",
                "material.world.decal.scorch_mark",
                "material.world.decal.concrete_crack",
                "mesh.world.prop.rock",
                "mesh.world.prop.barrier",
                "mesh.world.prop.concrete_block",
                "mesh.world.prop.crate",
                "mesh.world.prop.drum",
                "mesh.world.prop.pallet",
                "mesh.world.prop.pipe_section",
                "mesh.world.prop.utility_box",
                "mesh.world.prop.fence",
                "mesh.world.prop.industrial_light_signage",
                "mesh.world.prop.rubble",
                "mesh.world.vegetation.conifer",
                "mesh.world.vegetation.scrub",
                "mesh.world.vegetation.grass_clump",
                "mesh.world.resource.ferrous_ore",
                "mesh.world.resource.silicates",
                "mesh.world.resource.volatiles",
                "mesh.world.resource.rare_elements",
                "material.world.symbol.resource.ferrous_ore",
                "material.world.symbol.resource.silicates",
                "material.world.symbol.resource.volatiles",
                "material.world.symbol.resource.rare_elements"
            ];

            foreach (string id in requiredIds)
            {
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(id)),
                    $"Compiled world catalog is missing '{id}'.");
            }

            string[] terrainMaterialIds =
                requiredIds[..8];

            foreach (string id in terrainMaterialIds)
            {
                RuntimeMaterialData material =
                    RuntimeMaterialData.FromPayload(
                        catalog.Read(
                            AssetId.Parse(
                                id))
                        .Payload);

                Assert.NotNull(
                    material.BaseColorTexture);
                Assert.NotNull(
                    material.NormalTexture);
                Assert.NotNull(
                    material.OrmTexture);

                RuntimeTextureData baseColor =
                    RuntimeTextureData.FromPayload(
                        catalog.Read(
                            material.BaseColorTexture!.Value)
                        .Payload);
                RuntimeTextureData normal =
                    RuntimeTextureData.FromPayload(
                        catalog.Read(
                            material.NormalTexture!.Value)
                        .Payload);
                RuntimeTextureData orm =
                    RuntimeTextureData.FromPayload(
                        catalog.Read(
                            material.OrmTexture!.Value)
                        .Payload);

                Assert.Equal(
                    RuntimeTextureUsage.BaseColor,
                    baseColor.Usage);
                Assert.Equal(
                    RuntimeTextureUsage.Normal,
                    normal.Usage);
                Assert.Equal(
                    RuntimeTextureUsage.Orm,
                    orm.Usage);
                Assert.True(
                    baseColor.Mips.Count > 1);
                Assert.True(
                    normal.Mips.Count > 1);
                Assert.True(
                    orm.Mips.Count > 1);
            }

            RuntimeAssetRecord ferrous =
                catalog.Get(
                    AssetId.Parse(
                        "mesh.world.resource.ferrous_ore"));
            Assert.Contains(
                ferrous.Lods,
                static lod =>
                    lod.Level == 1 &&
                    lod.AssetId ==
                    "mesh.world.resource.ferrous_ore_lod1");

            string[] resourceMeshIds =
            [
                "mesh.world.resource.ferrous_ore",
                "mesh.world.resource.silicates",
                "mesh.world.resource.volatiles",
                "mesh.world.resource.rare_elements"
            ];
            var highDetailIndexCounts =
                new HashSet<int>();

            foreach (string resourceId in resourceMeshIds)
            {
                RuntimeMeshData highDetail =
                    RuntimeMeshData.FromPayload(
                        catalog.Read(
                            AssetId.Parse(
                                resourceId))
                        .Payload);
                RuntimeMeshData reduced =
                    RuntimeMeshData.FromPayload(
                        catalog.Read(
                            AssetId.Parse(
                                $"{resourceId}_lod1"))
                        .Payload);

                Assert.True(
                    highDetail.Indices.Count >
                    36,
                    $"Resource mesh '{resourceId}' must remain clustered geometry rather than the generic box.");
                Assert.True(
                    reduced.Indices.Count >
                    36,
                    $"Reduced resource mesh '{resourceId}_lod1' must preserve a clustered silhouette.");
                highDetailIndexCounts.Add(
                    highDetail.Indices.Count);
            }

            Assert.Equal(
                resourceMeshIds.Length,
                highDetailIndexCounts.Count);

            RuntimeAssetRecord vegetation =
                catalog.Get(
                    AssetId.Parse(
                        "mesh.world.vegetation.conifer"));
            Assert.Contains(
                vegetation.Lods,
                static lod =>
                    lod.Level == 1);
        }
        finally
        {
            if (Directory.Exists(runtimeRoot))
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
