using System.Numerics;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldPropPipeAxisTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_PROP_PIPE_AXIS"));

    [Fact(Skip = "Run the native straight-pipe axis qualifier first.", SkipUnless = nameof(Enabled))]
    public void BakedPipeRotationPreservesBoundsUvTopologyAndMaterialBindings()
    {
        string root = Environment.GetEnvironmentVariable("FORGELINE_PROP_PIPE_AXIS")!;
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, "baseline/runtime"));
        foreach (string variant in new[] { "first", "second", "reopened" })
        {
            var candidate = RuntimeAssetCatalog.Load(Path.Combine(root, variant, "runtime"));
            Assert.Equal(baseline.Manifest.Assets.Select(x => x.Id).Order(), candidate.Manifest.Assets.Select(x => x.Id).Order());
            foreach (var before in baseline.Manifest.Assets)
            {
                var id = AssetId.Parse(before.Id);
                var after = candidate.Get(id);
                Assert.Equal(before.Dependencies, after.Dependencies);
                Assert.Equal(before.MaterialReferences, after.MaterialReferences);
                Assert.Equal(before.Lods, after.Lods);
                Assert.Equal(before.CollisionReference, after.CollisionReference);
                Assert.Equal(before.Sockets, after.Sockets);
                if (before.Type != RuntimeAssetType.Mesh)
                {
                    Assert.Equal(before.RuntimeHash, after.RuntimeHash);
                    continue;
                }
                Assert.StartsWith("mesh.world.prop.pipe_section", before.Id, StringComparison.Ordinal);
                var a = RuntimeMeshData.FromPayload(baseline.Read(id).Payload);
                var b = RuntimeMeshData.FromPayload(candidate.Read(id).Payload);
                Assert.Equal(a.Attributes, b.Attributes);
                Assert.Equal(a.Indices, b.Indices);
                Assert.Equal(a.Sections, b.Sections);
                Assert.Equal(a.MaterialIds, b.MaterialIds);
                Assert.Equal(a.Vertices.Count, b.Vertices.Count);
                Assert.Equal(new Vector3(-.5f), new(b.Vertices.Min(x => x.X), b.Vertices.Min(x => x.Y), b.Vertices.Min(x => x.Z)));
                Assert.Equal(new Vector3(.5f), new(b.Vertices.Max(x => x.X), b.Vertices.Max(x => x.Y), b.Vertices.Max(x => x.Z)));
                for (int i = 0; i < a.Vertices.Count; i++)
                {
                    var x = a.Vertices[i]; var y = b.Vertices[i];
                    Assert.InRange(Vector3.Distance(new(x.Z, x.Y, -x.X), new(y.X, y.Y, y.Z)), 0, .00001f);
                    Assert.InRange(Vector3.Distance(new(x.NormalZ, x.NormalY, -x.NormalX), new(y.NormalX, y.NormalY, y.NormalZ)), 0, .00002f);
                    Assert.InRange(Vector3.Distance(new(x.TangentZ, x.TangentY, -x.TangentX), new(y.TangentX, y.TangentY, y.TangentZ)), 0, .00002f);
                    Assert.InRange(MathF.Abs(x.U - y.U), 0, .000001f);
                    Assert.InRange(MathF.Abs(x.V - y.V), 0, .000001f);
                    Assert.Equal(x.TangentW, y.TangentW);
                }
            }
        }
    }
}
