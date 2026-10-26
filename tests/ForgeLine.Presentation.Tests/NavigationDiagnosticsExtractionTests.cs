using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class NavigationDiagnosticsExtractionTests
{
    [Fact]
    public void ActualCheckedHorizonSurvivesTheCompletePublishedRevisionBoundary()
    {
        using var runtime = Create();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        var entities = runtime.Simulation.Entities;
        EntityId own = default;
        foreach (var entity in entities.Query<UnitIdentity, ControllableEntity>())
            if (entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck &&
                entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) own = entity;
        var pose = entities.GetComponent<WorldTransform>(own);
        entities.AddComponent(own, new MovementOrder(new PlayerId(1),
            pose.Position + new System.Numerics.Vector3(-35, 0, 30), SimulationTick.Zero, SimulationTick.Zero));
        interaction.SetSelection([own]);
        interaction.SetNavigationDiagnosticsOpen(true);
        runtime.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var source = Assert.IsType<NavigationDiagnosticSnapshot>(snapshot.NavigationDiagnosis);
        var view = new NavigationDiagnosticsView(true, true, source.Session, own,
            EnabledLayers: NavigationDiagnosticLayers.Lookahead);
        Assert.True(NavigationDiagnosticsViewModel.TryGetVehicle(snapshot, view, out var vehicle));
        var horizon = Assert.IsType<NavigationCheckedHorizon>(vehicle.Facts.CheckedHorizon);
        Assert.Equal(source.Tick, horizon.Source.SourceTick);
        Assert.Equal(source.Revision, horizon.Source.Revision);
        Assert.Equal(vehicle.Facts.ActualMotion!.Value.Binding, horizon.Binding);
        var draw = new DebugDraw();
        NavigationDiagnosticsViewModel.AppendLayers(draw, snapshot, view);
        Assert.Equal(96, draw.Lines.Length);
    }

    [Fact]
    public void PublicationIsOffByDefaultAndCopiesOnlyCurrentVisibleOwnedTrucks()
    {
        using var runtime = Create();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var off));
        Assert.Null(off.NavigationDiagnosis);
        var entities = runtime.Simulation.Entities;
        EntityId own = default, foreign = default;
        foreach (var entity in entities.Query<UnitIdentity, ControllableEntity>())
            if (entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.CargoTruck)
            {
                if (entities.GetComponent<ControllableEntity>(entity).Owner == new PlayerId(1)) own = entity;
                else foreign = entity;
            }
        Assert.True(own.IsValid); Assert.True(foreign.IsValid);
        interaction.SetSelection([own]);
        interaction.SetNavigationDiagnosticsOpen(true);
        runtime.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var source = Assert.IsType<NavigationDiagnosticSnapshot>(snapshot.NavigationDiagnosis);
        Assert.Equal(runtime.Simulation.SessionId, source.Session);
        Assert.Equal(snapshot.Tick, source.Tick);
        Assert.All(source.Vehicles, v => Assert.Equal(new PlayerId(1), v.Facts.Owner));
        Assert.DoesNotContain(source.Vehicles, v => v.Facts.Entity == foreign);
        var view = new NavigationDiagnosticsView(true, false, source.Session, own);
        Assert.True(NavigationDiagnosticsViewModel.TryGetVehicle(snapshot, view, out var captured));
        var oldPose = captured.Facts.Pose;
        Assert.False(NavigationDiagnosticsViewModel.TryGetVehicle(snapshot, view with { Selected = foreign }, out _));
        Assert.False(NavigationDiagnosticsViewModel.TryGetVehicle(snapshot, view with { Session = new(source.Session.Value + 1) }, out _));
        var draw = new DebugDraw();
        NavigationDiagnosticsViewModel.AppendLayers(draw, snapshot, view);
        Assert.Empty(draw.Labels); Assert.Equal(0, draw.Lines.Length);
        NavigationDiagnosticsViewModel.AppendLayers(draw, snapshot, view with { Layers = true });
        Assert.InRange(draw.Lines.Length, 12, 1024);
        draw.Clear();
        NavigationDiagnosticsViewModel.AppendLayers(draw, snapshot, view with { Visible = false, Layers = true });
        Assert.Equal(0, draw.Lines.Length);
        entities.SetComponent(own, entities.GetComponent<WorldTransform>(own) with { Position = oldPose.Position + System.Numerics.Vector3.UnitX });
        Assert.Equal(oldPose, captured.Facts.Pose);
        interaction.SetNavigationDiagnosticsOpen(false);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var closed));
        Assert.Null(closed.NavigationDiagnosis);
    }

    [Fact]
    public void DeathAndOwnershipChangeRemovePreviouslyPublishedFactsWithoutChangingOldCopies()
    {
        using var runtime = Create();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        interaction.SetNavigationDiagnosticsOpen(true);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var first));
        var source = Assert.IsType<NavigationDiagnosticSnapshot>(first.NavigationDiagnosis);
        var own = source.Vehicles[0].Facts.Entity;
        runtime.Simulation.Entities.SetComponent(own, new ControllableEntity(new PlayerId(2), ControllableEntityCategory.Unit));
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var changed));
        Assert.DoesNotContain(changed.NavigationDiagnosis!.Vehicles, v => v.Facts.Entity == own);
        Assert.Contains(source.Vehicles, v => v.Facts.Entity == own);
        var other = changed.NavigationDiagnosis.Vehicles[0].Facts.Entity;
        runtime.Simulation.Entities.SetComponent(other, new HealthState(0, 100));
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var dead));
        Assert.DoesNotContain(dead.NavigationDiagnosis!.Vehicles, v => v.Facts.Entity == other);
    }

    [Fact]
    public void EnablingCaptureDoesNotChangeAuthoritativeGameplayOrSerializedContinuation()
    {
        using var observed = Create();
        using var control = Create();
        var interaction = new PresentationInteractionState();
        interaction.SetNavigationDiagnosticsOpen(true);
        _ = WorldHoverExtractionTests.Observe(observed, interaction);
        foreach (ulong ticks in new ulong[] { 1, 30, 200 })
        {
            observed.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            control.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            Assert.Equal(MatchAuthoritativeSnapshot.Capture(control).ComputeSha256(),
                MatchAuthoritativeSnapshot.Capture(observed).ComputeSha256());
        }
        using var restored = MatchPersistenceService.Restore(MatchPersistenceService.CaptureSave(observed));
        observed.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(observed).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
    }

    private static MatchRuntime Create() => CentralDivideScenario.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
}
