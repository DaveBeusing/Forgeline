using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class ScatterAssetTests
{
    private static ScatterAssetMetadata Metadata => new()
    {
        FamilyId = "mesh.test.plant",
        GroundAnchor = new(0, 0, 0),
        NaturalSizeMeters = new(7, 17, 7),
        UniformScale = new(.8f, 1.2f),
        TintMultiplier = new(.9f, 1.08f),
        YawDegrees = new(0, 360),
        SlopeDegrees = new(0, 30),
        MinimumSpacingMeters = 7,
        BiomeAffinities = ["biome.dry", "biome.temperate"],
    };

    [Fact]
    public void RejectsInvalidRangesVersionsAndAuthority()
    {
        ScatterAssetMetadata[] invalid =
        [
            Metadata with { GeneratorVersion = 2 },
            Metadata with { UniformScale = new(2, 1) },
            Metadata with { TintMultiplier = new(float.NaN, 1) },
            Metadata with { GroundAnchor = new(float.PositiveInfinity, 0, 0) },
            Metadata with { NaturalSizeMeters = new(0, 17, 7) },
            Metadata with { MinimumSpacingMeters = -1 },
            Metadata with { BiomeAffinities = ["biome.dry", "biome.dry"] },
            Metadata with { BiomeAffinities = ["biome.temperate", "biome.dry"] },
            Metadata with { Interaction = (ScatterInteraction)2 },
        ];
        foreach (var metadata in invalid)
            Assert.Throws<InvalidDataException>(() => ScatterAssetValidation.Validate(metadata, RuntimeAssetType.Mesh));
        Assert.Throws<InvalidDataException>(() => ScatterAssetValidation.Validate(Metadata, RuntimeAssetType.Texture));
        Assert.Throws<InvalidDataException>(() => ScatterAssetValidation.Validate(Metadata with { GroundAnchor = new(0, -1, 0) },
            RuntimeAssetType.Mesh, new(-.5f, 0, -.5f, .5f, 1, .5f)));
    }

    [Fact]
    public void MetadataCompilesDeterministicallyAndLoadsWithoutSources()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-scatter-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(source);
        try
        {
            float[] positions = [-.5f, 0, 0, .5f, 0, 0, 0, 1, .5f];
            byte[] bytes = new byte[positions.Length * sizeof(float)];
            Buffer.BlockCopy(positions, 0, bytes, 0, bytes.Length);
            File.WriteAllText(Path.Combine(source, "plant.gltf"), JsonSerializer.Serialize(new
            {
                asset = new { version = "2.0" },
                buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String(bytes), byteLength = bytes.Length } },
                bufferViews = new[] { new { buffer = 0, byteLength = bytes.Length } },
                accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3" } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 } } } } },
            }));
            WriteDefinition(Metadata);
            byte[] originalDefinition = File.ReadAllBytes(Path.Combine(source, "plant.asset.json"));
            var first = AssetPipelineCompiler.Compile(source, runtime, clean: true);
            Assert.True(first.Success, string.Join("; ", first.Diagnostics.Select(d => d.Message)));
            var catalog = RuntimeAssetCatalog.Load(runtime);
            string signature = catalog.Manifest.ContentSignature;
            var second = AssetPipelineCompiler.Compile(source, runtime, clean: true);
            Assert.True(second.Success);
            Assert.Equal(signature, RuntimeAssetCatalog.Load(runtime).Manifest.ContentSignature);

            WriteDefinition(Metadata with { MinimumSpacingMeters = 8 });
            Assert.Equal(originalDefinition, File.ReadAllBytes(Path.Combine(source, "plant.asset.json")));
            Assert.True(AssetPipelineCompiler.Compile(source, runtime).Success);
            Assert.NotEqual(signature, RuntimeAssetCatalog.Load(runtime).Manifest.ContentSignature);
            File.WriteAllText(Path.Combine(source, "orphan.scatter.json"), "{}");
            var orphan = AssetPipelineCompiler.Compile(source, runtime);
            Assert.False(orphan.Success);
            Assert.Contains(orphan.Diagnostics, d => d.Code == "ASSET052");
            File.Delete(Path.Combine(source, "orphan.scatter.json"));
            Directory.Delete(source, recursive: true);
            catalog = RuntimeAssetCatalog.LoadCancellable(runtime, CancellationToken.None, requireIntegrity: true);
            var record = catalog.Get(AssetId.Parse("mesh.test.plant"));
            Assert.Equal(8, record.Scatter!.MinimumSpacingMeters);
            Assert.Equal(Metadata.FamilyId, record.Scatter.FamilyId);
            Assert.Equal(Metadata.BiomeAffinities, record.Scatter.BiomeAffinities);
            Assert.NotEmpty(catalog.Read(AssetId.Parse(record.Id)).Payload);
            Assert.True(RuntimeAssetQualification.Run(runtime, requireIntegrity: true).Success);

            void WriteDefinition(ScatterAssetMetadata metadata)
            {
                File.WriteAllText(Path.Combine(source, "plant.asset.json"),
                    JsonSerializer.Serialize(new SourceAssetDefinition { Id = "mesh.test.plant", Type = RuntimeAssetType.Mesh,
                        Source = "plant.gltf" }, RuntimeAssetCatalog.CreateJsonOptions()));
                File.WriteAllText(Path.Combine(source, "plant.scatter.json"),
                    JsonSerializer.Serialize(metadata, RuntimeAssetCatalog.CreateJsonOptions()));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
