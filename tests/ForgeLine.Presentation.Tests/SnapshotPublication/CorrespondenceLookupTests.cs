using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Presentation.RenderContracts;
using Xunit;

namespace ForgeLine.Presentation.Tests.SnapshotPublication;

public sealed class CorrespondenceLookupTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(31)]
    [InlineData(257)]
    public void ReorderedUniqueCurrentIdentitiesAgreeWithPerIndexInterpolation(int count)
    {
        var prior = Enumerable.Range(0, count).Select(i => Value(new((uint)i * 7919, (uint)(i % 3 + 1)), i + 1)).ToArray();
        prior[^1] = Value(new(uint.MaxValue, uint.MaxValue), count);
        var current = prior.Select(v => v with { Transform = v.Transform with { Position = v.Transform.Position + new Vector3(10) } }).ToArray();
        new Random(6173).Shuffle(current);
        var before = Snapshot(prior, 31); var after = Snapshot(current, 32);
        var buffer = new PresentationSnapshotBuffer(); var world = new RenderWorld();
        buffer.Publish(before); Assert.True(world.Update(buffer));
        buffer.Publish(after); Assert.True(world.Update(buffer));
        for (int i = 0; i < count; i++)
            foreach (float alpha in new[] { 0, .25f, .5f, 1 })
            {
                Assert.True(world.TryGetInterpolatedInstance(current[i].Entity, alpha, out var found));
                Assert.Equal(world.GetInterpolatedInstance(i, alpha), found);
                int previous = CorrespondenceContractTests.Find(before, current[i].Entity);
                Assert.Equal(RenderTransform.Interpolate(prior[previous].Transform, current[i].Transform, alpha), found.Transform);
            }
        Assert.False(world.TryGetInterpolatedInstance(new(7919, 99), .5f, out _));
        Assert.True(prior.AsSpan().SequenceEqual(before.Instances));
        Assert.True(current.AsSpan().SequenceEqual(after.Instances));
    }

    [Fact]
    public void AdmissionRebuildsOnlyForAcceptedDistinctSnapshotsAndResetsSessions()
    {
        var visibility = new CountingVisibility();
        var world = new RenderWorld(visibility); var buffer = new PresentationSnapshotBuffer();
        Assert.False(world.Update(buffer));
        Assert.False(world.TryGetInterpolatedInstance(new(7, 1), .5f, out _));
        Assert.Throws<InvalidOperationException>(() => world.GetInterpolatedInstance(0, .5f));
        var first = Snapshot([Value(new(7, 1), 10)], 31);
        buffer.Publish(first); Assert.True(world.Update(buffer));
        Assert.False(world.Update(buffer)); Assert.Equal(1, visibility.Rebuilds);
        Assert.Equal(first.Instances[0], world.GetInterpolatedInstance(0, 0));
        buffer.Publish(Snapshot([Value(new(7, 1), 90)], 30));
        Assert.False(world.Update(buffer)); Assert.Same(first, world.CurrentSnapshot);
        Assert.Equal(1, visibility.Rebuilds);
        var equal = Snapshot([Value(new(7, 1), 20)], 31);
        buffer.Publish(equal); Assert.True(world.Update(buffer));
        Assert.Same(first, world.PreviousSnapshot);
        Assert.Equal(15, world.GetInterpolatedInstance(0, .5f).Transform.Position.X);
        var skipped = Snapshot([Value(new(7, 2), 40), Value(new(99, 1), 50)], 40);
        buffer.Publish(skipped); Assert.True(world.Update(buffer));
        Assert.False(world.TryGetInterpolatedInstance(new(7, 1), .5f, out _));
        Assert.Equal(skipped.Instances[0], world.GetInterpolatedInstance(0, 0));
        Assert.Equal(skipped.Instances[1], world.GetInterpolatedInstance(1, 0));
        var reset = Snapshot([Value(new(7, 2), 60)], 0, 2);
        buffer.Publish(reset); Assert.True(world.Update(buffer));
        Assert.Null(world.PreviousSnapshot);
        Assert.False(world.TryGetInterpolatedInstance(new(99, 1), .5f, out _));
        Assert.Equal(reset.Instances[0], world.GetInterpolatedInstance(0, 0));
        buffer.Publish(Snapshot([], 1, 2)); Assert.True(world.Update(buffer));
        Assert.Equal(0, world.InstanceCount);
        Assert.False(world.TryGetInterpolatedInstance(new(7, 2), .5f, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.GetInterpolatedInstance(0, .5f));
        Assert.Equal(5, visibility.Rebuilds);
    }

    [Fact]
    public void UnsortedDuplicateCurrentKeysKeepTheOriginalBinaryTie()
    {
        var random = new Random(7919); var index = new RenderSnapshotIndex(false);
        for (int run = 0; run < 32; run++)
        {
            var values = Enumerable.Range(0, 65).Select(i => Value(new((uint)(i / 2), 1), i)).ToArray();
            random.Shuffle(values);
            var snapshot = Snapshot(values, 1); index.Rebuild(snapshot, null);
            foreach (var value in values)
            {
                int matches = values.Count(v => v.Entity == value.Entity);
                int expected = matches > 1 ? CorrespondenceContractTests.Find(snapshot, value.Entity)
                    : Array.FindIndex(values, v => v.Entity == value.Entity);
                Assert.Equal(expected, index.FindCurrent(value.Entity));
            }
        }
    }

    [Fact]
    public void WarmReorderedFramesAllocateNothingAndDoNotRebuildCorrespondence()
    {
        var prior = Enumerable.Range(1, 128).Select(i => Value(new((uint)i, 1), i)).ToArray();
        var current = prior.Reverse().Select(v => v with { Transform = v.Transform with { Position = v.Transform.Position + Vector3.One } }).ToArray();
        var visibility = new CountingVisibility(); var world = new RenderWorld(visibility);
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(Snapshot(prior, 1)); world.Update(buffer);
        buffer.Publish(Snapshot(current, 2)); world.Update(buffer);
        for (int i = 0; i < 1024; i++)
        {
            world.TryGetInterpolatedInstance(current[i % current.Length].Entity, .5f, out _);
            _ = world.GetInterpolatedInstance(i % current.Length, .5f);
        }
        int matched = 0, accepted = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 4096; i++)
        {
            if (world.TryGetInterpolatedInstance(current[i % current.Length].Entity, .5f, out _)) matched++;
            _ = world.GetInterpolatedInstance(i % current.Length, .5f);
            if (world.Update(buffer)) accepted++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(4096, matched);
        Assert.Equal(0, accepted);
        Assert.Equal(0, allocated);
        Assert.Equal(2, visibility.Rebuilds);
    }

    [Fact]
    public void ReorderedExceptionsRemainCurrentAndReceiverDecalsStillInterpolate()
    {
        RenderInstance[] current =
        [
            Value(new(4, 1), 40) with { Decoration = new(8, "mesh.test.decoration", 0, 1) },
            Value(new(2, 1), 20) with { VfxFeature = new(VfxEffectKind.PersistentDust) },
            Value(new(3, 1), 30) with { DecalFeature = new(DecalKind.Scorch, .5f, DecalSurface.Ground) },
            Value(new(1, 1), 10) with { DecalFeature = new(DecalKind.Scorch, .5f, DecalSurface.Receiver) }
        ];
        var prior = current.OrderBy(v => v.Entity).Select(v => v with { Transform = RenderTransform.Identity }).ToArray();
        var world = new RenderWorld(); var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(Snapshot(prior, 1)); world.Update(buffer);
        buffer.Publish(Snapshot(current, 2)); world.Update(buffer);
        for (int i = 0; i < current.Length; i++)
        {
            Assert.True(world.TryGetInterpolatedInstance(current[i].Entity, .5f, out var found));
            Assert.Equal(world.GetInterpolatedInstance(i, .5f), found);
            if (i < 3) Assert.Equal(current[i], found);
            else Assert.Equal(RenderTransform.Interpolate(RenderTransform.Identity, current[i].Transform, .5f), found.Transform);
        }
    }

    private static RenderInstance Value(EntityId id, float x) => CorrespondenceContractTests.Instance(id, x);
    private static PresentationSnapshot Snapshot(RenderInstance[] values, ulong tick, ulong session = 1) =>
        new(new(tick), TimeSpan.FromMilliseconds(50), values.Length, values.AsSpan(), sessionId: new(session));

    private sealed class CountingVisibility : IInstanceVisibilityIndex
    {
        internal int Rebuilds { get; private set; }
        public long RetainedBytes => 0;
        public void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous, IRenderSnapshotCorrespondence correspondence) => Rebuilds++;
        public bool IsVisible(int firstIndex, in ViewFrustum frustum) => true;
    }
}
