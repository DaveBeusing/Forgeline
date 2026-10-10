using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayDisplayQualificationTests
{
    public static TheoryData<int, int, uint, int, int> DisplayAndDragDirections
    {
        get
        {
            var cases = new TheoryData<int, int, uint, int, int>();
            foreach (var size in new[] { (1024, 768), (1600, 900), (3440, 1440), (3840, 2160) })
            foreach (uint dpi in new[] { 96u, 120u, 144u, 192u })
            foreach (int x in new[] { -1, 1 })
            foreach (int y in new[] { -1, 1 })
                cases.Add(size.Item1, size.Item2, dpi, x, y);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(DisplayAndDragDirections))]
    public void FullHudClipsEverySurfaceAndMarqueeAcrossDisplayMatrix(int width, int height, uint dpi, int x, int y)
    {
        using var device = new RecordingDevice();
        using var hud = new GameplayHudRenderer(device);
        var context = new RecordingContext { Width = width, Height = height };
        var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [],
            sessionId: new SimulationSessionId(1), playerExperience: default(PlayerExperienceSnapshot) with { Player = new PlayerId(1) });
        var view = RtsInformationLayerView.Empty with
        {
            IsDragSelecting = true,
            DragStart = new Vector2(x > 0 ? -100 : width + 100, y > 0 ? -100 : height + 100),
            DragCurrent = new Vector2(x > 0 ? width + 100 : -100, y > 0 ? height + 100 : -100)
        };
        var bounds = new AxisAlignedBounds(new Vector3(-1000), new Vector3(1000));
        var camera = new RtsCamera();
        hud.Render(context, camera, snapshot, bounds, view, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, dpi, 1);
        Assert.Equal(GameplayHudState.Ready, hud.State);
        Assert.True(hud.LastRenderedVertexCount > 0);
        Assert.All(device.Pipelines, pipeline =>
        {
            Assert.Equal(GraphicsCullMode.None, pipeline.CullMode);
            Assert.False(pipeline.DepthEnabled);
        });
        Assert.True(device.Pipeline!.AlphaBlendEnabled); // The information surface owns the translucent marquee.
        Assert.All(device.Buffers.SelectMany(buffer => buffer.Vertices), vertex =>
        {
            Assert.InRange(vertex.Position.X, -1f, 1f);
            Assert.InRange(vertex.Position.Y, -1f, 1f);
            Assert.InRange(vertex.Color.W, 0f, 1f);
        });
        var informationVertices = Assert.Single(device.Buffers,
            buffer => buffer.Vertices.Any(vertex => vertex.Color.W == .10f)).Vertices;
        Assert.Equal(6, informationVertices.Count(vertex => vertex.Color.W == .10f));
        Assert.Equal(24, informationVertices.Count(vertex => vertex.Color.W == .95f));
        var buffers = device.Buffers.ToArray();
        hud.Render(context, camera, snapshot, bounds, view with { IsDragSelecting = false }, default, default,
            FormationTemplate.Compact, CombatGroupOverviewView.Empty, default, dpi, 1);
        Assert.Equal(buffers, device.Buffers);
        Assert.DoesNotContain(device.Buffers.SelectMany(buffer => buffer.Vertices), vertex => vertex.Color.W == .10f);
    }
}
