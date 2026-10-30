using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceRuntimeSealTests
{
    [Fact]
    public void EvidenceProducerRejectsLegacyCatalogAndChangedOrMissingListedBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-reference-seal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "test.flasset");
            File.WriteAllBytes(file, new byte[20]);
            var record = RuntimeAssetIntegrity.SealRecord(file, new RuntimeAssetRecord
            {
                Id = "mesh.test.seal", Type = RuntimeAssetType.Mesh, SourcePath = "test.glb", RuntimePath = "test.flasset",
                SourceHash = new string('a', 64), BuildHash = new string('b', 64), RuntimeVersion = RuntimeAssetIntegrity.SupportedRuntimeVersion,
                CompilerVersion = "test-seal"
            });
            var legacy = new RuntimeAssetManifest { CompilerVersion = "test-seal", Assets = [record] };
            void Save(RuntimeAssetManifest manifest) => File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest, RuntimeAssetCatalog.CreateJsonOptions()));
            Save(legacy);
            Assert.NotNull(RuntimeAssetCatalog.Load(root)); // Historical inspectability stays compatible.
            Assert.Throws<InvalidDataException>(() => ReferenceRuntimeSeal.Load(root, out _));
            Save(RuntimeAssetIntegrity.SealManifest(legacy));
            var actual = ReferenceRuntimeSeal.Load(root, out object seal);
            Assert.Single(actual.AssetIds);
            Assert.Contains("allRecordSizesAndHashesVerified", JsonSerializer.Serialize(seal));
            File.WriteAllBytes(file, new byte[21]);
            Assert.Throws<InvalidDataException>(() => ReferenceRuntimeSeal.Load(root, out _));
            File.Delete(file);
            Assert.Throws<InvalidDataException>(() => ReferenceRuntimeSeal.Load(root, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
