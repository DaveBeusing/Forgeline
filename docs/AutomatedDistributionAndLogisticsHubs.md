# Automated Distribution and Logistics Hubs

## Purpose

Automated distribution maintains configured regional stock levels by creating and dispatching physical Cargo Truck deliveries over the authoritative logistics network.

The system automates repetitive transport scheduling while preserving the strategic choices exposed by stock thresholds, priorities, network topology, transport capacity, and infrastructure availability. It never mutates a remote inventory to simulate delivery.

## Logistics Hubs

LogisticsHub is a game-simulation capability backed by InventoryStorage.

A completed Logistics Hub registers as LogisticsNodeKind.LogisticsHub with cargo source, cargo destination, storage, and distribution capabilities. A disabled hub remains represented by its authoritative simulation component but its logistics node becomes unavailable for route selection until it is operational again.

The initial buildable Logistics Hub provides 3,000 units of aggregate storage capacity and consumes power like other constructed economic buildings.

## Stock Policies

Regional replenishment is driven by LogisticsStockPolicy. Each policy targets one resource at one destination entity and defines DesiredMinimum, DesiredTarget, DesiredMaximum, Priority, and Enabled.

Thresholds must satisfy 0 <= minimum <= target <= maximum.

The authoritative projected stock includes the destination's currently stored quantity plus the quantity of an already active request for the same policy. This prevents one deficit from creating a new request every simulation tick.

Policies enter simulation through SetLogisticsStockPolicyCommand and can be removed through RemoveLogisticsStockPolicyCommand.

## Request Lifecycle

Requests move through Pending, Assigned, InTransit, RetryPending, Completed, and Failed states.

A request is created only when projected quantity falls below DesiredMinimum. Its requested quantity is the amount required to reach DesiredTarget. Requests are coalesced by stock policy, so pending or retrying work is updated instead of duplicated.

Completed and failed requests remain in the diagnostic snapshot for a bounded retention period before they are removed from scheduler state.

## Source Selection

A source is eligible when its node is enabled, exposes cargo-source capability, has an authoritative inventory, contains available unreserved stock, retains any desired target configured for itself, and has a route to the destination under the configured route-cost policy.

Source selection is deterministic-friendly: logistics nodes are evaluated in stable node-ID order, LogisticsNetwork computes route validity and cost, the lowest-cost source wins, and equal costs are resolved by stable node ID.

The default distribution route policy uses the existing ground-road logistics policy. Distribution does not maintain a second connectivity model.

## Shipment Quantity

Assigned shipment quantity is bounded by the outstanding deficit, source surplus, Cargo Truck capacity, and destination free capacity. The dispatcher uses the minimum of those four values.

## Resource Reservations

Before truck assignment, the selected source quantity is reserved through InventoryStore. The reservation prevents other reservation-aware work from claiming the same stock.

The reservation is attached to the assigned truck as CargoTransportReservation. When the truck reaches the source, the reservation is validated, the exact quantity is released, and the existing inventory transfer moves that quantity into the truck cargo inventory. The reservation component is removed only after the physical load succeeds.

If transfer fails after reservation release, the reservation is restored before transport failure handling. If assignment is rejected or the truck disappears before loading, the dispatcher releases the outstanding reservation.

## Physical Cargo Transport

Automated distribution does not implement a parallel transport simulation.

After assignment it creates a normal CargoTransportOrder. CargoTransportSystem remains authoritative for driving to the source, loading cargo, following logistics routes through hierarchical navigation, rerouting where supported, driving to the destination, unloading, waiting on temporary constraints, reporting failure, and returning the truck to idle after completion.

Inventory changes therefore occur only at loading and unloading boundaries associated with the physical Cargo Truck lifecycle.

## Truck Assignment

A truck is eligible when ownership matches the destination when ownership is known, it has no active cargo order, it has no automated reservation, its state is Idle, its cargo inventory exists, and its cargo inventory is empty.

Eligible trucks are evaluated in stable entity order and the truck closest to the selected source is chosen.

## Priority and Fairness

Requests are primarily ordered Critical, High, Normal, then Low.

To reduce starvation under sustained higher-priority demand, waiting requests gain an effective priority step after a configurable aging interval. Aging is bounded at Critical. Requests at the same effective priority are ordered by creation tick and then stable request ID.

This policy is deliberately simple and deterministic-friendly rather than a global mathematical optimizer.

## Retry and Failure Handling

Temporary scheduling failures enter RetryPending and are retried after a fixed simulation-tick delay. Reasons include unavailable destination, no source surplus, no valid route, no truck, reservation failure, and assignment failure.

A physical transport failure is reconciled with reservation and cargo state. If a failed truck has not loaded cargo, the request can retry up to the configured transport-attempt limit. If it still carries cargo, the request fails rather than silently scheduling a duplicate shipment.

No failure path may leave a scheduler-owned source reservation behind.

## Diagnostics

AutomatedDistributionMetrics exposes configured policies, pending/assigned/in-transit/retry requests, unserved deficits, idle and active trucks, reserved source cargo, completed and failed totals, and average/maximum delivery latency in simulation ticks.

AutomatedDistributionDebugSnapshot exposes per-request read models without granting presentation access to mutable simulation state.

The world debug layer renders destination markers, source-to-destination resource-flow lines, request state, and requested shipment quantity. Presentation consumes read models only and never schedules requests or mutates logistics state.

## Determinism and Tick Ownership

Automated distribution is an ISimulationSystem in the Logistics phase. Scheduling uses simulation ticks rather than wall-clock time, stable entity and node iteration, explicit tie-breaking, authoritative inventory state, authoritative logistics connectivity, and the existing Cargo Truck lifecycle.

The system remains compatible with headless simulation and has no dependency on presentation, graphics, UI, or platform code.

## Validation

Regression coverage includes automatic replenishment to target, resource conservation, request coalescing, competing priorities with limited trucks, missing routes, truck loss before loading and reservation cleanup, Logistics Hub registration and availability state, and debug-read-model visualization.

The simulation benchmark host includes automated-distribution scheduling scenarios with tens to hundreds of hubs and requests. Benchmark timing is observational rather than a CI pass/fail threshold.

## Extension Boundary

Battlefield supply can use this shared request/dispatch foundation for resources such as Fuel and Ammunition. Battlefield-specific demand generation may add policy logic, but it should not introduce a second truck scheduler or bypass shared reservation and physical transport.
