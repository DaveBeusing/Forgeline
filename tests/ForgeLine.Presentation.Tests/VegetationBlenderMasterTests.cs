using System.Numerics;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class VegetationBlenderMasterTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_VEGETATION_MASTERS"));

    [Fact(Skip = "Run native vegetation qualification first.", SkipUnless = nameof(Enabled))]
    public void CompiledExportsPreserveLodBoundsPbrChannelsAndScopedFamilyChanges()
    {
        string root = Environment.GetEnvironmentVariable("FORGELINE_VEGETATION_MASTERS")!;
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, "baseline/runtime"));
        Assert.Equal(13, baseline.Manifest.Assets.Count);
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
                Assert.Equal(before.Sockets, after.Sockets);
                Assert.Equal(before.MechanicalParts, after.MechanicalParts);
                Assert.Equal(before.Bounds, after.Bounds);
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
                if (before.Id.StartsWith("mesh.world.vegetation.", StringComparison.Ordinal))
                    Assert.True(b.HasUv0 && b.HasTangents);
                bool conifer = before.Id.StartsWith("mesh.world.vegetation.conifer", StringComparison.Ordinal);
                bool reshaped = before.Id.Contains(".scrub", StringComparison.Ordinal) || before.Id.Contains(".grass_clump", StringComparison.Ordinal);
                int stemVertices = before.Id.EndsWith("_lod1", StringComparison.Ordinal) ? 36 : 60;
                for (int i = 0; i < a.Vertices.Count; i++)
                {
                    var x = a.Vertices[i];
                    var y = b.Vertices[i];
                    if (!reshaped)
                    {
                        Assert.Equal(new Vector3(x.X, x.Y, x.Z), new Vector3(y.X, y.Y, y.Z));
                        Assert.Equal(new Vector3(x.NormalX, x.NormalY, x.NormalZ), new Vector3(y.NormalX, y.NormalY, y.NormalZ));
                    }
                    Assert.InRange(y.Y, 0, 1);
                    Assert.InRange(y.X, -.500001f, .500001f);
                    Assert.InRange(y.Z, -.500001f, .500001f);
                    Assert.InRange(new Vector3(y.NormalX, y.NormalY, y.NormalZ).Length(), .9999f, 1.0001f);
                    float u = x.U + (conifer && i < stemVertices ? .5f : 0);
                    float v = x.V - (conifer && i < stemVertices ? .25f : 0);
                    Assert.InRange(MathF.Abs(u - y.U), 0, .000001f);
                    Assert.InRange(MathF.Abs(v - y.V), 0, .000001f);
                    if (!reshaped)
                    {
                        Assert.InRange(Vector3.Distance(new(x.TangentX, x.TangentY, x.TangentZ), new(y.TangentX, y.TangentY, y.TangentZ)), 0, .00002f);
                        Assert.Equal(x.TangentW, y.TangentW);
                    }
                    if (b.HasTangents)
                    {
                        Assert.InRange(new Vector3(y.TangentX, y.TangentY, y.TangentZ).Length(), .9999f, 1.0001f);
                        Assert.True(y.TangentW is -1 or 1);
                    }
                    else Assert.False(a.HasTangents);
                }
            }
        }
        var unchanged = RuntimeAssetCatalog.Load(Path.Combine(root, "first/runtime"));
        var edited = RuntimeAssetCatalog.Load(Path.Combine(root, "edited/runtime"));
        foreach (string name in new[] { "conifer", "broadleaf", "scrub", "grass_clump" })
        {
            var id = AssetId.Parse("mesh.world.vegetation." + name);
            var a = RuntimeMeshData.FromPayload(unchanged.Read(id).Payload);
            var b = RuntimeMeshData.FromPayload(edited.Read(id).Payload);
            int moved = 0;
            for (int i = 0; i < a.Vertices.Count; i++)
            {
                Assert.InRange(MathF.Abs(a.Vertices[i].Y - b.Vertices[i].Y), 0, .000001f);
                if (MathF.Abs(a.Vertices[i].X - b.Vertices[i].X) > .0009f) moved++;
            }
            Assert.Equal(1, moved);
        }
    }
}
