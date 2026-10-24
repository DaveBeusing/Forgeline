using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorDiagnosisTests
{
    [Fact]
    public void ImmediatePowerAndUnmatedPortAreIndependentStableObservations()
    {
        var fixture = new ConveyorTestFixture();
        EntityId piece = fixture.Build(ConveyorModule.ShortSegment, new Vector3(80, 0, 80));
        fixture.SetPower(piece, 0);
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var stopped));
        Assert.Equal(ConveyorBlockReason.NoPower, stopped.First.Reason);
        Assert.Equal(piece, stopped.First.Cause);
        Assert.Equal(ConveyorBlockReason.DisconnectedPort, stopped.Second.Reason);
        fixture.SetPower(piece, 1);
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var recovered));
        Assert.Equal(ConveyorBlockReason.DisconnectedPort, recovered.First.Reason);
        Assert.Equal(ConveyorBlockReason.NoPower, stopped.First.Reason);
        Assert.Equal(fixture.Simulation.CurrentTick, fixture.Conveyor.ObservationTick);
    }

    [Fact]
    public void ConnectedEmptySourceIdentifiesActualFullGenerationEndpoint()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId[] line = fixture.Connect(source, destination);
        fixture.Step();
        var observation = line.Select(e => fixture.Conveyor.TryGetObservation(e, out var value) ? value : default)
            .Single(value => value.First.Reason == ConveyorBlockReason.NoInput && value.First.Cause == source);
        Assert.Equal(source, observation.First.Cause);
        fixture.Give(source, ResourceIds.Steel, 20);
        fixture.Step();
        Assert.Contains(line, entity => fixture.Flow(entity).InputPerSecond > 0);
        Assert.Equal(ConveyorBlockReason.NoInput, observation.First.Reason);
    }
}
