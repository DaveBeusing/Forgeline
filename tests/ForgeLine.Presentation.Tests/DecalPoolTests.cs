using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class DecalPoolTests
{
    private static DecalRequest Ground(float x = 0, uint lifetime = 10) =>
        new(DecalKind.Crater, DecalSurface.Ground, new(new(x, .04f, 0), Quaternion.Identity, new(4, 1, 4)), lifetime);

    [Fact]
    public void SaturationExpiryCancellationAndSessionResetKeepStorageBounded()
    {
        var pool = new DecalPool(new(4, 2, 4));
        var visibility = new Visibility();
        var scratch = new RenderInstance[4];
        pool.BeginTick(new(1));
        Assert.True(pool.TrySpawn(Ground(), new(1), out var old));
        Assert.True(pool.TrySpawn(Ground(20), new(1), out _));
        Assert.False(pool.TrySpawn(Ground(30), new(1), out _));
        Assert.True(pool.TrySpawn(Ground(256), new(1), out _));
        Assert.True(pool.TrySpawn(Ground(-1), new(1), out _));
        Assert.False(pool.TrySpawn(Ground(512), new(1), out _));
        Assert.Equal(4, pool.Metrics.Active);
        Assert.Equal(3, pool.Metrics.Chunks);
        Assert.Equal(4, pool.CopyVisibleTo(scratch, new(1), visibility));
        float fullOpacity = scratch[0].DecalFeature.Opacity;
        Assert.Equal(4, pool.CopyVisibleTo(scratch, new(10), visibility));
        Assert.True(scratch[0].DecalFeature.Opacity < fullOpacity);
        pool.BeginTick(new(11));
        Assert.Equal(0, pool.Metrics.Active);
        Assert.Equal(0, pool.Metrics.Chunks);
        Assert.False(pool.Cancel(old));
        Assert.True(pool.TrySpawn(Ground(), new(12), out var current));
        Assert.False(pool.Cancel(old));
        Assert.True(pool.Cancel(current));
        pool.Reset();
        Assert.True(pool.TrySpawn(Ground(), new(1), out var afterReset));
        Assert.False(pool.Cancel(current));
        Assert.True(pool.Cancel(afterReset));
    }

    [Fact]
    public void GroundAuthorizationAndReceiverGenerationNeverPublishHiddenPoses()
    {
        var pool = new DecalPool(new(8, 2, 4));
        var visibility = new Visibility { GroundVisible = false };
        var scratch = new RenderInstance[4];
        Assert.True(pool.TrySpawn(Ground(), new(1), out _));
        Assert.Equal(0, pool.CopyVisibleTo(scratch, new(1), visibility));
        visibility.GroundVisible = true;
        Assert.Equal(1, pool.CopyVisibleTo(scratch, new(2), visibility));
        EntityId receiver = new(7, 3);
        visibility.Receiver = receiver;
        visibility.Pose = new(new(300, 8, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new(8, 4, 12));
        var attached = new DecalRequest(DecalKind.Hit, DecalSurface.Receiver,
            new(new(300, 8, -4), Quaternion.Identity, Vector3.One), Receiver: receiver, ReceiverLocalAnchor: new(.5f, 0, 0));
        Assert.True(pool.TrySpawn(attached, new(2), out _));
        Assert.Equal(2, pool.CopyVisibleTo(scratch, new(2), visibility));
        Assert.True(Vector3.Distance(new(300, 8, -4), scratch[1].Transform.Position) < .001f);
        visibility.Pose = visibility.Pose with { Position = new(600, 8, 0) };
        Assert.Equal(2, pool.CopyVisibleTo(scratch, new(3), visibility));
        Assert.Equal(1UL, pool.Metrics.Migrated);
        visibility.Receiver = new(7, 4);
        Assert.Equal(1, pool.CopyVisibleTo(scratch, new(4), visibility));
        Assert.Equal(1UL, pool.Metrics.Retired);
    }

    [Fact]
    public void ChurnAndSteadyPublicationReuseStorageAndDoNotMutatePublishedCopies()
    {
        var pool = new DecalPool(new(8, 8, 8));
        var visibility = new Visibility();
        var scratch = new RenderInstance[8];
        for (uint tick = 1; tick <= 10000; tick++)
        {
            pool.BeginTick(new(tick));
            Assert.True(pool.TrySpawn(Ground(lifetime: 1), new(tick), out _));
            Assert.Equal(1, pool.CopyVisibleTo(scratch, new(tick), visibility));
        }
        var saved = scratch[0];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) pool.CopyVisibleTo(scratch, new(10000), visibility);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(saved, scratch[0]);
        Assert.Equal(8, pool.Metrics.Capacity);
    }

    [Fact]
    public void InvalidRequestsAndSettingsFailBeforeAdmission()
    {
        var pool = new DecalPool();
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.TrySpawn(Ground() with { Strength = float.NaN }, new(1), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.TrySpawn(Ground() with { LifetimeTicks = 6001 }, new(1), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.TrySpawn(Ground() with { Surface = DecalSurface.Receiver }, new(1), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecalPool(new(0)));
        Assert.Equal(0, pool.Metrics.Active);
    }

    private sealed class Visibility : IDecalVisibility
    {
        public bool GroundVisible = true;
        public EntityId Receiver;
        public RenderTransform Pose;
        public bool IsGroundVisible(Vector3 position) => GroundVisible;
        public bool TryGetReceiver(EntityId receiver, out RenderTransform transform) { transform = Pose; return receiver.IsValid && receiver == Receiver; }
    }
}
