using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TankFinishQualificationTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_TANK_BEFORE_RUNTIME")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_TANK_AFTER_RUNTIME"));

    [Theory(Skip = "Compile both catalogs and set FORGELINE_TANK_BEFORE_RUNTIME/FORGELINE_TANK_AFTER_RUNTIME.", SkipUnless = nameof(Enabled))]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void FinishPreservesCompiledGeometryBindingsAndSampledMechanicalMatrices(int hz)
    {
        var before = RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_TANK_BEFORE_RUNTIME")!);
        var after = RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_TANK_AFTER_RUNTIME")!);
        foreach (string suffix in new[] { "", ".turret", ".lod1", ".lod2", ".damaged", ".critical", ".wreck", ".collision" })
        {
            var id = AssetId.Parse("unit.directorate.main_battle_tank" + suffix);
            var oldRecord = before.Get(id); var newRecord = after.Get(id);
            Assert.Equal(oldRecord.Dependencies, newRecord.Dependencies);
            Assert.Equal(oldRecord.MaterialReferences, newRecord.MaterialReferences);
            Assert.Equal(oldRecord.Lods, newRecord.Lods);
            Assert.Equal(oldRecord.CollisionReference, newRecord.CollisionReference);
            Assert.Equal(oldRecord.Sockets, newRecord.Sockets);
            Assert.Equal(oldRecord.MechanicalParts, newRecord.MechanicalParts);
            var a = RuntimeMeshData.FromPayload(before.Read(id).Payload);
            var b = RuntimeMeshData.FromPayload(after.Read(id).Payload);
            Assert.Equal(a.Attributes, b.Attributes); Assert.Equal(a.Indices, b.Indices);
            Assert.Equal(a.MaterialIds, b.MaterialIds); Assert.Equal(a.Sections, b.Sections);
            Assert.Equal(a.Vertices.Count, b.Vertices.Count);
            for (int i = 0; i < a.Vertices.Count; i++)
            {
                var x = a.Vertices[i]; var y = b.Vertices[i];
                Assert.Equal(x with { U = y.U, V = y.V, TangentX = y.TangentX, TangentY = y.TangentY, TangentZ = y.TangentZ }, y);
                Assert.InRange(Vector3.Distance(new(x.TangentX, x.TangentY, x.TangentZ), new(y.TangentX, y.TangentY, y.TangentZ)), 0, .00001f);
                if (suffix is not ("" or ".turret" or ".lod1")) Assert.Equal(x, y);
            }
            MechanicalPartBinding[] Bindings(RuntimeAssetRecord record) => record.MechanicalParts.Select(part =>
                new MechanicalPartBinding(part, record.Sockets.Where(s => s.Name == part.Socket)
                    .Select(s => new Vector3(s.X, s.Y, s.Z)).FirstOrDefault())).ToArray();
            var oldParts = Bindings(oldRecord); var newParts = Bindings(newRecord);
            MechanicalGeometryBatching.Assign(a, oldParts); MechanicalGeometryBatching.Assign(b, newParts);
            Assert.Equal(oldParts, newParts);
            var oldMatrices = new Matrix4x4[oldParts.Length]; var newMatrices = new Matrix4x4[newParts.Length];
            var scale = new Vector3(4, 3, 7.2f);
            var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationY(.37f) * Matrix4x4.CreateTranslation(17, 5, -29);
            foreach (var damage in Enum.GetValues<UnitPresentationDamageState>())
                for (int frame = 0; frame <= hz * 2; frame++)
                {
                    double seconds = (double)frame / hz;
                    var instance = new RenderInstance(new EntityId(1, 1), new(Vector3.Zero, Quaternion.Identity, scale),
                        new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)
                    {
                        UnitFeature = new(UnitIds.MainBattleTank, damage, .6f),
                        MechanicalPose = new(true, seconds * 2, seconds, seconds, 1.55, .2f, 0)
                    };
                    MechanicalTransforms.Compose(oldParts, instance, world, oldMatrices, 1);
                    MechanicalTransforms.Compose(newParts, instance, world, newMatrices, 1);
                    Assert.Equal(oldMatrices, newMatrices);
                }
        }
    }
}
