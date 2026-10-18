using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Graphics;
using ForgeLine.Graphics.RenderContracts;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests.RenderParallelization;

public sealed class RenderBoundaryTests
{
    [Fact]
    public void PreparedAndLegacyFramesUploadIdenticalInstancesWithoutChangingDiagnostics()
    {
        using var fixture = new EmptyCatalog();
        using var legacyDevice = new ContractGraphicsDevice();
        using var preparedDevice = new ContractGraphicsDevice();
        using var legacy = new SimpleInstanceRenderer(legacyDevice, fixture.Catalog);
        using var prepared = new SimpleInstanceRenderer(preparedDevice, fixture.Catalog);
        var world = World(96, 1);
        var camera = new RtsCamera();
        legacy.Render(new ContractGraphicsDevice.Context(), camera, world, .5f);
        prepared.Prepare(camera, world, new(1600, 900, .5f));
        Assert.Equal(default, prepared.LastDiagnostics);
        Assert.True(prepared.AssetsPrepared);
        prepared.Render(new ContractGraphicsDevice.Context(), camera, world, .5f);
        prepared.Submit(); prepared.Retire();
        Assert.Equal(legacy.LastDiagnostics, prepared.LastDiagnostics);
        Assert.Equal(legacyDevice.Buffers[^1].Bytes, preparedDevice.Buffers[^1].Bytes);
        Assert.Null(prepared.AssetFrameMetrics);
    }

    [Fact]
    public void DisabledSessionMeasurementAllocatesNoPerFrameObjects()
    {
        using var fixture = new EmptyCatalog(); using var graphics = new ContractGraphicsDevice();
        using var session = new RenderAssetSession(graphics, fixture.Catalog, TextureDetailQuality.Full);
        for (int i = 0; i < 128; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) Frame();
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Null(session.Metrics);
        void Frame()
        {
            session.BeginPrepare(new(17), 21); session.EndPrepare();
            session.BeginRecord(); session.EndRecord(); session.BeginRecord(); session.EndRecord();
            session.Submit(); session.Retire();
        }
    }

    [Fact]
    public void SessionRejectsRecordingPreparationWrongOrderAndForeignOwner()
    {
        using var fixture = new EmptyCatalog(); using var graphics = new ContractGraphicsDevice();
        using var session = new RenderAssetSession(graphics, fixture.Catalog, TextureDetailQuality.Full);
        graphics.IsRecording = true;
        Assert.Throws<InvalidOperationException>(() => session.BeginPrepare(default, 1));
        graphics.IsRecording = false;
        session.BeginPrepare(new(7), 42); session.EndPrepare();
        Assert.Throws<InvalidOperationException>(session.Submit);
        Assert.Throws<InvalidOperationException>(() => session.BeginPrepare(default, 2));
        session.BeginRecord(); session.EndRecord(); session.Submit(); session.Retire();
        Assert.Throws<InvalidOperationException>(session.BeginRecord);
        Exception? foreign = null;
        var thread = new Thread(() => foreign = Record.Exception(() => session.BeginPrepare(default, 3)));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(foreign);
        Assert.Equal(0, session.MaterialDiagnostics.LoadedMeshCount);
        session.MetricsEnabled = true;
        session.BeginPrepare(new(8), 43); session.EndPrepare(); session.BeginRecord(); session.EndRecord(); session.Submit(); session.Retire();
        Assert.Equal(new SimulationSessionId(8), session.Metrics!.Value.SessionId);
        Assert.Equal(43UL, session.Metrics.Value.SnapshotTick);
        Assert.NotNull(session.Metrics.Value.PrepareMilliseconds);
        Assert.Null(session.Metrics.Value.SubmissionFence);
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(32)] [InlineData(65537)]
    public void SeparateVisibilityRetainsLegacyClusterAndCorrespondenceResults(int count)
    {
        var world = World(count, 1);
        var current = world.CurrentSnapshot!;
        var legacy = new RenderSnapshotIndex(); legacy.Rebuild(current, null);
        var mapping = new RenderSnapshotIndex(false); mapping.Rebuild(current, null);
        var visibility = new InstanceVisibilityIndex(); visibility.Rebuild(current, null, mapping);
        var frustum = ViewFrustum.FromViewProjection(new RtsCamera().GetMatrices(1600, 900).ViewProjection);
        for (int i = 0; i < count; i += 32) Assert.Equal(legacy.IsVisible(i, frustum), visibility.IsVisible(i, frustum));
        Assert.Equal(legacy.RetainedBytes, mapping.RetainedBytes + visibility.RetainedBytes);
    }

    [Fact]
    public void SessionChangeClearsPreviousCorrespondenceAndNonfiniteBoundsStayConservative()
    {
        var world = World(32, 1); var buffer = new PresentationSnapshotBuffer();
        RenderInstance[] next = Enumerable.Range(1, 32).Select(i => Instance((uint)i, new(float.NaN))).ToArray();
        buffer.Publish(new(new SimulationTick(2), TimeSpan.Zero, 32, next, sessionId: new(2)));
        world.Update(buffer);
        Assert.Null(world.PreviousSnapshot);
        var frustum = ViewFrustum.FromViewProjection(new RtsCamera().GetMatrices(1600, 900).ViewProjection);
        Assert.True(world.ClusterVisible(0, frustum));
    }

    [Fact]
    public void ExternalVertexAndBindingAbiRemainsFrozen()
    {
        Assert.Equal(112, RenderBindingAbi.InstanceStride);
        Assert.Equal(48, RuntimeMeshData.VertexStride);
        Assert.Equal(112, Marshal.SizeOf(typeof(SimpleInstanceRenderer).GetNestedType("InstanceRenderData", BindingFlags.NonPublic)!));
        Assert.Equal(48, Marshal.SizeOf(typeof(SimpleInstanceRenderer).GetNestedType("SimpleVertex", BindingFlags.NonPublic)!));
        Assert.Equal(35, RenderBindingAbi.InstanceFrameConstants); Assert.Equal(51, RenderBindingAbi.TerrainFrameConstants);
        Assert.Equal(13, RenderBindingAbi.TerrainTextureSlots); Assert.Equal(15, RenderBindingAbi.ShadowTextureSlot);
        Assert.Equal(1, RenderBindingAbi.ShadowFrameConstantSlot);
    }
    private static RenderWorld World(int count, ulong session)
    {
        var buffer = new PresentationSnapshotBuffer(); var world = new RenderWorld();
        RenderInstance[] instances = Enumerable.Range(1, count).Select(i => Instance((uint)i, new(i % 32 - 16, 0, i / 32))).ToArray();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.Zero, count, instances, sessionId: new(session)));
        world.Update(buffer); return world;
    }
    private static RenderInstance Instance(uint id, Vector3 position) =>
        new(new EntityId(id, 1), new(position, Quaternion.Identity, Vector3.One), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
    private sealed class EmptyCatalog : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "render-contract-" + Guid.NewGuid().ToString("N"));
        internal RuntimeAssetCatalog Catalog { get; }
        internal EmptyCatalog()
        {
            Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "manifest.json"), """{"formatVersion":1,"compilerVersion":"1.12.0","assets":[]}""");
            Catalog = RuntimeAssetCatalog.Load(_root);
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
