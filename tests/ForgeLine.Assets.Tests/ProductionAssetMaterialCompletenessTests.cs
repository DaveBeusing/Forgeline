using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class ProductionAssetMaterialCompletenessTests
{
    private const int ExpectedSharedTextureCount = 19;
    private const long ExpectedSharedResidentBytes = 103_740;

    [Fact]
    public void PhysicalProductionMeshesResolveCompleteTexturedMaterials()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-production-materials-" +
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

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);
            RuntimeAssetRecord[] productionMeshes =
                catalog.Manifest.Assets
                    .Where(
                        static record =>
                            record.Type ==
                            RuntimeAssetType.Mesh &&
                            IsPhysicalProductionMesh(
                                record.Id))
                    .OrderBy(
                        static record =>
                            record.Id,
                        StringComparer.Ordinal)
                    .ToArray();

            Assert.NotEmpty(
                productionMeshes);

            var issues =
                new List<string>();

            foreach (RuntimeAssetRecord record in
                     productionMeshes)
            {
                ValidateMesh(
                    catalog,
                    record,
                    issues);
            }

            Assert.True(
                issues.Count == 0,
                "Production material inventory contains unresolved entries:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    issues));
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

    [Fact]
    public void SharedDirectorateTextureLibraryStaysWithinBaselineBudget()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-material-budget-" +
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
                result.Success);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);
            RuntimeAssetRecord[] textures =
                catalog.Manifest.Assets
                    .Where(
                        static record =>
                            record.Type ==
                            RuntimeAssetType.Texture &&
                            record.Id.StartsWith(
                                "texture.directorate.material.",
                                StringComparison.Ordinal))
                    .OrderBy(
                        static record =>
                            record.Id,
                        StringComparer.Ordinal)
                    .ToArray();

            Assert.Equal(
                ExpectedSharedTextureCount,
                textures.Length);

            long residentBytes =
                0;
            long compiledBytes =
                0;

            foreach (RuntimeAssetRecord record in
                     textures)
            {
                RuntimeTextureData texture =
                    RuntimeTextureData.FromPayload(
                        catalog.Read(
                            AssetId.Parse(
                                record.Id))
                        .Payload);
                residentBytes +=
                    texture.ResidentByteCount;
                compiledBytes +=
                    new FileInfo(
                        Path.Combine(
                            runtimeRoot,
                            record.RuntimePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)))
                    .Length;
            }

            Assert.Equal(
                ExpectedSharedResidentBytes,
                residentBytes);
            Assert.InRange(
                compiledBytes,
                ExpectedSharedResidentBytes,
                160_000);
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

    private static void ValidateMesh(
        RuntimeAssetCatalog catalog,
        RuntimeAssetRecord record,
        List<string> issues)
    {
        if (record.MaterialReferences.Count == 0)
        {
            issues.Add(
                $"{record.Id}: no production material reference.");
            return;
        }

        RuntimeMeshData mesh =
            RuntimeMeshData.FromPayload(
                catalog.Read(
                    AssetId.Parse(
                        record.Id))
                .Payload);

        if (!mesh.HasUv0)
        {
            issues.Add(
                $"{record.Id}: UV0 is unavailable.");
        }

        if (mesh.Sections.Any(
                static section =>
                    section.MaterialSlot < 0))
        {
            issues.Add(
                $"{record.Id}: one or more sections use the development fallback.");
        }

        if (mesh.MaterialIds.Count > 1)
        {
            issues.Add(
                $"{record.Id}: current production baseline expects one reusable material per render mesh.");
        }

        foreach (string materialId in
                 record.MaterialReferences)
        {
            RuntimeMaterialData material;
            try
            {
                material =
                    RuntimeMaterialData.FromPayload(
                        catalog.Read(
                            AssetId.Parse(
                                materialId))
                        .Payload);
            }
            catch (Exception exception)
            {
                issues.Add(
                    $"{record.Id}: material '{materialId}' could not be resolved ({exception.Message}).");
                continue;
            }

            ValidateTexture(
                catalog,
                record.Id,
                materialId,
                "Base Color",
                material.BaseColorTexture,
                RuntimeTextureUsage.BaseColor,
                issues);
            ValidateTexture(
                catalog,
                record.Id,
                materialId,
                "Normal",
                material.NormalTexture,
                RuntimeTextureUsage.Normal,
                issues);
            ValidateTexture(
                catalog,
                record.Id,
                materialId,
                "ORM",
                material.OrmTexture,
                RuntimeTextureUsage.Orm,
                issues);

            if (material.NormalTexture is not null &&
                !mesh.HasTangents)
            {
                issues.Add(
                    $"{record.Id}: normal-mapped material '{materialId}' has no tangent basis.");
            }
        }

        foreach (AssetLodReference lod in
                 record.Lods)
        {
            RuntimeAssetRecord lodRecord =
                catalog.Get(
                    AssetId.Parse(
                        lod.AssetId));

            if (!record.MaterialReferences.SequenceEqual(
                    lodRecord.MaterialReferences,
                    StringComparer.Ordinal))
            {
                issues.Add(
                    $"{record.Id}: LOD '{lod.AssetId}' changes the stable material identity.");
            }
        }
    }

    private static void ValidateTexture(
        RuntimeAssetCatalog catalog,
        string meshId,
        string materialId,
        string channel,
        AssetId? textureId,
        RuntimeTextureUsage expectedUsage,
        List<string> issues)
    {
        if (textureId is null)
        {
            issues.Add(
                $"{meshId}: material '{materialId}' is missing {channel}.");
            return;
        }

        try
        {
            RuntimeTextureData texture =
                RuntimeTextureData.FromPayload(
                    catalog.Read(
                        textureId.Value)
                    .Payload);

            if (texture.Usage !=
                expectedUsage)
            {
                issues.Add(
                    $"{meshId}: material '{materialId}' {channel} resolves to usage {texture.Usage}.");
            }

            if (texture.Mips.Count <= 1)
            {
                issues.Add(
                    $"{meshId}: material '{materialId}' {channel} has no complete mip chain.");
            }
        }
        catch (Exception exception)
        {
            issues.Add(
                $"{meshId}: material '{materialId}' {channel} could not be resolved ({exception.Message}).");
        }
    }

    private static bool IsPhysicalProductionMesh(
        string id)
    {
        if (id.Contains(
                ".collision",
                StringComparison.Ordinal))
        {
            return false;
        }

        return id.StartsWith(
                   "unit.directorate.",
                   StringComparison.Ordinal) ||
               id.StartsWith(
                   "building.directorate.",
                   StringComparison.Ordinal) ||
               id.StartsWith(
                   "infrastructure.directorate.",
                   StringComparison.Ordinal) ||
               id.StartsWith(
                   "mesh.world.prop.",
                   StringComparison.Ordinal) ||
               id.StartsWith(
                   "mesh.world.resource.",
                   StringComparison.Ordinal) ||
               id.StartsWith(
                   "mesh.world.vegetation.",
                   StringComparison.Ordinal);
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
