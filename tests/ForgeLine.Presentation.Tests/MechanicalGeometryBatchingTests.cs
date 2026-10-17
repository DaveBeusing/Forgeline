using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MechanicalGeometryBatchingTests
{
    private static readonly Vector3 Offset = new(4, 2, 3);
    private static RuntimeMeshData Mesh(string difference = "")
    {
        RuntimeMeshVertex[] first = [V(0, 0, 0, 0, 0), V(1, 0, 0, 1, 0), V(0, 1, 0, 0, 1)];
        var second = first.Select(v => v with { X = v.X + Offset.X, Y = v.Y + Offset.Y, Z = v.Z + Offset.Z }).ToArray();
        second[0] = difference switch
        {
            "uv" => second[0] with { U = .1f }, "normal" => second[0] with { NormalZ = -1 },
            "tangent" => second[0] with { TangentW = -1 }, "rounding" => second[0] with { TangentY = .0000005f }, "shape" => second[0] with { X = second[0].X + .01f }, _ => second[0]
        };
        uint[] indices = difference == "winding" ? [0, 1, 2, 3, 5, 4] : [0, 1, 2, 3, 4, 5];
        return new(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent, [.. first, .. second], indices, ["material.test"], [new(0, 6, 0)]);
    }
    private static RuntimeMeshVertex V(float x, float y, float z, float u, float v) => new(x, y, z, 0, 0, 1, u, v, 1, 0, 0, 1);
    private static MechanicalPartBinding[] Parts() =>
        [new(new("wheel-a", 0, 3, -1, MechanicalMotionKind.Wheel, "a"), Vector3.Zero), new(new("wheel-b", 3, 3, -1, MechanicalMotionKind.Wheel, "b"), Offset)];

    [Theory]
    [InlineData(1, 1, 1)] [InlineData(-2, 3, 4)] [InlineData(3.2f, 2.4f, 6.4f)]
    public void ReusedTriangleRangePreservesEveryAnimatedVertexAndNormalBasis(float x, float y, float z)
    {
        var mesh = Mesh(); var parts = Parts(); MechanicalGeometryBatching.Assign(mesh, parts);
        Assert.Equal(0, parts[0].GeometryPartIndex); Assert.Equal(0, parts[1].GeometryPartIndex); Assert.Equal(Offset, parts[1].GeometryOffset);
        var world = Matrix4x4.CreateScale(x, y, z) * Matrix4x4.CreateRotationY(.6f) * Matrix4x4.CreateTranslation(90, 15, 40);
        var instance = new RenderInstance(new EntityId(1, 1), RenderTransform.Identity, new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)
        { MechanicalPose = new(true, 1.7, 0, 1, -1, 0, 0), UnitFeature = new(UnitIds.CargoTruck, UnitPresentationDamageState.Intact) };
        var transforms = new Matrix4x4[2]; MechanicalTransforms.Compose(parts, instance, world, transforms);
        var reused = Matrix4x4.CreateTranslation(parts[1].GeometryOffset) * transforms[1];
        for (int i = 0; i < 3; i++)
        {
            var a = mesh.Vertices[i]; var b = mesh.Vertices[i + 3];
            Assert.True(Vector3.Distance(Vector3.Transform(new(a.X, a.Y, a.Z), reused), Vector3.Transform(new(b.X, b.Y, b.Z), transforms[1])) < .0001f);
        }
        Assert.Equal(Vector3.TransformNormal(Vector3.UnitZ, transforms[1]), Vector3.TransformNormal(Vector3.UnitZ, reused));
    }
    [Theory]
    [InlineData("uv")] [InlineData("normal")] [InlineData("tangent")] [InlineData("shape")] [InlineData("winding")]
    public void DifferentSurfaceDataOrTriangleOrderCannotBorrowGeometry(string difference)
    {
        var parts = Parts(); MechanicalGeometryBatching.Assign(Mesh(difference), parts); Assert.Equal(1, parts[1].GeometryPartIndex); Assert.Equal(Vector3.Zero, parts[1].GeometryOffset);
    }
    [Fact]
    public void TangentRoundoffAndDistinctAuthoredPivotsDoNotPreventEquivalentTriangleReuse()
    {
        var parts = Parts(); parts[1] = parts[1] with { Pivot = new(7, 2, 3) };
        MechanicalGeometryBatching.Assign(Mesh("rounding"), parts);
        Assert.Equal(0, parts[1].GeometryPartIndex); Assert.Equal(Offset, parts[1].GeometryOffset);
    }
    [Fact]
    public void MatchingRunsAfterTheFirstCanonicalPartRemainStableAndEmptyMeshesDoNotInventParts()
    {
        var parts = Parts(); MechanicalGeometryBatching.Assign(Mesh(), parts); var copy = parts.ToArray(); MechanicalGeometryBatching.Assign(Mesh(), parts); Assert.Equal(copy, parts);
        MechanicalGeometryBatching.Assign(Mesh(), []);
    }
}
