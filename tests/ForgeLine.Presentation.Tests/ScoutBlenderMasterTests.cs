using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ScoutBlenderMasterTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_SCOUT_MASTER"));

    [Fact(Skip = "Run build/scout/Qualify-Master.ps1 with real Blender first.", SkipUnless = nameof(Enabled))]
    public void SavedMastersPreserveCompiledStatesLodsSocketsAndSurfaceChannels()
    {
        string root = Environment.GetEnvironmentVariable("FORGELINE_SCOUT_MASTER")!;
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, "baseline/runtime"));
        string prefix = "unit.directorate.scout_vehicle";
        Assert.Equal(8, baseline.Manifest.Assets.Count(x => x.Type == RuntimeAssetType.Mesh && x.Id.StartsWith(prefix, StringComparison.Ordinal)));
        foreach (string variant in new[] { "first", "second", "reopened" })
        {
            var candidate = RuntimeAssetCatalog.Load(Path.Combine(root, variant, "runtime"));
            Assert.Equal(baseline.Manifest.Assets.Select(x => x.Id).Order(), candidate.Manifest.Assets.Select(x => x.Id).Order());
            foreach (var before in baseline.Manifest.Assets)
            {
                var id = AssetId.Parse(before.Id);
                var after = candidate.Get(id);
                Assert.Equal(before.Type, after.Type);
                Assert.Equal(before.Dependencies, after.Dependencies);
                Assert.Equal(before.MaterialReferences, after.MaterialReferences);
                Assert.Equal(before.TextureReferences, after.TextureReferences);
                Assert.Equal(before.Lods, after.Lods);
                Assert.Equal(before.CollisionReference, after.CollisionReference);
                Assert.Equal(before.AnimationReferences, after.AnimationReferences);
                Assert.Equal(before.Sockets, after.Sockets);
                Assert.Equal(before.MechanicalParts, after.MechanicalParts);
                if (before.Type != RuntimeAssetType.Mesh)
                {
                    Assert.Equal(before.RuntimeHash, after.RuntimeHash);
                    continue;
                }
                var a = RuntimeMeshData.FromPayload(baseline.Read(id).Payload);
                var b = RuntimeMeshData.FromPayload(candidate.Read(id).Payload);
                Assert.Equal(a.Attributes, b.Attributes);
                Assert.Equal(a.MaterialIds, b.MaterialIds);
                Assert.Equal(a.Sections, b.Sections);
                Assert.Equal(a.Indices, b.Indices);
                Assert.Equal(a.Vertices.Count, b.Vertices.Count);
                for (int i = 0; i < a.Vertices.Count; i++)
                {
                    var x = a.Vertices[i];
                    var y = b.Vertices[i];
                    Assert.True(MathF.Max(MathF.Abs(x.X - y.X), MathF.Max(MathF.Abs(x.Y - y.Y), MathF.Abs(x.Z - y.Z))) <= .00001f, before.Id);
                    Assert.True(Vector3.Distance(new(x.NormalX, x.NormalY, x.NormalZ), new(y.NormalX, y.NormalY, y.NormalZ)) <= .00002f, before.Id);
                    Assert.True(MathF.Abs(x.U - y.U) <= .000001f && MathF.Abs(x.V - y.V) <= .000001f, before.Id);
                    Assert.True(Vector3.Distance(new(x.TangentX, x.TangentY, x.TangentZ), new(y.TangentX, y.TangentY, y.TangentZ)) <= .00002f, before.Id);
                    Assert.Equal(x.TangentW, y.TangentW);
                }
            }
        }
        string? seed = Environment.GetEnvironmentVariable("FORGELINE_SCOUT_RUNTIME_SEED");
        if (!string.IsNullOrWhiteSpace(seed))
        {
            var original = RuntimeAssetCatalog.Load(seed);
            var replacement = RuntimeAssetCatalog.Load(Path.Combine(root, "second/runtime"));
            var replacements = replacement.Manifest.Assets.ToDictionary(x => x.Id);
            string target = Path.Combine(root, "overlay/runtime");
            Directory.CreateDirectory(target);
            foreach (var record in original.Manifest.Assets)
            {
                bool replaced = replacements.TryGetValue(record.Id, out var changed);
                var chosen = replaced ? changed! : record;
                string source = replaced ? Path.Combine(root, "second/runtime") : seed;
                string path = Path.Combine(target, chosen.RuntimePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(source, chosen.RuntimePath), path, overwrite: false);
            }
            var manifest = RuntimeAssetIntegrity.SealManifest(original.Manifest with
            {
                Assets = original.Manifest.Assets.Select(x => replacements.GetValueOrDefault(x.Id, x)).ToArray()
            });
            File.WriteAllBytes(Path.Combine(target, RuntimeAssetCatalog.ManifestFileName),
                JsonSerializer.SerializeToUtf8Bytes(manifest, RuntimeAssetCatalog.CreateJsonOptions()));
            Assert.True(RuntimeAssetQualification.Run(target, requireIntegrity: true).Success);
        }
    }
}
