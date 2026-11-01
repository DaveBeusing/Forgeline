# Base Conveyor Logistics

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Status and ownership](#status-and-ownership)
- [Operating boundary](#operating-boundary)
- [Conveyable material policy](#conveyable-material-policy)
- [V1 component kit](#v1-component-kit)
  - [Structural and ground rules](#structural-and-ground-rules)
- [Graph, ports and transfer invariants](#graph-ports-and-transfer-invariants)
  - [Provisional deterministic capacity contract](#provisional-deterministic-capacity-contract)
  - [Operating states and diagnostics](#operating-states-and-diagnostics)
- [Power and strategic impact](#power-and-strategic-impact)
- [Hybrid integration and compatibility](#hybrid-integration-and-compatibility)
- [Qualification requirements for implementation](#qualification-requirements-for-implementation)
- [Ownership boundaries](#ownership-boundaries)

</details>

## Status and ownership

The [construction implementation](ConveyorConstructionPlacementAndUX.md) and
[simulation implementation](ConveyorSimulationAndVehicleInterop.md) now supply paid
physical modules, directed building ports, finite inventory buffers, deterministic
junction flow and shared truck exchange. The detailed implementation document records
current rules, executed material/recovery/persistence contracts and the remaining
native acceptance boundaries. Regional Cargo Trucks continue to
use [Logistics Network and Routing](LogisticsNetworkAndRouting.md) and
[Cargo Transport Operations](CargoTransportOperations.md).

The sections below distinguish implemented rules from deferred controls. The detailed
implementation document owns concrete power, damage, recovery, port and persistence
rules and the executed evidence matrix. Native visual and reference-hardware
acceptance still require their own evidence.

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

The implemented hybrid sequence is:

`regional Cargo Truck -> storage / logistics hub -> local conveyor ->
processor -> local conveyor -> factory / supply depot ->
regional Cargo Truck or Supply Truck -> battlefield`.

Inventory remains simulation-owned and conserved. Conveyor transport does not spawn
resources, modify production costs, bypass existing power or technology gates, or
teleport across unconnected buildings.

## Conveyable material policy

Current local belts admit explicitly registered inventory-backed resources:
the original solids/packages plus two interim refinery fluids until pipelines exist.

| Resource family | V1 eligibility | Handling |
| --- | --- | --- |
| Ferrous Ore | Yes | Bulk, standard bin |
| Silicates | Yes | Bulk, standard bin |
| Steel | Yes | Solid intermediate stock |
| Electronics | Yes | Protected packaged stock |
| Ammunition | Yes | Sealed crates; no per-caliber simulation |
| Other processed solid intermediates | Only when explicitly registered | Requires catalog declaration and compatible port |
| Volatiles | Yes, interim | Actual Refinery ingredient via compatible local powered conveyors; regional Cargo Trucks remain available |
| Fuel | Yes, interim | Actual Refinery output toward compatible owned storage; never the ammunition-only Supply Depot belt input |
| Other fluid/gaseous stocks | No by default | Pipeline mechanics are not yet implemented |
| Vehicles, personnel, completed units | No | Not aggregate conveyor inventory |
| Oversize/special loads | No by default | Regional/special transport |

**Catalog rule:** eligibility uses stable ResourceId values and the explicit seven-ID
conveyor whitelist. It is not inferred from a display string, recipe, volume or model
appearance. Unknown IDs fail closed. A change to the resource catalog must explicitly qualify new materials.
Do not silently treat all inventory resources as belt-compatible.

## V1 component kit

The initial kit is deliberately bounded. A segment's world-space footprint and
support placement must be validated independently from its directed flow links.

| Part | Ports | Function | Priority |
| --- | --- | --- | --- |
| Short straight | 1 in, 1 out | 4 m routing increment | Required |
| Standard straight | 1 in, 1 out | 8 m routing increment | Required |
| 90-degree elbow | 1 in, 1 out | Uniform right-angle corner, left/right mirrored | Required |
| Height transition | 1 in, 1 out | Authored rise/fall between compatible elevation tiers | Required |
| Merger 2:1 | 2 in, 1 out | Deterministic fair arbitration | Required |
| Splitter 1:2 | 1 in, 2 out | Fair round-robin distribution; configured priorities deferred | Required |
| Building input adapter | 1 in, 1 building input | Explicit receiving connection | Required |
| Building output adapter | 1 building output, 1 out | Explicit dispatch connection | Required |
| Elevated support | Structural only | Intermittent column/beam, ground obstruction | Required |
| Dedicated high-span bridge | None defined | Optional later only if ordinary elevated kit cannot clear an obstacle | Deferred |

Choose **90-degree horizontal turns** for V1; no diagonal/45-degree free
rotation. Straight and curve pieces use a shared grid and cardinal facing.
Transitions are authored modular pieces, not arbitrary slope extrusion.

### Structural and ground rules

Placement enforces **5 m of vertical clearance** above sampled terrain. Current
accepted standard spans and ramps have a minimum 5.2 m underside; low transitions
are rejected. Navigation uses actual support footprints instead of projecting the
whole raised carrier onto the ground. Movement steering, penetration, swept motion
and dock reservation compare the actual vehicle top with the carrier underside.
Only a typed non-support conveyor piece can receive this clearance treatment.
Columns, ordinary obstacles and vehicles too tall for the span still block passage.
Headless navigation/docking contracts cover these paths; physical driving and visual
clearance remain native acceptance cases.

Supports require collision proxies and structural damage identity independent
of decoration. No new 3D transport physics: materials flow through aggregate
segment buffers while the renderer may interpolate visual belt motion.

## Graph, ports and transfer invariants

Keep a distinct conveyor topology owned by the headless simulation. Every
directed port has stable identity, owner/entity generation, position,
facing, height tier, resource filters and input/output role. Connecting two
ports requires geometric mating, correct direction and common ownership. Material
admission also requires a compatible reachable input. A building may have several ports but they
must be explicitly defined by content (see
[Conveyor Port Matrix](BaseConveyorPortMatrix.md)). Building selection,
construction and demolition revalidate connectivity; no inferred across-wall
or across-building links.

At a fixed tick, calculate offered quantity from the **available/unreserved**
inventory and allowed outputs, receiving space from the destination inventory
and reservations, then admit only what the path and all buffers can accept.
Transfers use the existing authoritative inventory reservation/transfer
mechanisms, or an equivalent explicit atomic simulation transaction.
Each transferred quantity is counted once across source, carrier and destination
inventories. Snapshot availability prevents incoming goods from leaving another
segment in the same tick. Remove and Replace reject occupied buffers, including
reservations. A destroyed carrier retains its cargo and identity for paid repair;
it must be reconnected and drained before removal. No cargo becomes a construction
refund and no removed empty buffer leaves an orphan inventory.

### Provisional deterministic capacity contract

These are the **implemented provisional constants**. Balance and physical hardware
qualification remain separate from the deterministic behavior tests:

| Item | Baseline |
| --- | --- |
| Standard belt nominal throughput | 100 resource units / simulated second |
| Short belt nominal throughput | Same 100 units/s, different geometry |
| Per-8 m segment buffer | 200 resource units aggregate |
| Per-4 m segment buffer | 100 resource units aggregate |
| Elbow or transition buffer | 200 resource units aggregate |
| Merger/splitter output limit | 100 resource units/s total |
| Flow calculation | Actual fixed-tick duration times rate, power and health fractions |
| Credit carry | None across offline ticks; fractional stock remains in inventory |
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

Merge mode is round-robin across eligible inputs with stable port ordering. Split
mode is round-robin among eligible outputs. Equal sub-unit transfer quanta avoid a
recurring fractional-tail bias at rates such as 30 Hz. Full or unavailable branches
are skipped without multiplying the shared budget. Player priorities, exclusive
routing and configurable resource filters remain deferred; the current splitter
chooses only destinations with compatible reachable building inputs.

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

Carriers draw 2 power and active junctions 4 from the construction payer's existing
network at Industrial priority; supports have no consumer. Brownout scales flow by
the allocated fraction, while zero allocation stops it. Below 70% integrity,
throughput also scales by health. Destroyed links and destroyed supporting feet stop
the affected path while retaining cargo. Repair restores the same carrier inventory
and entity. Topology is rebuilt from current full-generation identities each tick;
no stale route may deliver across a removed or hostile segment. Recovery creates no
accumulated offline throughput credit.

Construction must consume physical resources from existing authorized sources
and use normal placement and command validation. No universal money, free
infrastructure, or special player-only simulation privilege.

## Hybrid integration and compatibility

Local conveyor paths remain optional and explicitly built. They create no regional
road edges and do not replace Cargo Truck routes. Storage/Hub/Depot inventories are
the shared handoff, with actual dock loading and unloading. Stock targets bound belt
receipt by current inventory and the remaining accepted Assigned/InTransit truckload;
pending dispatch demand is reconciled with later real receipts.

The normal Gameplay-start test pays for its buildings, bands and supports from
Standard stock, imports Ore with an existing Cargo Truck, runs original Smelter
recipes and receives Steel in storage. Existing scenarios receive no automatic band
network. Player onboarding and new priority controls are separate future work; the
natural terminal-match gate and native interaction acceptance remain required.

Current authoritative state schema 5 persists buffer inventories, reservations,
cursors, active time and attachments. Save/replay payload schema 6 uses envelope
format 1. Existing construction commands reconstruct the network; derived topology
is rebuilt. Old incompatible state is explicitly rejected. The reliability fixes
add no stored state and do not change those versions. Save/replay tests compare full
authority hashes immediately and after subsequent ticks through damage and edits.

## Qualification requirements for implementation

- Resource whitelist accepts declared solids and rejects Fuel, units and unknown IDs.
- Ports reject incorrect facing, height, direction, owner or generation.
- The same input sequence yields identical merger/splitter decisions and buffers.
- Transfers conserve resources across all inventories; recipe input/output commits,
  extraction, construction and unit consumption reconcile separately per resource.
- Congestion, unplug, power loss, destruction and restoration have deterministic
  bounded recovery; no phantom transfer or deadlock caused by stale paths.
- Cargo Truck -> store -> conveyor -> factory -> conveyor -> supply depot
  correctly preserves all physical inventory and transport boundaries.
- Vehicles navigate beneath spans and ramps with sufficient clearance; real supports
  and insufficient-clearance geometry remain blocking.
- Selected overlays respect visibility, remain legible at RTS/strategic zoom
  and incur bounded rendering cost.
- Existing headless, navigation, logistics, production, supply and full-match
  CI gates remain unchanged.

## Ownership boundaries

Content defines parts and sockets; the simulation owns topology, finite inventory,
transfer and lifecycle. Construction owns paid placement and edit validation.
Navigation and docking retain vehicle motion authority. Assets and copied read models
describe completed state without creating material or a second simulation. Building
destruction cleanup remains with its existing lifecycle owner, including invalidating
ports and inventories; a conveyor cannot silently adopt a reused building index.
