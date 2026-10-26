using System.Text;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class NavigationDiagnosticReportTests
{
    [Fact]
    public void CurrentOwnedSelectionExportsItsActualSourcesAndRejectsStaleIdentities()
    {
        using var runtime = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var interaction = new PresentationInteractionState();
        var buffer = new PresentationSnapshotBuffer();
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands, runtime.BattlefieldRuntime.MatchStateEntity);
        runtime.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,
            new PresentationExtractionContext(runtime, new PlayerId(1), interaction, gateway)));
        EntityId selected = default;
        foreach (var entity in runtime.Simulation.Entities.Query<UnitIdentity, ControllableEntity>())
            if (runtime.Simulation.Entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck &&
                runtime.Simulation.Entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) selected = entity;
        Assert.True(selected.IsValid);
        interaction.SetSelection([selected]); interaction.SetNavigationDiagnosticsOpen(true);
        runtime.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.True(NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick,
            new(1), selected, runtime.Battlefield.Metadata.Key, 2026, out var bytes));
        Assert.InRange(bytes.Length, 1, NavigationDiagnosticReportEncoder.MaximumReportBytes);
        using var parsed = JsonDocument.Parse(bytes);
        Assert.Equal(2, parsed.RootElement.GetProperty("reportSchemaVersion").GetInt32());
        Assert.Equal(snapshot.Tick.Value, parsed.RootElement.GetProperty("sourceTick").GetUInt64());
        var vehicle = parsed.RootElement.GetProperty("selectedVehicle");
        Assert.Equal("available", vehicle.GetProperty("movementStatus").GetProperty("availability").GetString());
        Assert.Equal("available", vehicle.GetProperty("orientedVehicleHull").GetProperty("availability").GetString());
        Assert.Equal("available", parsed.RootElement.GetProperty("configuredLimits").GetProperty("availability").GetString());
        Assert.False(NavigationDiagnosticReportEncoder.TryEncode(snapshot, new(runtime.Simulation.SessionId.Value + 1), snapshot.Tick,
            new(1), selected, runtime.Battlefield.Metadata.Key, 2026, out _));
        Assert.False(NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick.Next(),
            new(1), selected, runtime.Battlefield.Metadata.Key, 2026, out _));
        Assert.False(NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick,
            new(1), new(selected.Index, selected.Generation + 1), runtime.Battlefield.Metadata.Key, 2026, out _));
        Assert.False(NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick,
            new(2), selected, runtime.Battlefield.Metadata.Key, 2026, out _));
        Assert.False(NavigationDiagnosticReportEncoder.TryEncode(snapshot, runtime.Simulation.SessionId, snapshot.Tick,
            new(1), selected, new string('x', 129), 2026, out _));
        var controller = new NavigationDiagnosticsController(true);
        controller.Update(true, true, false, true, buffer, runtime.Simulation.SessionId, [], new(1), runtime.Battlefield.Metadata.Key, 2026);
        Assert.Contains("rejected", controller.Feedback);
    }

    [Fact]
    public void LocalWriterPublishesOnceNeverOverwritesAndCleansOnlyItsOwnTemporaryFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-navigation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] report = Encoding.UTF8.GetBytes("{\"source\":\"test\"}");
            var id = Guid.NewGuid();
            var first = NavigationDiagnosticReportExporter.WriteReport(root, report, id);
            Assert.True(first.Exported);
            Assert.Equal(report, File.ReadAllBytes(first.Path!));
            var duplicate = NavigationDiagnosticReportExporter.WriteReport(root, Encoding.UTF8.GetBytes("{}"), id);
            Assert.Equal(NavigationDiagnosticExportFailure.WriteFailed, duplicate.Failure);
            Assert.Equal(report, File.ReadAllBytes(first.Path!));
            Assert.Single(Directory.GetFiles(root));
            var prior = Guid.NewGuid();
            string temporary = Path.Combine(root, ".navigation-" + prior.ToString("N") + ".tmp");
            File.WriteAllText(temporary, "preexisting");
            var failed = NavigationDiagnosticReportExporter.WriteReport(root, report, prior);
            Assert.Equal(NavigationDiagnosticExportFailure.WriteFailed, failed.Failure);
            Assert.Equal("preexisting", File.ReadAllText(temporary));
            string obstruction = Path.Combine(root, "file-instead-of-directory");
            File.WriteAllText(obstruction, "preserve");
            Assert.Equal(NavigationDiagnosticExportFailure.WriteFailed,
                NavigationDiagnosticReportExporter.WriteReport(obstruction, report).Failure);
            Assert.Equal("preserve", File.ReadAllText(obstruction));
            string oversized = Path.Combine(root, "not-created");
            Assert.Equal(NavigationDiagnosticExportFailure.ObservationUnavailable,
                NavigationDiagnosticReportExporter.WriteReport(oversized, new byte[NavigationDiagnosticReportEncoder.MaximumReportBytes + 1]).Failure);
            Assert.False(Directory.Exists(oversized));
        }
        finally
        {
            foreach (string file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
