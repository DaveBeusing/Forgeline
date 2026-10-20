using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation.RenderSubmission;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionReferenceTests;

namespace ForgeLine.Presentation.Tests.RenderSubmission;

public sealed class SubmissionSafetyTests
{
    [Fact]
    public void SpatialCandidatesAreConservativeForSweptBoundsAndKeepSourceIdentity()
    {
        var random = new Random(7294);
        var camera = new RtsCamera();
        var old = Enumerable.Range(0, 10000).Select(i => Root(i, new(random.Next(-2000, 2000), 0, random.Next(-2000, 2000)))).ToArray();
        var current = old.Select((r, i) => r with { Transform = r.Transform with {
            Position = r.Transform.Position + new Vector3(random.Next(-500, 500), 0, random.Next(-500, 500)),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i), Scale = new Vector3(1 + i % 30) } }).ToArray();
        current[17] = current[17] with { Transform = current[17].Transform with { Position = new(float.NaN) } };
        current[31] = current[31] with { Transform = current[31].Transform with { Scale = new(float.PositiveInfinity) } };
        var buffer = new PresentationSnapshotBuffer(); var world = new RenderWorld();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), old.Length, old)); world.Update(buffer);
        buffer.Publish(new(new SimulationTick(2), TimeSpan.FromMilliseconds(50), current.Length, current)); world.Update(buffer);
        var index = new SpatialSubmissionIndex(); index.Update(world);
        foreach (float distance in new[] { 12f, 120f, 420f, 900f })
        {
            camera.ApplyState(camera.CaptureState() with { Distance = distance });
            var frustum = ViewFrustum.FromViewProjection(camera.GetMatrices(1600, 900).ViewProjection);
            index.Query(frustum, current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                bool finite = InstanceVisibilityIndex.TrySweptBounds(current[i], old[i], out AxisAlignedBounds bounds);
                if (!finite || frustum.Intersects(bounds)) Assert.True(index.IsCandidate(i), $"Missing source {i}");
                Assert.Equal(current[i], world.CurrentSnapshot!.GetInstance(i));
            }
        }
        Assert.True(index.IsCandidate(17)); Assert.True(index.IsCandidate(31));
        int rebuilds = index.Rebuilds; index.Update(world); Assert.Equal(rebuilds, index.Rebuilds);
    }

    [Fact]
    public void FrameReuseInvalidatesAlphaTimeSnapshotAndSessionButNotCamera()
    {
        var camera = new RtsCamera(); var world = CreateWorld([Root(0, camera.Target)]);
        var cache = new SubmissionFrameCache();
        cache.Begin(world, .5f, 1); var a = cache.Get(world, 0); Assert.Equal(1, cache.Interpolations);
        cache.Begin(world, .5f, 1); Assert.Equal(a, cache.Get(world, 0)); Assert.Equal(1, cache.Hits);
        foreach (var key in new[] { (.75f, 1d), (.75f, 2d) })
        { cache.Begin(world, key.Item1, key.Item2); cache.Get(world, 0); Assert.Equal(1, cache.Interpolations); }
        var other = CreateWorld([Root(0, camera.Target + Vector3.One)]);
        cache.Begin(other, .75f, 2); Assert.NotEqual(a.Instance.Transform, cache.Get(other, 0).Instance.Transform);
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, [Root(0, camera.Target)], sessionId: new SimulationSessionId(27)));
        other.Update(buffer); cache.Begin(other, .75f, 2); cache.Get(other, 0); Assert.Equal(1, cache.Interpolations);
        cache.Clear();
    }

    [Fact]
    public void LightCandidatesRemainIndependentOfTheMainCamera()
    {
        var main = new RtsCamera(); var offscreen = main.Target + new Vector3(10000, 0, 10000);
        var world = CreateWorld([Root(0, main.Target), Root(1, offscreen)]);
        var index = new SpatialSubmissionIndex(); index.Update(world);
        // Separate clusters are required to demonstrate rejection independently.
        var roots = Enumerable.Range(0, 64).Select(i => Root(i, i < 32 ? main.Target : offscreen)).ToArray();
        world = CreateWorld(roots); index.Update(world);
        index.Query(ViewFrustum.FromViewProjection(main.GetMatrices(1600, 900).ViewProjection), 64);
        Assert.True(index.IsCandidate(0)); Assert.False(index.IsCandidate(63));
        var light = Matrix4x4.CreateLookAt(offscreen + new Vector3(0, 100, 100), offscreen, Vector3.UnitY) * Matrix4x4.CreateOrthographic(500, 500, 1, 1000);
        index.Query(ViewFrustum.FromViewProjection(light), 64);
        Assert.False(index.IsCandidate(0)); Assert.True(index.IsCandidate(63));
    }

    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    public void StableLargeSubmissionReusesLeasesAndReturnsToSmall(int count)
    {
        using var graphics = new FakeGraphicsDevice(); using var renderer = new SimpleInstanceRenderer(graphics);
        var context = new FakeGraphicsCommandContext(); var camera = new RtsCamera();
        var world = CreateWorld(Enumerable.Range(0, count).Select(i => Root(i, camera.Target)).ToArray());
        for (int i = 0; i < 32; i++) renderer.Render(context, camera, world, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++) renderer.Render(context, camera, world, 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(count, renderer.SubmissionMetrics.SubmittedInstances);
        world = CreateWorld([Root(0, camera.Target)]); renderer.Render(context, camera, world, 1);
        Assert.InRange(renderer.SubmissionMetrics.StagingCapacity, 1, count > 65536 ? 64 : 65536);
        Assert.InRange(renderer.SubmissionMetrics.BatchInstanceCapacity, 1, count > 65536 ? 64 : 65536);
    }

    [Fact]
    public void BatchEscapeBandUsesBoundedReusablePayloadLeases()
    {
        var lists = Enumerable.Range(0, 257).Select(_ => new PooledSubmissionList<int>()).ToArray();
        for (int pass = 0; pass < 32; pass++)
        {
            foreach (var list in lists) for (int i = 0; i < 257; i++) list.Add(i);
            foreach (var list in lists) list.Release();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < 32; pass++)
        {
            foreach (var list in lists) for (int i = 0; i < 257; i++) list.Add(i);
            foreach (var list in lists) list.Release();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        foreach (var list in lists) Assert.Equal(0, list.Capacity);
    }

    [Fact]
    public void ShadowBudgetKeepsRootsWholeAndSkipsUnaffordableHeavyRoots()
    {
        var snapshot = CreateWorld(Enumerable.Range(0, 20).Select(i => Root(i, Vector3.Zero)).ToArray()).CurrentSnapshot!;
        using var budget = new ShadowRootBudget(snapshot);
        for (int i = 0; i < 10; i++) budget.IncludePart(0, true);
        for (int i = 1; i < 20; i++) budget.IncludePart(i, i != 19);
        Assert.Equal(5, budget.Select(5));
        Assert.False(budget.Selected(0)); Assert.False(budget.Selected(19));
        Assert.Equal(5, Enumerable.Range(0, 20).Count(budget.Selected));
    }

    [Fact]
    public void NativeBudgetCountsPartsAndRecordingReusesPreparedRoots()
    {
        using var graphics = new FakeGraphicsDevice { DirectionalShadows = new(DirectionalShadowQuality.Low) };
        using var renderer = new SimpleInstanceRenderer(graphics);
        var camera = new RtsCamera(); var context = new FakeGraphicsCommandContext();
        var world = CreateWorld(Enumerable.Range(0, 1000).Select(i => Root(i, camera.Target)).ToArray());
        renderer.Render(context, camera, world, .5f);
        Assert.Equal(1000, renderer.WorkMetrics.Interpolations);
        var projection = camera.GetMatrices(context.Width, context.Height).ViewProjection;
        renderer.RenderShadow(context, camera, world, .5f, new(projection, camera.Target, 500, 1000), 17, 1);
        Assert.Equal(0, renderer.WorkMetrics.Interpolations);
        Assert.Equal(1000, renderer.WorkMetrics.PoseCacheHits);
        Assert.Equal(17, renderer.WorkMetrics.UploadedParts);
        Assert.Equal(17, renderer.WorkMetrics.DrawnParts);
        Assert.Equal(17, renderer.WorkMetrics.DrawnRoots);
        Assert.Equal(1, renderer.WorkMetrics.PipelineBindings);
        Assert.Equal(1000, renderer.LastDiagnostics.VisibleInstances);
    }
}
