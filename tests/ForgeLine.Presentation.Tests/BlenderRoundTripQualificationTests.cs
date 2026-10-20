using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BlenderRoundTripQualificationTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_ROUNDTRIP_ROOT"));

    public static bool ReadinessEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_READINESS_ROOT"));

    [Fact(Skip = "Set FORGELINE_BLENDER_READINESS_ROOT after the staged three-family exports.", SkipUnless = nameof(ReadinessEnabled))]
    public void BuildingVehicleAndPropPreserveCompiledContracts()
    {
        CompareExports(Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_READINESS_ROOT")!),
            ["first", "second", "reopened"], false, expectedTriangles: 13184,
            expectedAssets: 33, expectedMeshes: 21, baselineFolder: "baseline/runtime");
    }

    public static bool ProductionEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_PRODUCTION_ROOT"));

    [Fact(Skip = "Set FORGELINE_BLENDER_ROUNDTRIP_ROOT after native Blender fixture exports.", SkipUnless = nameof(Enabled))]
    public void ExportedProductionMeshesPreserveRuntimeContractsAndMechanicalBatching()
    {
        CompareExports(Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_ROUNDTRIP_ROOT")!), ["first", "second", "reopened"], false);
    }

    [Fact(Skip = "Set FORGELINE_BLENDER_PRODUCTION_ROOT after native surface qualification.", SkipUnless = nameof(ProductionEnabled))]
    public void DefaultSurfacePreparationPreservesCompiledGeometryAndBatching()
    {
        CompareExports(Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_PRODUCTION_ROOT")!), ["first", "second"], true);
    }

    public static bool MotionEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_MOTION_ROOT"));

    [Fact(Skip = "Set FORGELINE_BLENDER_MOTION_ROOT after rigid authoring exports.", SkipUnless = nameof(MotionEnabled))]
    public void RigidAuthoringPreservesCompiledGeometryAndBatching()
    {
        CompareExports(Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_MOTION_ROOT")!), ["first", "second", "reopened"], true, "_rigid/", 10536);
    }

    private static void CompareExports(string root, string[] variants, bool prepared, string folder = "_prepared/", int expectedTriangles = 13128,
        int expectedAssets = 30, int expectedMeshes = 19, string baselineFolder = "baseline-runtime")
    {
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, baselineFolder));
        Assert.Equal(expectedAssets, baseline.Manifest.Assets.Count);
        var reports = new List<object>();
        foreach (string name in variants)
        {
            var exported = RuntimeAssetCatalog.Load(Path.Combine(root, name, "runtime"));
            Assert.Equal(baseline.Manifest.Assets.Select(x => x.Id).Order(), exported.Manifest.Assets.Select(x => x.Id).Order());
            int meshes = 0, triangles = 0, parts = 0;
            float positionError = 0, tangentError = 0;
            foreach (var before in baseline.Manifest.Assets)
            {
                var id = AssetId.Parse(before.Id);
                var after = exported.Get(id);
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
                if (prepared)
                {
                    if (after.SourcePath.Contains(folder, StringComparison.Ordinal))
                        Assert.Contains(before.Id, after.SourcePath, StringComparison.Ordinal);
                    else
                        Assert.Equal(before.SourcePath, after.SourcePath);
                }
                else Assert.Contains("_roundtrip/", after.SourcePath.Replace('\\', '/'));
                var a = RuntimeMeshData.FromPayload(baseline.Read(id).Payload);
                var b = RuntimeMeshData.FromPayload(exported.Read(id).Payload);
                Assert.Equal(a.Attributes, b.Attributes);
                Assert.Equal(a.MaterialIds, b.MaterialIds);
                Assert.Equal(a.Sections, b.Sections);
                Assert.Equal(a.Indices, b.Indices);
                Assert.Equal(a.Vertices.Count, b.Vertices.Count);
                for (int i = 0; i < a.Vertices.Count; i++)
                {
                    var x = a.Vertices[i]; var y = b.Vertices[i];
                    float error = MathF.Max(MathF.Abs(x.X-y.X), MathF.Max(MathF.Abs(x.Y-y.Y),MathF.Abs(x.Z-y.Z)));
                    positionError = MathF.Max(positionError, error);
                    Assert.True(error <= .00001f, before.Id);
                    Assert.True(Vector3.Distance(new(x.NormalX,x.NormalY,x.NormalZ), new(y.NormalX,y.NormalY,y.NormalZ)) <= .00002f, before.Id);
                    Assert.True(MathF.Abs(x.U-y.U) <= .000001f && MathF.Abs(x.V-y.V) <= .000001f, before.Id);
                    float tangent = Vector3.Distance(new(x.TangentX,x.TangentY,x.TangentZ), new(y.TangentX,y.TangentY,y.TangentZ));
                    tangentError = MathF.Max(tangentError, tangent);
                    Assert.True(tangent <= .00002f, before.Id);
                    Assert.Equal(x.TangentW,y.TangentW);
                }
                var oldParts = Bindings(before); var newParts = Bindings(after);
                MechanicalGeometryBatching.Assign(a, oldParts); MechanicalGeometryBatching.Assign(b, newParts);
                Assert.Equal(oldParts.Select(p => p.GeometryPartIndex), newParts.Select(p => p.GeometryPartIndex));
                for (int i = 0; i < oldParts.Length; i++)
                    Assert.True(Vector3.Distance(oldParts[i].GeometryOffset,newParts[i].GeometryOffset) <= .000001f, before.Id);
                meshes++; triangles += a.Indices.Count/3; parts += before.MechanicalParts.Count;
            }
            Assert.Equal(expectedMeshes,meshes); Assert.Equal(expectedTriangles,triangles);
            reports.Add(new { variant = name, meshes, triangles, parts, maximumPositionError = positionError, maximumTangentError = tangentError, mechanicalBatchIdentityPreserved = true });
        }
        string? seed = Environment.GetEnvironmentVariable("FORGELINE_BLENDER_RUNTIME_SEED");
        if (!string.IsNullOrWhiteSpace(seed))
        {
            var original = RuntimeAssetCatalog.Load(seed);
            var replacement = RuntimeAssetCatalog.Load(Path.Combine(root,"second/runtime"));
            var replacements = replacement.Manifest.Assets.ToDictionary(x => x.Id);
            string target = Path.Combine(root,"overlay/runtime");
            Directory.CreateDirectory(target);
            foreach (var record in original.Manifest.Assets)
            {
                bool replaced = replacements.TryGetValue(record.Id,out var changed);
                string source = replaced ? Path.Combine(root,"second/runtime") : seed;
                var chosen = replaced ? changed! : record;
                string path = Path.Combine(target,chosen.RuntimePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(source,chosen.RuntimePath),path,overwrite:false);
            }
            var manifest = RuntimeAssetIntegrity.SealManifest(original.Manifest with
            { Assets = original.Manifest.Assets.Select(x => replacements.GetValueOrDefault(x.Id,x)).ToArray() });
            File.WriteAllBytes(Path.Combine(target,RuntimeAssetCatalog.ManifestFileName),JsonSerializer.SerializeToUtf8Bytes(manifest,RuntimeAssetCatalog.CreateJsonOptions()));
            Assert.True(RuntimeAssetQualification.Run(target,requireIntegrity:true).Success);
        }
        File.WriteAllText(Path.Combine(root,"runtime-comparison.json"),JsonSerializer.Serialize(reports,ReportOptions));
    }

    private static MechanicalPartBinding[] Bindings(RuntimeAssetRecord record) => record.MechanicalParts.Select(part =>
    {
        var socket = record.Sockets.FirstOrDefault(x => x.Name == part.Socket);
        return new MechanicalPartBinding(part,new(socket.X,socket.Y,socket.Z));
    }).ToArray();
}
