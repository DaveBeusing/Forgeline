using System.Numerics;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateSurfaceConsistencyTests
{
    [Fact]
    public void CompressedAtlasKeepsDielectricsNonmetallicAcrossEveryMip()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-surface-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(ProductionAssetFixture.CompileTo(root).Success);
            var catalog = RuntimeAssetCatalog.Load(root);
            var orm = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse("texture.directorate.surface.atlas_orm")).Payload);
            Assert.Equal(RuntimeTextureColorSpace.Linear, orm.ColorSpace);
            var decoder = new BCnEncoder.Decoder.BcDecoder();
            foreach (var mip in orm.Mips)
            {
                var pixels = decoder.DecodeRaw(mip.Pixels, mip.Width, mip.Height, BCnEncoder.Shared.CompressionFormat.Bc7);
                // Interior probes deliberately exclude the authored exposed-metal wear.
                foreach (int cell in new[] { 0, 2, 3, 4, 5, 6, 10, 11, 12, 13, 14, 15 })
                {
                    int x = (int)((cell % 4 + .30) * mip.Width / 4);
                    int y = (int)((cell / 4 + .30) * mip.Height / 4);
                    Assert.InRange((int)pixels[y * mip.Width + x].b, 0, 4);
                }
                foreach (int cell in new[] { 1, 7, 9 })
                {
                    int x = (int)((cell % 4 + .30) * mip.Width / 4);
                    int y = (int)((cell / 4 + .30) * mip.Height / 4);
                    Assert.InRange((int)pixels[y * mip.Width + x].b, 190, 255);
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RepresentativeMeshesKeepFiniteOrthonormalTangentsAndAtlasIsolation()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-mesh-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(ProductionAssetFixture.CompileTo(root).Success);
            var catalog = RuntimeAssetCatalog.Load(root);
            string[] ids = ["unit.directorate.main_battle_tank", "unit.directorate.scout_vehicle",
                "unit.directorate.cargo_truck", "unit.directorate.supply_truck",
                "building.directorate.command_core", "building.directorate.vehicle_factory", "building.directorate.power_plant"];
            foreach (string id in ids)
            {
                var record = catalog.Get(AssetId.Parse(id));
                if (id == "unit.directorate.scout_vehicle")
                {
                    var sensor = Assert.Single(record.MechanicalParts, x => x.Name == "sensor");
                    var glass = Assert.Single(record.MechanicalParts, x => x.Name == "fidelity_1");
                    Assert.Equal(sensor.Motion, glass.Motion);
                    Assert.Equal(sensor.Socket, glass.Socket);
                    Assert.Equal(sensor.Rate, glass.Rate);
                }
                foreach (string meshId in new[] { id }.Concat(record.Lods.Select(x => x.AssetId)))
                {
                    var mesh = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(meshId)).Payload);
                    Assert.True(mesh.HasTangents);
                    var meshRecord = catalog.Get(AssetId.Parse(meshId));
                    MechanicalMeshValidation.Validate(meshRecord.MechanicalParts, meshRecord.Sockets, mesh.Indices.Count);
                    Assert.All(mesh.Vertices, vertex =>
                    {
                        var n = new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ);
                        var t = new Vector3(vertex.TangentX, vertex.TangentY, vertex.TangentZ);
                        Assert.InRange(n.Length(), .999f, 1.001f);
                        Assert.InRange(t.Length(), .999f, 1.001f);
                        Assert.InRange(MathF.Abs(Vector3.Dot(n, t)), 0, .001f);
                        Assert.Equal(1f, MathF.Abs(vertex.TangentW));
                        Assert.InRange(vertex.U, 0, 1);
                        Assert.InRange(vertex.V, 0, 1);
                    });
                    for (int i = 0; i < mesh.Indices.Count; i += 3)
                    {
                        var a = mesh.Vertices[(int)mesh.Indices[i]];
                        var b = mesh.Vertices[(int)mesh.Indices[i + 1]];
                        var c = mesh.Vertices[(int)mesh.Indices[i + 2]];
                        Assert.Equal(Cell(a), Cell(b));
                        Assert.Equal(Cell(a), Cell(c));
                    }
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        static (int, int) Cell(RuntimeMeshVertex v) => ((int)(v.U * 4), (int)(v.V * 4));
    }
}
