using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform.Windows;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class ContentEvidenceNativeTests
{
    [Fact]
    public void ActualSuccessfulNativeFrameResolvesPendingTokenAndNextPreparationInvalidatesIt()
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE content evidence lifecycle", 160, 90));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        var source = Assert.IsAssignableFrom<IGraphicsRenderTelemetry>(graphics);
        var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero });
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
            [new(new(1, 1), RenderTransform.Identity, new RenderMeshHandle(1), new RenderMaterialHandle(1), RenderVisibilityMask.World)]));
        var world = new RenderWorld(); Assert.True(world.Update(buffer));
        using var renderer = new SimpleInstanceRenderer(graphics);
        renderer.EnableContentEvidence();
        Assert.Throws<InvalidDataException>(() => renderer.CaptureContentEvidence());
        var before = source.LastSubmission;
        _ = graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.BeginPass(GraphicsFramePass.World);
            renderer.Render(context, camera, world, 1);
        });
        var proof = renderer.CaptureContentEvidence();
        Assert.Equal(source.LastSubmission, proof.ActualSubmission);
        Assert.Equal(before!.Value.Submission + 1, proof.ActualSubmission.Submission);
        Assert.True(proof.ActualSubmission.SubmittedFence > before.Value.SubmittedFence);
        Assert.True(proof.MainInstances > 0); Assert.True(proof.FallbackInstances > 0);
        Assert.Equal(0, proof.DistinctRuntimeMaterials); Assert.Equal(0, proof.SkinnedInstances);
        renderer.Prepare(camera, world, new RenderPreparationRequest(160, 90, 1));
        Assert.Throws<InvalidDataException>(() => renderer.CaptureContentEvidence());
    }
}
