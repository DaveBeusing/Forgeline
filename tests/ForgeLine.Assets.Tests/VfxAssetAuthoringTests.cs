using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class VfxAssetAuthoringTests
{
    private static readonly string[] MeshAssetIds =
    [
        "vfx.combat.muzzle.infantry",
        "vfx.combat.muzzle.machine_gun",
        "vfx.combat.muzzle.tank_cannon",
        "vfx.combat.muzzle.artillery",
        "vfx.combat.projectile.bullet_tracer",
        "vfx.combat.projectile.cannon_shell",
        "vfx.combat.projectile.artillery_shell",
        "vfx.combat.projectile.generic_explosive",
        "vfx.combat.impact.dirt",
        "vfx.combat.impact.metal",
        "vfx.combat.impact.concrete",
        "vfx.combat.impact.armor",
        "vfx.combat.impact.explosive",
        "vfx.combat.explosion.small",
        "vfx.combat.explosion.medium",
        "vfx.combat.explosion.vehicle",
        "vfx.combat.explosion.building",
        "vfx.combat.explosion.ammunition_secondary",
        "vfx.combat.persistent.light_smoke",
        "vfx.combat.persistent.heavy_smoke",
        "vfx.combat.persistent.fire",
        "vfx.combat.persistent.sparks",
        "vfx.combat.persistent.dust",
        "vfx.destruction.vehicle_burst",
        "vfx.destruction.building_burst",
        "vfx.destruction.debris",
        "vfx.destruction.smoke_plume",
        "vfx.destruction.persistent_fire",
        "vfx.destruction.spark_emission",
        "vfx.destruction.dust_cloud",
        "vfx.logistics.loading",
        "vfx.logistics.unloading",
        "vfx.logistics.resource_transfer",
        "vfx.logistics.supply_transfer",
        "vfx.logistics.refuel",
        "vfx.logistics.rearm"
    ];

    private static readonly string[] MaterialAssetIds =
    [
        "material.vfx.muzzle",
        "material.vfx.projectile",
        "material.vfx.impact.dirt",
        "material.vfx.impact.metal",
        "material.vfx.explosion",
        "material.vfx.smoke",
        "material.vfx.fire",
        "material.vfx.spark",
        "material.vfx.dust",
        "material.vfx.logistics"
    ];

    [Fact]
    public void RequiredVfxFamiliesCompileAndLoadThroughRuntimeCatalog()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-vfx-" +
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
                275,
                result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            foreach (string id in
                     MeshAssetIds)
            {
                RuntimeAssetRecord record =
                    catalog.Get(
                        AssetId.Parse(
                            id));

                Assert.Equal(
                    RuntimeAssetType.Mesh,
                    record.Type);
                Assert.NotEmpty(
                    record.MaterialReferences);
            }

            foreach (string id in
                     MaterialAssetIds)
            {
                Assert.Equal(
                    RuntimeAssetType.Material,
                    catalog.Get(
                        AssetId.Parse(
                            id)).Type);
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
