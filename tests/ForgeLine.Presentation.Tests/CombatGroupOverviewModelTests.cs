using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CombatGroupOverviewModelTests
{
    [Fact]
    public void SummaryAggregatesAuthoritativeOperationalValues()
    {
        EntityId first =
            new(10, 1);
        EntityId second =
            new(11, 1);
        var operational =
            new CombatGroupOperationalSnapshot(
                new SimulationTick(5),
                [
                    Member(
                        first,
                        health: 0.8,
                        strength: 0.7,
                        fuel: 0.6,
                        ammunition: 0.4,
                        readiness: 0.5,
                        BattlefieldSupplyStatus.LowSupply,
                        FormationTemplate.Line,
                        CombatOrderKind.AttackMove,
                        CombatOrderStatus.Advancing),
                    Member(
                        second,
                        health: 0.6,
                        strength: 0.5,
                        fuel: 0.4,
                        ammunition: 0.2,
                        readiness: 0.3,
                        BattlefieldSupplyStatus.Critical,
                        FormationTemplate.Line,
                        CombatOrderKind.AttackMove,
                        CombatOrderStatus.Advancing)
                ]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();

        registry.Synchronize(
            new SimulationSessionId(1),
            operational.EligibleEntities);
        registry.Assign(
            1,
            [first, second],
            operational.EligibleEntities);
        selection.Replace(
            [first, second]);

        CombatGroupSummaryReadModel summary =
            CombatGroupOverviewModel.Create(
                    registry,
                    operational,
                    selection)
                .Groups[1];

        Assert.Equal(
            2,
            summary.MemberCount);
        Assert.True(
            summary.IsActive);
        Assert.True(
            summary.IsSelected);
        Assert.Equal(
            0.7,
            summary.Health,
            6);
        Assert.Equal(
            0.6,
            summary.Strength,
            6);
        Assert.Equal(
            0.5,
            summary.Fuel,
            6);
        Assert.Equal(
            0.3,
            summary.Ammunition,
            6);
        Assert.Equal(
            0.4,
            summary.Readiness,
            6);
        Assert.Equal(
            BattlefieldSupplyStatus.Critical,
            summary.SupplyStatus);
        Assert.True(
            summary.HasFormation);
        Assert.False(
            summary.MixedFormation);
        Assert.Equal(
            FormationTemplate.Line,
            summary.Formation);
        Assert.True(
            summary.HasOrder);
        Assert.False(
            summary.MixedOrder);
        Assert.Equal(
            CombatOrderKind.AttackMove,
            summary.Order);
        Assert.True(
            summary.HasOrderStatus);
        Assert.False(
            summary.MixedOrderStatus);
        Assert.Equal(
            CombatOrderStatus.Advancing,
            summary.OrderStatus);
    }

    [Fact]
    public void MixedFormationAndOrderAreNotPresentedAsCommon()
    {
        EntityId first =
            new(20, 1);
        EntityId second =
            new(21, 1);
        var operational =
            new CombatGroupOperationalSnapshot(
                new SimulationTick(6),
                [
                    Member(
                        first,
                        formation:
                            FormationTemplate.Line,
                        order:
                            CombatOrderKind.Attack,
                        orderStatus:
                            CombatOrderStatus.Engaging),
                    Member(
                        second,
                        formation:
                            FormationTemplate.Wedge,
                        order:
                            CombatOrderKind.HoldPosition,
                        orderStatus:
                            CombatOrderStatus.Holding)
                ]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();

        registry.Synchronize(
            new SimulationSessionId(2),
            operational.EligibleEntities);
        registry.Assign(
            2,
            [first, second],
            operational.EligibleEntities);

        CombatGroupSummaryReadModel summary =
            CombatGroupOverviewModel.Create(
                    registry,
                    operational,
                    selection)
                .Groups[2];

        Assert.True(
            summary.MixedFormation);
        Assert.True(
            summary.MixedOrder);
        Assert.True(
            summary.MixedOrderStatus);
        Assert.False(
            summary.IsSelected);
    }

    [Fact]
    public void PartialSelectionDoesNotReportGroupAsSelected()
    {
        EntityId first =
            new(25, 1);
        EntityId second =
            new(26, 1);
        var operational =
            new CombatGroupOperationalSnapshot(
                new SimulationTick(6),
                [
                    Member(
                        first),
                    Member(
                        second)
                ]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();

        registry.Synchronize(
            new SimulationSessionId(4),
            operational.EligibleEntities);
        registry.Assign(
            4,
            [first, second],
            operational.EligibleEntities);
        selection.SetSingle(
            first);

        CombatGroupSummaryReadModel summary =
            CombatGroupOverviewModel.Create(
                    registry,
                    operational,
                    selection)
                .Groups[4];

        Assert.True(
            summary.IsActive);
        Assert.False(
            summary.IsSelected);
        Assert.Equal(
            2,
            summary.MemberCount);
    }

    [Fact]
    public void MissingRuntimeFactsRemainExplicitlyUnavailable()
    {
        EntityId entity =
            new(30, 1);
        var operational =
            new CombatGroupOperationalSnapshot(
                new SimulationTick(7),
                [
                    new CombatGroupMemberReadModel(
                        entity,
                        ControllableEntityCategory.Logistics,
                        HasHealth: false,
                        HealthFraction: 0.0,
                        HasSupply: false,
                        SupplyStatus:
                            BattlefieldSupplyStatus.Supplied,
                        FuelFraction: 0.0,
                        AmmunitionFraction: 0.0,
                        HasReadiness: false,
                        Strength: 0.0,
                        Readiness: 0.0,
                        HasFormation: false,
                        Formation:
                            FormationTemplate.Compact,
                        HasOrder: false,
                        Order: default,
                        HasOrderStatus: false,
                        OrderStatus: default)
                ]);
        var registry =
            new CombatGroupRegistry();

        registry.Synchronize(
            new SimulationSessionId(3),
            operational.EligibleEntities);
        registry.Assign(
            3,
            [entity],
            operational.EligibleEntities);

        CombatGroupSummaryReadModel summary =
            CombatGroupOverviewModel.Create(
                    registry,
                    operational,
                    new SelectionSet())
                .Groups[3];

        Assert.False(
            summary.HasHealth);
        Assert.False(
            summary.HasStrength);
        Assert.False(
            summary.HasSupply);
        Assert.False(
            summary.HasReadiness);
        Assert.False(
            summary.HasFormation);
        Assert.False(
            summary.HasOrder);
    }

    private static CombatGroupMemberReadModel Member(
        EntityId entity,
        double health = 1.0,
        double strength = 1.0,
        double fuel = 1.0,
        double ammunition = 1.0,
        double readiness = 1.0,
        BattlefieldSupplyStatus supply =
            BattlefieldSupplyStatus.Supplied,
        FormationTemplate formation =
            FormationTemplate.Compact,
        CombatOrderKind order =
            CombatOrderKind.Stop,
        CombatOrderStatus orderStatus =
            CombatOrderStatus.Pending) =>
        new(
            entity,
            ControllableEntityCategory.Unit,
            true,
            health,
            true,
            supply,
            fuel,
            ammunition,
            true,
            strength,
            readiness,
            true,
            formation,
            true,
            order,
            true,
            orderStatus);
}
