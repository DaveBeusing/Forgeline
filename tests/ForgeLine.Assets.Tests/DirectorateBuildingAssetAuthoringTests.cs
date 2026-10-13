using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using System.Text.Json;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class DirectorateBuildingAssetAuthoringTests
{
    private static readonly string[] BuildingFamilies =
    [
        "command_core",
        "extractor",
        "smelter",
        "electronics_plant",
        "fuel_refinery",
        "vehicle_factory",
        "storage_depot",
        "supply_depot",
        "power_plant",
        "logistics_hub",
        "ammunition_plant",
        "barracks",
        "radar"
    ];

    [Fact]
    public void DirectorateBuildingsAndInfrastructureCompileWithStableRuntimeContracts()
    {
        string repositoryRoot =
            FindRepositoryRoot();
        string runtimeRoot =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-directorate-buildings-" +
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
                479,
                result.CompiledCount);

            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    runtimeRoot);

            RuntimeMeshData[] commandLods = [ReadCommandMesh(""), ReadCommandMesh(".lod1"), ReadCommandMesh(".lod2")];
            Assert.True(commandLods[0].Indices.Count > commandLods[1].Indices.Count);
            Assert.True(commandLods[1].Indices.Count > commandLods[2].Indices.Count);
            Assert.All(commandLods, mesh =>
            {
                Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                Assert.Single(mesh.Sections);
                Assert.Equal("material.directorate.building.structural", Assert.Single(mesh.MaterialIds));
                Assert.Equal(-.505f, mesh.Vertices.Min(v => v.X), precision: 5);
                Assert.Equal(.505f, mesh.Vertices.Max(v => v.X), precision: 5);
                Assert.Equal(-.5f, mesh.Vertices.Min(v => v.Y), precision: 5);
                Assert.Equal(.56f, mesh.Vertices.Max(v => v.Y), precision: 5);
                Assert.Equal(-.34f, mesh.Vertices.Min(v => v.Z), precision: 5);
                Assert.Equal(.48f, mesh.Vertices.Max(v => v.Z), precision: 5);
                Assert.Contains(mesh.Vertices, v => v.Z > .47f && MathF.Abs(v.X) < .14f && v.Y <= -.49f);
                // The loading mouth stays open below the lintel, not a painted box face.
                Assert.DoesNotContain(mesh.Vertices, v => MathF.Abs(v.X) < .09f && v.Z > .30f &&
                    v.Y > -.32f && v.Y < -.10f);
            });
            RuntimeAssetRecord command = catalog.Get(AssetId.Parse("building.directorate.command_core"));
            Assert.Equal(3, command.Sockets.Count);
            Assert.Contains(command.Sockets, s => s.Name == "service_access" && s.Y == -.414f && s.Z == .25f);
            foreach (string state in new[] { "foundation", "structural_frame", "partial_shell", "unpowered", "damaged", "critical", "destroyed" })
            {
                RuntimeMeshData mesh = ReadCommandMesh("." + state);
                Assert.NotEmpty(mesh.Indices);
                Assert.True(mesh.HasUv0 && mesh.HasTangents);
                Assert.Single(mesh.Sections);
            }

            RuntimeMeshData ReadCommandMesh(string suffix) => RuntimeMeshData.FromPayload(
                catalog.Read(AssetId.Parse("building.directorate.command_core" + suffix)).Payload);

            foreach (string family in new[] { "vehicle_factory", "power_plant", "barracks", "radar" })
            {
                string id = "building.directorate." + family;
                RuntimeMeshData Read(string suffix) => RuntimeMeshData.FromPayload(catalog.Read(AssetId.Parse(id + suffix)).Payload);
                RuntimeMeshData[] lods = [Read(""), Read(".lod1"), Read(".lod2")];
                Assert.True(lods[0].Indices.Count > lods[1].Indices.Count);
                Assert.True(lods[1].Indices.Count > lods[2].Indices.Count);
                Assert.All(lods, mesh =>
                {
                    Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                    Assert.Single(mesh.Sections);
                    Assert.Equal("material.directorate.building.structural", Assert.Single(mesh.MaterialIds));
                    Assert.Equal(-.5f, mesh.Vertices.Min(v => v.Y), precision: 5);
                    Assert.InRange(mesh.Vertices.Max(v => v.Y), .15f, .56f);
                    Assert.All(mesh.Vertices, v => Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)));
                });
                foreach (string state in new[] { "foundation", "structural_frame", "partial_shell", "unpowered", "damaged", "critical", "destroyed" })
                {
                    RuntimeMeshData mesh = Read("." + state);
                    Assert.NotEmpty(mesh.Indices);
                    Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                    Assert.Single(mesh.Sections);
                    Assert.True(mesh.Indices.Count < lods[0].Indices.Count);
                }
                RuntimeAssetRecord primary = catalog.Get(AssetId.Parse(id));
                MechanicalMeshValidation.Validate(primary.MechanicalParts, primary.Sockets, lods[0].Indices.Count);
                if (family == "vehicle_factory")
                {
                    Assert.Contains(primary.Sockets, s => s.Name == "motion_loading_door" && MathF.Abs(s.Y + .12f) < 1e-6f && MathF.Abs(s.Z - .4f) < 1e-6f);
                    Assert.Contains(primary.Sockets, s => s.Name == "motion_overhead_crane" && s.Y == .24f && s.Z == -.05f);
                    MechanicalMeshPart door = Assert.Single(primary.MechanicalParts, p => p.Motion == MechanicalMotionKind.Door);
                    // The operational leaf clears the 3m MBT; fixed jambs/lintel leave a real opening.
                    Assert.True((-.48f + door.Travel + .5f) * 12 > 3);
                    Assert.DoesNotContain(lods[2].Vertices, v => MathF.Abs(v.X) < .24f && v.Z > .40f && v.Y > -.40f && v.Y < -.15f);
                    Assert.True(lods[0].Vertices.Max(v => v.Z) * 24 + 7.2f / 2 < 20);
                }
                if (family == "radar")
                    Assert.Contains(primary.MechanicalParts, p => p.Motion == MechanicalMotionKind.Radar);
                if (family == "power_plant")
                    Assert.Contains(primary.MechanicalParts, p => p.Motion == MechanicalMotionKind.Fan);
                Assert.Empty(catalog.Get(AssetId.Parse(id + ".lod1")).MechanicalParts);
                Assert.Empty(catalog.Get(AssetId.Parse(id + ".lod2")).MechanicalParts);
            }

            foreach (string family in
                     BuildingFamilies)
            {
                string primaryId =
                    $"building.directorate.{family}";
                RuntimeAssetRecord primary =
                    catalog.Get(
                        AssetId.Parse(
                            primaryId));

                Assert.Equal(
                    RuntimeAssetType.Mesh,
                    primary.Type);
                Assert.Equal(
                    "building.directorate.module.collision_box",
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
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod1")));
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            $"{primaryId}.lod2")));

                if (family is "extractor" or "smelter" or "electronics_plant" or "fuel_refinery" or "ammunition_plant" or "storage_depot" or "supply_depot" or "logistics_hub")
                {
                    using var kit = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot,
                        "assets/source/buildings/directorate/authoring", family + ".kit.json")));
                    float[] minimum = kit.RootElement.GetProperty("bounds").GetProperty("min").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    float[] maximum = kit.RootElement.GetProperty("bounds").GetProperty("max").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    RuntimeMeshData[] lods = [ReadMesh(""), ReadMesh(".lod1"), ReadMesh(".lod2")];
                    Assert.True(lods[0].Indices.Count > lods[1].Indices.Count);
                    Assert.True(lods[1].Indices.Count > lods[2].Indices.Count);
                    Assert.Equal(3, primary.Sockets.Count);
                    Assert.Contains(primary.Sockets, x => x.Name is "service_access" or "loading_access" or "supply_transfer");
                    if (family is "storage_depot" or "supply_depot" or "logistics_hub")
                    {
                        float height = kit.RootElement.GetProperty("gameScaleMeters")[1].GetSingle();
                        var cargo = Assert.Single(primary.Sockets, x => x.Name == "cargo_load");
                        Assert.Equal(1.388f, height * (.5f + cargo.Y), precision: 5);
                        if (family == "supply_depot")
                        {
                            var transfer = Assert.Single(primary.Sockets, x => x.Name == "supply_transfer");
                            Assert.Equal(1.2f, height * (.5f + transfer.Y), precision: 5);
                        }
                    }
                    foreach (var mesh in lods)
                    {
                        Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                        Assert.Single(mesh.Sections);
                        Assert.Equal("material.directorate.building.structural", Assert.Single(mesh.MaterialIds));
                        Assert.Equal(-.5f, mesh.Vertices.Min(x => x.Y), precision: 5);
                        Assert.All(mesh.Vertices, x =>
                        {
                            Assert.InRange(x.X, minimum[0] - .00001f, maximum[0] + .00001f);
                            Assert.InRange(x.Y, minimum[1] - .00001f, maximum[1] + .00001f);
                            Assert.InRange(x.Z, minimum[2] - .00001f, maximum[2] + .00001f);
                            Assert.InRange(x.U, .017f, .983f);
                            Assert.InRange(x.V, .017f, .983f);
                            var normal = new System.Numerics.Vector3(x.NormalX, x.NormalY, x.NormalZ);
                            var tangent = new System.Numerics.Vector3(x.TangentX, x.TangentY, x.TangentZ);
                            Assert.InRange(normal.Length(), .999f, 1.001f);
                            Assert.InRange(tangent.Length(), .999f, 1.001f);
                            Assert.InRange(MathF.Abs(System.Numerics.Vector3.Dot(normal, tangent)), 0, .001f);
                        });
                    }
                    foreach (string state in new[] { "foundation", "structural_frame", "partial_shell", "idle", "unpowered", "damaged", "critical", "destroyed" })
                    {
                        var mesh = ReadMesh("." + state);
                        Assert.NotEmpty(mesh.Indices);
                        Assert.True(mesh.HasNormals && mesh.HasUv0 && mesh.HasTangents);
                        Assert.Single(mesh.Sections);
                    }
                    Assert.True(ReadMesh(".structural_frame").Indices.Count > ReadMesh(".foundation").Indices.Count);
                    Assert.True(ReadMesh(".partial_shell").Indices.Count < lods[1].Indices.Count);

                    RuntimeMeshData ReadMesh(string suffix) => RuntimeMeshData.FromPayload(
                        catalog.Read(AssetId.Parse(primaryId + suffix)).Payload);
                }
            }

            string[] sharedIds =
            [
                "building.directorate.module.foundation",
                "building.directorate.module.structural_frame",
                "building.directorate.module.partial_shell",
                "building.directorate.module.state_idle",
                "building.directorate.module.state_unpowered",
                "building.directorate.module.state_damaged",
                "building.directorate.module.state_critical",
                "building.directorate.module.destroyed",
                "building.directorate.module.collision_box",
                "material.directorate.building.structural",
                "material.directorate.building.unpowered",
                "material.directorate.building.damaged",
                "material.directorate.building.critical",
                "material.directorate.building.destroyed",
                "material.directorate.symbol.building.command",
                "material.directorate.symbol.building.extraction",
                "material.directorate.symbol.building.processing",
                "material.directorate.symbol.building.factory",
                "material.directorate.symbol.building.storage",
                "material.directorate.symbol.building.supply",
                "material.directorate.symbol.building.power",
                "infrastructure.directorate.road.straight",
                "infrastructure.directorate.road.curve_short",
                "infrastructure.directorate.road.curve_long",
                "infrastructure.directorate.road.junction_t",
                "infrastructure.directorate.road.junction_cross",
                "infrastructure.directorate.road.yard_transition",
                "infrastructure.directorate.road.shoulder",
                "infrastructure.directorate.road.damaged",
                "infrastructure.directorate.road.destroyed",
                "infrastructure.directorate.bridge.road.intact",
                "infrastructure.directorate.bridge.road.damaged",
                "infrastructure.directorate.bridge.road.destroyed",
                "material.directorate.infrastructure.road",
                "material.directorate.infrastructure.road_shoulder",
                "material.directorate.infrastructure.road_damaged",
                "material.directorate.infrastructure.bridge",
                "material.directorate.symbol.infrastructure.road",
                "material.directorate.symbol.infrastructure.bridge"
            ];

            foreach (string id in sharedIds)
            {
                Assert.True(
                    catalog.Contains(
                        AssetId.Parse(
                            id)),
                    $"Compiled catalog is missing '{id}'.");
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
}
