using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests.SnapshotPublication;

public sealed class CombatPayloadTests
{
    [Fact]
    public void UnchangedPayloadKeepsFreshTickAndOldReaderWhileEveryCopiedFactInvalidatesReuse()
    {
        var member = SelectedCombatGroupTests.Member(new(1, 1), UnitIds.MainBattleTank, true);
        var list = new List<CombatGroupMemberReadModel> { member };
        var first = CombatGroupOperationalSnapshot.CaptureOwned(new(1), new(1), list, null);
        var second = CombatGroupOperationalSnapshot.CaptureOwned(new(2), new(1), list, first);
        Assert.NotSame(first, second);
        Assert.Same(first.Members, second.Members);
        Assert.Same(first.EligibleEntities, second.EligibleEntities);
        Assert.Equal(1UL, first.Tick.Value);
        Assert.Equal(2UL, second.Tick.Value);
        var variants = new[]
        {
            member with { Entity = new(1, 2) }, member with { Category = ControllableEntityCategory.Logistics },
            member with { HasHealth = false }, member with { HealthFraction = .7 },
            member with { HasSupply = false }, member with { FuelFraction = .7 }, member with { AmmunitionFraction = .7 },
            member with { SupplyStatus = BattlefieldSupplyStatus.Supplied }, member with { HasReadiness = false },
            member with { Strength = .7 }, member with { Readiness = .7 }, member with { HasFormation = true },
            member with { Formation = FormationTemplate.Wedge }, member with { HasOrder = true },
            member with { Order = CombatOrderKind.AttackMove }, member with { HasOrderStatus = true },
            member with { OrderStatus = CombatOrderStatus.Advancing }, member with { Unit = UnitIds.SupplyTruck },
            member with { CombatEligible = false }
        };
        foreach (var changed in variants)
        {
            list[0] = changed;
            var captured = CombatGroupOperationalSnapshot.CaptureOwned(new(3), new(1), list, second);
            Assert.NotSame(second.Members, captured.Members);
            Assert.True(captured.TryGet(changed.Entity, out var actual));
            Assert.Equal(changed, actual);
            Assert.Equal(member, first.Members[0]);
        }
        list[0] = member;
        Assert.NotSame(first.Members, CombatGroupOperationalSnapshot.CaptureOwned(new(0), new(2), list, first).Members);
        list.Clear();
        var empty = CombatGroupOperationalSnapshot.CaptureOwned(new(4), new(1), list, first);
        Assert.Empty(empty.Members);
        Assert.Single(first.Members);
    }

    [Fact]
    public void AllocationWindowDetectsPositiveAllocationAndBoundsOnlyWarmedSummaryWork()
    {
        Assert.True(Measure(() => GC.KeepAlive(new byte[4096]), 256) >= 256L * 4096);
        var members = Enumerable.Range(1, 1000).Select(i => SelectedCombatGroupTests.Member(
            new((uint)i, 1), UnitIds.MainBattleTank, true)).ToArray();
        var selection = new SelectionSet();
        selection.Replace(members.Select(m => m.Entity).ToArray());
        var snapshot = new CombatGroupOperationalSnapshot(new(1), members);
        long bytes = Measure(() => GC.KeepAlive(SelectedCombatGroup.Create(snapshot, selection)), 256);
        Assert.InRange(bytes, 0, 256L * 512);
    }

    private static long Measure(Action operation, int samples)
    {
        for (int i = 0; i < 1024; i++) operation();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < samples; i++) operation();
        return GC.GetAllocatedBytesForCurrentThread() - start;
    }
}
