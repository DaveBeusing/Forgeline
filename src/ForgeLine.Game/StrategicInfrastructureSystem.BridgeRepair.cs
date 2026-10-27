using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed partial class StrategicInfrastructureSystem
{
    private SimulationCoordinator? _bridgeOwner;
    private InventoryStore? _bridgeStock;
    private FactionIntelligenceStore? _bridgeIntelligence;

    public void BindBridgeRepairs(SimulationCoordinator simulation, InventoryStore inventories, FactionIntelligenceStore intelligence)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(inventories);
        ArgumentNullException.ThrowIfNull(intelligence);
        if (_bridgeOwner is not null && !ReferenceEquals(_bridgeOwner, simulation))
            throw new InvalidOperationException("Infrastructure has one simulation owner.");
        _bridgeOwner = simulation; _bridgeStock = inventories; _bridgeIntelligence = intelligence;
    }
    public bool Matches(SimulationCoordinator simulation) => ReferenceEquals(_bridgeOwner, simulation);

    public BridgeRepairFailure EvaluateBridgeRepair(SimulationContext context, PlayerId player, EntityId engineer,
        EntityId bridge, EntityId source, uint expectedRevision)
    {
        if (_bridgeOwner is null || !ReferenceEquals(_bridgeOwner.Entities, context.Entities) ||
            _bridgeStock is null || _bridgeIntelligence is null) return BridgeRepairFailure.ServiceUnavailable;
        bool running = false;
        foreach (EntityId match in context.Entities.Query<MatchState>())
            running |= context.Entities.GetComponent<MatchState>(match).Lifecycle == MatchLifecyclePhase.Running;
        if (!running) return BridgeRepairFailure.MatchUnavailable;
        if (!context.Entities.TryGetComponent(bridge, out BridgeCombat combat) ||
            !context.Entities.TryGetComponent(bridge, out StrategicInfrastructure infrastructure) ||
            !context.Entities.TryGetComponent(bridge, out StrategicInfrastructureState state) ||
            !context.Entities.TryGetComponent(bridge, out HealthState health)) return BridgeRepairFailure.InvalidBridge;
        if (combat.Revision != expectedRevision) return BridgeRepairFailure.StaleRevision;
        if (combat.Revision == uint.MaxValue) return BridgeRepairFailure.RevisionExhausted;
        if (!player.IsSpecified || player.Value > uint.MaxValue ||
            !_bridgeIntelligence.IsEntityCurrentlyIdentified(new FactionId((uint)player.Value), bridge))
            return BridgeRepairFailure.NotIdentified;
        if (!infrastructure.Restorable) return BridgeRepairFailure.NotRestorable;
        BridgeRepairFailure engineerFailure = CheckEngineer(context, player, engineer, infrastructure.NavigationBlocker);
        if (engineerFailure != BridgeRepairFailure.None) return engineerFailure;
        foreach (EntityId other in context.Entities.Query<BridgeRepairWork>())
            if (other != bridge && context.Entities.GetComponent<BridgeRepairWork>(other).Engineer == engineer)
                return BridgeRepairFailure.EngineerBusy;
        if (context.Entities.TryGetComponent(bridge, out BridgeRepairWork work))
        {
            if (work.Owner != player) return BridgeRepairFailure.OtherPlayerRepair;
            if (health.Current + 1e-9 < work.ExpectedHealth) return BridgeRepairFailure.DamagedAgain;
            return state.State == StrategicInfrastructureOperationalState.Restoring
                ? BridgeRepairFailure.None : BridgeRepairFailure.InvalidBridge;
        }
        if (state.IsOperational && health.Current >= health.Maximum) return BridgeRepairFailure.NotDamaged;
        if (!context.Entities.TryGetComponent(source, out CompletedBuilding building) ||
            !context.Entities.TryGetComponent(source, out InventoryStorage storage) ||
            !context.Entities.TryGetComponent(source, out WorldTransform sourcePose) ||
            !context.Entities.TryGetComponent(source, out HealthState sourceHealth) || sourceHealth.IsDepleted ||
            !_bridgeStock.Contains(storage.InventoryId)) return BridgeRepairFailure.InvalidSource;
        if (building.Owner != player) return BridgeRepairFailure.ForeignSource;
        var engineerPose = context.Entities.GetComponent<WorldTransform>(engineer);
        if (HorizontalDistanceSquared(engineerPose.Position, sourcePose.Position) >
            BridgeCombat.MaterialRange * BridgeCombat.MaterialRange) return BridgeRepairFailure.SourceOutOfRange;
        return _bridgeStock.GetAvailableQuantity(storage.InventoryId, ResourceIds.Steel) < BridgeCombat.SteelCost ||
            _bridgeStock.GetAvailableQuantity(storage.InventoryId, ResourceIds.Electronics) < BridgeCombat.ElectronicsCost
            ? BridgeRepairFailure.MissingMaterials : BridgeRepairFailure.None;
    }

    internal BridgeRepairFailure StartBridgeRepair(SimulationContext context, PlayerId player, EntityId engineer,
        EntityId bridge, EntityId source, uint expectedRevision)
    {
        var failure = EvaluateBridgeRepair(context, player, engineer, bridge, source, expectedRevision);
        if (failure != BridgeRepairFailure.None) return failure;
        if (context.Entities.TryGetComponent(bridge, out BridgeRepairWork existing))
            context.Entities.SetComponent(bridge, existing with { Engineer = engineer, Block = BridgeRepairFailure.None });
        else
        {
            var storage = context.Entities.GetComponent<InventoryStorage>(source);
            if (!_bridgeStock!.Remove(storage.InventoryId, ResourceIds.Steel, BridgeCombat.SteelCost).Succeeded ||
                !_bridgeStock.Remove(storage.InventoryId, ResourceIds.Electronics, BridgeCombat.ElectronicsCost).Succeeded)
                throw new InvalidOperationException("Bridge material consumption failed after availability validation.");
            var health = context.Entities.GetComponent<HealthState>(bridge);
            double start = Math.Max(1, health.Current);
            context.Entities.SetComponent(bridge, new HealthState(start, health.Maximum));
            context.Entities.AddComponent(bridge, new BridgeRepairWork(player, engineer, source, start, start));
            context.Entities.SetComponent(bridge, new StrategicInfrastructureState(StrategicInfrastructureOperationalState.Restoring, 0));
            _restoreStartedCount++;
        }
        BumpBridgeRevision(context, bridge);
        new HoldPositionCommand(player, [engineer], context.Tick).Execute(context);
        new StopMovementCommand(player, [engineer], context.Tick).Execute(context);
        SynchronizeAuthoritativeTopology(context); _initialized = true; UpdateMetrics(context);
        return BridgeRepairFailure.None;
    }

    public void DisableBridgeFromCombat(SimulationContext context, EntityId bridge)
    {
        if (!DisableCombatBridge(context, bridge)) return;
        SynchronizeAuthoritativeTopology(context); _initialized = true; UpdateMetrics(context);
    }
    private bool DisableCombatBridge(SimulationContext context, EntityId bridge)
    {
        if (!context.Entities.HasComponent<BridgeCombat>(bridge) ||
            !context.Entities.TryGetComponent(bridge, out StrategicInfrastructureState state)) return false;
        bool changed = state.State != StrategicInfrastructureOperationalState.Disabled ||
            context.Entities.HasComponent<BridgeRepairWork>(bridge) ||
            context.Entities.TryGetComponent(bridge, out HealthState current) && current.Current > 0;
        context.Entities.SetComponent(bridge, StrategicInfrastructureState.Disabled);
        if (context.Entities.TryGetComponent(bridge, out HealthState health))
            context.Entities.SetComponent(bridge, new HealthState(0, health.Maximum));
        context.Entities.RemoveComponent<BridgeRepairWork>(bridge);
        if (changed) { BumpBridgeRevision(context, bridge); _disableCount++; }
        return changed;
    }

    // Called only by the existing restoration loop; there is no second tick/progress owner.
    private bool AdvanceBridgeRestoration(SimulationContext context, EntityId bridge,
        StrategicInfrastructure infrastructure, StrategicInfrastructureState state)
    {
        if (!context.Entities.TryGetComponent(bridge, out BridgeRepairWork work) ||
            !context.Entities.TryGetComponent(bridge, out HealthState health))
            return DisableCombatBridge(context, bridge);
        if (health.Current + 1e-9 < work.ExpectedHealth)
            return DisableCombatBridge(context, bridge);
        var failure = CheckEngineer(context, work.Owner, work.Engineer, infrastructure.NavigationBlocker);
        if (failure != BridgeRepairFailure.None)
        {
            context.Entities.SetComponent(bridge, work with { Block = failure });
            return false;
        }
        uint progress = checked(state.RestorationProgressTicks + 1);
        double nextHealth = progress >= infrastructure.RestorationTicks ? health.Maximum :
            work.StartHealth + (health.Maximum - work.StartHealth) * (progress / (double)infrastructure.RestorationTicks);
        context.Entities.SetComponent(bridge, new HealthState(nextHealth, health.Maximum));
        if (progress >= infrastructure.RestorationTicks)
        {
            context.Entities.SetComponent(bridge, StrategicInfrastructureState.Operational);
            context.Entities.RemoveComponent<BridgeRepairWork>(bridge);
            BumpBridgeRevision(context, bridge); _restoreCompletedCount++;
            return true;
        }
        context.Entities.SetComponent(bridge, new StrategicInfrastructureState(StrategicInfrastructureOperationalState.Restoring, progress));
        context.Entities.SetComponent(bridge, work with { ExpectedHealth = nextHealth, Block = BridgeRepairFailure.None });
        return false;
    }
    private static BridgeRepairFailure CheckEngineer(SimulationContext context, PlayerId player, EntityId engineer,
        AxisAlignedBounds bounds)
    {
        if (!context.Entities.TryGetComponent(engineer, out UnitIdentity unit) || unit.UnitId != UnitIds.CombatEngineer ||
            !context.Entities.TryGetComponent(engineer, out ControllableEntity control) ||
            !context.Entities.TryGetComponent(engineer, out WorldTransform pose) ||
            !context.Entities.TryGetComponent(engineer, out HealthState health) || health.IsDepleted)
            return BridgeRepairFailure.EngineerUnavailable;
        if (control.Owner != player) return BridgeRepairFailure.ForeignEngineer;
        float dx = MathF.Max(0, MathF.Max(bounds.Minimum.X - pose.Position.X, pose.Position.X - bounds.Maximum.X));
        float dz = MathF.Max(0, MathF.Max(bounds.Minimum.Z - pose.Position.Z, pose.Position.Z - bounds.Maximum.Z));
        return dx * dx + dz * dz <= BridgeCombat.EngineerRange * BridgeCombat.EngineerRange
            ? BridgeRepairFailure.None : BridgeRepairFailure.EngineerOutOfRange;
    }
    private static void BumpBridgeRevision(SimulationContext context, EntityId bridge)
    {
        var combat = context.Entities.GetComponent<BridgeCombat>(bridge);
        context.Entities.SetComponent(bridge, combat with { Revision = combat.Revision < uint.MaxValue ? combat.Revision + 1 : uint.MaxValue });
    }
    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);
}
