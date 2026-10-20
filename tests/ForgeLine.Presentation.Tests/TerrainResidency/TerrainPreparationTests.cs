using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.TerrainResidency;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests.TerrainResidency;

public sealed class TerrainPreparationTests
{
    internal static TerrainWorld FlatWorld(int radius = 2)
    {
        var settings = new WorldGridSettings();
        var chunks = new List<TerrainChunk>();
        for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
                chunks.Add(new(new(x, z), new TerrainHeightfield(65, 256, new float[65 * 65])));
        return new(settings, chunks);
    }

    [Fact]
    public void ImmutablePreparationPreservesPositionsUvSplatAndAuthoritativeHeights()
    {
        var world = DevelopmentTerrainFactory.CreateRepresentativeWorld(chunkRadius: 1);
        var policy = new TerrainPreparationSettings(); var profile = TerrainPresentationProfile.CreateCentralDivide();
        var source = new TerrainPreparedSource(world, new(), profile, policy);
        foreach (var chunk in world.Chunks)
        {
            float[] before = Enumerable.Range(0, 65 * 65).Select(i => chunk.Heightfield.GetHeight(i % 65, i / 65)).ToArray();
            int index = Array.IndexOf(source.Chunks, chunk);
            PreparedTerrainChunk data = source.Build(index, CancellationToken.None);
            TerrainMeshData expected = TerrainMeshGenerator.Generate(chunk);
            for (int i = 0; i < expected.Vertices.Length; i++)
            {
                Assert.Equal(expected.Vertices[i].Position, data.Vertices[i].Position);
                Assert.Equal(expected.Vertices[i].LocalUv, data.Vertices[i].LocalUv);
                Assert.InRange(data.Vertices[i].Normal.Length(), .99999f, 1.00001f);
            }
            var splat = TerrainSplatMapBuilder.Build(chunk, profile);
            Assert.Equal(splat.Palette, data.Splat.Palette);
            for (int i = 0; i < splat.Mips.Count; i++) Assert.Equal(splat.Mips[i].Pixels, data.Splat.Mips[i].Pixels);
            Assert.Equal(before, Enumerable.Range(0, 65 * 65).Select(i => chunk.Heightfield.GetHeight(i % 65, i / 65)));
        }
    }

    [Fact]
    public void PreparedLruIsBoundedAndReusesMostRecentChunks()
    {
        var policy = new TerrainPreparationSettings { MaximumPreparedChunks = 2, MaximumPreparedBytes = 1024 * 1024,
            MaximumChunkPreparationBytes = 1024 * 1024 };
        var source = new TerrainPreparedSource(FlatWorld(), new(), TerrainPresentationProfile.CreateCentralDivide(), policy);
        var cache = new TerrainPreparedCache(policy);
        cache.Admit(0, source.Build(0, CancellationToken.None)); cache.Admit(1, source.Build(1, CancellationToken.None));
        var recent = cache.Get(0);
        cache.Admit(2, source.Build(2, CancellationToken.None));
        Assert.Same(recent, cache.Get(0)); Assert.Null(cache.Get(1));
        for (int i = 3; i < source.Chunks.Length; i++)
        {
            cache.Admit(i, source.Build(i, CancellationToken.None));
            Assert.InRange(cache.Count, 0, 2); Assert.InRange(cache.Bytes, 0, policy.MaximumPreparedBytes);
        }
        Assert.True(cache.Evictions > 0); cache.Clear(); Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void RequiredCpuDataSurvivesGpuEvictionWithoutRegeneration()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 1),
            preparation: new() { PrefetchEnabled = false });
        var camera = new RtsCamera(new() { InitialDistance = 12 }); var context = new TerrainTestContext();
        foreach (float x in new[] { 128f, 384, 128 })
        {
            camera.ApplyState(camera.CaptureState() with { Target = new(x, 0, 128) });
            renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera);
            Assert.Equal(1, renderer.LastDiagnostics.VisibleChunks);
        }
        Assert.Equal(2, renderer.WorkMetrics.MeshBuilds); Assert.Equal(2, renderer.WorkMetrics.SplatBuilds);
        Assert.Equal(3, renderer.LastDiagnostics.ChunkUploads); Assert.True(renderer.WorkMetrics.CacheHits > 0);
        Assert.InRange(renderer.WorkMetrics.PreparedBytes, 1, 32 * 1024 * 1024);
    }

    [Fact]
    public void StaticIndexBufferIsSharedAndFallbackUploadsRemainExecutable()
    {
        using var device = new TerrainTestDevice(); var world = FlatWorld(1);
        using var renderer = new TerrainRenderer(device, world);
        Assert.Equal(world.Chunks.Count + 1, device.Inner.Buffers.Count);
        Assert.All(device.Inner.Buffers, b => Assert.True(b.LastWriteBytes > 0));
        renderer.Dispose(); Assert.All(device.Inner.Buffers, b => Assert.Equal(1, b.DisposeCount));
        Assert.Equal(0, renderer.WorkMetrics.PreparedBytes);
        Assert.Equal(0, renderer.WorkMetrics.ResidentGeometryBytes); Assert.Equal(0, renderer.WorkMetrics.ResidentControlBytes);
    }

    [Fact]
    public void CancelledLateWorkCannotPublishIntoANewGeneration()
    {
        var source = new TerrainPreparedSource(FlatWorld(0), new(), TerrainPresentationProfile.CreateCentralDivide(), new());
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var worker = new TerrainPreparationWorker();
        worker.Start([0], (index, token) => { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return source.Build(index, TestContext.Current.CancellationToken); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); worker.Cancel(); release.Set();
        Assert.True(SpinWait.SpinUntil(() => { worker.Harvest(); return !worker.Busy; }, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, worker.Discarded); Assert.Equal(0, worker.PendingCount);
    }

    [Fact]
    public void FailedGpuPublicationNeverLeavesTheOldViewMarkedReady()
    {
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 1));
        var camera = new RtsCamera(new() { InitialDistance = 12 }); var context = new TerrainTestContext();
        camera.ApplyState(camera.CaptureState() with { Target = new(128, 0, 128) });
        renderer.Prepare(camera, context.Width, context.Height); var previous = camera.CaptureState();
        device.Inner.FailBufferCreation = device.Inner.Buffers.Count + 1;
        camera.ApplyState(previous with { Target = new(384, 0, 128) });
        Assert.Throws<InvalidOperationException>(() => renderer.Prepare(camera, context.Width, context.Height));
        camera.ApplyState(previous); Assert.Throws<InvalidOperationException>(() => renderer.Render(context, camera));
        device.Inner.FailBufferCreation = 0; renderer.Prepare(camera, context.Width, context.Height); renderer.Render(context, camera);
        Assert.Equal(1, renderer.LastDiagnostics.VisibleChunks);
    }

    [Fact]
    public void PreparationLimitsAndScopeCancellationFailBeforePartialReady()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainIndexPatterns(int.MaxValue, 8 * 1024 * 1024));
        using var device = new TerrainTestDevice();
        using var renderer = new TerrainRenderer(device, FlatWorld(), residency: new(true, 25));
        using var stopped = new AssetPreparationScope(() => true);
        Assert.Throws<OperationCanceledException>(() => renderer.Prepare(new(), 1600, 900));
    }
}
