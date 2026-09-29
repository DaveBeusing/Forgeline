using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PlayerCommandBoundaryTests
{
    [Fact]
    public void MovementResultCarriesSessionCorrelationAndExecutionTick()
    {
        using VerticalSliceScenario scenario =
            CreateHumanScenario(seed: 4101);
        var gateway =
            CreateGateway(scenario);

        EntityId unit =
            scenario.West.StartingUnits[0];

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitMovement(
                scenario.West.Player,
                [unit],
                new Vector3(32.0f, 0.0f, 32.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);

        Assert.True(receipt.Accepted);
        Assert.True(receipt.CorrelationId.IsSpecified);
        Assert.Equal(
            scenario.Simulation.SessionId,
            receipt.SessionId);
        Assert.Equal(
            scenario.Simulation.CurrentTick.Next(),
            receipt.TargetTick);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            receipt.CorrelationId,
            result.CorrelationId);
        Assert.Equal(
            receipt.SessionId,
            result.SessionId);
        Assert.Equal(
            PlayerCommandKind.Movement,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(0, result.RejectedTargets);
        Assert.Equal(
            scenario.Simulation.CurrentTick,
            result.ResolvedAtTick);
    }

    [Fact]
    public void ResultDeliveryIsOrderedAndSurvivesDelayedConsumption()
    {
        using VerticalSliceScenario scenario =
            CreateHumanScenario(seed: 4102);
        var gateway =
            CreateGateway(
                scenario,
                maximumOutstanding: 4);
        EntityId unit =
            scenario.West.StartingUnits[0];

        PlayerCommandSubmissionReceipt first =
            gateway.SubmitMovement(
                scenario.West.Player,
                [unit],
                new Vector3(24.0f, 0.0f, 24.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);
        PlayerCommandSubmissionReceipt second =
            gateway.SubmitMovement(
                scenario.West.Player,
                [unit],
                new Vector3(48.0f, 0.0f, 48.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(2, gateway.Results.Count);

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel firstResult));
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel secondResult));

        Assert.Equal(
            first.CorrelationId,
            firstResult.CorrelationId);
        Assert.Equal(
            second.CorrelationId,
            secondResult.CorrelationId);
        Assert.True(
            firstResult.ResolvedAtTick <=
            secondResult.ResolvedAtTick);
    }

    [Fact]
    public void FullBoundaryRejectsNewSubmissionWithoutDroppingExistingResults()
    {
        using VerticalSliceScenario scenario =
            CreateHumanScenario(seed: 4103);
        var gateway =
            CreateGateway(
                scenario,
                maximumOutstanding: 2);
        EntityId unit =
            scenario.West.StartingUnits[0];

        PlayerCommandSubmissionReceipt first =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                16.0f);
        PlayerCommandSubmissionReceipt second =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                32.0f);
        PlayerCommandSubmissionReceipt rejected =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                48.0f);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal(
            PlayerCommandSubmissionFailure.BoundaryFull,
            rejected.Failure);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(2, gateway.Results.Count);
        Assert.True(
            gateway.Results.TryRead(out _));

        PlayerCommandSubmissionReceipt resumed =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                64.0f);

        Assert.True(resumed.Accepted);
    }

    [Fact]
    public void FreshRuntimeUsesDifferentSessionIdentity()
    {
        using VerticalSliceScenario first =
            CreateHumanScenario(seed: 4104);
        using VerticalSliceScenario second =
            CreateHumanScenario(seed: 4104);

        Assert.True(
            first.Simulation.SessionId.IsSpecified);
        Assert.True(
            second.Simulation.SessionId.IsSpecified);
        Assert.NotEqual(
            first.Simulation.SessionId,
            second.Simulation.SessionId);
    }

    private static VerticalSliceScenario CreateHumanScenario(
        ulong seed)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        return VerticalSliceScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }

    private static PlayerCommandGateway CreateGateway(
        VerticalSliceScenario scenario,
        int maximumOutstanding = 128)
    {
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity,
                maximumOutstanding);
        scenario.Simulation.RegisterTickObserver(
            gateway);
        return gateway;
    }

    private static PlayerCommandSubmissionReceipt SubmitMovement(
        PlayerCommandGateway gateway,
        VerticalSliceScenario scenario,
        EntityId unit,
        float coordinate) =>
        gateway.SubmitMovement(
            scenario.West.Player,
            [unit],
            new Vector3(
                coordinate,
                0.0f,
                coordinate),
            scenario.Simulation.CurrentTick,
            FormationTemplate.Compact);
}
