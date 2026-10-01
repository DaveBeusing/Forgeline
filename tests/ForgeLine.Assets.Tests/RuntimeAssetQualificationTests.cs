using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RuntimeAssetQualificationTests
{
    [Fact]
    public void QualificationRecordsRuntimeFootprintAndValidatesReferences()
    {
        using var runtime =
            new RuntimeFixture();

        RuntimeAssetRecord texture =
            Record(
                "texture.test.armor",
                RuntimeAssetType.Texture,
                "textures/test/armor.flasset");
        RuntimeAssetRecord material =
            Record(
                "material.test.armor",
                RuntimeAssetType.Material,
                "materials/test/armor.flasset") with
            {
                TextureReferences =
                [
                    texture.Id
                ],
                Dependencies =
                [
                    texture.Id
                ]
            };
        RuntimeAssetRecord lod =
            Record(
                "mesh.test.vehicle.lod1",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle/lod1.flasset");
        RuntimeAssetRecord mesh =
            Record(
                "mesh.test.vehicle",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle.flasset") with
            {
                MaterialReferences =
                [
                    material.Id
                ],
                Dependencies =
                [
                    material.Id,
                    lod.Id
                ],
                Lods =
                [
                    new AssetLodReference(
                        1,
                        lod.Id,
                        250.0f)
                ],
                Bounds =
                    new AssetBounds(
                        -1.0f,
                        0.0f,
                        -2.0f,
                        1.0f,
                        2.0f,
                        2.0f)
            };

        runtime.WriteAsset(
            texture,
            payloadBytes: 32);
        runtime.WriteAsset(
            material,
            payloadBytes: 16);
        runtime.WriteAsset(
            lod,
            payloadBytes: 48);
        runtime.WriteAsset(
            mesh,
            payloadBytes: 96);
        runtime.WriteManifest(
            texture,
            material,
            lod,
            mesh);

        RuntimeAssetQualificationReport report =
            RuntimeAssetQualification.Run(
                runtime.Root);

        Assert.True(
            report.Success);
        Assert.Equal(
            4,
            report.AssetCount);
        Assert.Equal(
            2,
            report.MeshCount);
        Assert.Equal(
            1,
            report.TextureCount);
        Assert.Equal(
            1,
            report.MaterialCount);
        Assert.True(
            report.TotalRuntimeBytes >
            0);
        Assert.True(
            report.MeshRuntimeBytes >
            report.TextureRuntimeBytes);
        Assert.Empty(
            report.Issues);
        Assert.Equal(
            mesh.Id,
            report.LargestAssets[0].AssetId);
        Assert.True(
            report.AssetReadDuration >=
            TimeSpan.Zero);
    }

    [Fact]
    public void QualificationFailsPredictablyWhenRuntimePayloadIsMissing()
    {
        using var runtime =
            new RuntimeFixture();
        RuntimeAssetRecord missing =
            Record(
                "texture.test.missing",
                RuntimeAssetType.Texture,
                "textures/test/missing.flasset");

        runtime.WriteManifest(
            missing);

        RuntimeAssetQualificationReport report =
            RuntimeAssetQualification.Run(
                runtime.Root);

        Assert.False(
            report.Success);
        Assert.Contains(
            report.Issues,
            static issue =>
                issue.Code ==
                "ASSETQ001");
    }

    [Fact]
    public void QualificationRejectsBrokenLodChains()
    {
        using var runtime =
            new RuntimeFixture();
        RuntimeAssetRecord mesh =
            Record(
                "mesh.test.vehicle",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle.flasset") with
            {
                Lods =
                [
                    new AssetLodReference(
                        1,
                        "mesh.test.missing_lod",
                        250.0f)
                ]
            };

        runtime.WriteAsset(
            mesh,
            payloadBytes: 32);
        runtime.WriteManifest(
            mesh);

        RuntimeAssetQualificationReport report =
            RuntimeAssetQualification.Run(
                runtime.Root);

        Assert.False(
            report.Success);
        Assert.Contains(
            report.Issues,
            static issue =>
                issue.Code ==
                "ASSETQ006" &&
                issue.Message.Contains(
                    "missing_lod",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void QualificationRejectsNonIncreasingLodDistances()
    {
        using var runtime =
            new RuntimeFixture();
        RuntimeAssetRecord lod1 =
            Record(
                "mesh.test.vehicle.lod1",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle/lod1.flasset");
        RuntimeAssetRecord lod2 =
            Record(
                "mesh.test.vehicle.lod2",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle/lod2.flasset");
        RuntimeAssetRecord mesh =
            Record(
                "mesh.test.vehicle",
                RuntimeAssetType.Mesh,
                "meshes/test/vehicle.flasset") with
            {
                Lods =
                [
                    new AssetLodReference(
                        1,
                        lod1.Id,
                        500.0f),
                    new AssetLodReference(
                        2,
                        lod2.Id,
                        400.0f)
                ]
            };

        runtime.WriteAsset(
            mesh,
            payloadBytes: 32);
        runtime.WriteAsset(
            lod1,
            payloadBytes: 16);
        runtime.WriteAsset(
            lod2,
            payloadBytes: 8);
        runtime.WriteManifest(
            mesh,
            lod1,
            lod2);

        RuntimeAssetQualificationReport report =
            RuntimeAssetQualification.Run(
                runtime.Root);

        Assert.False(
            report.Success);
        Assert.Contains(
            report.Issues,
            static issue =>
                issue.Code ==
                "ASSETQ004");
    }

    private static RuntimeAssetRecord Record(
        string id,
        RuntimeAssetType type,
        string runtimePath) =>
        new()
        {
            Id = id,
            Type = type,
            SourcePath =
                $"source/{id}.source",
            RuntimePath =
                runtimePath,
            SourceHash =
                "source-hash",
            BuildHash =
                "build-hash",
            RuntimeVersion = 1,
            CompilerVersion =
                "test"
        };

    private sealed class RuntimeFixture : IDisposable
    {
        public RuntimeFixture()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "forgeline-asset-qualification-" +
                    Guid.NewGuid().ToString(
                        "N"));
            Directory.CreateDirectory(
                Root);
        }

        public string Root { get; }

        public void WriteAsset(
            RuntimeAssetRecord record,
            int payloadBytes)
        {
            string path =
                Path.Combine(
                    Root,
                    record.RuntimePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

            RuntimeAssetFile.Write(
                path,
                record.Type,
                ReadOnlySpan<byte>.Empty,
                new byte[payloadBytes]);
        }

        public void WriteManifest(
            params RuntimeAssetRecord[] records)
        {
            var manifest =
                new RuntimeAssetManifest
                {
                    CompilerVersion =
                        "test",
                    Assets =
                        records
                };

            File.WriteAllText(
                Path.Combine(
                    Root,
                    RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.Serialize(
                    manifest,
                    RuntimeAssetCatalog.CreateJsonOptions()));
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    Root))
            {
                Directory.Delete(
                    Root,
                    recursive: true);
            }
        }
    }
}
