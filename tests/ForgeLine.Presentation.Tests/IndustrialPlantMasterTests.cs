using System.Numerics;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class IndustrialPlantMasterTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_INDUSTRIAL_PLANT_MASTERS"));

    [Theory(Skip = "Run build/industrial-plants/Qualify-Masters.ps1 with real Blender first.", SkipUnless = nameof(Enabled))]
    [InlineData("fuel_refinery")]
    [InlineData("ammunition_plant")]
    public void SavedMastersPreserveCompiledStatesLodsSocketsAndSurfaceChannels(string family)
    {
        string root = Path.Combine(Environment.GetEnvironmentVariable("FORGELINE_INDUSTRIAL_PLANT_MASTERS")!, family);
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, "baseline/runtime"));
        string prefix = "building.directorate." + family;
        Assert.Equal(11, baseline.Manifest.Assets.Count(x => x.Type == RuntimeAssetType.Mesh && x.Id.StartsWith(prefix, StringComparison.Ordinal)));
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
    }
}
