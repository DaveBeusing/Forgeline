using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests.SnapshotPublication;

public sealed class CorrespondenceContractTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(4967)]
    [InlineData(7919)]
    public void SortedMappingAndEveryInterpolatedFieldMatchTheBinaryReference(int seed)
    {
        var random = new Random(seed);
        var prior = Enumerable.Range(1, 2048).Where(i => i % 7 != 0)
            .Select(i => Instance(new((uint)i, (uint)random.Next(1, 4)), i)).ToArray();
        var current = Enumerable.Range(1, 2200).Where(i => i % 5 != 0)
            .Select(i => Instance(new((uint)i, (uint)random.Next(1, 4)), i + 50)).ToArray();
        var before = Snapshot(prior, 1);
        var after = Snapshot(current, 2);
        var index = new RenderSnapshotIndex(false);
        index.Rebuild(after, before);
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        buffer.Publish(before); Assert.True(world.Update(buffer));
        buffer.Publish(after); Assert.True(world.Update(buffer));
        for (int i = 0; i < current.Length; i++)
        {
            int expected = Find(before, current[i].Entity);
            Assert.Equal(expected, index.PreviousIndex(i));
            foreach (float alpha in new[] { 0f, .25f, .5f, 1f })
            {
                var actual = world.GetInterpolatedInstance(i, alpha);
                Assert.True(world.TryGetInterpolatedInstance(current[i].Entity, alpha, out var byEntity));
                Assert.Equal(actual, byEntity);
                if (expected < 0) { Assert.Equal(current[i], actual); continue; }
                var old = prior[expected];
                Assert.Equal(RenderTransform.Interpolate(old.Transform, current[i].Transform, alpha), actual.Transform);
                Assert.Equal(MechanicalPose.Interpolate(old.MechanicalPose, current[i].MechanicalPose, alpha), actual.MechanicalPose);
                Assert.Equal(old.UnitFeature.AimYawRadians + MathF.IEEERemainder(
                    current[i].UnitFeature.AimYawRadians - old.UnitFeature.AimYawRadians, MathF.Tau) * alpha,
                    actual.UnitFeature.AimYawRadians);
                Assert.Equal(current[i], actual with { Transform = current[i].Transform,
                    MechanicalPose = current[i].MechanicalPose, UnitFeature = current[i].UnitFeature });
            }
        }
    }

    [Fact]
    public void SessionChangeAndRetainedCallerArraysPreserveIdentityAndLifetime()
    {
        RenderInstance[] caller = [Instance(new(3, 1), 1)];
        var retained = new PresentationSnapshot(new(1), TimeSpan.Zero, 1, caller.AsSpan(), sessionId: new(1));
        caller[0] = Instance(new(3, 2), 99);
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        buffer.Publish(retained); world.Update(buffer);
        for (ulong i = 2; i < 200; i++)
        {
            buffer.Publish(Snapshot([Instance(new(3, 1), (float)i)], i));
            world.Update(buffer);
            Assert.Equal(1, retained.Instances[0].Transform.Position.X);
        }
        buffer.Publish(new(new(0), TimeSpan.Zero, 1, caller.AsSpan(), sessionId: new(2)));
        world.Update(buffer);
        Assert.Null(world.PreviousSnapshot);
        Assert.Equal(caller[0], world.GetInterpolatedInstance(0, .5f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnorderedCallerAndProductionDecalTailPreserveBinarySemanticsWithoutReordering(bool shuffled)
    {
        RenderInstance[] prior = [Instance(new(1, 1), 1), Instance(new(2, 1), 2),
            Instance(new(0xE0000000, 1), 3), Instance(new(0xF0000000, 1), 4),
            Instance(new(0xD0000000, 3), 5), Instance(new(0xD0000001, 2), 6)];
        var current = prior.Select(v => v with { Transform = v.Transform with { Position = v.Transform.Position * 2 } }).ToArray();
        current[1] = current[1] with { Entity = new(2, 2) };
        if (shuffled) { new Random(61).Shuffle(prior); new Random(92).Shuffle(current); }
        var before = Snapshot(prior, 1); var after = Snapshot(current, 2);
        var index = new RenderSnapshotIndex(false);
        index.Rebuild(after, before);
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        buffer.Publish(before); world.Update(buffer);
        foreach (var value in prior) Assert.Equal(Find(before, value.Entity) >= 0,
            world.TryGetInterpolatedInstance(value.Entity, .5f, out _));
        buffer.Publish(after); world.Update(buffer);
        for (int i = 0; i < current.Length; i++)
            Assert.Equal(Find(before, current[i].Entity), index.PreviousIndex(i));
        foreach (var value in current)
        {
            int slot = Find(after, value.Entity);
            bool present = world.TryGetInterpolatedInstance(value.Entity, .5f, out var found);
            Assert.Equal(slot >= 0, present);
            if (present) Assert.Equal(world.GetInterpolatedInstance(slot, .5f), found);
        }
        Assert.True(current.AsSpan().SequenceEqual(after.Instances));
        for (int i = 0; i < 128; i++) index.Rebuild(after, before);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) index.Rebuild(after, before);
        Assert.Equal(bytes, GC.GetAllocatedBytesForCurrentThread());
        var oversized = Snapshot(Enumerable.Range(1, 70_000).Select(i => Instance(new((uint)i, 1), i)).ToArray(), 3);
        index.Rebuild(oversized, before);
        index.Rebuild(after, before);
        Assert.InRange(index.RetainedBytes, 0, 4L * 4 * 65_536);
        index.Rebuild(Snapshot([], 4), after);
    }

    [Fact]
    public void WreckEffectsAndDecalsRetainTheirExistingInterpolationSemantics()
    {
        var wreck = Instance(new(1, 2), 10) with
        { UnitFeature = new(ForgeLine.Game.UnitIds.MainBattleTank, UnitPresentationDamageState.Wreck) };
        var effect = Instance(new(0xF0000000, 1), 20) with
        { VfxFeature = new(VfxEffectKind.PersistentDust, 23, 7, true, 2) };
        var ground = Instance(new(0xD0000000, 5), 30) with
        { DecalFeature = new(DecalKind.TireTrack, .5f, DecalSurface.Ground) };
        var receiver = Instance(new(0xD0000001, 6), 40) with
        { DecalFeature = new(DecalKind.Scorch, .5f, DecalSurface.Receiver) };
        RenderInstance[] current = [wreck, effect, ground, receiver];
        var prior = current.Select(v => v with { Transform = RenderTransform.Identity }).ToArray();
        prior[1] = prior[1] with { VfxFeature = new(VfxEffectKind.PersistentDust, 99, 1, true, 1) };
        var buffer = new PresentationSnapshotBuffer(); var world = new RenderWorld();
        buffer.Publish(Snapshot(prior, 1)); world.Update(buffer);
        buffer.Publish(Snapshot(current, 2)); world.Update(buffer);
        Assert.Equal(effect, world.GetInterpolatedInstance(1, .5f));
        Assert.Equal(ground, world.GetInterpolatedInstance(2, .5f));
        Assert.Equal(RenderTransform.Interpolate(prior[0].Transform, wreck.Transform, .5f), world.GetInterpolatedInstance(0, .5f).Transform);
        int receiverPrevious = Find(Snapshot(prior, 1), receiver.Entity);
        Assert.Equal(receiverPrevious < 0 ? receiver.Transform : RenderTransform.Interpolate(prior[receiverPrevious].Transform, receiver.Transform, .5f), world.GetInterpolatedInstance(3, .5f).Transform);
        for (int i = 0; i < current.Length; i++)
        {
            int slot = Find(Snapshot(current, 2), current[i].Entity);
            bool present = world.TryGetInterpolatedInstance(current[i].Entity, .5f, out var found);
            Assert.Equal(slot >= 0, present);
            if (present) Assert.Equal(world.GetInterpolatedInstance(slot, .5f), found);
        }
    }

    [Fact]
    public void DuplicatePredecessorsRetainTheExistingBinaryTie()
    {
        var prior = Snapshot([Instance(new(1, 1), 1), Instance(new(1, 1), 2), Instance(new(3, 1), 3)], 1);
        var current = Snapshot([Instance(new(1, 1), 4), Instance(new(3, 1), 5)], 2);
        var index = new RenderSnapshotIndex(false); index.Rebuild(current, prior);
        for (int i = 0; i < current.InstanceCount; i++) Assert.Equal(Find(prior, current.Instances[i].Entity), index.PreviousIndex(i));
    }

    [Fact]
    public void RandomUnorderedAndDuplicateInputsMatchEveryBinarySearchResult()
    {
        var random = new Random(6173);
        var mapping = new RenderSnapshotIndex(false);
        for (int sample = 0; sample < 100; sample++)
        {
            var prior = Enumerable.Range(0, 257).Select(i => Instance(new((uint)random.Next(0, 128), (uint)random.Next(0, 4)), i)).ToArray();
            var current = Enumerable.Range(0, 271).Select(i => Instance(new((uint)random.Next(0, 160), (uint)random.Next(0, 4)), i)).ToArray();
            prior[0] = Instance(new(uint.MaxValue, uint.MaxValue), 1);
            current[0] = Instance(new(uint.MaxValue, uint.MaxValue), 2);
            prior[1] = Instance(new(0, uint.MaxValue), 3);
            current[1] = Instance(new(0, uint.MaxValue), 4);
            var before = Snapshot(prior, 1); var after = Snapshot(current, 2);
            mapping.Rebuild(after, before);
            for (int i = 0; i < current.Length; i++) Assert.Equal(Find(before, current[i].Entity), mapping.PreviousIndex(i));
            foreach (var instance in current) Assert.Equal(Find(after, instance.Entity), mapping.FindCurrent(instance.Entity));
        }
    }

    internal static PresentationSnapshot Snapshot(RenderInstance[] values, ulong tick) =>
        new(new(tick), TimeSpan.FromMilliseconds(50), values.Length, values.AsSpan(), sessionId: new(1));

    internal static RenderInstance Instance(EntityId id, float value) => new(id,
        new(new(value, value / 2, -value), Quaternion.CreateFromAxisAngle(Vector3.UnitY, value / 10),
            new(1 + value / 100, 2, 3)), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
        UnitFeature: default(UnitFeaturePresentationMetadata) with { AimYawRadians = value % 2 == 0 ? 3.13f : -3.13f },
        MechanicalPose: new(true, value, value / 2, value / 10, value / 20, value / 100, value / 200));

    internal static int Find(PresentationSnapshot snapshot, EntityId entity)
    {
        int low = 0, high = snapshot.InstanceCount - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int comparison = snapshot.Instances[mid].Entity.CompareTo(entity);
            if (comparison == 0) return mid;
            if (comparison < 0) low = mid + 1; else high = mid - 1;
        }
        return -1;
    }
}
