using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectedCombatGroupTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void CountsUniqueCopiedLiveMembersAndComponentCoverage(int count)
    {
        var members = Enumerable.Range(1, count).Select(i => Member(new EntityId((uint)i, 1),
            i % 2 == 0 ? UnitIds.MainBattleTank : UnitIds.SupplyTruck, i % 2 == 0)).ToArray();
        var selected = new SelectionSet();
        selected.Replace(members.Select(static m => m.Entity).ToArray());
        var operational = new CombatGroupOperationalSnapshot(new SimulationTick(9), members.Concat(members).ToArray());
        var group = SelectedCombatGroup.Create(operational, selected);
        Assert.Equal(count, operational.Members.Count);
        Assert.Equal(count, group.LiveCount);
        Assert.Equal(count / 2, group.CombatCount);
        Assert.Equal(count / 2, group.ReadinessCount);
        Assert.Equal(count, group.HealthCount);
        Assert.Equal(0.2, group.Health, 6);
        Assert.Equal(count, group.DamagedCount);
        Assert.Equal(count, group.UnsuppliedCount);
        Assert.Equal(count / 2, group.Composition(3));
        Assert.Equal(count - count / 2, group.Composition(6));
        if (count > 1) Assert.Equal(0.8, group.Readiness, 6);
    }

    [Fact]
    public void StaleGenerationsAndBuildingsDoNotBecomeUnitComposition()
    {
        var selected = new SelectionSet();
        selected.Replace([new EntityId(1, 1), new EntityId(2, 1), new EntityId(3, 1)]);
        var operational = new CombatGroupOperationalSnapshot(new SimulationTick(5),
            [Member(new EntityId(1, 2), UnitIds.MainBattleTank, true), Member(new EntityId(2, 1), default, false)]);
        var group = SelectedCombatGroup.Create(operational, selected);
        Assert.Equal(3, group.TotalCount);
        Assert.Equal(1, group.LiveCount);
        Assert.Equal(1, group.Composition(7));
        Assert.Equal(new EntityId(2, 1), group.DamagedMember);
    }

    internal static CombatGroupMemberReadModel Member(EntityId entity, UnitId unit, bool combat) =>
        new(entity, ControllableEntityCategory.Unit, true, 0.2, true, BattlefieldSupplyStatus.Critical,
            0.3, 0.4, combat, 0.5, 0.8, false, default, false, default, false, default, unit, combat);
}
