using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class NavigationDiagnosticsControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyAuthorizedInputOpensThePanelAndClosingDisablesLayers(bool authorized)
    {
        var controller = new NavigationDiagnosticsController(authorized);
        var buffer = new PresentationSnapshotBuffer();
        var session = new SimulationSessionId(1);
        var view = controller.Update(true, true, true, false, buffer, session, [], new(1), "central-divide", 2026);
        Assert.Equal(authorized, view.Visible);
        Assert.Equal(authorized, view.Layers);
        view = controller.Update(true, true, false, false, buffer, session, [], new(1), "central-divide", 2026);
        Assert.False(view.Visible); Assert.False(view.Layers); Assert.False(controller.Open);
    }

    [Fact]
    public void BlockedInputCannotOpenExportOrDrawAndSessionChangeClearsState()
    {
        var controller = new NavigationDiagnosticsController(true);
        var buffer = new PresentationSnapshotBuffer();
        var view = controller.Update(false, true, true, true, buffer, new(1), [new EntityId(1, 1)], new(1), "central-divide", 2026);
        Assert.False(view.Visible); Assert.Empty(controller.Feedback);
        controller.Update(true, true, true, false, buffer, new(1), [], new(1), "central-divide", 2026);
        view = controller.Update(false, false, false, false, buffer, new(1), [], new(1), "central-divide", 2026);
        Assert.False(view.Visible); Assert.False(view.Layers);
        view = controller.Update(true, false, false, false, buffer, new(2), [], new(1), "central-divide", 2026);
        Assert.False(view.Visible); Assert.False(view.Layers); Assert.False(controller.Open);
    }

    [Fact]
    public void SelectionGenerationChangeImmediatelyClearsGeometryAndOldExportFeedback()
    {
        var controller = new NavigationDiagnosticsController(true);
        var buffer = new PresentationSnapshotBuffer();
        var view = controller.Update(true, true, true, false, buffer, new(1), [new EntityId(7, 1)], new(1), "central-divide", 2026);
        Assert.True(view.Layers);
        view = controller.Update(true, false, false, false, buffer, new(1), [new EntityId(7, 2)], new(1), "central-divide", 2026);
        Assert.True(view.Visible);
        Assert.False(view.Layers);
        Assert.Equal(NavigationDiagnosticLayers.None, view.EnabledLayers);
        Assert.Empty(view.Feedback);
    }

    [Fact]
    public void LayersToggleIndependentlyAndAllOffClearsEveryBit()
    {
        var controller = new NavigationDiagnosticsController(true);
        var buffer = new PresentationSnapshotBuffer();
        var view = controller.Update(true, true, true, false, buffer, new(1), [], new(1), "central-divide", 2026);
        Assert.Equal(NavigationDiagnosticLayers.Hull, view.EnabledLayers);
        view = controller.Update(true, false, false, false, buffer, new(1), [], new(1), "central-divide", 2026, nextLayer: true);
        Assert.Equal(1, view.FocusedLayer);
        view = controller.Update(true, false, true, false, buffer, new(1), [], new(1), "central-divide", 2026, scrollDelta: 3);
        Assert.Equal(NavigationDiagnosticLayers.Hull | NavigationDiagnosticLayers.Route, view.EnabledLayers);
        Assert.Equal(3, view.Scroll);
        view = controller.Update(true, false, false, false, buffer, new(1), [], new(1), "central-divide", 2026, clearLayers: true);
        Assert.False(view.Layers);
        Assert.Equal(NavigationDiagnosticLayers.None, view.EnabledLayers);
    }

    [Fact]
    public void ExportReadsLatestPublicationAndRejectsAStaleSessionOrSelection()
    {
        var controller = new NavigationDiagnosticsController(true);
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), 0, [],
            sessionId: new(99)));
        var view = controller.Update(true, true, false, true, buffer, new(1), [new EntityId(1, 1)], new(1), "central-divide", 2026);
        Assert.True(view.Visible);
        Assert.Contains("rejected", controller.Feedback);
    }
}
