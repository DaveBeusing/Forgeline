#if FORGELINE_DEVELOPER_TOOLS
using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DeveloperSimulationActionsTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void ResourceGrantReportsCapacityLimitedActualInventoryChange()
    {
        using MatchRuntime runtime = CreateRuntime();
        DeveloperSessionAccess access = Activate(runtime);
        EntityId core = runtime.GetBase(Owner).CommandCore;
        InventoryId inventory = runtime.Simulation.Entities.GetComponent<InventoryStorage>(core).InventoryId;
        double before = runtime.Inventories.GetQuantity(inventory, ResourceIds.RareElements);
        double expected = runtime.Inventories.GetAddableQuantity(inventory, ResourceIds.RareElements, 1_000_000);

        DeveloperResult result = Apply(runtime, access, new(DeveloperRevisionDomain.Simulation, 0),
            DeveloperOperation.GrantResource, DeveloperTarget.ForEntity(core),
            DeveloperValue.FromUInt32(ResourceIds.RareElements.Value),
            DeveloperValue.FromUInt32((uint)DeveloperInventoryRole.BuildingStock), DeveloperValue.FromDouble(1_000_000));

        Assert.Equal(DeveloperRequestStatus.Applied, result.Status);
        Assert.Equal(1UL, result.AppliedTick!.Value.Value);
        Assert.Equal(expected, result.AffectedValues.Single().Value.AsDouble());
        Assert.InRange(expected, 0.001, 1_000_000 - 0.001);
        Assert.Equal(before + expected, runtime.Inventories.GetQuantity(inventory, ResourceIds.RareElements));
        Assert.Equal(1UL, result.Revision.Value);
    }

    [Fact]
    public void StaleEntityGenerationAndInactiveSessionAreRejectedWithoutQueueing()
    {
        using MatchRuntime runtime = CreateRuntime();
        EntityId core = runtime.GetBase(Owner).CommandCore;
        var dispatcher = runtime.Services.DeveloperActions;
        DeveloperSessionAccess active = Activate(runtime);
        EntityId stale = new(core.Index, checked(core.Generation + 1));
        var request = Request(runtime, new(DeveloperRevisionDomain.Simulation, 0), DeveloperOperation.RepairEntity,
            DeveloperTarget.ForEntity(stale), DeveloperValue.FromDouble(10));
        DeveloperResult staleResult = dispatcher.Submit(active, Context(runtime, request.ExpectedRevision), request);
        Assert.Equal(DeveloperRequestStatus.Rejected, staleResult.Status);
        Assert.Equal(DeveloperFailureReason.InvalidTarget, staleResult.Failure);
        Assert.Equal(0, runtime.Simulation.PendingCommandCount);

        DeveloperSessionAccess inactive = DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, false);
        var inactiveRequest = Request(runtime, new(DeveloperRevisionDomain.Simulation, 0), DeveloperOperation.RepairEntity,
            DeveloperTarget.ForEntity(core), DeveloperValue.FromDouble(10));
        DeveloperResult inactiveResult = dispatcher.Submit(inactive, Context(runtime, inactiveRequest.ExpectedRevision), inactiveRequest);
        Assert.Equal(DeveloperRequestStatus.Rejected, inactiveResult.Status);
        Assert.Equal(DeveloperFailureReason.SessionNotActivated, inactiveResult.Failure);
        Assert.Equal(0, runtime.Simulation.PendingCommandCount);

        DeveloperSessionAccess oldSession = DeveloperToolsCapability.Current.BindSession(
            new(runtime.Simulation.SessionId.Value + 1), true);
        var oldSessionRequest = Request(runtime, new(DeveloperRevisionDomain.Simulation, 0), DeveloperOperation.RepairEntity,
            DeveloperTarget.ForEntity(core), DeveloperValue.FromDouble(10));
        DeveloperResult oldSessionResult = dispatcher.Submit(oldSession,
            Context(runtime, oldSessionRequest.ExpectedRevision), oldSessionRequest);
        Assert.Equal(DeveloperRequestStatus.Rejected, oldSessionResult.Status);
        Assert.Equal(DeveloperFailureReason.StaleSession, oldSessionResult.Failure);
        Assert.Equal(0, runtime.Simulation.PendingCommandCount);
    }

    [Fact]
    public void RefillUsesUnitCapacitiesAndSpawnRegistersExactlyOneUnit()
    {
        using MatchRuntime runtime = CreateRuntime();
        DeveloperSessionAccess access = Activate(runtime);
        EntityId unit = runtime.GetBase(Owner).StartingUnits.First(entity =>
            runtime.Simulation.Entities.HasComponent<UnitFuelState>(entity) &&
            runtime.Simulation.Entities.HasComponent<AmmunitionState>(entity));
        UnitFuelState fuel = runtime.Simulation.Entities.GetComponent<UnitFuelState>(unit);
        AmmunitionState ammo = runtime.Simulation.Entities.GetComponent<AmmunitionState>(unit);
        double fuelBefore = runtime.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        double ammoBefore = runtime.Inventories.GetQuantity(ammo.InventoryId, ResourceIds.Ammunition);
        if (fuelBefore > 0) Assert.True(runtime.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, Math.Min(1, fuelBefore)).Succeeded);
        if (ammoBefore > 0) Assert.True(runtime.Inventories.Remove(ammo.InventoryId, ResourceIds.Ammunition, Math.Min(1, ammoBefore)).Succeeded);
        double fuelReduced = runtime.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel);
        double ammoReduced = runtime.Inventories.GetQuantity(ammo.InventoryId, ResourceIds.Ammunition);

        DeveloperResult refill = Apply(runtime, access, new(DeveloperRevisionDomain.Simulation, 0),
            DeveloperOperation.RefillUnit, DeveloperTarget.ForEntity(unit),
            DeveloperValue.FromDouble(1), DeveloperValue.FromDouble(1));
        Assert.Equal(DeveloperRequestStatus.Applied, refill.Status);
        Assert.Equal(2, refill.AffectedValues.Count);
        Assert.True(refill.AffectedValues.All(value => value.Value.AsDouble() > 0));
        Assert.True(runtime.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) >=
            fuelReduced + refill.AffectedValues[0].Value.AsDouble());
        Assert.True(runtime.Inventories.GetQuantity(ammo.InventoryId, ResourceIds.Ammunition) >=
            ammoReduced + refill.AffectedValues[1].Value.AsDouble());

        Assert.True(runtime.Services.UnitDefinitions.TryGet(
            runtime.Simulation.Entities.GetComponent<UnitIdentity>(unit).UnitId, out UnitDefinition? definition));
        Vector3 position = FindOpenPosition(runtime, definition!);
        int before = CountOwnedUnits(runtime);
        DeveloperResult spawn = Apply(runtime, access, refill.Revision, DeveloperOperation.SpawnUnit,
            DeveloperTarget.ForPlayer(Owner), DeveloperValue.FromUInt32(definition!.Id.Value),
            DeveloperValue.FromDouble(position.X), DeveloperValue.FromDouble(position.Y), DeveloperValue.FromDouble(position.Z));
        Assert.Equal(DeveloperRequestStatus.Applied, spawn.Status);
        Assert.Equal(DeveloperEffectKind.SpawnedEntity, spawn.AffectedValues.Single().Kind);
        EntityId spawned = spawn.AffectedValues.Single().Target.Entity;
        Assert.True(runtime.Simulation.Entities.IsAlive(spawned));
        Assert.Equal(definition.Id, runtime.Simulation.Entities.GetComponent<UnitIdentity>(spawned).UnitId);
        Assert.Equal(Owner, runtime.Simulation.Entities.GetComponent<ControllableEntity>(spawned).Owner);
        Assert.Equal(before + 1, CountOwnedUnits(runtime));
    }

    [Fact]
    public void SaveAndReplayReconstructActionsParameterAuditAndPendingCommand()
    {
        using MatchRuntime original = CreateRuntime();
        DeveloperSessionAccess access = Activate(original);
        EntityId core = original.GetBase(Owner).CommandCore;

        DeveloperResult set = Apply(original, access, new(DeveloperRevisionDomain.Parameters, 0), DeveloperOperation.SetParameter,
            DeveloperTarget.Global, DeveloperValue.FromDouble(0.5), parameter: DeveloperParameterId.ConveyorThroughputMultiplier);
        Assert.Equal(DeveloperRequestStatus.Applied, set.Status);
        Assert.Equal(1UL, set.AppliedTick!.Value.Value);
        DeveloperResult reset = Apply(original, access, set.Revision, DeveloperOperation.ResetParameter,
            DeveloperTarget.Global, parameter: DeveloperParameterId.ConveyorThroughputMultiplier);
        Assert.Equal(DeveloperRequestStatus.Applied, reset.Status);
        Assert.Equal(2UL, reset.AppliedTick!.Value.Value);

        var simulationRevision = new DeveloperStateRevision(DeveloperRevisionDomain.Simulation, 0);
        DeveloperResult resource = Apply(original, access, simulationRevision, DeveloperOperation.GrantResource,
            DeveloperTarget.ForEntity(core), DeveloperValue.FromUInt32(ResourceIds.RareElements.Value),
            DeveloperValue.FromUInt32((uint)DeveloperInventoryRole.BuildingStock), DeveloperValue.FromDouble(5));
        simulationRevision = resource.Revision;
        DeveloperResult damage = Apply(original, access, simulationRevision, DeveloperOperation.DamageEntity,
            DeveloperTarget.ForEntity(core), DeveloperValue.FromDouble(25),
            DeveloperValue.FromUInt32(DirectorateContent.WeaponIds.MainBattleCannon.Value),
            DeveloperValue.FromDouble(0), DeveloperValue.FromDouble(1));
        Assert.True(damage.AffectedValues.Single().Value.AsDouble() > 0);
        simulationRevision = damage.Revision;
        DeveloperResult repair = Apply(original, access, simulationRevision, DeveloperOperation.RepairEntity,
            DeveloperTarget.ForEntity(core), DeveloperValue.FromDouble(10));
        Assert.Equal(DeveloperEffectKind.HealthRestored, repair.AffectedValues.Single().Kind);
        simulationRevision = repair.Revision;

        var pendingRequest = Request(original, simulationRevision, DeveloperOperation.GrantResource,
            DeveloperTarget.ForEntity(core), new[] { DeveloperValue.FromUInt32(ResourceIds.RareElements.Value),
                DeveloperValue.FromUInt32((uint)DeveloperInventoryRole.BuildingStock), DeveloperValue.FromDouble(1) });
        DeveloperResult pendingReceipt = original.Services.DeveloperActions.Submit(access,
            Context(original, simulationRevision), pendingRequest);
        Assert.Equal(DeveloperRequestStatus.Accepted, pendingReceipt.Status);
        Assert.Equal(5UL, original.Simulation.CurrentTick.Value);

        MatchSaveData save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(
            MatchPersistenceService.CaptureSave(original)));
        MatchReplayData replayData = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(
            MatchPersistenceService.CaptureReplay(original)));
        Assert.Equal(2, save.ParameterChanges!.Length);
        Assert.Equal(6, save.Commands.Count);
        Assert.Null(save.Commands.Single(c => c.Sequence == 6).DeveloperPayload!.ExecutionActivated);

        using MatchRuntime restored = MatchPersistenceService.RestoreDeveloper(save, DeveloperToolsCapability.Current,
            cancellationToken: TestContext.Current.CancellationToken);
        using MatchRuntime replayed = MatchPersistenceService.PlayDeveloperReplay(replayData, DeveloperToolsCapability.Current);
        string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(replayed).ComputeSha256());
        Assert.Equal(1, restored.Simulation.PendingCommandCount);
        Assert.Equal(1, replayed.Simulation.PendingCommandCount);
    }

    private static MatchRuntime CreateRuntime() => MatchRuntime.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 74_2026), TestContext.Current.CancellationToken);

    private static DeveloperSessionAccess Activate(MatchRuntime runtime)
    {
        var access = DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, true);
        runtime.Services.DeveloperActions.BindAccess(access);
        return access;
    }

    private static DeveloperResult Apply(MatchRuntime runtime, DeveloperSessionAccess access, DeveloperStateRevision revision,
        DeveloperOperation operation, DeveloperTarget target, DeveloperValue first = default, DeveloperValue second = default,
        DeveloperValue third = default, DeveloperValue fourth = default, DeveloperParameterId parameter = DeveloperParameterId.None)
    {
        DeveloperValue[] values = [first, second, third, fourth];
        int count = fourth.IsValid ? 4 : third.IsValid ? 3 : second.IsValid ? 2 : first.IsValid ? 1 : 0;
        var request = Request(runtime, revision, operation, target, values.AsSpan(0, count), parameter);
        DeveloperResult receipt = runtime.Services.DeveloperActions.Submit(access, Context(runtime, revision), request);
        Assert.Contains(receipt.Status, new[] { DeveloperRequestStatus.Accepted, DeveloperRequestStatus.Pending });
        runtime.Simulation.AdvanceOneTick();
        DeveloperResult? completed = runtime.Services.DeveloperActions.CaptureResults()
            .SingleOrDefault(result => result.Correlation == request.Correlation);
        Assert.NotNull(completed);
        return completed!;
    }

    private static DeveloperRequest Request(MatchRuntime runtime, DeveloperStateRevision revision, DeveloperOperation operation,
        DeveloperTarget target, DeveloperValue value, DeveloperParameterId parameter = DeveloperParameterId.None) =>
        Request(runtime, revision, operation, target, new[] { value }.AsSpan(), parameter);

    private static DeveloperRequest Request(MatchRuntime runtime, DeveloperStateRevision revision, DeveloperOperation operation,
        DeveloperTarget target, ReadOnlySpan<DeveloperValue> values, DeveloperParameterId parameter = DeveloperParameterId.None) =>
        new(runtime.Simulation.SessionId, Guid.NewGuid(), revision, operation, target, parameter, values);

    private static DeveloperExecutionContext Context(MatchRuntime runtime, DeveloperStateRevision revision) => new(
        runtime.Simulation.SessionId, revision,
        player => runtime.MatchConfiguration.Participants.Any(participant => participant.Player == player),
        runtime.Simulation.Entities.IsAlive);

    private static Vector3 FindOpenPosition(MatchRuntime runtime, UnitDefinition definition)
    {
        var entities = runtime.Simulation.Entities;
        Vector3 origin = entities.GetComponent<WorldTransform>(runtime.GetBase(Owner).CommandCore).Position;
        NavigationGrid grid = runtime.Services.Navigation.World.Grid;
        NavigationCapabilities capabilities = NavigationCapabilities.For(definition.MovementClass);
        Vector3 half = definition.VisualScale * 0.5f + new Vector3(0.5f, 0, 0.5f);
        for (int radius = 40; radius <= 1600; radius += 20)
            for (int angle = 0; angle < 360; angle += 15)
            {
                double radians = angle * Math.PI / 180;
                Vector3 candidate = origin + new Vector3((float)Math.Cos(radians) * radius, 0,
                    (float)Math.Sin(radians) * radius);
                if (!grid.TryWorldToCell(candidate, out var cell) || !grid.IsTraversable(cell, capabilities)) continue;
                bool overlaps = false;
                foreach (EntityId existing in entities.Query<WorldTransform, SpatialPresence>())
                {
                    AxisAlignedBounds bounds = entities.GetComponent<SpatialPresence>(existing)
                        .CreateEntry(existing, entities.GetComponent<WorldTransform>(existing)).Bounds;
                    if (candidate.X + half.X >= bounds.Minimum.X && candidate.X - half.X <= bounds.Maximum.X &&
                        candidate.Z + half.Z >= bounds.Minimum.Z && candidate.Z - half.Z <= bounds.Maximum.Z)
                    { overlaps = true; break; }
                }
                if (!overlaps) return candidate;
            }
        throw new InvalidOperationException("No valid spawn position found for action acceptance.");
    }

    private static int CountOwnedUnits(MatchRuntime runtime)
    {
        int count = 0;
        foreach (EntityId entity in runtime.Simulation.Entities.Query<UnitIdentity>())
            if (runtime.Simulation.Entities.TryGetComponent(entity, out ControllableEntity control) && control.Owner == Owner)
                count++;
        return count;
    }
}
#endif
