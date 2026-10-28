using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class TacticalDisplaySettingsTests
{
    [Theory]
    [InlineData(TacticalTeamPalette.BlueOrange)]
    [InlineData(TacticalTeamPalette.Grayscale)]
    public void DenseOwnedMinimapKeepsEveryAlternateTeamGlyphWithinExistingCapacity(TacticalTeamPalette palette)
    {
        const int count = 4096;
        var settings = new TacticalDisplaySettings { TeamPalette = palette };
        var army = Enumerable.Range(1, count).Select(i => new RenderInstance(new((uint)i, 1),
            new(new Vector3(i % 64 - 32, 0, i / 64 - 32), Quaternion.Identity, Vector3.One),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            Selectable: new(new PlayerId(1), ControllableEntityCategory.Unit),
            UnitFeature: new(UnitIds.MainBattleTank, default))).ToArray();
        var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(1) };
        var snapshot = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), count, army,
            playerExperience: experience);
        using var graphics = new RecordingDevice();
        using var renderer = new RtsInformationOverlayRenderer(graphics);
        renderer.ConfigureDisplay(settings);
        renderer.Render(new RecordingContext(), new RtsCamera(), snapshot,
            new AxisAlignedBounds(new(-100), new(100)), RtsInformationLayerView.Empty,
            CombatGroupOverviewView.Empty, 96);
        var teamColor = settings.TeamColor(false, Vector4.One);
        Assert.Equal(count * 30, graphics.Buffers.SelectMany(buffer => buffer.Vertices).Count(vertex => vertex.Color == teamColor));
        Assert.InRange(renderer.LastRenderedVertexCount, count * 30, 262_144);
    }

    [Fact]
    public void PalettePreservesOpacityAndKeepsTeamValuesDistinctInGrayscale()
    {
        var standard = new Vector4(.3f, .6f, .2f, .5f);
        Assert.Equal(standard, new TacticalDisplaySettings().TeamColor(false, standard));
        foreach (var palette in new[] { TacticalTeamPalette.BlueOrange, TacticalTeamPalette.Grayscale })
        {
            var settings = new TacticalDisplaySettings { TeamPalette = palette };
            settings.Validate();
            var friendly = settings.TeamColor(false, standard);
            var enemy = settings.TeamColor(true, standard);
            Assert.NotEqual(friendly, enemy);
            Assert.Equal(.5f, friendly.W);
            Assert.Equal(.5f, enemy.W);
            Assert.True(MathF.Abs(Luma(friendly) - Luma(enemy)) > .08f);
        }
    }

    [Theory]
    [InlineData(1024, 720, 96)]
    [InlineData(1280, 720, 192)]
    [InlineData(1920, 1080, 120)]
    [InlineData(3440, 1440, 144)]
    public void DisplayTransitionsKeepVitalRegionsInsideSafeBounds(int width, int height, uint dpi)
    {
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            var layout = GameplayHudLayout.Create(width, height, dpi, scale);
            foreach (var rect in new[] { layout.TopStatusBar, layout.Minimap, layout.AlertStack })
            {
                Assert.False(rect.IsEmpty);
                Assert.InRange(rect.X, layout.SafeArea.X, layout.SafeArea.Right);
                Assert.InRange(rect.Right, layout.SafeArea.X, layout.SafeArea.Right + .01f);
                Assert.InRange(rect.Y, layout.SafeArea.Y, layout.SafeArea.Bottom);
                Assert.InRange(rect.Bottom, layout.SafeArea.Y, layout.SafeArea.Bottom + .01f);
            }
            Assert.False(layout.TopStatusBar.Intersects(layout.Minimap));
        }
    }

    private static float Luma(Vector4 value) => .2126f * value.X + .7152f * value.Y + .0722f * value.Z;
}
