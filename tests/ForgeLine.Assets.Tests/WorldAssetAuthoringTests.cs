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
            Assert.Equal(373, result.CompiledCount);

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

            foreach (string family in new[] { "conifer", "scrub", "grass_clump", "broadleaf" })
            {
                RuntimeMeshData high = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse($"mesh.world.vegetation.{family}")).Payload);
                RuntimeMeshData low = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse($"mesh.world.vegetation.{family}_lod1")).Payload);
                Assert.True(low.Indices.Count < high.Indices.Count);
                Assert.Equal(high.MaterialIds.ToArray(), low.MaterialIds.ToArray());
                foreach (RuntimeMeshData mesh in new[] { high, low })
                {
                    Assert.True(mesh.HasUv0 && mesh.HasTangents);
                    Assert.Equal(0, mesh.Vertices.Min(x => x.Y), 5);
                    Assert.Equal(1, mesh.Vertices.Max(x => x.Y), 5);
                    Assert.True(mesh.Vertices.Max(x => x.Z) - mesh.Vertices.Min(x => x.Z) > .8f,
                        $"{family} must remain volumetric instead of a single plane.");
                    Assert.All(mesh.Vertices, x =>
                    {
                        Assert.InRange(x.X, -.50001f, .50001f);
                        Assert.InRange(x.Z, -.50001f, .50001f);
                    });
                }
            }

            foreach (string name in new[] { "rock_slab", "rock_spire", "barrier_module", "pipe_elbow", "loading_gantry", "road_edge", "bridge_apron", "loading_apron", "service_fence" })
            {
                string id = $"mesh.world.prop.{name}";
                RuntimeAssetRecord record = catalog.Get(AssetId.Parse(id));
                Assert.Equal("mesh.world.kit.collision_centered", record.CollisionReference);
                var high = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(id)).Payload);
                var low = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(id + "_lod1")).Payload);
                Assert.True(high.Indices.Count > low.Indices.Count);
                Assert.Equal(high.MaterialIds.ToArray(), low.MaterialIds.ToArray());
                foreach (var mesh in new[] { high, low })
                {
                    Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                    Assert.Single(mesh.Sections);
                    Assert.Equal("material.world.prop.industrial", Assert.Single(mesh.MaterialIds));
                    Assert.All(mesh.Vertices, v =>
                    {
                        Assert.InRange(v.X, -.50001f, .50001f);
                        Assert.InRange(v.Y, -.50001f, .50001f);
                        Assert.InRange(v.Z, -.50001f, .50001f);
                    });
                }
            }
            foreach (string suffix in new[] { "", "_lod1" })
            {
                var tree = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse("mesh.world.vegetation.broadleaf" + suffix)).Payload);
                var root = tree.Vertices.Where(v => v.Y == 0).ToArray();
                Assert.NotEmpty(root);
                Assert.Equal(0, root.Min(v => v.X) + root.Max(v => v.X), 5);
                Assert.Equal(0, root.Min(v => v.Z) + root.Max(v => v.Z), 5);
            }
            Assert.Equal("mesh.world.kit.collision_rooted", catalog.Get(AssetId.Parse("mesh.world.vegetation.broadleaf")).CollisionReference);
            var ore = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse("mesh.world.resource.ferrous_ore")).Payload);
            Assert.Equal(-.4892709f, ore.Vertices.Min(v => v.X), 5);
            Assert.Equal(.4902523f, ore.Vertices.Max(v => v.X), 5);
            Assert.Equal(-.5f, ore.Vertices.Min(v => v.Y), 5);
            Assert.Equal(.21f, ore.Vertices.Max(v => v.Y), 5);

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
