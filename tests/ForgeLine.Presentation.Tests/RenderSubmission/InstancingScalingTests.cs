using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionReferenceTests;

namespace ForgeLine.Presentation.Tests.RenderSubmission;

public sealed class InstancingScalingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void CompiledCratesUseOneCompatibleBatchWithExactPayload(int count)
    {
        using var assets = new CompiledAssets();
        using var graphics = new FakeGraphicsDevice();
        using var renderer = new SimpleInstanceRenderer(graphics, assets.Catalog);
        var camera = new RtsCamera();
        var world = CreateWorld(Enumerable.Range(0, count).Select(i => Crate(i, camera.Target)).ToArray());
        var context = new FakeGraphicsCommandContext { CaptureDraws = true };
        renderer.Render(context, camera, world, 1);
        Assert.Equal(count, renderer.SubmissionMetrics.SubmittedInstances);
        Assert.Equal(count, context.TotalInstances);
        Assert.Equal(count * 112L, renderer.SubmissionMetrics.UploadBytes);
        Assert.Equal(count == 0 ? 0 : 1, context.IndexedDrawCalls);
        Assert.Equal(count == 0 ? 0 : 1, renderer.WorkMetrics.MeshResolutions);
        Assert.Equal(count == 0 ? 0 : 1, renderer.WorkMetrics.MaterialResolutions);
        Assert.Equal(count == 0 ? 0 : 1, renderer.WorkMetrics.PipelineBindings);
        Assert.Equal(count == 0 ? 0 : 4, renderer.WorkMetrics.TextureBindings);
        if (count == 0) Assert.Empty(context.Draws);
        else
        {
            var draw = Assert.Single(context.Draws);
            Assert.Equal(112, draw.InstanceStride);
            Assert.Equal(0, draw.InstanceOffset);
            Assert.Equal(count * 112, ((FakeGraphicsBuffer)draw.Instances).LastWriteBytes);
            Assert.Equal(4, draw.Textures.Count);
            Assert.NotSame(draw.Geometry, draw.Instances);
            Assert.Equal(count, renderer.LastDiagnostics.TexturedRuntimeMeshInstances);
        }
    }

    [Fact]
    public void SameMeshAndHandleWithDifferentResolvedMaterialsStaysSplit()
    {
        using var assets = new CompiledAssets();
        using var graphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var renderer = new SimpleInstanceRenderer(graphics, assets.Catalog);
        var camera = new RtsCamera(new() { InitialDistance = 12 });
        var normal = Root(0, camera.Target) with
        { BuildingFeature = new(BuildingIds.CommandCore, BuildingPresentationState.Operational) };
        var critical = normal with { Entity = new(2, 1), BuildingFeature = new(BuildingIds.CommandCore, BuildingPresentationState.Critical) };
        // Interleaving must merge only compatible records, retaining order inside each batch.
        var world = CreateWorld([normal, critical, normal with { Entity = new(3, 1) }, critical with { Entity = new(4, 1) }]);
        var context = new FakeGraphicsCommandContext { CaptureDraws = true };
        renderer.Render(context, camera, world, 1);
        Assert.Equal(2, context.Draws.Count);
        Assert.All(context.Draws, draw => Assert.Equal(2, draw.InstanceCount));
        Assert.Same(context.Draws[0].Geometry, context.Draws[1].Geometry);
        Assert.Same(context.Draws[0].Pipeline, context.Draws[1].Pipeline);
        Assert.Equal(0, context.Draws[0].InstanceOffset);
        Assert.Equal(224, context.Draws[1].InstanceOffset);
        Assert.Equal(4, context.TotalInstances);
        Assert.Equal(8, renderer.WorkMetrics.TextureBindings);
        var upload = ((FakeGraphicsBuffer)context.Draws[0].Instances).LastUpload;
        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(upload);
        Assert.Equal(.35f, floats[2 * 28 + 24]);
        Assert.Equal(.35f, floats[3 * 28 + 24]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenRootsNeverReachMainOrShadowUpload(bool shadow)
    {
        using var graphics = new FakeGraphicsDevice { DirectionalShadows = new(DirectionalShadowQuality.Low) };
        using var renderer = new SimpleInstanceRenderer(graphics);
        var camera = new RtsCamera();
        var roots = Enumerable.Range(0, 1000).Select(i => Root(i, camera.Target) with
        { Visibility = i % 10 == 0 ? RenderVisibilityMask.World : RenderVisibilityMask.Debug }).ToArray();
        var context = new FakeGraphicsCommandContext { CaptureDraws = true };
        var world = CreateWorld(roots);
        if (shadow) renderer.RenderShadow(context, camera, world, 1,
            new(camera.GetMatrices(context.Width, context.Height).ViewProjection, camera.Target, 500, 1000), 1000, 1000);
        else renderer.Render(context, camera, world, 1);
        Assert.Equal(100, context.TotalInstances);
        Assert.Equal(11200, ((FakeGraphicsBuffer)Assert.Single(context.Draws).Instances).LastWriteBytes);
        var hidden = CreateWorld(roots.Select(r => r with { Visibility = RenderVisibilityMask.None }).ToArray());
        var empty = new FakeGraphicsCommandContext();
        if (shadow) renderer.RenderShadow(empty, camera, hidden, 1,
            new(camera.GetMatrices(empty.Width, empty.Height).ViewProjection, camera.Target, 500, 1000), 1000, 1000);
        else renderer.Render(empty, camera, hidden, 1);
        Assert.Equal(0, empty.IndexedDrawCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SweptRotatedNonUniformBoundsMatchReferenceAcrossFrustumEdges(bool shadow)
    {
        using var graphics = new FakeGraphicsDevice { CaptureUploads = true, DirectionalShadows = new(DirectionalShadowQuality.Low) };
        using var referenceGraphics = new FakeGraphicsDevice { CaptureUploads = true, DirectionalShadows = new(DirectionalShadowQuality.Low) };
        using var renderer = new SimpleInstanceRenderer(graphics);
        using var reference = new SimpleInstanceRenderer(referenceGraphics, clusterCulling: false);
        var camera = new RtsCamera();
        var previous = Enumerable.Range(0, 2048).Select(i => Root(i, camera.Target +
            new Vector3((i % 64 - 32) * 20, 0, (i / 64 - 16) * 20)) with
        {
            Transform = new(camera.Target + new Vector3((i % 64 - 32) * 20, 0, (i / 64 - 16) * 20),
                Quaternion.CreateFromYawPitchRoll(i * .3f, .7f, -.4f), new(18, .25f, 3)),
            WorldFeature = i % 3 == 0 ? new(WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, default, false) : default,
            UnitFeature = i % 3 == 1 ? new(UnitIds.MainBattleTank, UnitPresentationDamageState.Intact) : default,
            MechanicalPose = i % 3 == 1 ? new(true, 2.3, 1.6, 1.6, 1.55, .15f, 0) : default
        }).ToArray();
        var current = previous.Select((r, i) => r with { Transform = r.Transform with
        { Position = r.Transform.Position + new Vector3(i % 2 == 0 ? 90 : -90, 0, 40),
          Rotation = Quaternion.CreateFromYawPitchRoll(-i * .3f, -.7f, .4f), Scale = new(-3, 12, .5f) } }).ToArray();
        var world = CreateWorld(previous);
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new ForgeLine.Simulation.SimulationTick(2), TimeSpan.FromMilliseconds(50), current.Length, current));
        Assert.True(world.Update(buffer));
        foreach (float distance in new[] { 120f, 420f, 900f })
        foreach (float alpha in new[] { 0f, .25f, .5f, 1f })
        {
            camera.ApplyState(camera.CaptureState() with { Distance = distance });
            var context = new FakeGraphicsCommandContext { CaptureDraws = true };
            var expected = new FakeGraphicsCommandContext { CaptureDraws = true };
            if (shadow)
            {
                var frame = new GraphicsDirectionalShadowFrame(camera.GetMatrices(1600, 900).ViewProjection, camera.Target, 750, 2000);
                renderer.RenderShadow(context, camera, world, alpha, frame, int.MaxValue, int.MaxValue, alpha + .5);
                reference.RenderShadow(expected, camera, world, alpha, frame, int.MaxValue, int.MaxValue, alpha + .5);
            }
            else
            {
                renderer.Render(context, camera, world, alpha, alpha + .5);
                reference.Render(expected, camera, world, alpha, alpha + .5);
            }
            Assert.True(expected.TotalInstances > 0);
            Assert.Equal(expected.TotalInstances, context.TotalInstances);
            Assert.Equal(expected.IndexedDrawCalls, context.IndexedDrawCalls);
            Assert.Equal(((FakeGraphicsBuffer)expected.Draws[0].Instances).LastUpload,
                ((FakeGraphicsBuffer)context.Draws[0].Instances).LastUpload);
        }
    }

    internal static RenderInstance Crate(int index, Vector3 position) => Root(index, position) with
    { WorldFeature = new(WorldVisualId.PropCrate, WorldPresentationKind.Prop, default, false) };

    internal sealed class CompiledAssets : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "forgeline-instancing-" + Guid.NewGuid().ToString("N"));
        public RuntimeAssetCatalog Catalog { get; }
        public CompiledAssets()
        {
            Directory.CreateDirectory(_root);
            try
            {
                var crate = WorldPresentationCatalog.Get(WorldVisualId.PropCrate);
                var building = BuildingPresentationCatalog.Get(BuildingIds.CommandCore);
                var mesh = new RuntimeMeshData(RuntimeMeshAttributes.Normal | RuntimeMeshAttributes.Uv0 | RuntimeMeshAttributes.Tangent,
                    [new(0,0,0,0,1,0,0,0,1,0,0,1), new(1,0,0,0,1,0,1,0,1,0,0,1), new(0,0,1,0,1,0,0,1,1,0,0,1)],
                    [0,2,1], [crate.MaterialAssetId], [new(0,3,0)]);
                var records = new List<RuntimeAssetRecord>();
                foreach (string id in new[] { crate.MeshAssetId, crate.ReducedLodAssetId, building.MeshAssetId }.Distinct())
                    records.Add(Write(id, RuntimeAssetType.Mesh, mesh.ToPayload()));
                foreach (string id in new[] { crate.MaterialAssetId, building.MaterialAssetId,
                    BuildingPresentationCatalog.ResolveMaterialAssetId(new(BuildingIds.CommandCore, BuildingPresentationState.Critical)) }.Distinct())
                    records.Add(Write(id, RuntimeAssetType.Material, id.EndsWith(".critical", StringComparison.Ordinal)
                        ? "{\"emissiveMultiplier\":0.35}"u8.ToArray() : "{}"u8.ToArray()));
                File.WriteAllText(Path.Combine(_root, RuntimeAssetCatalog.ManifestFileName),
                    JsonSerializer.Serialize(new RuntimeAssetManifest { CompilerVersion = "test", Assets = records }, RuntimeAssetCatalog.CreateJsonOptions()));
                Catalog = RuntimeAssetCatalog.Load(_root);
            }
            catch { Dispose(); throw; }
        }
        private RuntimeAssetRecord Write(string id, RuntimeAssetType type, byte[] payload)
        {
            string path = id + ".flasset";
            RuntimeAssetFile.Write(Path.Combine(_root, path), type, [], payload);
            return new() { Id = id, Type = type, SourcePath = path, RuntimePath = path,
                SourceHash = "test", BuildHash = "test", RuntimeVersion = 1, CompilerVersion = "test" };
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }
}
