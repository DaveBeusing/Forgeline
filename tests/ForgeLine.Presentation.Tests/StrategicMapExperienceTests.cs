using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class StrategicMapExperienceTests
{
    [Theory]
    [InlineData(StrategicOverlayMode.Logistics, "ROUTES", "LOAD N/A")]
    [InlineData(StrategicOverlayMode.Supply, "SUPPLY", "CRITICAL")]
    [InlineData(StrategicOverlayMode.Sensors, "SENSORS", "RANGE")]
    [InlineData(StrategicOverlayMode.Navigation, "NAVIGATION", "SECTOR")]
    [InlineData(StrategicOverlayMode.Power, "POWER", "BROWNOUT")]
    [InlineData(StrategicOverlayMode.All, "ALL LAYERS", "LOCAL")]
    [InlineData(StrategicOverlayMode.None, "OVERLAYS OFF", "F10")]
    public void VisibleLayerNamesAndShapeLegendDoNotImplyUnavailableTelemetryIsHealthy(StrategicOverlayMode mode, string label, string legend)
    {
        Assert.Equal(label, RtsStrategicOverlayHudModel.Label(mode));
        Assert.Contains(legend, RtsStrategicOverlayHudModel.Legend(mode));
    }
    [Theory]
    [InlineData(5, 1, 1, StrategicOverlayMode.Power, false)]
    [InlineData(4, 2, 1, StrategicOverlayMode.Power, false)]
    [InlineData(4, 1, 2, StrategicOverlayMode.Power, false)]
    [InlineData(4, 1, 1, StrategicOverlayMode.Supply, false)]
    [InlineData(4, 1, 1, StrategicOverlayMode.Power, true)]
    [InlineData(4, 1, 1, StrategicOverlayMode.Power, false)]
    public void UnknownAndUpdatingLayersRequireExactSessionTickPlayerMode(ulong tick, ulong session, uint player, StrategicOverlayMode mode, bool terminal)
    {
        var overlay = new StrategicOverlaySnapshot(new(4), StrategicOverlayMode.Power, [], [], [], [], [], [], [], [], new(1), new(1));
        var experience = default(PlayerExperienceSnapshot) with { Player = new(player), Tick = new(tick), MatchStatus = terminal ? PlayerMatchStatus.Defeat : PlayerMatchStatus.Active };
        var snapshot = new PresentationSnapshot(new(tick), TimeSpan.FromSeconds(.05), 0, [], sessionId: new(session), playerExperience: experience, strategicOverlay: overlay);
        Assert.Equal(tick == 4 && session == 1 && player == 1 && mode == StrategicOverlayMode.Power && !terminal, RtsStrategicOverlayHudModel.IsCurrent(snapshot, mode));
        Assert.False(RtsStrategicOverlayHudModel.IsCurrent(null, mode));
    }
    [Fact]
    public void BrownoutHasFourLineDiamondWithoutPhysicalPowerConnections()
    {
        var model = new StrategicPowerEntityReadModel(new(1, 1), Vector3.Zero, new(1), false, 0, false,
            PowerGeneratorState.Offline, true, 10, 5, PowerPriority.Industrial, true, PowerOperationalState.Brownout);
        var snapshot = new StrategicOverlaySnapshot(new(4), StrategicOverlayMode.Power, [], [], [], [], [], [], [model], []);
        var draw = new DebugDraw { Enabled = true };
        RtsStrategicOverlayVisualization.Draw(draw, StrategicOverlayMode.Power, snapshot);
        Assert.Contains(draw.Labels, label => label.Text == "POWER BROWNOUT");
        Assert.True(draw.Lines.Length >= 4);
        Assert.All(draw.Lines.ToArray(), line => { Assert.InRange(line.Start.X, -3, 3); Assert.InRange(line.End.X, -3, 3); });
    }
    [Fact]
    public void StrategicListsAreOwnedReadOnlyAndExtractionCarriesAuthority()
    {
        var rows = new[] { new StrategicSensorReadModel(new(2, 1), Vector3.Zero, 10, 20, 5) };
        var snapshot = new StrategicOverlaySnapshot(new(4), StrategicOverlayMode.Sensors, [], [], [], rows, [], [], [], []);
        rows[0] = default;
        Assert.Equal(new EntityId(2, 1), snapshot.Sensors[0].Entity);
        Assert.Throws<NotSupportedException>(() => ((IList<StrategicSensorReadModel>)snapshot.Sensors).Clear());
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetStrategicOverlay(StrategicOverlayMode.All);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction); scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var capture));
        Assert.True(RtsStrategicOverlayHudModel.IsCurrent(capture, StrategicOverlayMode.All));
        Assert.Equal(capture.SessionId, capture.StrategicOverlay!.Session);
        Assert.DoesNotContain(capture.StrategicOverlay.PowerEntities, row => row.Entity == scenario.GetBase(new(2)).CommandCore);
    }
}
