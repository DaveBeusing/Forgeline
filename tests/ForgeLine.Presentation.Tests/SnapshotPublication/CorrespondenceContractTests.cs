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
