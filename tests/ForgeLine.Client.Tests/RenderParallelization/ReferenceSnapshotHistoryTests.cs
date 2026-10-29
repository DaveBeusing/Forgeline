using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.RenderParallelization;

public sealed class ReferenceSnapshotHistoryTests
{
    [Fact]
    public void CurrentOnlyPreservesExistingIdentityAndFirstSnapshotBehavior()
    {
        var current = Snapshot(2, 20);
        var world = ReferenceSnapshotHistory.Create(current, null);
        Assert.Same(current, world.CurrentSnapshot);
        Assert.Null(world.PreviousSnapshot);
        Assert.Equal(current.Instances[0], world.GetInterpolatedInstance(0, .5f));
        Assert.Equal("existing-fixture", ReferenceSnapshotHistory.Fingerprint("existing-fixture", null));
    }

    [Fact]
    public void PopulatedHistoryExercisesCorrespondenceAndBindsPreviousStateToIdentity()
    {
        var previous = Snapshot(1, 4);
        var current = Snapshot(2, 20);
        var world = ReferenceSnapshotHistory.Create(current, previous);
        Assert.Same(previous, world.PreviousSnapshot);
        Assert.Same(current, world.CurrentSnapshot);
        Assert.Equal(12, world.GetInterpolatedInstance(0, .5f).Transform.Position.X);
        string hash = ReferenceSnapshotHistory.Fingerprint("current", previous);
        Assert.Equal(hash, ReferenceSnapshotHistory.Fingerprint("current", Snapshot(1, 4)));
        Assert.NotEqual(hash, ReferenceSnapshotHistory.Fingerprint("current", Snapshot(1, 8)));
        Assert.NotEqual(hash, ReferenceSnapshotHistory.Fingerprint("changed-current", previous));
        Assert.Equal(4, previous.Instances[0].Transform.Position.X);
    }

    [Fact]
    public void GenerationReuseDoesNotInterpolateAgainstTheOldEntity()
    {
        var current = Snapshot(2, 20, generation: 2);
        var world = ReferenceSnapshotHistory.Create(current, Snapshot(1, 4));
        Assert.Equal(current.Instances[0], world.GetInterpolatedInstance(0, .5f));
    }

    [Theory]
    [InlineData(2, 1, 50)]
    [InlineData(3, 1, 50)]
    [InlineData(1, 2, 50)]
    [InlineData(1, 1, 40)]
    public void InvalidHistoryIsRejectedBeforeRendering(ulong tick, ulong session, int milliseconds)
    {
        Assert.Throws<ArgumentException>(() => ReferenceSnapshotHistory.Create(Snapshot(2, 20),
            Snapshot(tick, 4, session, milliseconds: milliseconds)));
    }

    private static PresentationSnapshot Snapshot(ulong tick, float x, ulong session = 1,
        uint generation = 1, int milliseconds = 50) =>
        new(new(tick), TimeSpan.FromMilliseconds(milliseconds), 1,
            [new RenderInstance(new EntityId(1, generation), new(new(x, 0, 0), Quaternion.Identity, Vector3.One),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)], sessionId: new(session));
}
