# Conveyor simulation and vehicle interoperability

Implementation: 0.1.453, branch `feature/conveyor-flow`. Started from master
`eb23d5ca` (conveyor construction, PR #300), then integrated current master
`d4e100e7dfdc28426d6191ab1a9cd123cc00ade4` (graphics asset bindings, PR #301).
Implementation head: `2646f9d04406a44c0a0ec44e23d9fab19bd9fd66`.
Draft PR: [#302](https://github.com/DaveBeusing/Forgeline/pull/302).
Documentation-only follow-ups are visible in that PR history; build evidence is
recorded in `ConveyorSimulationBuild.json`.

The user explicitly requested implementation and a pushed PR without tests.
No automated tests were added or executed. CI, native execution, smoke runs,
headless execution, stress runs and save/replay round trips were not performed.
All runtime and visual acceptance below remains manual and pending.

## Flow authority

`ConveyorSimulationSystem` runs in Economy after extraction, with no presentation,
Windows or rendering dependency. Constructed modules retain their entity ID,
generation, owner, geometry, health and explicit building attachment. Each active
module owns an InventoryStore inventory, restricted to the five stable resource IDs
Ferrous Ore, Silicates, Steel, Electronics and Ammunition. Fuel, Volatiles, units and
unknown resources are excluded. Supports and caps have no inventory or drive.

| Module | Total buffer | Maximum ingress and egress |
| --- | ---: | ---: |
| 4m short belt, either building adapter | 100 | 100 units/s |
| 8m standard belt, either curve, either ramp | 200 | 100 units/s |
| Merger, splitter | 100 shared across resources and ports | 100 units/s total |

Rate is multiplied by the actual fixed tick duration, allocated power fraction and
health fraction below 70% integrity. Every carrier draws 2 power, a junction 4,
on the construction payer's existing player power network, at Industrial priority.
Missing membership or zero allocation stops flow; brownout reduces throughput.
A destroyed foot stops carriers attached at that socket until repaired. Current
balance constants are provisional, not a claim of runtime tuning.

Every move uses atomic InventoryStore.Transfer: source availability excludes all
reservations and the sink enforces its real remaining shared/resource capacity.
The tick snapshots available stock per inventory before transfers. Incoming goods
cannot depart again during that tick, preventing entity-order-dependent traversal
of an entire line. Continuous quantities preserve fractional remainders, without
discarding small cargo or accumulating offline throughput credit.

Inputs, outputs and resources have persistent round-robin cursors. Each arbitration
round selects one eligible sender per receiver and one requesting output per sender;
a successful transfer advances both port cursors and the resource cursor. Quanta are
at most one unit, with fractional quanta at tick/capacity boundaries. Competing merger
inputs alternate; splitter outputs alternate; blocked/rejecting/offline branches are
skipped and the remaining branch can use the shared 100/s budget. A deterministic
bounded number of rounds covers the tick budget and two-port contention. Cursors
advance only for actual transfers, surviving idle periods and replay.

Connections require the same owner, complementary direction, opposite facing and
position/height within 0.02m. A building connection additionally requires the correct
adapter's exact building entity/generation attachment. Ambiguous coincident sockets
fail closed. Connection lookup is spatially bucketed; the graph and resource reachability
are rebuilt from authority each tick, including removal, repair and building death.
A branch receives a resource only when a matching building input is reachable.
Full/offline destinations retain that compatibility, allowing bounded backpressure.
Cycles without a compatible sink admit no new cargo. Network IDs are derived from
the minimum full entity identity in each connected component; there are no road edges.

## Activated building ports

Authored sockets are relative to the building's ground origin, rotated by its actual
pose: INPUT at `(-width/2, 5.8, 0)`, facing west; OUTPUT at `(width/2, 5.8, 0)`,
facing east. They are tier 0 and leave +Z loading bays available. The completed,
living owned building must match the exact dimensions below; unsupported content
and scale variants do not acquire automatic ports.

| Building | Expected width,height,depth | Input | Output |
| --- | --- | --- | --- |
| Command Core | 20,12,20 | Eligible solids/packages | Same, respecting retained stock |
| Extractor | 12,8,12 | None | Its actual eligible extracted resource |
| Storage Depot | 14,8,14 | Eligible solids/packages | Same |
| Smelter | 18,10,16 | Actual supported recipe ingredients: Ferrous Ore | Actual completed recipe outputs: Steel |
| Electronics Fabricator | 18,10,18 | Actual recipe: Silicates | Actual recipe: Electronics |
| Ammunition Plant | 18,10,18 | Actual recipe: Steel, Electronics | Actual recipe: Ammunition |
| Logistics Hub | 18,9,18 | Eligible solids/packages | Same |
| Supply Depot | 16,8,16 | Ammunition | Ammunition |
| Barracks | 18,8,16 | Eligible costs of supported unit recipes | None |
| Vehicle Factory | 26,12,24 | Eligible costs of supported unit recipes | None |

Production input and output ports bind distinct ProductionFacility inventories;
unit facilities bind their real input inventory. Extractors bind their actual output
InventoryStorage and require the matching extractor owner/resource. Storage, hub and
supply ports use the existing authoritative shared inventories. No proxy stock,
recipe completion shortcut, invisible external shipment or synthetic unit cargo exists.
The current Refinery only uses Volatiles/Fuel, so has no belt ports. Power Plant and
Radar also have none. New port geometry requires an explicit content update.

## Trucks, stock policy and recovery

The explicit hybrid handoff is the existing Storage Depot, Logistics Hub or Supply
Depot inventory. Cargo Trucks bring regional goods through normal docks; local
conveyors consume available unreserved stock. Ammunition delivered by belt can be
loaded into Supply Trucks through the normal depot system. Fuel and regional/front
routes continue through existing vehicles, loading queues, reservations and road rules.

Enabled stock policies retain their DesiredTarget in shared source inventories.
At a destination with a policy, conveyor receipt is limited to DesiredTarget minus
current stock minus already committed Assigned/InTransit truck quantities. Pending
truck demand is reconciled against subsequent actual receipts by the existing
scheduler. The belt creates no second logistics request. In-flight belt stock is a
finite upstream buffer, not a promise of destination delivery; a truck may cover a
shortage while the belt waits. A partly unloaded truck's whole committed request is
conservatively retained until completion. Without a stock policy, actual capacity is
the limit. Production's separate output inventory does not retain its input targets.

A full/rejecting/downstream-offline sink stalls the immediate buffer, then bounded
upstream buffers. Freeing stock or repairing/reconnecting the route resumes service
without an offline catch-up burst. Remove and Replace recheck actual inventory stock
and reservations and refuse occupied parts; empty buffer inventories are destroyed
with their removed module. No cargo is refunded as construction Steel.

Combat retains a destroyed conveyor entity, health-zero wreck, buffer and footprint.
A wreck stops transfer and its power consumer is disabled. Repair works with cargo
present: nonfatal repairs cost 4 Steel; a wreck costs its original paid module Steel.
It restores health and drive, retaining the same inventory and entity. Reconnect and
drain before removing an occupied wreck. Destroyed support feet remain real ground
obstacles until repaired or safely removed, avoiding lost stock or invisible clearance.

## Vehicle clearance and presentation

The existing StrategicInfrastructureSystem navigation rebuild excludes carrier AABBs
and includes actual support footprints. Current intact standard spans and ramps have
a minimum 5.2m underside. Placement samples terrain and requires 5m clearance; an
insufficiently high ramp/transition is rejected, rather than projected onto the ground
or allowed to clip a vehicle. Feet remain excluded from road corridors and loading
lanes. This package reuses those authored raised geometry rules; it adds no low ramp
or fallen span that would require a new ground blocker shape.

Owned completed-tick snapshots copy buffer resources, ingress/egress, capacity,
connection entity IDs, network identity, active time, health/power status and causes.
Flowing, idle/no-input, disconnected, blocked, congested, offline, damaged, critical
and destroyed select existing kit state meshes/materials. Conveyor mechanical clips
use simulation ActiveSeconds; stopped ticks do not advance them. Foreign topology,
buffers, throughput and private animation phase are not published.

Hover shows rates, cargo, connections and causes; Operations exposes buffers, power,
nominal capacity, measured utilization and local links within existing list bounds.
Selecting or hovering an owned carrier adds a bounded box, output links and rate/
buffer label from the snapshot. Headless match report schema 3 includes the same
conveyor facts for all match participants for offline diagnosis.

## Persistence and manual acceptance

Authoritative state schema is 5, save/replay envelope schema 6. Buffer inventory IDs,
stock/reservations, cursors, active time, measured state and attachments are included
in the existing authoritative ECS/inventory snapshot and hash. Existing recorded
ConveyorConstruction commands rebuild that state through deterministic replay;
derived topology and UI caches carry no authority. Older authoritative checkpoints
and replays are explicitly rejected as incompatible; there is no guessed migration
of pre-flow cargo. Manual save/replay qualification remains pending.

Compile-only evidence covers Release Client/dependencies and Headless. This does
not establish conservation, fairness, replay identity, visual quality or navigation
acceptance. Manually exercise:

1. Extractor Ferrous Ore to Smelter to storage; Silicates to Electronics; Steel and
   Electronics through a merger to Ammunition Plant, then depot and Supply Truck.
2. Two saturated inputs and two outputs: observe round robin, total 100/s, one blocked
   branch taking zero, and full use of the open branch without starvation.
3. Fill every downstream inventory, observe finite backpressure, drain and resume.
   Count source + all buffers + destination, including mixed/fractional resources.
4. Reserve source stock for production/trucks; concurrently dispatch a committed truck
   and belt delivery to a policy target. Check reservations and target accounting.
5. Power loss, brownout, damage, foot destruction, occupied wreck repair, removal and
   replacement. Check cargo conservation and no stock refund or entity-generation alias.
6. Drive real Cargo/Supply Trucks and combat vehicles below standard/raised/ramp spans,
   beside real feet and through dock lanes, before and after edits or foot destruction.
7. Save/load and replay identical command histories, comparing full authoritative hashes,
   inventory totals and cursors. Repeat with a disconnected cycle and a large base.
8. Inspect owner/fog isolation, selected links, Operations bounds, flowing/stalled clips,
   state materials, LODs, reduced motion and camera readability in the native client.
