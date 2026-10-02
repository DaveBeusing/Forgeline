using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SkirmishOpponentDebugVisualizationTests
{
    [Fact]
    public void StrategicReadModelDrawsHomeObjectiveAndStatus()
    {
        var debugDraw =
            new DebugDraw
            {
                Enabled = true
            };
        var model =
            new SkirmishOpponentDebugReadModel(
                new ForgeLine.Core.EntityId(1, 1),
                new PlayerId(2),
                SkirmishStrategicState.Attacking,
                SkirmishStrategicGoal.AttackObjective,
                new SkirmishEconomyAssessment(
                    500.0,
                    300.0,
                    300.0,
                    200.0,
                    200.0,
                    100.0,
                    300.0,
                    200.0,
                    150.0,
                    0,
                    0,
                    4,
                    2,
                    0.8),
                new SkirmishForceAssessment(
                    12,
                    9,
                    2,
                    3,
                    1,
                    2,
                    1,
                    0.82,
                    0.72,
                    4,
                    2),
                new Vector3(2_600.0f, 0.0f, 1_536.0f),
                new Vector3(470.0f, 0.0f, 1_536.0f),
                HasChosenObjective: true,
                new SimulationTick(200),
                DecisionsTaken: 10,
                OperationalObjective:
                    SkirmishOperationalObjective.PressureObjective,
                GroupObjective:
                    new SkirmishGroupObjectiveReadModel(
                        new ForgeLine.Core.EntityId(3, 1),
                        CombatOrderKind.AttackMove,
                        new Vector3(480.0f, 0.0f, 1_520.0f),
                        HasDestination: true,
                        ForgeLine.Core.EntityId.Invalid,
                        FormationTemplate.Column,
                        InitialMemberCount: 6,
                        SurvivingMemberCount: 5),
                SupplyRequirement:
                    SkirmishSupplyRequirement.Fuel,
                RetreatReason:
                    SkirmishRetreatReason.LowFuel);

        SkirmishOpponentDebugVisualization.Draw(
            debugDraw,
            [model]);

        Assert.True(
            debugDraw.Lines.Length > 10);
        Assert.Equal(
            4,
            debugDraw.Labels.Count);
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "Attacking / AttackObjective",
                    StringComparison.Ordinal));
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "Operational PressureObjective",
                    StringComparison.Ordinal));
        Assert.Contains(
            debugDraw.Labels,
            label =>
                label.Text.Contains(
                    "Supply Fuel Retreat LowFuel",
                    StringComparison.Ordinal));
    }
}
