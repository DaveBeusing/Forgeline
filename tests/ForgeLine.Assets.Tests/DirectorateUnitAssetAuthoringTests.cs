using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateUnitAssetAuthoringTests
{
    private static readonly string[] InfantryLodSuffixes = ["", ".lod1", ".lod2"];
    private static readonly UnitAssetExpectation[] Units =
    [
        new(
            "rifle_squad",
            ["weapon_muzzle"]),
        new(
            "scout_vehicle",
            ["weapon_muzzle", "sensor_origin"]),
        new(
            "main_battle_tank",
            ["turret_pivot", "gun_pivot", "weapon_muzzle", "recoil_anchor"]),
        new(
            "self_propelled_artillery",
            ["gun_pivot", "weapon_muzzle", "recoil_anchor"]),
        new(
            "cargo_truck",
            ["cargo_load"]),
        new(
            "supply_truck",
            ["cargo_load", "supply_transfer"])
    ];

    [Fact]
    public void DirectorateVerticalSliceUnitFamiliesCompileWithLodsCollisionSocketsAndSymbols()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-directorate-units-" +
                Guid.NewGuid().ToString("N"));

        try
        {
            AssetCompilationResult result =
                ProductionAssetFixture.CompileTo(runtimeRoot);

            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(
                        static diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Message}")));
            Assert.Equal(
                482,
                result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            Assert.True(
                catalog.Contains(
                    AssetId.Parse(
                        "unit.directorate.main_battle_tank.turret")));

            RuntimeMeshData[] tankLods = [ReadTank(""), ReadTank(".turret"), ReadTank(".lod1"), ReadTank(".lod2")];
            Assert.True(tankLods[0].Indices.Count + tankLods[1].Indices.Count > tankLods[2].Indices.Count);
            Assert.True(tankLods[2].Indices.Count > tankLods[3].Indices.Count);
            Assert.All(tankLods, mesh =>
            {
                Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                Assert.Single(mesh.Sections);
                Assert.Equal("material.directorate.unit.main_battle_tank", Assert.Single(mesh.MaterialIds));
            });
            foreach (RuntimeMeshData mesh in new[] { tankLods[0], tankLods[2], tankLods[3] })
            {
                Assert.Equal(-.5f, mesh.Vertices.Min(v => v.Y), precision: 5);
                Assert.Equal(-.49f, mesh.Vertices.Min(v => v.X), precision: 5);
                Assert.Equal(.49f, mesh.Vertices.Max(v => v.X), precision: 5);
                Assert.Equal(-.43f, mesh.Vertices.Min(v => v.Z), precision: 5);
            }
            Assert.Equal(.64f, tankLods[1].Vertices.Max(v => v.Z), precision: 5);
            Assert.DoesNotContain(tankLods[1].Vertices, v => v.Z > .635f && MathF.Abs(v.X) < .008f && MathF.Abs(v.Y - .16f) < .008f);
            RuntimeAssetRecord tankRecord = catalog.Get(AssetId.Parse("unit.directorate.main_battle_tank"));
            Assert.Contains(tankRecord.Sockets, s => s.Name == "weapon_muzzle" && s.X == 0 && s.Y == .16f && s.Z == .64f);
            Assert.Contains(tankRecord.Sockets, s => s.Name == "turret_pivot" && s.Y == .10f && s.Z == .02f);
            Assert.Contains(tankRecord.Sockets, s => s.Name == "gun_pivot" && s.Y == .16f && s.Z == .10f);
            Assert.Contains(tankRecord.Sockets, s => s.Name == "recoil_anchor" && s.Y == .16f && s.Z == .16f);
            foreach (var part in tankRecord.MechanicalParts.Where(p => p.Motion == MechanicalMotionKind.Wheel))
            {
                AssetSocket pivot = Assert.Single(tankRecord.Sockets, s => s.Name == part.Socket);
                var vertices = tankLods[0].Indices.Skip(part.FirstIndex).Take(part.IndexCount)
                    .Select(i => tankLods[0].Vertices[(int)i]).ToArray();
                Assert.InRange(pivot.X, vertices.Min(v => v.X), vertices.Max(v => v.X));
                Assert.InRange(pivot.Y, vertices.Min(v => v.Y), vertices.Max(v => v.Y));
                Assert.InRange(pivot.Z, vertices.Min(v => v.Z), vertices.Max(v => v.Z));
            }
            Assert.Equal(8, tankRecord.MechanicalParts.Count(p => p.Motion == MechanicalMotionKind.Wheel));
            var turretRecord = catalog.Get(AssetId.Parse("unit.directorate.main_battle_tank.turret"));
            Assert.Equal(2, turretRecord.MechanicalParts.Count);
            Assert.Contains(turretRecord.Sockets, s => s.Name == "motion_cannon" && s.Y == .16f && s.Z == .10f);
            foreach (string state in new[] { ".damaged", ".critical", ".wreck" })
            {
                var mesh = ReadTank(state);
                Assert.True(mesh.HasUv0 && mesh.HasTangents);
                Assert.Single(mesh.Sections);
                if (state == ".wreck") Assert.True(mesh.Vertices.Max(v => v.Y) < tankLods[1].Vertices.Max(v => v.Y));
            }

            foreach (string family in new[] { "rifle_squad", "combat_engineer" })
            {
                string id = "unit.directorate." + family;
                var primary = catalog.Get(AssetId.Parse(id));
                Assert.Equal("unit.directorate.rifle_squad.collision", primary.CollisionReference);
                Assert.Equal(16, primary.MechanicalParts.Count);
                Assert.Equal(8, primary.MechanicalParts.Count(part => part.Motion == MechanicalMotionKind.InfantryStride));
                Assert.Equal(4, primary.MechanicalParts.Count(part => part.Motion == MechanicalMotionKind.Cannon));
                Assert.Contains(primary.Sockets, socket => socket.Name == "weapon_muzzle" && socket.X == .05f && socket.Y == .05f && socket.Z == .42f);
                RuntimeMeshData[] lods = InfantryLodSuffixes.Select(suffix => RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(id + suffix)).Payload)).ToArray();
                Assert.True(lods[0].Indices.Count > lods[1].Indices.Count && lods[1].Indices.Count > lods[2].Indices.Count);
                MechanicalMeshValidation.Validate(primary.MechanicalParts, primary.Sockets, lods[0].Indices.Count);
                Assert.All(lods, mesh =>
                {
                    Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                    Assert.Single(mesh.Sections);
                    Assert.Equal("material.directorate.unit.rifle_squad", Assert.Single(mesh.MaterialIds));
                    Assert.Equal(-.5f, mesh.Vertices.Min(vertex => vertex.Y), precision: 5);
                });
                foreach (string state in new[] { ".damaged", ".critical", ".wreck" })
                {
                    var mesh = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(id + state)).Payload);
                    Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                    if (state == ".wreck") Assert.True(mesh.Vertices.Max(vertex => vertex.Y) < -.35f);
                }
            }
            foreach (string family in new[] { "scout_vehicle", "self_propelled_artillery", "cargo_truck", "supply_truck" })
            {
                var wreck = RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse("unit.directorate." + family + ".wreck")).Payload);
                Assert.True(wreck.HasNormals && wreck.HasUv0 && wreck.HasTangents);
                Assert.Single(wreck.Sections);
                Assert.True(wreck.Vertices.Max(vertex => vertex.Y) < 0);
            }

            RuntimeMeshData ReadTank(string suffix) => RuntimeMeshData.FromPayload(
                catalog.Read(AssetId.Parse("unit.directorate.main_battle_tank" + suffix)).Payload);

            foreach (UnitAssetExpectation unit in Units)
            {
                string primaryId =
                    $"unit.directorate.{unit.Name}";
                RuntimeAssetRecord primary =
                    catalog.Get(
                        AssetId.Parse(
                            primaryId));

                Assert.Equal(
                    RuntimeAssetType.Mesh,
                    primary.Type);
                Assert.Equal(
                    $"{primaryId}.collision",
                    primary.CollisionReference);
                Assert.Contains(
                    primary.Lods,
                    lod =>
                        lod.Level == 1 &&
                        lod.AssetId ==
                        $"{primaryId}.lod1");
                Assert.Contains(
                    primary.Lods,
                    lod =>
                        lod.Level == 2 &&
                        lod.AssetId ==
                        $"{primaryId}.lod2");

                foreach (string requiredSocket in
                         unit.RequiredSockets)
                {
                    Assert.Contains(
                        primary.Sockets,
                        socket =>
                            socket.Name ==
                            requiredSocket);
                }

                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod1")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod2")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.collision")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"material.directorate.unit.{unit.Name}")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"material.directorate.symbol.{unit.Name}")));
            }
        }
        finally
        {
            if (Directory.Exists(
                    runtimeRoot))
            {
                Directory.Delete(
                    runtimeRoot,
                    recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ForgeLine.sln")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test host.");
    }

    private sealed record UnitAssetExpectation(
        string Name,
        string[] RequiredSockets);
}
