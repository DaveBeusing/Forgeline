using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourceDepositMaterialCacheTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_MATERIAL_CACHE")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_RUNTIME_SEED"));

    [Fact(Skip = "Set a qualified canonical runtime seed and a fresh material cache destination.", SkipUnless = nameof(Enabled))]
    public void SeedOnlyVerifiedUnchangedMaterialsAndTexturesForIndependentMeshCompiles()
    {
        string seed = Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_RUNTIME_SEED")!;
        string target = Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_MATERIAL_CACHE")!;
        Assert.False(Directory.Exists(target));
        var original = RuntimeAssetCatalog.Load(seed);
        foreach (string family in new[] { "ferrous_ore", "silicates", "volatiles", "rare_elements" })
        {
            var ids = new HashSet<string>();
            var pending = new Queue<string>();
            pending.Enqueue("material.world.resource." + family);
            while (pending.TryDequeue(out var id))
            {
                if (!ids.Add(id)) continue;
                var record = original.Get(AssetId.Parse(id));
                Assert.True(record.Type is RuntimeAssetType.Material or RuntimeAssetType.Texture);
                foreach (string dependency in record.Dependencies) pending.Enqueue(dependency);
            }
            var records = original.Manifest.Assets.Where(record => ids.Contains(record.Id)).ToArray();
            Assert.Equal(4, records.Length);
            string output = Path.Combine(target, family);
            Directory.CreateDirectory(output);
            foreach (var record in records)
            {
                string source = Path.Combine(seed, record.RuntimePath);
                Assert.True(RuntimeAssetIntegrity.Matches(source, record));
                string destination = Path.Combine(output, record.RuntimePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            var manifest = RuntimeAssetIntegrity.SealManifest(original.Manifest with { Assets = records });
            File.WriteAllBytes(Path.Combine(output, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.SerializeToUtf8Bytes(manifest, RuntimeAssetCatalog.CreateJsonOptions()));
            Assert.True(RuntimeAssetQualification.Run(output, requireIntegrity: true).Success);
            Assert.DoesNotContain(records, record => record.Type == RuntimeAssetType.Mesh);
        }
    }
}
