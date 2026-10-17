using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RigidClipPlaybackTests
{
    private static readonly AnimationChannel Translation = new(0, AnimationPath.Translation, AnimationInterpolation.Linear,
        [0, 1, 2], [Vector4.Zero, new(.1f, 0, 0, 0), Vector4.Zero]);
    private static readonly AnimationChannel Rotation = new(0, AnimationPath.Rotation, AnimationInterpolation.Linear,
        [0, 1, 2], [new(0, 0, 0, 1), new(0, MathF.Sin(.25f), 0, MathF.Cos(.25f)), new(0, 0, 0, 1)]);
    private static readonly RuntimeAnimationData Clip = new("cycle", [new("arm", -1)], [Translation, Rotation]);
    private static readonly MechanicalPartBinding[] Parts =
        [new(new("arm", 0, 3, -1, MechanicalMotionKind.Static, "pivot"), Vector3.Zero)
        { ClipTranslation = Translation, ClipRotation = Rotation }];
    private static RenderInstance Instance(double time) => new RenderInstance(new EntityId(1, 1),
        new RenderTransform(Vector3.Zero, Quaternion.Identity, Vector3.One), new RenderMeshHandle(1),
        RenderMaterialHandle.Default, RenderVisibilityMask.World) { MechanicalPose = new(true, 0, time, time, -1, 0, 0) };

    [Fact]
    public void PlaybackUsesCopiedClockLoopsFreezesAndReturnsNeutralAtReducedDetail()
    {
        var instance = Instance(1); Matrix4x4[] result = new Matrix4x4[1];
        MechanicalTransforms.Compose(Parts, instance, Matrix4x4.Identity, result, clip: Clip);
        Matrix4x4 expected = Matrix4x4.CreateRotationY(.5f) * Matrix4x4.CreateTranslation(.1f, 0, 0);
        Assert.InRange(Vector3.Distance(Vector3.Transform(Vector3.UnitX, expected), Vector3.Transform(Vector3.UnitX, result[0])), 0, .000001f);
        Matrix4x4 frozen = result[0];
        instance = instance with { MechanicalPose = instance.MechanicalPose with { Seconds = 300 } };
        MechanicalTransforms.Compose(Parts, instance, Matrix4x4.Identity, result, clip: Clip);
        Assert.Equal(frozen, result[0]);
        MechanicalTransforms.Compose(Parts, Instance(3), Matrix4x4.Identity, result, clip: Clip);
        Assert.Equal(frozen, result[0]);
        MechanicalTransforms.Compose(Parts, Instance(1), Matrix4x4.Identity, result, 0, Clip);
        Assert.Equal(Matrix4x4.Identity, result[0]);
        MechanicalTransforms.Compose(Parts, Instance(1), Matrix4x4.Identity, result, .5f, Clip);
        Assert.InRange(result[0].Translation.X, .04999f, .05001f);
        MechanicalTransforms.Compose(Parts, Instance(0), Matrix4x4.Identity, result, clip: Clip);
        Assert.Equal(Matrix4x4.Identity, result[0]);
    }

    [Fact]
    public void WreckDestroyedAndAbsentSnapshotNeverAnimate()
    {
        var instance = Instance(1);
        RenderInstance[] cases = [instance with { MechanicalPose = default },
            instance with { UnitFeature = new(UnitIds.MainBattleTank, UnitPresentationDamageState.Wreck) },
            instance with { BuildingFeature = new(BuildingIds.Radar, BuildingPresentationState.Destroyed) }];
        foreach (var item in cases)
        {
            Matrix4x4[] result = new Matrix4x4[1];
            MechanicalTransforms.Compose(Parts, item, Matrix4x4.Identity, result, clip: Clip);
            Assert.Equal(Matrix4x4.Identity, result[0]);
        }
    }

    [Fact]
    public void SnapshotInterpolationAndHierarchyComposeWithoutAllocatingPerInstance()
    {
        var instance = Instance(0) with { MechanicalPose = MechanicalPose.Interpolate(Instance(0).MechanicalPose, Instance(1).MechanicalPose, .5f) };
        MechanicalPartBinding[] hierarchy = [Parts[0], new(new("child", 3, 3, 0, MechanicalMotionKind.Static, "pivot"), Vector3.Zero)];
        Matrix4x4[] matrices = new Matrix4x4[2];
        MechanicalTransforms.Compose(hierarchy, instance, Matrix4x4.Identity, matrices, clip: Clip);
        Assert.Equal(matrices[0], matrices[1]);
        for (int i = 0; i < 128; i++) MechanicalTransforms.Compose(hierarchy, instance, Matrix4x4.Identity, matrices, clip: Clip);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MechanicalTransforms.Compose(hierarchy, instance, Matrix4x4.Identity, matrices, clip: Clip);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(matrices[0].Translation.X, .04999f, .05001f);
    }

    [Fact]
    public void UnitQuaternionInterpolationChoosesShortestArcAndStepHoldsBoundary()
    {
        var negative = new AnimationChannel(0, AnimationPath.Rotation, AnimationInterpolation.Linear,
            [0, 1], [new(0, 0, 0, 1), new(0, 0, 0, -1)]);
        Assert.InRange(MathF.Abs(negative.Evaluate(.5f).W), .99999f, 1.00001f);
        var step = new AnimationChannel(0, AnimationPath.Translation, AnimationInterpolation.Step,
            [0, 1, 2], [Vector4.Zero, Vector4.UnitX, Vector4.Zero]);
        Assert.Equal(Vector4.Zero, step.Evaluate(.999f)); Assert.Equal(Vector4.UnitX, step.Evaluate(1));
    }
}
