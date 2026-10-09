using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayHudRuntimeTests
{
    [Theory]
    [InlineData(800, 600, 96, 1.0f)]
    [InlineData(1280, 720, 144, 1.25f)]
    [InlineData(1920, 1080, 192, 2.0f)]
    [InlineData(3440, 1440, 144, 1.0f)]
    public void MetricsAnchorWithinSafeAreaAndNeverCaptureWorldInput(int width, int height, uint dpi, float scale)
    {
        GameplayHudLayout layout = GameplayHudLayout.Create(width, height, dpi, scale);
        HudRect metrics = layout.RuntimeMetrics;
        Assert.Equal(layout.SafeArea.Right, metrics.Right, 3);
        Assert.Equal(layout.SafeArea.Y, metrics.Y);
        Assert.False(metrics.Intersects(layout.TopStatusBar));
        Assert.False(metrics.Intersects(layout.ActionDock));
        Assert.False(metrics.Intersects(layout.AlertStack));
        Assert.True(layout.SafeArea.Contains(new(metrics.X, metrics.Y)));
        Assert.True(layout.SafeArea.Contains(new(metrics.Right, metrics.Bottom)));
        Assert.False(HudInteractionContext.BlocksWorldPointer(new(metrics.X + 1, metrics.Y + 1), layout, true));

        using var device = new RecordingDevice();
        using var renderer = new ResourcePowerHudRenderer(device, null);
        var context = new RecordingContext { Width = width, Height = height };
        renderer.Render(context, null, layout, new RuntimeMetricsView(144, 20, RuntimeSimulationState.Running));
        Assert.All(device.Buffer!.Vertices, v =>
        {
            Assert.InRange(v.Position.X, -1f, 1f);
            Assert.InRange(v.Position.Y, -1f, 1f);
        });
    }

    [Fact]
    public void HudTransitionsFromLoadingThroughPlayerBindingToReadyAndBackOnRestart()
    {
        using var device = new RecordingDevice();
        using var renderer = new GameplayHudRenderer(device);
        var context = new RecordingContext();
        var camera = new RtsCamera();
        AxisAlignedBounds bounds = new(new(-100f), new(100f));
        renderer.Render(context, camera, null, bounds, default, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, 96, 1);
        Assert.Equal(GameplayHudState.WaitingForSnapshot, renderer.State);
        Assert.NotEmpty(device.Buffer!.Vertices);
        var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, []);
        Assert.Equal(GameplayHudState.WaitingForPlayerData, GameplayHudRenderer.ResolveState(snapshot));
        renderer.Render(context, camera, snapshot, bounds, default, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, 96, 1);
        Assert.Equal(GameplayHudState.WaitingForPlayerData, renderer.State);
        var bound = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [],
            playerExperience: default(PlayerExperienceSnapshot));
        renderer.Render(context, camera, bound, bounds, default, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, 96, 1);
        Assert.Equal(GameplayHudState.Ready, renderer.State);
        renderer.Render(context, camera, null, bounds, default, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, 96, 1);
        Assert.Equal(GameplayHudState.WaitingForSnapshot, renderer.State);
    }

    [Fact]
    public void ResourcePanelsAndGlyphsSurviveTheirCounterclockwiseScreenWinding()
    {
        using var device = new RecordingDevice();
        using var renderer = new ResourcePowerHudRenderer(device, null);
        var context = new RecordingContext();
        var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [],
            playerExperience: default(PlayerExperienceSnapshot));
        renderer.Render(context, snapshot, GameplayHudLayout.Create(context.Width, context.Height, 96));
        Vertex[] vertices = device.Buffer!.Vertices;
        Vector2 ab = vertices[1].Position - vertices[0].Position;
        Vector2 ac = vertices[2].Position - vertices[0].Position;
        Assert.True(ab.X * ac.Y - ab.Y * ac.X > 0); // Viewport Y inversion makes this counterclockwise on screen.
        Assert.Equal(GraphicsCullMode.None, device.Pipeline!.CullMode);
        var originalBuffer = device.Buffer;
        renderer.Render(context, snapshot, GameplayHudLayout.Create(1280, 720, 144));
        Assert.Same(originalBuffer, device.Buffer);
    }
}
