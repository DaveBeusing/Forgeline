using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Authorized, bounded observations; an absent recommendation is not an estimated truck requirement.</summary>
public readonly record struct CargoFleetCapacityObservation(
    int BacklogRequests,
    int ActiveTrucks,
    int IdleTrucks,
    int EffectiveTrucks,
    EntityId AffectedFacility);

/// <summary>Completed-tick fleet-capacity evidence; distinguishes occupied trucks from broken logistics.</summary>
public sealed class CargoFleetShortageTracker
{
    private const double ObservationSeconds = 10.0;
    private const double ClearSeconds = 3.0;
    private const double CooldownSeconds = 5.0;
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _lastTick;
    private int _qualifyingTicks;
    private int _clearingTicks;
    private ulong _cooldownUntilTick;
    private int _initialBacklog;
    private bool _active;
    private CargoFleetCapacityObservation _last;

    public CargoFleetCapacityObservation? Observe(
        SimulationContext context,
        SimulationSessionId session,
        PlayerId player,
        AutomatedDistributionDebugSnapshot distribution,
        InventoryStore inventories)
    {
        if (!session.IsSpecified || !player.IsSpecified || distribution.Tick != context.Tick ||
            context.TickDuration.TotalSeconds <= 0)
            return ObserveEvidence(session, player, context.Tick, context.TickDuration, default, false);

        var entities = context.Entities;
        int backlog = 0;
        EntityId facility = default;
        for (int requestIndex = 0; requestIndex < distribution.Requests.Count; requestIndex++)
        {
            var request = distribution.Requests[requestIndex];
            if (request.State != LogisticsTransportRequestState.RetryPending ||
                request.FailureReason != LogisticsTransportRequestFailureReason.NoTruckAvailable ||
                !entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy policy) ||
                !policy.Enabled || !entities.IsAlive(policy.TargetEntity) ||
                !entities.TryGetComponent(policy.TargetEntity, out CompletedBuilding target) ||
                target.Owner != player ||
                entities.TryGetComponent(policy.TargetEntity, out HealthState health) && health.IsDepleted)
                continue;

            backlog++;
            if (!facility.IsValid || policy.TargetEntity.CompareTo(facility) < 0)
                facility = policy.TargetEntity;
        }

        int active = 0, idle = 0, blocked = 0;
        foreach (EntityId truck in entities.Query<CargoTransport>(QueryIterationOrder.StableByEntityIndex))
        {
            var cargo = entities.GetComponent<CargoTransport>(truck);
            if (cargo.Owner != player || entities.HasComponent<SupplyTruck>(truck) ||
                !entities.IsAlive(truck) ||
                entities.TryGetComponent(truck, out HealthState truckHealth) && truckHealth.IsDepleted) continue;
            if (!inventories.Contains(cargo.CargoInventory))
            {
                blocked++;
                continue;
            }

            if (entities.HasComponent<CargoFuelServiceState>(truck) ||
                entities.HasComponent<CargoFuelServiceDemand>(truck) ||
                entities.HasComponent<ResupplyOrder>(truck) ||
                entities.HasComponent<NavigationFailureState>(truck) ||
                entities.TryGetComponent(truck, out GroundMovementState motion) &&
                    motion.Status is GroundMovementStatus.OutOfFuel or GroundMovementStatus.Stuck ||
                entities.TryGetComponent(truck, out VehicleQueueEntry queue) &&
                    queue.WaitReason is VehicleQueueWaitReason.Predecessor or
                        VehicleQueueWaitReason.OccupiedExit or VehicleQueueWaitReason.Full or
                        VehicleQueueWaitReason.GeometryBlocked or VehicleQueueWaitReason.Unavailable)
            {
                blocked++;
                continue;
            }

            if (!entities.TryGetComponent(truck, out CargoTransportRuntimeState state)) continue;
            if (entities.HasComponent<CargoTransportOrder>(truck))
            {
                if (state.Lifecycle is CargoTransportLifecycleState.ToOrigin or
                    CargoTransportLifecycleState.Loading or CargoTransportLifecycleState.ToDestination or
                    CargoTransportLifecycleState.Unloading)
                    active++;
                else blocked++;
            }
            else if (state.Lifecycle == CargoTransportLifecycleState.Idle &&
                     !entities.HasComponent<CargoTransportReservation>(truck) &&
                     inventories.GetTotalQuantity(cargo.CargoInventory) <= 0.000000001)
            {
                if (entities.TryGetComponent(truck, out UnitFuelState fuel) &&
                    (!inventories.Contains(fuel.InventoryId) ||
                     inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) <= 0.000000001))
                    blocked++;
                else idle++;
            }
            else blocked++;
        }

        // NoTruckAvailable proves a source and structural network path existed.
        // Only a fully occupied, currently productive fleet can support a fleet
        // expansion recommendation. Zero vehicles, rescue, full bays and other
        // blocked states are not evidence of an inadequate fleet size.
        bool candidate = backlog > 0 && active > 0 && idle == 0 && blocked == 0;
        var observed = new CargoFleetCapacityObservation(backlog, active, idle, active + idle, facility);
        return Advance(session, player, context.Tick, context.TickDuration, observed,
            candidate && IsOwnedTarget(entities, player, facility));
    }

    // Pure fixed-tick state contract, also used to validate hysteresis without
    // constructing world entities or a renderer.
    internal CargoFleetCapacityObservation? ObserveEvidence(
        SimulationSessionId session, PlayerId player, SimulationTick tick,
        TimeSpan tickDuration, in CargoFleetCapacityObservation observed, bool eligible) =>
        Advance(session, player, tick, tickDuration, observed, eligible);

    private CargoFleetCapacityObservation? Advance(
        SimulationSessionId session, PlayerId player, SimulationTick tick,
        TimeSpan tickDuration, in CargoFleetCapacityObservation observed, bool candidate)
    {
        if (_session != session || _player != player || tick.Value < _lastTick.Value)
        {
            _session = session;
            _player = player;
            _lastTick = default;
            _qualifyingTicks = _clearingTicks = _initialBacklog = 0;
            _cooldownUntilTick = 0;
            _active = false;
            _last = default;
        }
        if (tick == _lastTick && tick.Value != 0)
            return _active ? _last : null;
        _lastTick = tick;

        if (!session.IsSpecified || !player.IsSpecified || tickDuration.TotalSeconds <= 0)
        {
            ResetEvidence();
            return null;
        }

        int activationTicks = Math.Max(1, (int)Math.Ceiling(ObservationSeconds / tickDuration.TotalSeconds));
        int clearTicks = Math.Max(1, (int)Math.Ceiling(ClearSeconds / tickDuration.TotalSeconds));
        int cooldownTicks = Math.Max(1, (int)Math.Ceiling(CooldownSeconds / tickDuration.TotalSeconds));
        int backlog = observed.BacklogRequests;
        if (candidate)
        {
            _clearingTicks = 0;
            if (_qualifyingTicks == 0) _initialBacklog = backlog;
            _qualifyingTicks = Math.Min(activationTicks, _qualifyingTicks + 1);
            if (!_active && tick.Value >= _cooldownUntilTick &&
                _qualifyingTicks >= activationTicks && backlog >= _initialBacklog)
                _active = true;
            if (_active) _last = observed;
        }
        else
        {
            _qualifyingTicks = 0;
            _initialBacklog = 0;
            _clearingTicks++;
            if (_active && _clearingTicks >= clearTicks)
            {
                _active = false;
                _cooldownUntilTick = tick.Value + (ulong)cooldownTicks;
            }
        }
        return _active ? _last : null;
    }

    private static bool IsOwnedTarget(EntityRegistry entities, PlayerId player, EntityId entity) =>
        entities.IsAlive(entity) &&
        entities.TryGetComponent(entity, out CompletedBuilding completed) &&
        completed.Owner == player &&
        (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted);

    private void ResetEvidence()
    {
        _qualifyingTicks = _clearingTicks = _initialBacklog = 0;
        _active = false;
        _last = default;
    }
}
