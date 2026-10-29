using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;

namespace ForgeLine.Presentation.Tests;

public sealed class ChunkDecorationTests
{
    [Fact]
    public void SnapshotCopiesCompactInputsAndKeepsGlobalIdentities()
    {
        var chunks = ChunkDecorationFixture.Generate(1000, origin: new(-1, -1));
        var snapshot = new ChunkDecorationSnapshot(chunks.Reverse());
        Assert.Equal(1000, snapshot.InstanceCount);
        Assert.Equal(28000, snapshot.CompactInstanceBytes);
        var original = snapshot.CaptureInstances(new(-1, -1));
        var copy = snapshot.CaptureInstances(new(-1, -1)); copy[0] = default;
        Array.Reverse(chunks);
        Assert.Equal(original, snapshot.CaptureInstances(new(-1, -1)));
        Assert.Throws<ArgumentException>(() => new ChunkDecorationSnapshot([chunks[0], chunks[0]]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkDecorationRenderSettings { MaximumResidentInstances = 65537 }.Validate());
    }

    [Fact]
    public void ExtractorPublishesDecorationSeparatelyWithoutAllocatingEntitiesOrPickTargets()
    {
        var decorations = new ChunkDecorationSnapshot(ChunkDecorationFixture.Generate(1000));
        var buffer = new PresentationSnapshotBuffer(); var simulation = new SimulationCoordinator();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer, decorations: new ChunkDecorationProvider(decorations)));
        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.One, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new VisualIdentity(1));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.Equal(1, simulation.Entities.EntityCount);
        Assert.Equal(1, snapshot.SimulationEntityCount);
        Assert.Single(snapshot.Instances.ToArray());
        Assert.Same(decorations, snapshot.ChunkDecorations);
        var next = simulation.Entities.CreateEntity();
        Assert.Equal(entity.Index + 1, next.Index);
        simulation.AdvanceOneTick();
        Assert.Same(decorations, snapshot.ChunkDecorations);
    }

    [Fact]
    public void EvictionReturnAndInterpolationNeverMoveDecoration()
    {
        var chunks = ChunkDecorationFixture.Generate(900, 3, new(-1, -1));
        var snapshot = new ChunkDecorationSnapshot([chunks[0], chunks[^1]]);
        var cache = new ChunkDecorationResidency();
        var settings = new ChunkDecorationRenderSettings { MaximumResidentChunks = 1, MaximumResidentInstances = 1000 };
        var left = ChunkDecorationFixture.Camera(new(-128, 0, -128), 180);
        var right = ChunkDecorationFixture.Camera(new(384, 0, 384), 180);
        cache.Prepare(snapshot, default, left, new(1600, 900, 1), settings);
        Assert.Equal(1, cache.Diagnostics.ResidentChunks);
        var expected = cache.World.CurrentSnapshot!.Instances.ToArray();
        cache.Prepare(snapshot, default, right, new(1600, 900, 1), settings);
        Assert.Equal(1, cache.Diagnostics.Evictions);
        cache.Prepare(snapshot, default, left, new(1600, 900, 1), settings);
        Assert.Equal(expected, cache.World.CurrentSnapshot!.Instances.ToArray());
        for (int i = 0; i < cache.World.InstanceCount; i++)
        {
            var item = cache.World.GetInterpolatedInstance(i, 0);
            Assert.Equal(expected[i].Transform, item.Transform);
            Assert.False(item.Entity.IsValid);
            Assert.False(item.Selectable.IsSelectable);
            Assert.False(item.WorldFeature.IsInspectable);
        }
        Assert.InRange(cache.Diagnostics.ResidentInstanceBytes, 1, 1000L * System.Runtime.CompilerServices.Unsafe.SizeOf<RenderInstance>());
    }

    [Fact]
    public void BudgetFailureIsAtomicAndShadowCoverageIsPrepared()
    {
        var chunks = ChunkDecorationFixture.Generate(900, 3, new(-1, -1));
        var snapshot = new ChunkDecorationSnapshot([chunks[0], chunks[^1]]);
        var cache = new ChunkDecorationResidency();
        var settings = new ChunkDecorationRenderSettings { MaximumResidentChunks = 1 };
        var left = ChunkDecorationFixture.Camera(new(-128, 0, -128), 180);
        var right = ChunkDecorationFixture.Camera(new(384, 0, 384), 180);
        cache.Prepare(snapshot, default, left, new(1600, 900, 1), settings);
        var before = cache.Diagnostics;
        var shadow = new GraphicsDirectionalShadowFrame(right.GetMatrices(1600, 900).ViewProjection, right.Target, 200, 1000);
        Assert.Throws<InvalidOperationException>(() => cache.Prepare(snapshot, default, left, new(1600, 900, 1, shadow), settings));
        Assert.Equal(before, cache.Diagnostics);
        cache.AssertPrepared(left, 1600, 900, null);
        cache.Prepare(snapshot, default, left, new(1600, 900, 1, shadow), settings with { MaximumResidentChunks = 2 });
        Assert.Equal(2, cache.Diagnostics.RequiredChunks);
        Assert.Throws<InvalidOperationException>(() => cache.AssertPrepared(right, 1600, 900, null));
        Assert.Throws<InvalidOperationException>(() => cache.AssertPrepared(left, 800, 600, null));
    }

    [Fact]
    public void CompiledMeshPivotsNormalizeEveryLodToTheSamePhysicalAnchor()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-decoration-anchor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = ChunkDecorationNativeTests.CreateCatalog(root);
            foreach (var (id, pivot, top) in new[]
            {
                ("mesh.world.vegetation.grass_clump", new Vector3(1,3,-1), new Vector3(1,11,-1)),
                ("mesh.world.vegetation.grass_clump_lod1", new Vector3(-5,-10,-15), new Vector3(-5,30,-15))
            })
            {
                var normalized = SimpleInstanceRenderer.NormalizeDecorationBounds(catalog.Get(ForgeLine.Assets.AssetId.Parse(id)).Bounds!.Value, 0);
                Assert.Equal(Vector3.Zero, Vector3.Transform(pivot, normalized));
                Assert.InRange(Vector3.Distance(Vector3.UnitY, Vector3.Transform(top, normalized)), 0, .000001f);
                var centered = SimpleInstanceRenderer.NormalizeDecorationBounds(catalog.Get(ForgeLine.Assets.AssetId.Parse(id)).Bounds!.Value, .5f);
                Assert.InRange(Vector3.Transform(Vector3.Lerp(pivot, top, .5f), centered).Length(), 0, .000001f);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1000, 2)]
    [InlineData(10000, 5)]
    public void DenseRendererUsesSeparateBoundedInstancingAndStableDensity(int count, int side)
    {
        var snapshot = new ChunkDecorationSnapshot(ChunkDecorationFixture.Generate(count, side));
        Assert.Equal(count, snapshot.InstanceCount);
        var world = ChunkDecorationFixture.World(snapshot);
        var camera = ChunkDecorationFixture.Camera(new(side * 128, 0, side * 128), side * 600);
        using var graphics = new FakeGraphicsDevice { CaptureUploads = true };
        using var renderer = new SimpleInstanceRenderer(graphics);
        renderer.Prepare(camera, world, new(1600, 900, 1));
        renderer.Render(new FakeGraphicsCommandContext(), camera, world, .3f, 2.0);
        Assert.Equal(0, renderer.LastDiagnostics.TotalInstances);
        Assert.Equal(0, world.InstanceCount);
        Assert.Equal(count, renderer.DecorationDiagnostics.Render.VisibleInstances);
        Assert.InRange(renderer.DecorationDiagnostics.Render.DrawCalls, 1, 2);
        Assert.Equal(count * 112L, renderer.DecorationDiagnostics.UploadBytes);
        long bytes = renderer.DecorationDiagnostics.InstanceBufferBytes;
        for (int frame = 0; frame < 12; frame++)
        {
            renderer.Prepare(camera, world, new(1600, 900, .1f));
            renderer.Render(new FakeGraphicsCommandContext(), camera, world, .1f, 2.0);
            Assert.Equal(bytes, renderer.DecorationDiagnostics.InstanceBufferBytes);
            Assert.Equal(0, renderer.DecorationDiagnostics.Admissions);
        }
        var cache = new ChunkDecorationResidency();
        cache.Prepare(snapshot, default, camera, new(1600, 900, 1), new());
        var full = cache.World.CurrentSnapshot!.Instances.ToArray();
        var half = new VegetationRenderSettings { Density = .5f };
        var selected = full.Where(item => half.Includes(item, camera.Position)).Select(i => i.Decoration.StableId).ToArray();
        Assert.InRange(selected.Length, count / 3, count * 2 / 3);
        Assert.Equal(selected, full.Reverse().Where(item => half.Includes(item, camera.Position))
            .Select(i => i.Decoration.StableId).Reverse());
        Assert.All(full, item =>
        {
            Assert.False(item.Entity.IsValid);
            Assert.False(new VegetationRenderSettings { Density = 0 }.Includes(item, camera.Position));
            Assert.True(new VegetationRenderSettings().Includes(item, camera.Position));
            var bounds = PresentationBounds.ResolveAxisAlignedBounds(item);
            Assert.True(bounds.Minimum.Y <= item.Transform.Position.Y);
            Assert.True(bounds.Maximum.Y >= item.Transform.Position.Y + item.Transform.Scale.Y);
        });
    }
}
