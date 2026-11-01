using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class EngagementPolicyTests
{
    private static readonly PlayerId Player = new(1);

    [Theory]
    [InlineData(FirePolicy.HoldFire)]
    [InlineData(FirePolicy.ReturnFire)]
    [InlineData(FirePolicy.FireAtWill)]
    public void PolicyEditsPreserveExplicitHoldAndGroupMembership(FirePolicy policy)
    {
        using var runtime = Create();
        var entities = runtime.Simulation.Entities;
        var first = Armed(runtime);
        Assert.True(runtime.Services.UnitDefinitions.TryGet(UnitIds.RifleSquad, out var definition));
        var second = runtime.UnitFactory.Create(definition!, entities.GetComponent<WorldTransform>(first).Position + new Vector3(8, 0, 0), Player);
        var gateway = Gateway(runtime);
        var hold = PlayerTacticalActionCommand.HoldPosition(Player, [first, second], runtime.Simulation.CurrentTick,
            runtime.Intelligence, runtime.Services.Weapons, runtime.Services.ArtilleryWeapons);
        runtime.Simulation.SubmitCommand(hold, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick();
        var before = entities.GetComponent<CombatOrderState>(first);
        var group = entities.GetComponent<CombatGroupMember>(first);
        Assert.True(gateway.SubmitFirePolicy(Player, [first, second], policy, runtime.Simulation.CurrentTick).Accepted);
        runtime.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
        Assert.Equal(before, entities.GetComponent<CombatOrderState>(first));
        Assert.Equal(group, entities.GetComponent<CombatGroupMember>(first));
        Assert.Equal(policy, entities.GetComponent<FirePolicyState>(first).Policy);
        Assert.Equal(policy, entities.GetComponent<FirePolicyState>(second).Policy);
        Assert.True(gateway.Results.TryRead(out var result));
        Assert.Equal(2, result.AcceptedTargets);
        Assert.Equal(0, result.RejectedTargets);
    }

    [Fact]
    public void MixedSelectionRejectsForeignStaleDisabledAndUnarmedMembers()
    {
        using var runtime = Create();
        var entities = runtime.Simulation.Entities;
        var first = Armed(runtime);
        var foreign = Armed(runtime, new(2));
        var truck = runtime.GetBase(Player).StartingUnits.First(e => !entities.HasComponent<WeaponState>(e));
        var disabled = entities.CreateEntity();
        entities.AddComponent(disabled, new ControllableEntity(Player, ControllableEntityCategory.None));
        var gateway = Gateway(runtime);
        Assert.True(gateway.SubmitFirePolicy(Player, [first, first, foreign, truck, disabled, new(first.Index, first.Generation + 1)],
            FirePolicy.ReturnFire, runtime.Simulation.CurrentTick).Accepted);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var result));
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(5, result.RejectedTargets);
        Assert.Equal(FirePolicy.FireAtWill, entities.GetComponent<FirePolicyState>(foreign).Policy);
        Assert.False(entities.HasComponent<FirePolicyState>(truck));
    }

    [Fact]
    public void ReturnFireRequiresRealRetaliationAndNeverInventsHiddenTarget()
    {
        using var runtime = Create();
        var first = Armed(runtime);
        var entities = runtime.Simulation.Entities;
        var enemy = Armed(runtime, new(2));
        var weapon = entities.GetComponent<WeaponState>(first);
        entities.SetComponent(first, weapon with { Target = enemy });
        var gateway = Gateway(runtime);
        gateway.SubmitFirePolicy(Player, [first], FirePolicy.ReturnFire, runtime.Simulation.CurrentTick);
        runtime.Simulation.AdvanceOneTick();
        var policy = entities.GetComponent<FirePolicyState>(first);
        Assert.False(policy.RetaliationTarget.IsValid);
        Assert.False(policy.Permits(enemy));
        Assert.False(entities.GetComponent<WeaponState>(first).Target.IsValid);
    }

    [Fact]
    public void PolicyAndOptionalControlGroupsRoundTripWithoutChangingAuthoritativeSchema()
    {
        using var runtime = Create();
        var first = Armed(runtime);
        Gateway(runtime).SubmitFirePolicy(Player, [first], FirePolicy.ReturnFire, runtime.Simulation.CurrentTick);
        runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        var save = MatchPersistenceService.CaptureSave(runtime) with
        { ControlGroups = new(Player, 2, [new(2, "SPEARHEAD", [first])]) };
        var copy = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(save));
        Assert.Equal(first, Assert.Single(Assert.Single(copy.ControlGroups!.Slots).Members));
        using var restored = MatchPersistenceService.Restore(copy);
        using var replayed = MatchPersistenceService.PlayReplay(MatchPersistenceService.CaptureReplay(runtime));
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(replayed).ComputeSha256());
        Assert.Equal(FirePolicy.ReturnFire, restored.Simulation.Entities.GetComponent<FirePolicyState>(first).Policy);
        Assert.Equal(20, MatchAuthoritativeSnapshot.CurrentSchemaVersion);
        Assert.Equal(12, copy.SchemaVersion);
    }

    [Fact]
    public void InvalidAndOlderPolicyPayloadsFailBeforeRestoration()
    {
        using var runtime = Create();
        var gateway = Gateway(runtime);
        gateway.SubmitFirePolicy(Player, [Armed(runtime)], FirePolicy.HoldFire, runtime.Simulation.CurrentTick);
        runtime.Simulation.AdvanceOneTick();
        var save = MatchPersistenceService.CaptureSave(runtime);
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save with { SchemaVersion = 11 }));
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save with
        { ControlGroups = new(Player, 0, [new(0, "BAD", Enumerable.Repeat(new EntityId(1, 1), 1025).ToArray())]) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => gateway.SubmitFirePolicy(Player, [Armed(runtime)],
            (FirePolicy)255, runtime.Simulation.CurrentTick));
    }

    [Fact]
    public void FullMailboxRejectsPolicySubmission()
    {
        using var runtime = Create();
        var gateway = Gateway(runtime, 1);
        var first = Armed(runtime);
        Assert.False(gateway.SubmitFirePolicy(Player, Enumerable.Repeat(first, 1025).ToArray(), FirePolicy.ReturnFire, runtime.Simulation.CurrentTick).Accepted);
        Assert.Equal(0, gateway.OutstandingCount);
        Assert.True(gateway.SubmitFirePolicy(Player, [first], FirePolicy.HoldFire, runtime.Simulation.CurrentTick).Accepted);
        Assert.False(gateway.SubmitFirePolicy(Player, [first], FirePolicy.ReturnFire, runtime.Simulation.CurrentTick).Accepted);
    }

    [Fact]
    public void PolicyKeepsDeferredOrdersAndNeverReissuesAnUnreachableLeg()
    {
        using var runtime = Create();
        var first = Armed(runtime);
        var gateway = Gateway(runtime);
        runtime.Simulation.SubmitCommand(new QueuedOrdersCommand(Player, [first],
            new(QueuedOrderKind.Move, new(-10000, 0, -10000), default, FormationTemplate.Compact, default),
            QueuedOrderEdit.Replace, false, default, runtime.Intelligence), runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.RunTicks(8, TestContext.Current.CancellationToken);
        var before = runtime.Simulation.Entities.GetComponent<QueuedUnitOrders>(first);
        gateway.SubmitFirePolicy(Player, [first], FirePolicy.ReturnFire, runtime.Simulation.CurrentTick);
        runtime.Simulation.RunTicks(24, TestContext.Current.CancellationToken);
        var after = runtime.Simulation.Entities.GetComponent<QueuedUnitOrders>(first);
        Assert.Equal(before.Entries, after.Entries);
        Assert.Equal(before.IssuedAt, after.IssuedAt);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(QueuedOrderStatus.Blocked, after.Status);
    }

    internal static MatchRuntime Create() => CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(
        MatchScenarioProfile.Gameplay, 7301) with { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) },
        TestContext.Current.CancellationToken);
    private static EntityId Armed(MatchRuntime runtime, PlayerId player = default) =>
        runtime.GetBase(player.IsSpecified ? player : Player).StartingUnits.First(e => runtime.Simulation.Entities.HasComponent<FirePolicyState>(e));
    private static PlayerCommandGateway Gateway(MatchRuntime runtime, int capacity = 128)
    {
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity, capacity, intelligence: runtime.Intelligence,
            weapons: runtime.Services.Weapons, artilleryWeapons: runtime.Services.ArtilleryWeapons);
        runtime.Simulation.RegisterTickObserver(gateway);
        return gateway;
    }
}
