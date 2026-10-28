using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum GameplayProgressKind : byte
{
    Extraction, CargoLoaded, CargoUnloaded, SteelCompleted, ScoutProduced, SupplyTruckProduced,
    SupplyLoadedFuel, SupplyLoadedAmmunition, FieldFuelTransferred, FieldAmmunitionTransferred, DisruptionResolved, SupplyLoadCompleted, OrePolicyApplied,
    ConveyorDelivered, ProductionCompleted, ResearchCompleted, DisruptionObserved
}

public readonly record struct GameplayProgressEvent(ulong Identity, SimulationSessionId SessionId, SimulationTick Tick, PlayerId Player,
    GameplayProgressKind Kind, EntityId Actor, EntityId Source, EntityId Target, ulong OrderIdentity,
    ResourceId Resource, double Quantity, uint DefinitionId);

/// <summary>Bounded completed-action observations. Never controls economy or commands.</summary>
public sealed partial class GameplayProgressJournal : ISimulationTickObserver, IResourceExtractionSink
{
    // Simulation-owned subscribers retain bounded facts; display only after OnTickCompleted.
    internal event Action<GameplayProgressEvent>? Recorded;
    private readonly EntityRegistry _entities;
    private readonly SimulationSessionId _session;
    private readonly Dictionary<EntityId, double> _productionTotals = [];
    private readonly Dictionary<PlayerId, Dictionary<(GameplayProgressKind Kind, ResourceId Resource, uint Definition), GameplayProgressEvent>> _latest = [];
    private readonly Dictionary<PlayerId, IReadOnlyList<GameplayProgressEvent>> _copies = [];
    private readonly Dictionary<PlayerId, Dictionary<(GameplayProgressKind Kind, ResourceId Resource, uint Definition), GameplayProgressEvent>> _first = [];
    private readonly Dictionary<PlayerId, IReadOnlyList<GameplayProgressEvent>> _firstCopies = [];
    private readonly HashSet<EntityId> _blocked = [];
    private readonly HashSet<EntityId> _policies = [];
    private readonly List<EntityId> _stale = [];
    internal Func<EntityRegistry, EntityId, PlayerWorkSummary>? Work { get; set; }
    private readonly Dictionary<(PlayerId, EntityId, EntityId, ulong), byte> _loads = [];
    private ulong _identity;
    private SimulationTick _completed;
    private bool _observed;
    internal GameplayProgressJournal(EntityRegistry entities, SimulationSessionId session) { _entities = entities; _session = session; }

    internal void Record(SimulationTick tick, PlayerId player, GameplayProgressKind kind, EntityId actor,
        EntityId source, EntityId target, ulong order, ResourceId resource, double quantity, uint definition = 0)
    {
        if (player.Value <= 0 || !_entities.IsAlive(actor) || !_entities.IsAlive(target) ||
            source.IsValid && !_entities.IsAlive(source) || !double.IsFinite(quantity) || quantity <= 0 ||
            !_entities.TryGetComponent(target, out ControllableEntity recipient) || recipient.Owner != player) return;
        if (_entities.TryGetComponent(actor, out ControllableEntity sender) && sender.Owner != player) return;
        if (!_latest.TryGetValue(player, out var ledger))
        {
            if (_latest.Count >= 16) return;
            _latest.Add(player, ledger = []);
        }
        var key = (kind, resource, definition);
        if (!ledger.ContainsKey(key) && ledger.Count >= 64) return;
        if (ledger.TryGetValue(key, out var previous) && previous.Tick == tick && previous.Actor == actor &&
            previous.Source == source && previous.Target == target && previous.OrderIdentity == order && previous.Resource == resource) return;
        ledger[key] = new(++_identity, _session, tick, player, kind, actor, source, target, order, resource, quantity, definition);
        Recorded?.Invoke(ledger[key]);
        _copies.Remove(player);
        if (!_first.TryGetValue(player, out var first)) _first.Add(player, first = []);
        if (first.TryAdd(key, ledger[key])) _firstCopies.Remove(player);
        if (kind is GameplayProgressKind.SupplyLoadedFuel or GameplayProgressKind.SupplyLoadedAmmunition)
        {
            var loadKey = (player, source, target, order);
            if (!_loads.ContainsKey(loadKey) && _loads.Count >= 64) return;
            byte parts = (byte)(_loads.GetValueOrDefault(loadKey) | (kind == GameplayProgressKind.SupplyLoadedFuel ? 1 : 2));
            if (parts == 3)
            {
                _loads.Remove(loadKey);
                Record(tick, player, GameplayProgressKind.SupplyLoadCompleted, actor, source, target, order, default, quantity, definition);
            }
            else _loads[loadKey] = parts;
        }
    }

    public IReadOnlyList<GameplayProgressEvent> Capture(PlayerId player, SimulationTick completedTick)
    {
        if (!_observed || completedTick != _completed || !_latest.TryGetValue(player, out var ledger)) return Array.Empty<GameplayProgressEvent>();
        if (!_copies.TryGetValue(player, out var copy))
        {
            copy = Array.AsReadOnly(ledger.Values.Where(x => x.Tick.Value <= completedTick.Value).OrderBy(x => x.Identity).ToArray());
            _copies.Add(player, copy);
        }
        return copy;
    }

    public IReadOnlyList<GameplayProgressEvent> CaptureFirst(PlayerId player, SimulationTick completedTick)
    {
        if (!_observed || completedTick != _completed || !_first.TryGetValue(player, out var ledger)) return Array.Empty<GameplayProgressEvent>();
        foreach (var item in ledger.Values)
            if (item.Tick.Value > completedTick.Value) return Array.Empty<GameplayProgressEvent>();
        if (!_firstCopies.TryGetValue(player, out var copy))
        {
            copy = Array.AsReadOnly(ledger.Values.OrderBy(x => x.Identity).ToArray());
            _firstCopies.Add(player, copy);
        }
        return copy;
    }

    public void OnExtracted(in ResourceExtractionResult result)
    {
        if (_entities.TryGetComponent(result.Extractor, out ControllableEntity owner))
            Record(result.Tick, owner.Owner, GameplayProgressKind.Extraction, result.Extractor, result.Deposit,
                result.Extractor, result.Tick.Value, result.ResourceId, result.Quantity);
    }

    public void OnTickCompleted(SimulationContext context)
    {
        if (_observed && context.Tick.Value <= _completed.Value) return;
        foreach (var entity in context.Entities.Query<ControllableEntity>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.HasComponent<ProductionFacility>(entity) && !context.Entities.HasComponent<UnitProductionFacility>(entity)) continue;
            var owner = context.Entities.GetComponent<ControllableEntity>(entity).Owner;
            if (context.Entities.TryGetComponent(entity, out ProductionFacility trackedProduction))
            {
                double previousOutput = _productionTotals.GetValueOrDefault(entity);
                _productionTotals[entity] = trackedProduction.TotalOutputQuantity;
                if (context.Entities.TryGetComponent(entity, out CompletedBuilding producedBuilding) && producedBuilding.Owner == owner &&
                    trackedProduction.LastCompletedTick == context.Tick)
                {
                    double output = trackedProduction.TotalOutputQuantity - previousOutput;
                    Record(context.Tick, owner, GameplayProgressKind.ProductionCompleted, entity, entity, entity,
                        trackedProduction.CompletedCycles, default, output, producedBuilding.BuildingId.Value);
                    if (producedBuilding.BuildingId == BuildingIds.Smelter)
                        Record(context.Tick, owner, GameplayProgressKind.SteelCompleted, entity, entity, entity,
                            trackedProduction.CompletedCycles, ResourceIds.Steel, output, producedBuilding.BuildingId.Value);
                }
            }
            var work = Work?.Invoke(context.Entities, entity) ?? PlayerWorkSummary.None;
            if (work.State == PlayerWorkState.Blocked && _blocked.Add(entity))
                Record(context.Tick, owner, GameplayProgressKind.DisruptionObserved, entity, entity, entity,
                    context.Tick.Value, default, 1);
            else if (work.State == PlayerWorkState.Running && _blocked.Remove(entity))
                Record(context.Tick, owner, GameplayProgressKind.DisruptionResolved, entity, entity, entity,
                    context.Tick.Value, default, 1);
        }
        foreach (var entity in context.Entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
        {
            var policy = context.Entities.GetComponent<LogisticsStockPolicy>(entity);
            if (policy.Enabled && policy.ResourceId == ResourceIds.FerrousOre &&
                context.Entities.TryGetComponent(policy.TargetEntity, out CompletedBuilding target) && target.BuildingId == BuildingIds.Smelter &&
                _policies.Add(entity))
                Record(context.Tick, target.Owner, GameplayProgressKind.OrePolicyApplied, entity, policy.TargetEntity,
                    policy.TargetEntity, ((ulong)entity.Generation << 32) | entity.Index, ResourceIds.FerrousOre, 1, target.BuildingId.Value);
        }
        _stale.Clear();
        foreach (var entity in _policies) if (!context.Entities.IsAlive(entity)) _stale.Add(entity);
        foreach (var entity in _stale) _policies.Remove(entity);
        _stale.Clear();
        foreach (var entity in _blocked) if (!context.Entities.IsAlive(entity)) _stale.Add(entity);
        foreach (var entity in _stale) _blocked.Remove(entity);
        _stale.Clear();
        foreach (var entity in _productionTotals.Keys) if (!context.Entities.IsAlive(entity)) _stale.Add(entity);
        foreach (var entity in _stale) _productionTotals.Remove(entity);
        _completed = context.Tick;
        _observed = true;
    }
}
