using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfrastructureSourceQualificationTests
{
    public static bool Enabled=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_INFRASTRUCTURE_ORIGINAL_RUNTIME"));

    [Fact(Skip="Compile original and canonical infrastructure sources first.",SkipUnless=nameof(Enabled))]
    public void CanonicalBlenderSourcesPreserveAllCompiledGeometryAndBindings()
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
            Assert.Equal(a.Sections,b.Sections);
            Assert.Equal(a.Indices,b.Indices);
            Assert.Equal(a.Vertices.ToArray(),b.Vertices.ToArray());
            if(!before.Id.Contains("collision",StringComparison.Ordinal))
                Assert.Contains("infrastructure/directorate/sources/blender/",after.SourcePath.Replace('\\','/'),StringComparison.Ordinal);
            meshes++;
        }
        Assert.Equal(19,meshes);
    }
}
