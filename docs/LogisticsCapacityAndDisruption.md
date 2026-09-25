# Logistics Capacity and Disruption

## Purpose

FORGELINE logistics throughput is constrained by the authoritative logistics graph rather than by presentation-only utilization values.

Capacity, saturation, infrastructure availability, rerouting, backlog, and downstream shortages use the same logistics, inventory, physical transport, production, and battlefield-supply state that normal gameplay uses.

## Capacity Model

Logistics edges expose their configured throughput through `CapacityPerSecond`.

Relevant logistics nodes also have an effective throughput budget based on their role:

| Node kind | Initial throughput |
| --- | ---: |
| Extractor output | 150 units/s |
| Storage depot | 300 units/s |
| Processing facility | 200 units/s |
| Logistics hub | 600 units/s |
| Supply depot | 300 units/s |

These values are initial balancing parameters, not separate inventories. Each logistics node stores its effective throughput as graph metadata, so specific infrastructure definitions can override the role default without changing capacity-accounting code.

The capacity tracker converts throughput into a bounded simulation window. The default is 20 simulation ticks at 20 Hz, or one second. A shipment admitted during that window reserves its quantity against every traversed edge and relevant node until the window expires or the reservation becomes invalid.

Capacity accounting is simulation-tick based. Wall-clock time does not affect logistics throughput.

## Capacity Admission

Automated distribution performs capacity admission before it reserves source inventory or assigns a physical Cargo Truck.

Source selection evaluates routable sources together with currently available Cargo Truck capacity and current network capacity. A cheaper source whose route is saturated does not prevent dispatch from another source with usable capacity.

A candidate shipment is admitted only when:

- the route is structurally available;
- each traversed edge has enough remaining window capacity;
- each traversed node has enough remaining window capacity;
- source inventory has sufficient unreserved surplus;
- a compatible Cargo Truck is available;
- the destination can accept the shipment.

A successful admission creates a bounded throughput reservation. The reservation prevents multiple dispatch decisions in the same capacity window from exceeding the achievable graph throughput.

If a later inventory reservation or truck assignment fails, the throughput reservation is released immediately.

## Load States

Network health uses four load states:

- `Healthy` — utilization below 70%;
- `Busy` — utilization from 70% to below 90%;
- `Saturated` — utilization from 90% through 100%;
- `Blocked` — the infrastructure is disabled or otherwise unavailable.

Utilization is the scheduled load divided by the effective capacity for the active simulation window.

The thresholds are diagnostic states. Capacity enforcement itself uses the actual remaining capacity and does not wait for a state transition.

## Congestion and Route Choice

Capacity-aware route searches reuse the authoritative logistics topology.

When multiple structurally valid routes exist, the route planner adds a lightweight congestion cost derived from prospective edge and node utilization. The initial cost is quadratic, which makes increasingly busy infrastructure progressively less attractive without introducing lane-level traffic simulation.

A traversal whose prospective shipment would exceed an edge or node capacity is not eligible for that dispatch.

This permits demand to spread across alternate routes when capacity is available elsewhere.

## Structural Versioning Versus Dynamic Load

Topology and infrastructure availability remain versioned by `LogisticsNetwork`.

Changes such as:

- node enable/disable;
- edge enable/disable;
- node or edge removal;
- topology changes;

invalidate cached routes and advance the network version.

Dynamic capacity load does not advance the topology version. Otherwise every shipment would invalidate active physical routes and create unnecessary route churn.

Capacity-aware route planning therefore uses current load state without placing its result in the structural route cache.

## Backlog

Demand that cannot currently be dispatched remains represented by the automated distribution request lifecycle.

Pending and retry-pending requests form the current backlog.

Backlog metrics expose:

- request count;
- requested quantity;
- requests specifically blocked by capacity saturation.

Backlog is not converted into synthetic deliveries. Recovery occurs only when the existing request can pass source, capacity, route, truck, and destination checks and can be executed through the normal Cargo Truck lifecycle.

## Bottleneck Diagnosis

Distribution diagnostics map the current limiting condition to a stable bottleneck reason:

- `InsufficientSourceStock`;
- `InsufficientTruckCapacity`;
- `SaturatedLinkOrHub`;
- `DisconnectedRoute`;
- `DestinationFull`;
- `DestinationUnavailable`;
- `TransportFailure`.

Diagnostics explain current state. They do not alter priorities, add resources, create routes, or repair infrastructure.

## Infrastructure Disruption

Simulation commands can explicitly disable or restore logistics nodes and edges.

Node availability changes are persisted as simulation state on the owning entity. Building logistics registration honors that override, so a disabled node is not silently re-enabled by a later lifecycle pass.

Edge availability changes remain authoritative in the logistics graph.

Disabling infrastructure advances the logistics network version. Existing Cargo Truck routes detect that version change and use the existing transport rerouting behavior from the last physically confirmed logistics-node anchor.

If no alternate route exists, the transport or distribution request waits or retries according to the existing lifecycle rather than teleporting cargo or bypassing the disconnected graph.

## Restoration

Restoring infrastructure makes the same graph element eligible again.

No replacement inventory, resource duplication, or scripted recovery is created. Pending demand, physical cargo, stock policies, production requirements, and battlefield supply continue from their authoritative state.

Recovery therefore emerges through the normal sequence:

`route available -> capacity available -> dispatch -> physical transport -> inventory arrival -> downstream system recovery`.

## Downstream Consequences

Because capacity and disruption act before physical delivery, downstream effects use existing systems:

- a production input inventory can starve and expose `NoInput`;
- depot reserves can deplete;
- regional automated distribution can accumulate backlog;
- Fuel and Ammunition replenishment can stop reaching Supply Depots;
- battlefield supply can degrade units through its existing Low, Critical, and Unsupplied states.

No separate shortage simulation is required.

## Diagnostics and Debug Visualization

`LogisticsCapacityDebugSnapshot` exposes presentation-safe read models for:

- node and edge effective capacity;
- scheduled load;
- utilization;
- load state;
- backlog count and quantity;
- denied capacity reservations;
- aggregate health.

The development world-debug view renders capacity state over the logistics network. Blocked and saturated infrastructure remains visually distinguishable from healthy infrastructure.

Automated distribution continues to render request flow and request state, while Cargo Transport continues to render physical transport state.

## Headless and Deterministic-Friendly Behavior

Capacity and disruption use simulation ticks, stable logistics identifiers, stable request ordering, and the existing fixed-tick command/system pipeline.

They do not depend on:

- graphics;
- UI;
- windowing;
- wall-clock decisions.

The behavior is therefore available to headless scenarios and automated tests.

## Validation

Regression coverage includes:

- real throughput limiting;
- capacity-window expiration and recovery;
- alternate routing around a saturated cheap link;
- disabled infrastructure excluded from routing;
- persistent node disable/restore;
- edge disable/restore without topology recreation;
- backlog creation under saturation;
- disconnected delivery followed by recovery after restoration;
- factory input starvation followed by production recovery;
- battlefield supply remaining Unsupplied during disconnection and recovering through the same depot and transport chain after restoration;
- source selection falling back to a routable source with available capacity;
- route churn releasing stale capacity reservations;
- capacity debug visualization.

Benchmark coverage includes capacity-aware route planning under load and repeated topology changes. Benchmark timing remains observational and is not a hard CI threshold.
