using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests.RenderQualification;

public sealed class ScalingReferenceFixtureTests
{
    [Theory]
    [InlineData(VisualReferenceSceneKind.Scaling1000High, 1000, false)]
    [InlineData(VisualReferenceSceneKind.Scaling1000Low, 1000, true)]
    [InlineData(VisualReferenceSceneKind.Scaling10000High, 10000, false)]
    [InlineData(VisualReferenceSceneKind.Scaling10000Low, 10000, true)]
    [InlineData(VisualReferenceSceneKind.Scaling100000High, 100000, false)]
    [InlineData(VisualReferenceSceneKind.Scaling100000Low, 100000, true)]
    public void ProtocolCreatesTwoBoundedImmutableHistories(VisualReferenceSceneKind kind, int count, bool low)
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var fixture = VisualReferenceScene.Create(kind, terrain);
        var repeat = VisualReferenceScene.Create(kind, terrain);
        Assert.Equal(fixture.Fingerprint, repeat.Fingerprint);
        Assert.Equal(count, fixture.Snapshot.InstanceCount);
        Assert.Equal(count, fixture.PreviousSnapshot!.InstanceCount);
        Assert.Equal(31UL, fixture.PreviousSnapshot.Tick.Value);
        Assert.Equal(32UL, fixture.Snapshot.Tick.Value);
        Assert.Equal(1UL, fixture.Snapshot.SessionId.Value);
        Assert.Equal(TimeSpan.FromMilliseconds(50), fixture.Snapshot.TickDuration);
        Assert.Equal(0, fixture.Snapshot.SimulationEntityCount);
        int distant = 0;
        for (int i = 0; i < count; i++)
        {
            var before = fixture.PreviousSnapshot.Instances[i];
            var after = fixture.Snapshot.Instances[i];
            Assert.Equal((uint)i + 1, after.Entity.Index);
            Assert.Equal(before.Entity, after.Entity);
            Assert.Equal(new Vector3(.1f, .12f, .1f), after.Transform.Scale);
            Assert.Equal(Quaternion.Identity, before.Transform.Rotation);
            Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitY, .01f), after.Transform.Rotation);
            Assert.Equal(before.Transform.Position + new Vector3(.02f, 0, .03f), after.Transform.Position);
            Assert.Equal(WorldVisualId.PropCrate, after.WorldFeature.Visual);
            Assert.False(after.WorldFeature.IsInspectable);
            Assert.False(after.Selectable.IsSelectable);
            Assert.Equal(default, after.MechanicalPose);
            if (after.Transform.Position.X > 4000) distant++;
        }
        Assert.Equal(low ? count * 9 / 10 : 0, distant);
        var world = new RenderWorld();
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(fixture.PreviousSnapshot); world.Update(buffer);
        buffer.Publish(fixture.Snapshot); world.Update(buffer);
        Assert.NotEqual(fixture.Snapshot.Instances[0].Transform.Position,
            world.GetInterpolatedInstance(0, .5f).Transform.Position);
    }

    [Fact]
    public void TerrainOnlyAndExistingFixturesRemainDistinct()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var empty = VisualReferenceScene.Create(VisualReferenceSceneKind.ScalingTerrainOnly, terrain);
        Assert.Equal(0, empty.Snapshot.InstanceCount);
        Assert.Equal(0, empty.PreviousSnapshot!.InstanceCount);
        Assert.False(RenderScalingReferenceScene.Contains(VisualReferenceSceneKind.WorldProps1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderScalingReferenceScene.Create(VisualReferenceSceneKind.WorldProps1000, terrain));
        var high = VisualReferenceScene.Create(VisualReferenceSceneKind.Scaling1000High, terrain);
        var low = VisualReferenceScene.Create(VisualReferenceSceneKind.Scaling1000Low, terrain);
        Assert.NotEqual(high.Fingerprint, low.Fingerprint);
        Assert.NotEqual(empty.Fingerprint, high.Fingerprint);
        Assert.Null(VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitYard, terrain).PreviousSnapshot);
    }
}
