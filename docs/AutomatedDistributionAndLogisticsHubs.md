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

The authoritative projected stock includes the destination's currently stored quantity and already assigned or in-transit work. One active request per stock policy prevents duplicate deficits. Pending/RetryPending quantities are **not** committed inbound stock: they are recalculated from current inventory and `DesiredTarget` on each completed simulation tick, preserving the original request identity and waiting timestamp. An obsolete pending request (destination at target, disabled/removed policy, or a changed policy destination/resource identity) becomes terminal without reserving a truck or counting a physical delivery. In-flight cargo remains owned by the transport lifecycle.

Policies enter simulation through SetLogisticsStockPolicyCommand and can be removed through RemoveLogisticsStockPolicyCommand.

## Request Lifecycle

Requests move through Pending, Assigned, InTransit, RetryPending, Completed, and Failed states.

A request is created only when projected quantity falls below DesiredMinimum. Its requested quantity is the amount required to reach DesiredTarget. Requests are coalesced by stock policy, so pending or retrying work is updated instead of duplicated.

Completed and failed requests remain in the diagnostic snapshot for a bounded retention period before they are removed from scheduler state.

### Deterministic lifecycle and fairness matrix

| Transition | FIFO age / identity | Reservation and metric behavior |
| --- | --- | --- |
| Deficit below minimum → Pending | New `RequestId`, `CreatedAtTick` and `WaitingSinceTick` | No stock or throughput reservation |
| Pending/RetryPending coalesced with policy or stock change | Original identity and waiting age preserved; requested quantity follows live target | No speculative delivery or duplicated request |
| Pending → RetryPending on NoSource, NoRoute, NoTruck, NoDriveFuel, full destination, or capacity failure | Original waiting age preserved; `StateChangedAtTick` and bounded `NextAttemptTick` update | No truck, stock, or route reservation retained |
| RetryPending → Pending when eligible | Original FIFO age and request ID preserved | Reevaluated once by the pending dispatch pass |
| Pending → Assigned | Original request identity remains | Exactly one accepted truck, atomic source reservation and graph capacity; only a committed assignment counts in per-tick throughput |
| Assigned → InTransit | Source reservation consumption produces real cargo | Quantity remains accounted for in inventories and transport; no requeue |
| Delivery with remaining target deficit → Pending | Fresh `WaitingSinceTick` **only after an actual completed shipment** to yield fairly to other deficits | Prior shipment is complete; no duplicate transfer |
| Target fulfilled → Completed | Terminal retention is bounded | Completion after actual delivery counted once; satisfaction without shipment does **not** increment delivery totals |
| Empty failed truck → RetryPending / retry limit → Failed | Original age retained until terminal | Release remaining source and graph reservations; obey attempt limit |
| Removed/disabled/retargeted policy while waiting | Terminal, no reused stale identity | No new shipment; in-flight ownership stays with the cargo subsystem |

Dispatcher ordering remains **effective priority → Critical recovery rank (only within Critical) → `WaitingSinceTick` → stable RequestId**. Aging promotes lower classes at most to High and never displaces an urgent Critical recovery. All eligible Pending requests are assessed once per tick in this order; a blocked oldest request records its reason and backs off without globally blocking independently feasible later demand. Physical loading-bay FIFO and truck movement are unchanged.

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

To reduce starvation under sustained higher-priority demand, waiting requests gain an effective priority step after a configurable aging interval. Aging is bounded at **High** so urgent Critical recovery retains priority. Requests at the same effective priority and Critical recovery rank are ordered by original waiting tick and then stable request ID.

This policy is deliberately simple and deterministic-friendly rather than a global mathematical optimizer.

### Player operational priority and live effectiveness

Operational scheduling reuses the four existing stock policy classes (`Critical`, `High`, `Normal`, `Low`) rather than introducing a second global dispatch policy. A facility may maintain separate resource stock policies. Production queue and battlefield supply priorities remain distinct existing controls. Source reserves, physical truck/dock FIFO, capacity reservations and conservation rules take precedence over preferred scheduling order.

The player stages the class in the existing radial `PRIOR` control and applies it through the normal stock policy action. Until that command is accepted by simulation, the displayed stored policy is unchanged. Invalid classes are rejected with explicit `InvalidPriority` feedback; ownership, entity generation and session checks continue to use the existing player command boundary.

The dispatcher updates priorities for Pending/RetryPending requests without resetting request identity or waiting age. Once a truck is assigned, its shipment retains the priority at admission; editing the policy does not silently reclassify loaded or reserved work. After a completed shipment, any remaining demand is evaluated using the latest configured class.

Each diagnostic request reports its configured shipment class and an effective scheduling class. The effective value includes bounded waiting-time aging and the existing Critical recovery qualifier; only waiting demand is promoted. The copied player stock row reports both classes and its current observed request status. The existing radial stock entry displays the configured class, any effective promotion and observed route/stock/truck/fuel/capacity obstruction. A configured class without an outstanding deficit is idle, not a promised shipment. A reported blockage describes observed dispatch feasibility, not proof of another system's underlying cause.

## Retry and Failure Handling

Temporary scheduling failures enter RetryPending and are retried after a fixed simulation-tick delay. Reasons include unavailable destination, no source surplus, no valid route, no truck, reservation failure, and assignment failure.

A physical transport failure is reconciled with reservation and cargo state. If a failed truck has not loaded cargo, the request can retry up to the configured transport-attempt limit. If it still carries cargo, the request fails rather than silently scheduling a duplicate shipment.

No failure path may leave a scheduler-owned source reservation behind.

## Diagnostics

AutomatedDistributionMetrics exposes configured policies, pending/assigned/in-transit/retry requests, unserved deficits, idle and active trucks, reserved source cargo, completed and failed totals, and average/maximum delivery latency in simulation ticks. Bounded additional fields expose oldest backlog wait, stock/route/truck/fuel-blocked demand, observed physically blocked assigned docks, and the count/quantity of **actually committed** dispatch assignments in the current tick. A successful feasibility assessment is not an inventory transfer; physical delivery happens later at the normal loading/unloading boundaries. A zero committed-dispatch metric does not itself mean all backlog demand is physically blocked at the bay.

AutomatedDistributionDebugSnapshot exposes per-request read models without granting presentation access to mutable simulation state.

The world debug layer renders destination markers, source-to-destination resource-flow lines, request state, and requested shipment quantity. Presentation consumes read models only and never schedules requests or mutates logistics state.

## Determinism and Tick Ownership

Automated distribution is an ISimulationSystem in the Logistics phase. Scheduling uses simulation ticks rather than wall-clock time, stable entity and node iteration, explicit tie-breaking, authoritative inventory state, authoritative logistics connectivity, and the existing Cargo Truck lifecycle.

The system remains compatible with headless simulation and has no dependency on presentation, graphics, UI, or platform code.

## Validation

Regression coverage includes automatic replenishment to target, resource conservation, request coalescing, competing priorities with limited trucks, missing routes, truck loss before loading and reservation cleanup, Logistics Hub registration and availability state, and debug-read-model visualization.

The simulation benchmark host includes automated-distribution scheduling scenarios with tens to hundreds of hubs and requests. Benchmark timing is observational rather than a CI pass/fail threshold.

## Battlefield Supply Integration Boundary

Battlefield Fuel and Ammunition already use this shared request/dispatch foundation for regional replenishment. Supply Depot stock policies create normal automated-distribution demand, real Cargo Trucks move that stock through the logistics graph, and battlefield providers transfer only physically available inventory at valid supply range.

Battlefield-specific demand and recipient logic must not introduce a second regional truck scheduler or bypass shared reservation, route capacity, physical transport, and inventory conservation.

## Human Player Stock Policies

The shared player action surface exposes automated-distribution policy for one selected owned logistics-capable entity.

- `L` opens the logistics view.
- `Tab` selects a resource.
- `M` cycles the edited Minimum / Target / Maximum threshold.
- Left / Right changes the selected threshold while preserving `0 <= minimum <= target <= maximum`.
- `T` cycles logistics priority.
- `Enter` submits the current stock policy through `PlayerCommandGateway`.
- `C` removes an existing policy.

The panel reads current stock, existing policy values, request state, assigned Cargo Truck, failure reason, and bottleneck reason from immutable completed-tick read models. Submission is not delivery: `AutomatedDistributionSystem` still creates requests, reserves real source stock and route capacity, assigns eligible Cargo Trucks, and relies on `CargoTransportSystem` for physical loading, movement, and unloading.

Player commands validate target ownership before delegating to the existing stock-policy commands. Invalid thresholds, foreign targets, stale policy entities, unavailable source stock, missing routes, saturated capacity, unavailable trucks, and full destinations remain authoritative failures rather than UI-side exceptions or resource grants.


## Logistics setup

INPUTS / USE INPUTS prepares catalog-based editor thresholds for additional production steps. Preview creates no requests. MIN uses stock plus outstanding demand; TARGET remains the refill goal; MAX is validated/saved but is not enforced as a hard inventory limit. Shared storage capacity remains authoritative. Requested demand and physically assigned incoming loads are displayed separately. See [Logistics setup and operation](LogisticsSetup.md) for the complete first refill sequence.

The [opening logistics guide](OnboardingInteraction.md) explains these same controls and uses [completed-action evidence](OnboardingQualification.md). Reading a card, starting stock, an accepted policy or a provider assignment never substitutes for an actual delivery or field transfer.

## Authorized player-facing active requests

The simulation's retained distribution read model now publishes the completed capture tick alongside the existing stable request identifiers, retry/failure reasons, assigned truck and requested/reserved quantities. The local player-action extractor only copies requests for the selected owned policy destination or its currently assigned owned Cargo Truck, and only when the captured distribution tick matches the player-action extraction tick. It checks policy existence and entity generation, filters origin/destination through locally owned live nodes, and derives loaded-but-not-delivered quantity only from the assigned current cargo transport state. Completed requests are not displayed as active; retained failures preserve their actual failure status. The copied read model is capped at 128 entries and reports the matching count independently. No debug request list is accessed by HUD input or rendering, and the distribution dispatcher/route capacity/physical loading logic remains unchanged.

The policy editor now steps newly edited values in 50-unit increments, but these are **stock thresholds**, not the authoritative quantity of an individual Cargo Truck trip. Existing saved policies are not rounded or migrated. The INPUTS recipe-step count is also independent. No simulation stock-policy validation or save schema is changed.
