using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfrastructureSourceQualificationTests
{
    public static bool Enabled=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_ORIGINAL_RUNTIME"));

    [Fact(Skip="Compile original and canonical infrastructure sources first.",SkipUnless=nameof(Enabled))]
    public void CanonicalBlenderSourcesPreserveBindingsAndBoundTheTwoContinuousCurveRefinements()
    {
        var original=RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_ORIGINAL_RUNTIME")!);
        var canonical=RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_CANONICAL_RUNTIME")!);
        Assert.Equal(27,original.Manifest.Assets.Count);
        int meshes=0;
        foreach(var before in original.Manifest.Assets)
        {
            var id=AssetId.Parse(before.Id);var after=canonical.Get(id);
            Assert.Equal(before.Type,after.Type);
            Assert.Equal(before.Dependencies,after.Dependencies);
            Assert.Equal(before.MaterialReferences,after.MaterialReferences);
            Assert.Equal(before.TextureReferences,after.TextureReferences);
            Assert.Equal(before.Lods,after.Lods);
            Assert.Equal(before.CollisionReference,after.CollisionReference);
            Assert.Equal(before.Sockets,after.Sockets);
            Assert.Equal(before.MechanicalParts,after.MechanicalParts);
            if(before.Type!=RuntimeAssetType.Mesh)
            { Assert.Equal(before.RuntimeHash,after.RuntimeHash);continue; }
            var a=RuntimeMeshData.FromPayload(original.Read(id).Payload);
            var b=RuntimeMeshData.FromPayload(canonical.Read(id).Payload);
            Assert.Equal(a.Attributes,b.Attributes);
            Assert.Equal(a.MaterialIds,b.MaterialIds);
            bool shortCurve=before.Id=="infrastructure.directorate.road.curve_short";
            bool longCurve=before.Id=="infrastructure.directorate.road.curve_long";
            if(shortCurve||longCurve)
            {
                Assert.Equal(shortCurve?36:60,b.Vertices.Count);
                Assert.Equal(shortCurve?60:108,b.Indices.Count);
                Assert.True(b.Vertices.Count<=a.Vertices.Count);
                Assert.True(b.Indices.Count<=a.Indices.Count);
                Assert.Equal(a.Sections.Count,b.Sections.Count);
                Assert.Equal(a.Sections[0].MaterialSlot,b.Sections[0].MaterialSlot);
                CheckRange(x=>x.X);CheckRange(x=>x.Y);CheckRange(x=>x.Z);
                CheckRange(x=>x.U);CheckRange(x=>x.V);
                void CheckRange(Func<RuntimeMeshVertex,float> channel)
                {
                    Assert.Equal(a.Vertices.Min(channel),b.Vertices.Min(channel));
                    Assert.Equal(a.Vertices.Max(channel),b.Vertices.Max(channel));
                }
            }
            else if(before.Id.StartsWith("mesh.world.prop.",StringComparison.Ordinal))
            {
                // Upstream prop masters preserve the original seed within the existing static export tolerances.
                Assert.Equal(a.Sections,b.Sections);
                Assert.Equal(a.Indices,b.Indices);
                Assert.Equal(a.Vertices.Count,b.Vertices.Count);
                for(int i=0;i<a.Vertices.Count;i++)
                {
                    var x=a.Vertices[i];var y=b.Vertices[i];
                    Assert.InRange(MathF.Abs(x.X-y.X),0,.00001f);
                    Assert.InRange(MathF.Abs(x.Y-y.Y),0,.00001f);
                    Assert.InRange(MathF.Abs(x.Z-y.Z),0,.00001f);
                    Assert.InRange(MathF.Abs(x.NormalX-y.NormalX),0,.00002f);
                    Assert.InRange(MathF.Abs(x.NormalY-y.NormalY),0,.00002f);
                    Assert.InRange(MathF.Abs(x.NormalZ-y.NormalZ),0,.00002f);
                    Assert.InRange(MathF.Abs(x.U-y.U),0,.000001f);
                    Assert.InRange(MathF.Abs(x.V-y.V),0,.000001f);
                    Assert.InRange(MathF.Abs(x.TangentX-y.TangentX),0,.00002f);
                    Assert.InRange(MathF.Abs(x.TangentY-y.TangentY),0,.00002f);
                    Assert.InRange(MathF.Abs(x.TangentZ-y.TangentZ),0,.00002f);
                    Assert.Equal(x.TangentW,y.TangentW);
                }
            }
            else
            {
                Assert.Equal(a.Sections,b.Sections);
                Assert.Equal(a.Indices,b.Indices);
                Assert.Equal(a.Vertices.ToArray(),b.Vertices.ToArray());
            }
            if(before.Id.StartsWith("infrastructure.directorate.",StringComparison.Ordinal))
                Assert.Contains("infrastructure/directorate/sources/blender/",after.SourcePath.Replace('\\','/'),StringComparison.Ordinal);
            meshes++;
        }
        Assert.Equal(19,meshes);
    }
}
