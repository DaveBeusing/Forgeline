using System.Numerics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SplashTimelineTests
{
    private static readonly string[] ExpectedVisibleLayerOrder = ["background", "fog", "monogram", "wordmark", "subtitle"];

    private static readonly SplashKeyframe[] Frames =
    [
        new(0, 0, 1, Vector2.Zero, 0),
        new(2, 1, 2, new Vector2(2, 4), 1)
    ];

    [Fact]
    public void BeforeFirstKeyframeClamps() =>
        Assert.Equal(0, SplashTimelineEvaluator.Evaluate(Frames, -1).Opacity);

    [Fact]
    public void AfterLastKeyframeClamps() =>
        Assert.Equal(1, SplashTimelineEvaluator.Evaluate(Frames, 5).Opacity);

    [Fact]
    public void InterpolatesAllChannels()
    {
        SplashLayerState state = SplashTimelineEvaluator.Evaluate(Frames, 1);
        Assert.Equal(0.5f, state.Opacity);
        Assert.Equal(1.5f, state.Scale);
        Assert.Equal(new Vector2(1, 2), state.Offset);
        Assert.Equal(0.5f, state.GlowIntensity);
    }

    [Fact]
    public void EqualTimestampsAreSafe()
    {
        SplashKeyframe[] frames =
        [
            new(0, 0, 1, Vector2.Zero, 0),
            new(0, 1, 1, Vector2.Zero, 0)
        ];
        Assert.Equal(1, SplashTimelineEvaluator.Evaluate(frames, 1).Opacity);
    }

    [Fact]
    public void InvalidTimelineRejected()
    {
        SplashDefinition definition = UndefinedBehaviorStudioSplash.Create();
        definition.Validate();
        var invalid = definition with { MinimumVisibleDuration = TimeSpan.FromSeconds(4) };
        Assert.Throws<ArgumentException>(() => invalid.Validate());
    }

    [Fact]
    public void SkipHonorsMinimumDurationAndFadesOut()
    {
        var controller = new SplashScreenController(UndefinedBehaviorStudioSplash.Create());
        controller.Start();
        controller.Update(TimeSpan.FromSeconds(0.8), new SplashInputState(true));
        Assert.False(controller.WasSkipped);
        controller.Update(TimeSpan.FromSeconds(0.4), new SplashInputState(true));
        Assert.True(controller.WasSkipped);
        Assert.False(controller.IsComplete);
        controller.Update(TimeSpan.FromSeconds(0.3), new SplashInputState(false));
        Assert.True(controller.IsComplete);
    }

    [Fact]
    public void CompletesNaturally()
    {
        var controller = new SplashScreenController(UndefinedBehaviorStudioSplash.Create());
        controller.Start();
        controller.Update(TimeSpan.FromSeconds(3), new SplashInputState(false));
        Assert.True(controller.IsComplete);
        Assert.False(controller.WasSkipped);
    }
    [Fact]
    public void PreflightDropsMissingOptionalLayers()
    {
        SplashDefinition definition = UndefinedBehaviorStudioSplash.Create();
        SplashDefinition? ready = SplashAssetPreflight.Prepare(
            definition,
            id => !id.EndsWith(".background", StringComparison.Ordinal));
        Assert.NotNull(ready);
        Assert.Equal(5, ready.Layers.Count);
        Assert.DoesNotContain(ready.Layers, layer => layer.Id == "background");
    }

    [Fact]
    public void PreflightFailsSafelyWhenRequiredArtworkMissing()
    {
        var diagnostics = new List<string>();
        SplashDefinition? ready = SplashAssetPreflight.Prepare(
            UndefinedBehaviorStudioSplash.Create(),
            id => !id.EndsWith(".wordmark", StringComparison.Ordinal),
            diagnostics.Add);
        Assert.Null(ready);
        Assert.Contains(diagnostics, message => message.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void PreflightHandlesLookupFailureWithoutCrashingStartup()
    {
        SplashDefinition? ready = SplashAssetPreflight.Prepare(
            UndefinedBehaviorStudioSplash.Create(),
            _ => throw new IOException("Asset lookup failed"));
        Assert.Null(ready);
    }
    [Fact]
    public void SplashSurfaceCarriesOpacityWithoutMenuControls()
    {
        FrontendSurfaceView view = FrontendSurfaceView.StudioSplash(1.5f, 0.25f);
        Assert.Equal(FrontendSurfaceKind.StudioSplash, view.Kind);
        Assert.Equal(1.5f, view.SplashElapsedSeconds);
        Assert.Equal(0.25f, view.SplashMasterOpacity);
        Assert.Empty(view.MenuEntries);
    }

    [Fact]
    public void OutOfBoundsLayerAnchorIsRejected()
    {
        SplashDefinition original = UndefinedBehaviorStudioSplash.Create();
        SplashLayerDefinition[] layers = original.Layers.ToArray();
        layers[0] = layers[0] with { Anchor = new Vector2(2f, 0.5f) };
        Assert.Throws<ArgumentException>(() => (original with { Layers = layers }).Validate());
    }

    [Fact]
    public void ControllerRendersVisibleLayersInDrawOrder()
    {
        var controller = new SplashScreenController(UndefinedBehaviorStudioSplash.Create());
        var renderer = new SplashRecorder();
        controller.Start();
        controller.Update(TimeSpan.FromSeconds(2.5), new SplashInputState(false));
        controller.Render(renderer);
        Assert.Equal(1, renderer.Begins);
        Assert.Equal(1, renderer.Ends);
        Assert.Equal(ExpectedVisibleLayerOrder, renderer.Layers);
    }

    private sealed class SplashRecorder : ISplashRenderer
    {
        public int Begins { get; private set; }
        public int Ends { get; private set; }
        public List<string> Layers { get; } = [];
        public void BeginSplash() => Begins++;
        public void DrawLayer(SplashLayerDefinition layer, in SplashLayerState state, float opacity) =>
            Layers.Add(layer.Id);
        public void EndSplash() => Ends++;
    }
}
