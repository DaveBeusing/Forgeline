using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectedUnitOrderExtractionTests
{
    [Fact]
    public void AcceptedMovementIsExtractedWithFinalDestinationAndOwnedSelection()
    {
        var player = new PlayerId(1);
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, seed: 9821) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(
                westComputerControlled: false, eastComputerControlled: false)
        };
        using MatchRuntime scenario = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        EntityId friendly = scenario.GetBase(player).StartingUnits[0];
        EntityId foreign = scenario.GetBase(new PlayerId(2)).StartingUnits[0];

        var interaction = new PresentationInteractionState();
        interaction.SetSelection([friendly, foreign]);
        var gateway = new PlayerCommandGateway(
            scenario.Simulation, scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity);
        var extraction = new PresentationExtractionContext(
            scenario, player, interaction, gateway);
        var buffer = new PresentationSnapshotBuffer();
        scenario.Simulation.RegisterTickObserver(gateway);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(buffer, extraction));

        Vector3 destination = new(240, 0, 260);
        gateway.SubmitMovement(player, [friendly], destination,
            scenario.Simulation.CurrentTick, FormationTemplate.Compact);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot current));
        SelectedUnitOrderSnapshot orders = Assert.IsType<SelectedUnitOrderSnapshot>(current.SelectedOrders);
        Assert.Equal(current.SessionId, orders.SessionId);
        Assert.Equal(current.Tick, orders.Tick);
        Assert.True(orders.MatchesSelection(current.SessionId, [friendly, foreign]));
        SelectedUnitOrderView order = Assert.Single(orders.Orders.ToArray());
        Assert.Equal(friendly, order.Entity);
        Assert.Equal(SelectedUnitOrderKind.Move, order.Kind);
        Assert.True(order.HasTarget);
        Assert.Equal(destination, order.Target);

        // Copy ownership: the previously published selection cannot mutate when
        // interactive selection changes before the next simulation tick.
        interaction.SetSelection([foreign]);
        Assert.False(orders.MatchesSelection(current.SessionId, [foreign]));
        Assert.Equal(destination, order.Target);
    }
}
