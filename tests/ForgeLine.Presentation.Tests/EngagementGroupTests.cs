using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class EngagementGroupTests
{
    [Fact]
    public void SavedGroupsRestoreOverlapsNamesAndOnlySurvivingOwnedGenerations()
    {
        EntityId first = new(1, 1), second = new(2, 1), third = new(3, 1), foreign = new(4, 1);
        var registry = new CombatGroupRegistry();
        registry.Synchronize(new(11), [first, second, third]);
        registry.Assign(1, [first, second], [first, second, third]);
        registry.Assign(2, [second, third, foreign], [first, second, third]);
        registry.Rename(2, "SUPPORT");
        var saved = registry.Capture(new(1));
        var restored = new CombatGroupRegistry();
        Assert.True(restored.Restore(new(12), new(1), saved, [first, new(2, 2), third]));
        Assert.Equal(new[] { first }, restored.GetValidMembers(1));
        Assert.Equal(new[] { third }, restored.GetValidMembers(2));
        Assert.Equal("SUPPORT", restored.GetSlot(2).Label);
        Assert.Equal(2, restored.ActiveSlot);
        restored.Clear(1);
        var selection = new SelectionSet();
        Assert.True(restored.Recall(2, selection));
        Assert.Equal(new[] { third }, selection.ToArray());
        Assert.False(restored.Restore(new(13), new(2), saved, [first, third]));
        Assert.Equal(new[] { third }, restored.GetValidMembers(2));
        restored.Synchronize(new(14), [first, third]);
        Assert.Empty(restored.GetValidMembers(2)); // A new game does not inherit a loaded game.
    }

    [Fact]
    public void SavedOrganizationIsDetachedBoundedAndInvalidDataIsAtomic()
    {
        var registry = new CombatGroupRegistry();
        var members = Enumerable.Range(1, 1025).Select(i => new EntityId((uint)i, 1)).ToArray();
        registry.Synchronize(new(1), members);
        Assert.Equal(1024, registry.Assign(0, members, members));
        var data = registry.Capture(new(1));
        registry.Clear(0);
        Assert.Equal(1024, data.Slots[0].Members.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<EntityId>)data.Slots[0].Members)[0] = default);
        Assert.False(registry.Restore(new(2), new(1), data with { Slots = [data.Slots[0], data.Slots[0]] }, members));
        Assert.Empty(registry.GetValidMembers(0));
    }

    [Fact]
    public void GroupSummaryShowsMixedPolicyAndIndividualOrderOverrideWithRealCoverage()
    {
        EntityId first = new(1, 1), second = new(2, 1), truck = new(3, 1);
        CombatGroupMemberReadModel Member(EntityId id) => new(id, ControllableEntityCategory.Unit,
            false, 0, false, default, 0, 0, false, 0, 0, false, default,
            true, CombatOrderKind.HoldPosition, true, CombatOrderStatus.Holding,
            UnitIds.RifleSquad, true, true, FirePolicy.ReturnFire);
        var selection = new SelectionSet(); selection.Replace([first, second, truck]);
        var facts = new CombatGroupOperationalSnapshot(new(4),
            [Member(first), Member(second) with { Order = CombatOrderKind.Retreat, FirePolicy = FirePolicy.HoldFire },
             Member(truck) with { Unit = UnitIds.CargoTruck, CombatEligible = false, HasFirePolicy = false, HasOrder = false }]);
        var summary = SelectedCombatGroup.Create(facts, selection);
        Assert.Equal(2, summary.PolicyCount);
        Assert.Equal("ENGAGEMENT MIXED", summary.PolicyLabel);
        Assert.Equal("OBJECTIVE MIXED", summary.ObjectiveLabel);
        Assert.Equal(0, summary.ReadinessCount);
        Assert.Equal(0, summary.SupplyCount);
        Assert.Equal(1, summary.Composition(5));
    }

    [Fact]
    public void MovementPatrolAndCompletedOrdersHaveHonestObjectives()
    {
        EntityId unit = new(5, 1);
        var selection = new SelectionSet();selection.Replace([unit]);
        var member = new CombatGroupMemberReadModel(unit, ControllableEntityCategory.Unit,
            false, 0, false, default, 0, 0, false, 0, 0, false, default, true,
            CombatOrderKind.AttackMove, true, CombatOrderStatus.Complete,
            CombatEligible:true,HasMovementIntent:true);
        SelectedCombatGroup Summarize(CombatGroupMemberReadModel fact) =>
            SelectedCombatGroup.Create(new(new(2),[fact]),selection);
        Assert.Equal("OBJECTIVE MOVE", Summarize(member).ObjectiveLabel);
        Assert.Equal("OBJECTIVE PATROL", Summarize(member with { Patrolling=true }).ObjectiveLabel);
        Assert.Equal("OBJECTIVE NONE", Summarize(member with { HasMovementIntent=false }).ObjectiveLabel);
    }

    [Fact]
    public void ExistingCombatRingExposesPolicyItemsWithExplicitUnarmedRejection()
    {
        PlayerActionSnapshot Actions(int eligible) => new(new(1), new(2), [], 0, null, null,
            tactical: new([new(1, 1)], 1, 1, 0, 0, 0, false, false, default, default, [], [], firePolicyEligibleCount: eligible));
        Assert.Equal(11, PlayerActionDockInteractionLayout.GetItemCount(PlayerActionPanelMode.Tactical, Actions(1)));
        for (int index = 8; index < 11; index++)
        {
            Assert.NotEmpty(PlayerActionDockHudModel.ResolveRingItemTitle(PlayerActionPanelMode.Tactical, index, Actions(1), false));
            Assert.Equal(PlayerActionDockItemState.Enabled,
                PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Tactical, index, Actions(1)));
            Assert.Equal(PlayerActionDockItemState.Disabled("NO ARMED UNITS"),
                PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Tactical, index, Actions(0)));
        }
    }
}
