using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MechanicalMotionTests
{
    [Theory]
    [InlineData(MechanicalMotionKind.Radar, .285f, .6f)]
    [InlineData(MechanicalMotionKind.Fan, .292f, 2.2f)]
    public void OperationalRotorsKeepScaledPivotsFreezePhaseAndDisableForWrecks(MechanicalMotionKind motion, float height, float rate)
    {
        Vector3 pivot = new(0, height, 0);
        MechanicalPartBinding[] parts = [new(new("rotor", 0, 3, -1, motion, "pivot", rate), pivot)];
        Matrix4x4 world = Matrix4x4.CreateScale(14, 18, 14) * Matrix4x4.CreateTranslation(650, 15, 520);
        Matrix4x4[] matrices = new Matrix4x4[1];
        var instance = Instance() with
        {
            BuildingFeature = new(BuildingIds.Radar, BuildingPresentationState.Operational),
            MechanicalPose = new(true, 0, 1.55, 1.6, -1, 0, 0)
        };
        MechanicalTransforms.Compose(parts, instance, world, matrices);
        Matrix4x4 active = matrices[0];
        Assert.True(Vector3.Distance(Vector3.Transform(pivot, world), Vector3.Transform(pivot, active)) < .0001f);
        Assert.NotEqual(world, active);
        instance = instance with
        {
            BuildingFeature = new(BuildingIds.Radar, BuildingPresentationState.Unpowered),
            MechanicalPose = instance.MechanicalPose with { Seconds = 100 }
        };
        MechanicalTransforms.Compose(parts, instance, world, matrices);
        Assert.Equal(active, matrices[0]);
        MechanicalTransforms.Compose(parts, instance, world, matrices, weight: 0);
        Assert.Equal(world, matrices[0]);
        instance = instance with { BuildingFeature = new(BuildingIds.Radar, BuildingPresentationState.Destroyed) };
        MechanicalTransforms.Compose(parts, instance, world, matrices);
        Assert.Equal(world, matrices[0]);
    }

    [Fact]
    public void DetailMotionIsNeutralAcrossBothLodHysteresisEdges()
    {
        foreach (float threshold in new[] { 90f, 150f })
        {
            Assert.Equal(0, SimpleInstanceRenderer.MechanicalDetailWeight(threshold * .88f, threshold, 1));
            Assert.InRange(SimpleInstanceRenderer.MechanicalDetailWeight(threshold * 1.12f, threshold, 1), 0, .00001f);
            Assert.Equal(1, SimpleInstanceRenderer.MechanicalDetailWeight(threshold * 2, threshold, 1));
        }
    }

    [Fact]
    public void InterpolationFreezesAtCompletedTickAndDoesNotCrossSessions()
    {
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        RenderInstance first = Instance() with { MechanicalPose = new(true, 0, 0, 1, -1, 0, 0) };
        RenderInstance second = first with { MechanicalPose = new(true, 2, 1, 2, -1, .2f, 1) };
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromSeconds(1), 1, new[] { first }, sessionId: new(1)));
        world.Update(buffer);
        buffer.Publish(new(new SimulationTick(2), TimeSpan.FromSeconds(1), 1, new[] { second }, sessionId: new(1)));
        world.Update(buffer);
        Assert.Equal(1, world.GetInterpolatedInstance(0, .5f).MechanicalPose.TravelMeters);
        Assert.Equal(second.MechanicalPose, world.GetInterpolatedInstance(0, 100).MechanicalPose);
        Assert.Equal(second.MechanicalPose, world.GetInterpolatedInstance(0, 100).MechanicalPose);
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromSeconds(1), 1, new[] { first }, sessionId: new(2)));
        world.Update(buffer);
        Assert.Equal(first.MechanicalPose, world.GetInterpolatedInstance(0, .5f).MechanicalPose);
    }

    [Fact]
    public void HierarchyKeepsPivotFixedAndComposesChildBeforeParent()
    {
        MechanicalPartBinding[] parts =
        [
            new(new("turret", 0, 3, -1, MechanicalMotionKind.TurretYaw, "pivot"), new(0, 1, 0)),
            new(new("gun", 3, 3, 0, MechanicalMotionKind.Cannon, "gun", Travel: .1f), new(0, 1, .2f))
        ];
        RenderInstance instance = Instance() with
        {
            UnitFeature = new(UnitIds.MainBattleTank, UnitPresentationDamageState.Intact, MathF.PI / 2),
            MechanicalPose = new(true, 0, 0, 1, -1, .2f, 0)
        };
        Matrix4x4[] transforms = new Matrix4x4[2];
        MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, transforms);
        Assert.True(Vector3.Distance(new(0, 1, 0), Vector3.Transform(new Vector3(0, 1, 0), transforms[0])) < .0001f);
        Assert.True(Vector3.Distance(new(.2f, 1, 0), Vector3.Transform(new Vector3(0, 1, .2f), transforms[1])) < .0001f);
    }

    [Fact]
    public void CompletedTickHistoryFreezesIdleAndResetsAfterGapsOrInvisibleEntities()
    {
        var history = new MechanicalPoseHistory();
        EntityId entity = new(1, 1);
        history.Begin(SimulationSessionId.None, 1, .05);
        history.Capture(entity, Vector3.Zero, Quaternion.Identity, true, false, 0, false);
        history.Begin(SimulationSessionId.None, 2, .05);
        MechanicalPose moving = history.Capture(entity, Vector3.UnitZ, Quaternion.Identity, true, true, 0, true);
        Assert.Equal(1, moving.TravelMeters);
        Assert.Equal(.05, moving.ActiveSeconds);
        history.Begin(SimulationSessionId.None, 3, .05);
        MechanicalPose stopped = history.Capture(entity, Vector3.UnitZ, Quaternion.Identity, false, false, 0, false);
        Assert.Equal(moving.TravelMeters, stopped.TravelMeters);
        Assert.Equal(moving.ActiveSeconds, stopped.ActiveSeconds);
        Assert.Equal(moving.LastShotSeconds, stopped.LastShotSeconds);
        history.Begin(SimulationSessionId.None, 4, .05);
        history.Begin(SimulationSessionId.None, 5, .05);
        MechanicalPose revealed = history.Capture(entity, new(0, 0, 100), Quaternion.Identity, true, false, 0, false);
        Assert.Equal(0, revealed.TravelMeters);
        Assert.Equal(-1, revealed.LastShotSeconds);
        history.Begin(SimulationSessionId.None, 100, .05);
        Assert.Equal(0, history.Capture(entity, Vector3.UnitZ, Quaternion.Identity, true, false, 0, false).TravelMeters);
    }

    [Fact]
    public void RecoilUsesEventTimeAndNeverStartsBeforeEvent()
    {
        MechanicalPartBinding[] parts = [new(new("gun", 0, 3, -1, MechanicalMotionKind.Cannon, "pivot", Travel: .1f), Vector3.Zero)];
        Matrix4x4[] matrices = new Matrix4x4[1];
        var instance = Instance() with { MechanicalPose = new(true, 0, 0, .9, 1, 0, 0) };
        MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, matrices);
        Assert.Equal(Matrix4x4.Identity, matrices[0]);
        instance = instance with { MechanicalPose = instance.MechanicalPose with { Seconds = 1.05 } };
        MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, matrices);
        Assert.InRange(matrices[0].M43, -.10001f, -.09999f);
        instance = instance with { MechanicalPose = instance.MechanicalPose with { Seconds = 1.4 } };
        MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, matrices);
        Assert.Equal(Matrix4x4.Identity, matrices[0]);
    }

    [Fact]
    public void ThousandInstanceCompositionIsRepeatableAndAllocatesNothingAfterWarmup()
    {
        MechanicalPartBinding[] parts = [new(new("wheel", 0, 3, -1, MechanicalMotionKind.Wheel, "pivot"), Vector3.Zero)];
        Matrix4x4[] matrices = new Matrix4x4[1];
        var instance = Instance() with { MechanicalPose = new(true, 3, 2, 1, -1, 0, 0) };
        for (int i = 0; i < 1024; i++) MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, matrices);
        Matrix4x4 expected = matrices[0];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MechanicalTransforms.Compose(parts, instance, Matrix4x4.Identity, matrices);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(expected, matrices[0]);
    }

    [Fact]
    public void InfantryStrideUsesTravelKeepsHipFixedAndDisablesForWrecksAndReducedDetail()
    {
        Vector3 hip = new(.04f, .015f, .22f);
        MechanicalPartBinding[] parts = [new(new("leg", 0, 3, -1,
            MechanicalMotionKind.InfantryStride, "hip", Rate: 4, Travel: .42f), hip)];
        var instance = Instance() with { UnitFeature = new(UnitIds.RifleSquad, UnitPresentationDamageState.Intact),
            MechanicalPose = new(true, .35, 100, 100, -1, 0, 0) };
        var world = Matrix4x4.CreateScale(1.8f) * Matrix4x4.CreateTranslation(3, .9f, 5);
        Matrix4x4[] output = new Matrix4x4[1];
        MechanicalTransforms.Compose(parts, instance, world, output);
        Assert.NotEqual(world, output[0]);
        Assert.True(Vector3.Distance(Vector3.Transform(hip, world), Vector3.Transform(hip, output[0])) < .0001f);
        Matrix4x4 walking = output[0];
        instance = instance with { MechanicalPose = instance.MechanicalPose with { ActiveSeconds = 200, Seconds = 200 } };
        MechanicalTransforms.Compose(parts, instance, world, output);
        Assert.Equal(walking, output[0]);
        MechanicalTransforms.Compose(parts, instance, world, output, weight: 0);
        Assert.Equal(world, output[0]);
        instance = instance with { UnitFeature = instance.UnitFeature with { DamageState = UnitPresentationDamageState.Wreck } };
        MechanicalTransforms.Compose(parts, instance, world, output);
        Assert.Equal(world, output[0]);
    }

    private static RenderInstance Instance() => new(new EntityId(1, 1),
        new RenderTransform(Vector3.Zero, Quaternion.Identity, Vector3.One),
        new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World);
}
