# Base Conveyor Logistics

## Status and ownership

The [construction implementation](ConveyorConstructionPlacementAndUX.md) and
[simulation implementation](ConveyorSimulationAndVehicleInterop.md) now supply paid
physical modules, directed building ports, finite inventory buffers, deterministic
junction flow and shared truck exchange. The detailed implementation document records
current rules and manual acceptance still pending. Regional Cargo Trucks continue to
use [Logistics Network and Routing](LogisticsNetworkAndRouting.md) and
[Cargo Transport Operations](CargoTransportOperations.md).

The remaining design sections below describe the wider V1 contract. Where they discuss
future controls or qualification gates, the implemented scope and concrete power,
damage, recovery, port and persistence rules in the implementation document take
precedence. A passing compilation does not qualify runtime behavior.

FORGELINE remains an RTS first. Build a production network once, then control strategic
throughput, priorities and vulnerable links instead of individual cargo items.

## Operating boundary

A **base cluster** is an explicitly connected group of owned industrial facilities,
storage and transshipment nodes. Conveyor connectivity is built and validated locally;
geometric proximity alone grants no transfer. Conveyors transfer resources only between
compatible powered or passive ports through an enabled directed path. Regional roads and
Cargo Trucks remain authoritative for distances between clusters, expansion sites,
forward depots and any resource or operation outside the conveyor-compatible contract.
Supply Trucks still reload through their existing loading bays and deliver to military
recipients. Conveyors never directly feed a battlefield unit.

The [Directorate conveyor visual kit](ConveyorVisualLanguage.md) fixes the
raised presentation dimensions, modular silhouettes and matching socket frames.
Its preproduction JSON and greybox artwork are **not** transport implementation.

The intended hybrid sequence is:

`regional Cargo Truck -> storage / logistics hub -> local conveyor ->
processor -> local conveyor -> factory / supply depot ->
regional Cargo Truck or Supply Truck -> battlefield`.

Inventory remains simulation-owned and conserved. Conveyor transport does not spawn
resources, modify production costs, bypass existing power or technology gates, or
teleport across unconnected buildings.

## Conveyable material policy

V1 admits explicitly registered **solid or packaged, inventory-backed** resources:

| Resource family | V1 eligibility | Handling |
| --- | --- | --- |
| Ferrous Ore | Yes | Bulk, standard bin |
| Silicates | Yes | Bulk, standard bin |
| Steel | Yes | Solid intermediate stock |
| Electronics | Yes | Protected packaged stock |
| Ammunition | Yes | Sealed crates; no per-caliber simulation |
| Other processed solid intermediates | Only when explicitly registered | Requires catalog declaration and compatible port |
| Fuel, Volatiles and other fluid/gaseous stocks | No in V1 | Remain road/cargo; future pipelines require their own design |
| Vehicles, personnel, completed units | No | Not aggregate conveyor inventory |
| Oversize/special loads | No by default | Regional/special transport |

**Catalog rule:** eligibility is by stable ResourceId and transport class, not
inferred from a display string, recipe, volume or model appearance. Unknown IDs fail
closed. A change to the resource catalog must explicitly qualify new materials.
Do not silently treat all inventory resources as belt-compatible.

## V1 component kit

The initial kit is deliberately bounded. A segment's world-space footprint and
support placement must be validated independently from its directed flow links.

| Part | Ports | Function | Priority |
| --- | --- | --- | --- |
| Short straight | 1 in, 1 out | 4 m routing increment | Required |
| Standard straight | 1 in, 1 out | 8 m routing increment | Required |
| 90Â° elbow | 1 in, 1 out | Uniform right-angle corner, left/right mirrored | Required |
| Height transition | 1 in, 1 out | Authored rise/fall between compatible elevation tiers | Required |
| Merger 2:1 | 2 in, 1 out | Deterministic fair arbitration | Required |
| Splitter 1:2 | 1 in, 2 out | Configured priority or fair distribution | Required |
| Building input adapter | 1 in, 1 building input | Explicit receiving connection | Required |
| Building output adapter | 1 building output, 1 out | Explicit dispatch connection | Required |
| Elevated support | Structural only | Intermittent column/beam, ground obstruction | Required |
| Dedicated high-span bridge | â€” | Optional later only if ordinary elevated kit cannot clear an obstacle | Deferred |

Choose **90-degree horizontal turns** for V1; no diagonal/45-degree free
rotation. Straight and curve pieces use a shared grid and cardinal facing.
Transitions are authored modular pieces, not arbitrary slope extrusion.

### Structural and ground rules

The design clearance target is **5 m of unobstructed vertical vehicle passage**
from traversable terrain to the underside of the moving assembly, subject to
verification against the current truck collision envelope. Top rail, housing and
belt surface are above the clearance envelope. The clearance is a proposed
acceptance target, **not** a current navigation dimension. Support columns and
low ramps occupy ground footprints and must be checked against road lanes,
building foundations, turn radii, paths and other supports. Suspended spans do
not automatically block an entire road cell. A vehicle passing under a span
must remain governed by its existing road/navigation system.

Supports require collision proxies and structural damage identity independent
of decoration. No new 3D transport physics: materials flow through aggregate
segment buffers while the renderer may interpolate visual belt motion.

## Graph, ports and transfer invariants

Keep a distinct conveyor topology owned by the headless simulation. Every
directed port has stable identity, owner/entity generation, grid coordinate,
facing, height tier, transport class and input/output role. Connecting two
ports requires geometric mating, correct direction, common eligible material
class and an enabled connection. A building may have several ports but they
must be explicitly defined by content (see
[Conveyor Port Matrix](BaseConveyorPortMatrix.md)). Building selection,
construction and demolition revalidate connectivity; no inferred across-wall
or across-building links.

At a fixed tick, calculate offered quantity from the **available/unreserved**
inventory and allowed outputs, receiving space from the destination inventory
and reservations, then admit only what the path and all buffers can accept.
Transfers use the existing authoritative inventory reservation/transfer
mechanisms, or an equivalent explicit atomic simulation transaction.
Each admitted quantity must be counted exactly once: upstream decrement plus
in-flight buffer plus downstream increment is invariant, with a documented
handling path for dismantled or destroyed occupied components. For early V1,
reject dismantling a nonempty segment until it is drained or explicitly returned
by a conservation-safe recovery operation; never discard or duplicate stock.

### Provisional deterministic capacity contract

These numbers are **design baselines requiring balance and performance
qualification**, not engine capabilities or existing gameplay constants:

| Item | Baseline |
| --- | --- |
| Standard belt nominal throughput | 100 resource units / simulated second |
| Short belt nominal throughput | Same 100 units/s, different geometry |
| Per-8 m segment buffer | 200 resource units aggregate |
| Per-4 m segment buffer | 100 resource units aggregate |
| Elbow or transition buffer | 200 resource units aggregate |
| Merger/splitter output limit | 100 resource units/s total |
| Flow calculation | Fixed-tick, deterministic fractional credit accumulator |
| Credit carry | Bounded; no burst beyond output/buffer capacity |
| Fairness | Stable port IDs and persisted round-robin cursor |
| Material sequencing | Stable resource ID within a selected input, no inventory-map iteration dependence |

At a 20 Hz simulation configuration, a 100 units/s component earns
5 units/tick. This is an illustrative calculation, not a separate hard-coded
clock: use actual fixed-tick duration so later tick-rate changes do not alter
units per simulated second. Junctions cannot multiply throughput. Multiple
sources feeding a single merger share its finite output, and a splitter's
two outputs share its finite input. No upstream consumption occurs unless the
downstream edge can accept transfer. Full buffers propagate backpressure;
saturated edges stop cleanly and resume deterministically as space returns.
No overflowing, negative, NaN or infinite stock is permitted.

Default merge mode: strict round-robin across eligible inputs with stable
port-ID tie-breaking. Default split mode: round-robin among eligible outputs;
player-configured preferred-output policy may consume available capacity first
but cannot starve a nonpreferred output indefinitely unless the player
explicitly selects exclusive routing. Filtering by resource class remains
a later capability; the V1 splitter only chooses a valid eligible destination.

### Operating states and diagnostics

Expose copied, owner-authorized statuses: **Disconnected, Idle, Flowing,
Congested, Blocked, Unpowered, Damaged, Destroyed**. Distinguish zero supply
from no destination and from downstream full. Capture at minimum:
source/destination port and resource, nominal/actual throughput, buffered
quantity/capacity, link availability, demand, power condition, block reason
and selected-path identity. Selected and relevant hovered objects show a
directional flow overlay and concise diagnostic, not a permanent full-world
billboard field. Debug/performance views may include segment count, transfer
count, longest blocked chain and fixed-tick cost. Visibility respects existing
faction intelligence and selection permissions.

## Power and strategic impact

Powered drives/active junctions require the existing logical power allocation;
passive supports do not. Exact wattage belongs to an explicit balancing pass
before implementing content. Underpowered drives stop movement but preserve
contents. Damaged components may reduce throughput when validated by a
concrete damage rule; destroyed links stop transfer and invalidate affected
paths. Graph updates use stable generations and a bounded rebuild/invalidation
strategy; no stale route may deliver across a removed or hostile segment.
Repairs and restoration restore connectivity only after valid simulation state
reconstruction.

Construction must consume physical resources from existing authorized sources
and use normal placement and command validation. No universal money, free
infrastructure, or special player-only simulation privilege.

## Hybrid migration and compatibility

1. **Contracts first:** qualify material classes, ports, parts, throughput
   invariants, collision clearance and failure rules without changing current
   regional dispatch or savegame representations.
2. **Parallel optional paths:** introduce simulation-owned conveyor graph and
   headless validation. Keep Cargo Truck routes fully functional. Do not remove
   established road edges or infer automatic belt links.
3. **Placement and assets:** add raised kit source masters, authored sockets,
   collision/support footprints, runtime compilation and a dedicated
   infrastructure placement mode. Verify roads remain passable below spans.
4. **Transshipment:** connect owned local inventories through explicit port
   adapters. Preserve actual Cargo Truck unloading at storage/hub and Supply
   Truck loading at depots; no vehicle becomes a belt entity.
5. **Dispatcher selection:** once conservation, capacity and diagnostics pass,
   prefer valid local conveyor paths **only for eligible resources and within
   the same explicitly connected base**. Outside routes and failures retain
   current vehicle behavior; do not silently duplicate the same demand on both.
6. **Scenario rollout:** add a demonstrable starter-base chain to Central Divide
   behind a controlled content/feature transition. Validate production,
   disruption, loss/recovery, save/load, headless replay and terminal match
   before considering any legacy route removal.
7. **Player onboarding:** teach ports, direction, routing, congestion, vehicle
   handoff and repair with existing contextual HUD/Operations surfaces.

Existing savegames and policy data are not auto-migrated merely by adding
this design; a later implementation that persists graph or in-flight stock
requires an explicit versioned save/replay contract and compatibility tests.

## Qualification requirements for implementation

- Resource whitelist accepts declared solids and rejects Fuel, units and unknown IDs.
- Ports reject incorrect facing, height, direction, owner or generation.
- The same input sequence yields identical merger/splitter decisions and buffers.
- Every tick conserves resources across inventories, reservations and in-flight stock.
- Congestion, unplug, power loss, destruction and restoration have deterministic
  bounded recovery; no phantom transfer or deadlock caused by stale paths.
- Cargo Truck -> store -> conveyor -> factory -> conveyor -> supply depot
  correctly preserves all physical inventory and transport boundaries.
- Vehicles keep full navigation under clear spans; columns/ramps alone obstruct.
- Selected overlays respect visibility, remain legible at RTS/strategic zoom
  and incur bounded rendering cost.
- Existing headless, navigation, logistics, production, supply and full-match
  CI gates remain unchanged.

## Deferred implementation ownership

Implementation should be separated into (a) stable content/port and transfer
contracts, (b) simulation-owned conveyor topology and bounded throughput,
(c) placement/collision/navigation, (d) asset masters and animations,
(e) inventory/transshipment and dispatcher integration, and (f) UI/read-model,
scenario and end-to-end qualification. Each can begin independently where
its declared contracts allow; cross-system integration remains serial.
