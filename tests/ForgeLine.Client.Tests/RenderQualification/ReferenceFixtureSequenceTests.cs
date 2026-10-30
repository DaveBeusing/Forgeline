using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceFixtureSequenceTests
{
    [Fact]
    public void AcknowledgementsCannotSkipDuplicateOrCoalesceCameraPoses()
    {
        var sequence = new ReferenceFixtureSequence(3);
        Assert.False(sequence.TryRead(out _));
        Assert.Throws<InvalidOperationException>(() => sequence.Request(1));
        sequence.Request(0);
        Assert.Throws<InvalidOperationException>(() => sequence.Request(1));
        Assert.True(sequence.TryRead(out int ordinal)); Assert.Equal(0, ordinal);
        Assert.Throws<InvalidOperationException>(() => sequence.Acknowledge(1));
        sequence.Acknowledge(0);
        Assert.Throws<InvalidOperationException>(() => sequence.Acknowledge(0));
        Assert.False(sequence.TryRead(out _));
        sequence.Request(1); sequence.Acknowledge(1);
        sequence.Request(2); sequence.Acknowledge(2);
        Assert.Equal(2, sequence.Acknowledged);
        Assert.Throws<InvalidOperationException>(() => sequence.Request(3));
    }
    [Fact]
    public void UnsupportedUiAndMultipleCaseRequestsCannotBeRelabelledAsMatchedWorldFixtures()
    {
        var request = Request();
        ReferenceFixtureHostCapture.ValidateSingleCase(request);
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { UiScenarios = [UiReferenceScenario.GameplayTooltip] }));
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { Scenes = [VisualReferenceSceneKind.IndustrialBase, VisualReferenceSceneKind.BattlefieldCombat] }));
        Assert.Throws<NotSupportedException>(() => ReferenceFixtureHostCapture.ValidateSingleCase(request with { Scenes = [VisualReferenceSceneKind.MaterialFoundation] }));
    }
    [Fact]
    public void ColdWindowHasNoInventedWarmBudgetSamples()
    {
        var request = Request() with { RunProfile = ReferenceRunProfile.RendererCold, Warmups = 0, Samples = 128 };
        ReferenceFixtureHostCapture.ValidateSingleCase(request);
        var sequence = new ReferenceFixtureSequence(128);
        for (int index = 0; index < 128; index++) { sequence.Request(index); sequence.Acknowledge(index); }
        Assert.Throws<InvalidOperationException>(() => sequence.Request(128));
    }
    private static VisualReferenceCaptureRequest Request() => new()
    {
        OutputRoot = "unused", RuntimeRoot = "unused", Commit = new string('a', 40),
        Scenes = [VisualReferenceSceneKind.IndustrialBase], Profiles = [RenderQuality.Medium],
        Displays = [new(1920, 1080)], Zooms = [RtsReferenceZoom.NormalGameplay], Warmups = 64, Samples = 512
    };
}
