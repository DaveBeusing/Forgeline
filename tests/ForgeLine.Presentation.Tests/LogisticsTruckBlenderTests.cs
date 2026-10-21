using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class LogisticsTruckBlenderTests
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_TRUCK_BASELINE_RUNTIME")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_TRUCK_CURRENT_RUNTIME"));

    [Theory(Skip = "Set FORGELINE_TRUCK_BASELINE_RUNTIME and FORGELINE_TRUCK_CURRENT_RUNTIME after compiling both catalogs.", SkipUnless = nameof(Enabled))]
    [InlineData("cargo_truck")]
    [InlineData("supply_truck")]
    public void BlenderBindingsPreserveExactCompiledGeometryAndWheelBatching(string family)
    {
        var baseline = RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_TRUCK_BASELINE_RUNTIME")!);
        var current = RuntimeAssetCatalog.Load(Environment.GetEnvironmentVariable("FORGELINE_TRUCK_CURRENT_RUNTIME")!);
        foreach (string suffix in new[] { "", ".lod1", ".lod2", ".damaged", ".critical", ".wreck", ".collision" })
        {
            var id = AssetId.Parse("unit.directorate." + family + suffix);
            var before = baseline.Get(id);
            var after = current.Get(id);
            Assert.Equal(before.Dependencies, after.Dependencies);
            Assert.Equal(before.MaterialReferences, after.MaterialReferences);
            Assert.Equal(before.Lods, after.Lods);
            Assert.Equal(before.CollisionReference, after.CollisionReference);
            Assert.Equal(before.Sockets, after.Sockets);
            Assert.Equal(before.MechanicalParts, after.MechanicalParts);
            var a = RuntimeMeshData.FromPayload(baseline.Read(id).Payload);
            var b = RuntimeMeshData.FromPayload(current.Read(id).Payload);
            Assert.Equal(a.Attributes, b.Attributes);
            Assert.Equal(a.MaterialIds, b.MaterialIds);
            Assert.Equal(a.Sections, b.Sections);
            Assert.Equal(a.Indices, b.Indices);
            Assert.Equal(a.Vertices, b.Vertices);
            var oldParts = before.MechanicalParts.Select(p => new MechanicalPartBinding(p,
                before.Sockets.Where(s => s.Name == p.Socket).Select(s => new System.Numerics.Vector3(s.X,s.Y,s.Z)).FirstOrDefault())).ToArray();
            var newParts = after.MechanicalParts.Select(p => new MechanicalPartBinding(p,
                after.Sockets.Where(s => s.Name == p.Socket).Select(s => new System.Numerics.Vector3(s.X,s.Y,s.Z)).FirstOrDefault())).ToArray();
            MechanicalGeometryBatching.Assign(a, oldParts);
            MechanicalGeometryBatching.Assign(b, newParts);
            Assert.Equal(oldParts, newParts);
        }
    }
}
