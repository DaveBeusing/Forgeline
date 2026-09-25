# Cargo Transport Operations

## Purpose

Physical cargo transport connects aggregate inventories through visible ground transport vehicles without turning individual resource units into world entities.

Cargo remains an aggregate quantity inside a Cargo Truck inventory. Resources move between distant economic inventories only through the sequence:

1. travel to an origin logistics node;
2. load by an atomic inventory transfer into the truck;
3. follow the selected logistics route through authoritative ground navigation;
4. arrive at the destination;
5. unload by atomic inventory transfer into the destination inventory.

The transport lifecycle runs in the fixed-tick simulation and does not depend on rendering, UI, audio, or wall-clock time.

## Cargo Truck Definition

CargoTruckDefinition describes the initial ground cargo vehicle.

The default definition uses the stable key:

`unit.cargo_truck`

It defines:

- aggregate cargo capacity;
- authoritative GroundMovement parameters;
- visual scale and visual identity metadata.

CargoTruckFactory creates the simulation entity with:

- WorldTransform;
- CargoTransport;
- InventoryStorage for the dedicated cargo inventory;
- GroundMovement and GroundMovementState;
- a wheeled NavigationAgent;
- CargoTransportRuntimeState;
- controllable logistics metadata;
- spatial presence;
- visual identity.

The cargo inventory is created through the shared InventoryStore. Transport storage therefore uses the same capacity, resource, transfer, and conservation semantics as economic storage.

## Transport Orders

CargoTransportOrder contains:

- origin logistics node;
- destination logistics node;
- resource ID;
- requested quantity;
- submission tick;
- partial-load policy.

The initial implementation supports one active cargo order per transport.

Automated distribution and dispatch optimization are intentionally separate. A later dispatcher can schedule and reuse Cargo Trucks by assigning these transport orders without bypassing the physical lifecycle.

## Lifecycle

Cargo transport uses explicit states:

### Idle

The transport has no active cargo order.

### ToOrigin

The truck moves to the order's origin through the existing MovementOrder and hierarchical navigation stack.

No cargo is transferred while the truck is still travelling.

### Loading

The source inventory is resolved from the logistics node's authoritative economic entity.

Loading uses InventoryStore.Transfer from the source inventory into the truck cargo inventory. The operation is atomic.

### ToDestination

The logistics graph selects an ordered LogisticsRoute.

Each logistics route segment is translated into a physical movement target at the segment's destination node. The existing hierarchical navigation system and GroundMovementSystem remain authoritative for actual movement and transform mutation.

The transport advances its logistics-route anchor only after physically reaching the corresponding node.

### Unloading

The destination inventory is resolved from the destination economic entity.

Unloading uses InventoryStore.Transfer from the truck cargo inventory into the destination inventory. The transfer is atomic and limited by current destination capacity.

### Waiting

Waiting is an explicit non-failure state.

Current reasons include:

- origin unavailable;
- origin resource unavailable;
- route unavailable;
- route invalidated while in transit;
- destination unavailable;
- destination capacity unavailable.

The system re-evaluates the condition deterministically on subsequent fixed ticks.

### Failed

A transport enters Failed only for conditions that cannot be handled as ordinary waiting or rerouting, including invalid origin/destination contracts, missing inventories, invalid route anchors, transfer invariant failures, or authoritative navigation failure.

Failure state remains visible through diagnostics instead of silently discarding the order.

## Partial-Load Policy

Two policies are supported.

### AllowPartial

The truck loads the minimum of:

- requested quantity;
- source available quantity;
- cargo inventory remaining capacity.

A positive partial quantity is allowed to depart and complete the order.

### RequireRequestedQuantity

The complete requested quantity must be available at the origin and fit inside the truck cargo inventory before loading begins.

If the source does not currently contain enough unreserved quantity, the truck waits at the origin.

A request larger than the truck's capacity fails rather than being silently truncated.

## Origin and Destination Inventory Resolution

Cargo sources resolve inventories in this order when applicable:

1. ProductionFacility output inventory;
2. InventoryStorage;
3. ResourceExtractor output storage.

Cargo destinations resolve:

1. ProductionFacility input inventory;
2. InventoryStorage.

The logistics node capabilities remain authoritative for whether a node is a valid cargo source or destination.

## Route Integration

The logistics graph and navigation system have separate responsibilities.

LogisticsNetwork determines the strategic transport path between economic nodes.

HierarchicalNavigationSystem converts each selected node target into traversable ground navigation.

GroundMovementSystem remains the only system that mutates ground-vehicle transforms.

CargoTransportSystem never writes a vehicle position directly.

## Route Versioning and Rerouting

A LogisticsRoute is valid only for the LogisticsNetworkVersion under which it was calculated.

If the graph version changes during transit:

1. the current logistics route is rejected;
2. current navigation state is cleared;
3. the transport returns to its last physically confirmed logistics-node anchor when necessary;
4. a new route is requested from that anchor to the original destination;
5. the transport waits if no route currently exists.

This prevents stale route state from causing cargo teleportation or advancing the route without physical movement.

## Destination Capacity

Destination capacity is checked at unload time.

If only part of the carried quantity fits, that quantity is atomically unloaded and the remaining cargo stays inside the truck.

If no quantity fits, the transport waits at the destination.

When capacity becomes available, unloading continues from the cargo inventory without reloading or recreating resources.

## Vehicle Destruction and Cargo Loss

Cargo carried by a destroyed Cargo Truck is lost.

The transport system tracks the dedicated cargo inventory belonging to each managed transport. When the entity is no longer alive:

- the remaining cargo quantity is added to the lost-cargo diagnostic total;
- the dedicated cargo inventory is destroyed;
- no cargo is transferred back to the origin or forward to the destination.

This produces deterministic loss semantics and prevents stale transport inventories from duplicating resources.

Future salvage or cargo-drop gameplay can replace this policy deliberately without changing the fundamental conservation boundary.

## Resource Conservation

The hard invariant is:

`source + cargo in transit + destination + accounted loss`

must not increase as a result of transport execution.

Loading and unloading use the shared atomic InventoryStore.Transfer operation. There is no distant source-to-destination transfer path inside the cargo transport system.

Development builds retain the inventory subsystem's invariant checks for invalid or non-finite quantities.

## Diagnostics

CargoTransportMetrics exposes:

- total managed transports;
- active transports;
- waiting transports;
- failed transports;
- cargo quantity currently in transit;
- cumulative delivered quantity;
- cumulative lost quantity;
- completed order count;
- reroute count;
- route failure count.

CargoTransportDebugSnapshot exposes presentation-safe per-transport read models containing state, wait/failure reason, resource and quantity, route anchors/version, position, and current movement target.

The development F2 world-debug path renders transport positions, movement targets, lifecycle state, and current cargo quantity.

Presentation consumes these read models and never owns or mutates transport simulation state.

## Validation

Headless tests cover:

- complete source-to-truck-to-destination transport;
- exact resource conservation;
- partial loading;
- full-load waiting and recovery;
- destination-capacity blocking and recovery;
- route invalidation and alternate-route recovery;
- destruction of a loaded transport;
- bounded operation with hundreds of simultaneous transports.

CargoTransportBenchmarks measures bounded 100- and 500-transport physical delivery batches through the fixed-tick movement/navigation and transport lifecycle.

Benchmark timing remains measurement evidence rather than a hardware-dependent correctness gate.

## Current Boundary

The current implementation deliberately does not include:

- automated dispatch optimization;
- battlefield resupply;
- rail transport execution;
- convoy specialization;
- escorts;
- detailed road traffic or lane congestion;
- final Cargo Truck art or animation.

Those features may schedule, specialize, or present the existing transport lifecycle. They must not bypass the physical cargo and conservation semantics defined here.
