using ForgeLine.Graphics;
using Xunit;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionGraphics;
using static ForgeLine.Presentation.Tests.RenderSubmission.SubmissionReferenceTests;

namespace ForgeLine.Presentation.Tests.RenderSubmission;

public sealed class InstancingFrameReuseTests
{
    [Fact]
    public void AdmittedSlotsKeepMainAndShadowStorageSeparateUntilCompletion()
    {
        using var graphics = new FakeGraphicsDevice { ValidateFences = true,
            DirectionalShadows = new(DirectionalShadowQuality.Low) };
        using var renderer = new SimpleInstanceRenderer(graphics);
        var camera = new RtsCamera();
        var world = CreateWorld(Enumerable.Range(0, 65).Select(i => Root(i, camera.Target)).ToArray());
        var main0 = new FakeGraphicsCommandContext { FrameIndex = 0, SubmissionFence = 1, CaptureDraws = true };
        renderer.Render(main0, camera, world, 1);
        var mainBuffer0 = (FakeGraphicsBuffer)Assert.Single(main0.Draws).Instances;
        var shadow0 = new FakeGraphicsCommandContext { FrameIndex = 0, SubmissionFence = 1, CaptureDraws = true };
        renderer.RenderShadow(shadow0, camera, world, 1,
            new(camera.GetMatrices(1600, 900).ViewProjection, camera.Target, 500, 1000), 65, 1);
        var shadowBuffer0 = (FakeGraphicsBuffer)Assert.Single(shadow0.Draws).Instances;
        Assert.NotSame(mainBuffer0, shadowBuffer0);
        var main1 = new FakeGraphicsCommandContext { FrameIndex = 1, SubmissionFence = 2, CaptureDraws = true };
        renderer.Render(main1, camera, world, 1);
        var mainBuffer1 = (FakeGraphicsBuffer)Assert.Single(main1.Draws).Instances;
        Assert.NotSame(mainBuffer0, mainBuffer1);
        Assert.Equal(1, mainBuffer0.WriteCount);
        Assert.Equal(1, shadowBuffer0.WriteCount);
        // Device admission supplies a completed slot. The renderer itself has no fence API.
        graphics.CompletedFence = 1;
        var reused = new FakeGraphicsCommandContext { FrameIndex = 0, SubmissionFence = 3, CaptureDraws = true };
        renderer.Render(reused, camera, world, 1);
        Assert.Same(mainBuffer0, Assert.Single(reused.Draws).Instances);
        Assert.Equal(2, mainBuffer0.WriteCount);
        Assert.Equal(1, mainBuffer1.WriteCount);
        Assert.Equal(0, mainBuffer0.DisposeCount);
        Assert.Equal(65 * 112, mainBuffer0.LastWriteBytes);
        Assert.Equal(65 * 112, shadowBuffer0.LastWriteBytes);
        graphics.CompletedFence = 3;
    }

    [Fact]
    public void FenceDoubleRejectsEarlyOverwriteAndRendererRecoversAfterAdmission()
    {
        using var graphics = new FakeGraphicsDevice { ValidateFences = true };
        using var renderer = new SimpleInstanceRenderer(graphics);
        var camera = new RtsCamera();
        var world = CreateWorld([Root(0, camera.Target)]);
        var first = new FakeGraphicsCommandContext { SubmissionFence = 7, CaptureDraws = true };
        renderer.Render(first, camera, world, 1);
        var buffer = (FakeGraphicsBuffer)Assert.Single(first.Draws).Instances;
        Assert.Throws<InvalidOperationException>(() => renderer.Render(new FakeGraphicsCommandContext(), camera, world, 1));
        Assert.Equal(1, buffer.WriteCount);
        graphics.CompletedFence = 7;
        renderer.Render(new FakeGraphicsCommandContext { SubmissionFence = 8 }, camera, world, 1);
        Assert.Equal(2, buffer.WriteCount);
        graphics.CompletedFence = 8;
    }

    [Fact]
    public void GrowthOnlyReplacesTheCompletedSlotAndFailedAllocationRetainsItsBuffer()
    {
        using var graphics = new FakeGraphicsDevice { ValidateFences = true };
        using var renderer = new SimpleInstanceRenderer(graphics);
        var camera = new RtsCamera();
        var small = CreateWorld([Root(0, camera.Target)]);
        var first = new FakeGraphicsCommandContext { SubmissionFence = 1, CaptureDraws = true };
        renderer.Render(first, camera, small, 1);
        var original = (FakeGraphicsBuffer)Assert.Single(first.Draws).Instances;
        var other = new FakeGraphicsCommandContext { FrameIndex = 1, SubmissionFence = 2, CaptureDraws = true };
        renderer.Render(other, camera, small, 1);
        var otherBuffer = (FakeGraphicsBuffer)Assert.Single(other.Draws).Instances;
        graphics.CompletedFence = 1;
        var large = CreateWorld(Enumerable.Range(0, 129).Select(i => Root(i, camera.Target)).ToArray());
        graphics.FailBufferCreation = graphics.Buffers.Count + 1;
        Assert.Throws<InvalidOperationException>(() => renderer.Render(new FakeGraphicsCommandContext(), camera, large, 1));
        Assert.Equal(0, original.DisposeCount);
        Assert.Equal(0, otherBuffer.DisposeCount);
        graphics.FailBufferCreation = 0;
        var grown = new FakeGraphicsCommandContext { SubmissionFence = 3, CaptureDraws = true };
        renderer.Render(grown, camera, large, 1);
        var replacement = (FakeGraphicsBuffer)Assert.Single(grown.Draws).Instances;
        Assert.NotSame(original, replacement);
        Assert.Equal(1, original.DisposeCount);
        Assert.Equal(0, otherBuffer.DisposeCount);
        Assert.Equal(129 * 112, replacement.LastWriteBytes);
        Assert.True(replacement.Description.SizeInBytes >= 129 * 112);
        Assert.Equal(1, otherBuffer.WriteCount);
        graphics.CompletedFence = 3;
    }
}
