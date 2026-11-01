# Conveyor construction, placement and UX

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Draw route connects a source building to a destination building without manually
placing either adapter. Eligible buildings expose their actual directed ports;
the construction service owns route geometry, placement validation and payment.
[Conveyor simulation and vehicle interoperability](ConveyorSimulationAndVehicleInterop.md)
defines finite inventory flow, resource restrictions, drive power and recovery.

The original fixed-grid construction implementation was introduced from master
`d7c45792` (0.1.450). Its recorded commands retain their original planner. New
interactive routes explicitly select automatic directed routing so freely placed
buildings no longer need matching horizontal 4m grid phases.

<details>
<summary>On this page</summary>

- [V1 interaction and layout](#v1-interaction-and-layout)
  - [Shared placement grid and crossing belts](#shared-placement-grid-and-crossing-belts)
  - [Connection feedback and snapping](#connection-feedback-and-snapping)
- [Deterministic geometry decisions](#deterministic-geometry-decisions)
  - [Automatic terrain height](#automatic-terrain-height)
  - [Shared routing rules](#shared-routing-rules)
- [Building ports and connectivity](#building-ports-and-connectivity)
- [Paid edits and command flow](#paid-edits-and-command-flow)
- [Automated acceptance](#automated-acceptance)
- [Manual acceptance checklist, not executed](#manual-acceptance-checklist-not-executed)
- [Connections in paid construction templates](#connections-in-paid-construction-templates)

</details>

## V1 interaction and layout

Open the existing Build menu and select **Conveyor / Draw route**, or select a
specific short/standard belt, curve hand, ramp, merger, splitter, I/O adapter,
end cap, support or **Conveyor overpass**. These entries use menu-only IDs starting at 2000, intercepted
before building placement; they do not masquerade as building definitions or go
through SubmitBuild. The existing paginated Build menu handles the longer list.

| Gesture while conveyor placement owns input | Behavior |
| --- | --- |
| first click on a source building in Draw route | choose its output and anchor the route; no command or cost yet |
| first click on a free belt output or ground | extend that output or start a standalone route |
| pointer movement | current-tick authoritative plan, part cages and directional IN/OUT arrows |
| second click on a destination building, belt input or ground | choose the input automatically and request the complete route only when its preview is current and valid |
| click with a special module selected | request that individual module and automatic feet |
| Q / E | rotate special modules by 90 degrees |
| Tab | change the preferred bend order for equivalent automatic routes |
| T | select carrier tier 0 or 1; endpoint snapping adopts the starting socket tier |
| V | cycle Build → Remove → Replace → Repair; point at the owned part to edit |
| Escape / right click | cancel the unsubmitted plan and leave placement |

### Shared placement grid and crossing belts

Building and conveyor pointer placement share a visible **4m grid**. The local
48m-wide grid follows the terrain around the current placement; it disappears
when placement ends. Buildings sample their foundation at the snapped position.
Exact compatible sockets take priority over the grid, so older freely placed
buildings and saved layouts remain connectable. Existing scripted construction
and recorded commands retain their original requested coordinates.

Select **Conveyor overpass** to cross a lower belt without joining its flow.
The pointer marks the crossing center; Q/E rotates its travel direction. One
confirmation builds a 24m assembly: an 8m rising ramp, an 8m upper straight, an
8m descending ramp and four automatic feet. The raised deck is 2m above the lower
deck. Its feet stand outside the center crossing. Both endpoints return to tier 0;
the assembly cannot start at tier 1. Normal terrain, building, loading-lane,
road/support, ownership and stock checks still apply to every part.

The quote is **66 Steel** on flat free ground; an existing owned endpoint foot
can reduce it. Construction pays and commits the complete assembly together.
It uses the existing authored ramp, straight and support meshes. The saved world
contains those ordinary pieces, so selection, damage, repair, individual removal,
finite buffers and save/replay follow the same rules as manually built ramps.
A crossing at the same height still fails the collision check. The two decks
connect only through their own directed sockets and never mix cargo at the crossing.

### Connection feedback and snapping

The pointer catches compatible building and conveyor sockets within 28 physical
pixels at UI scale 1 (scaled with the UI). A nearby socket takes priority over
the fallback of clicking a building body. A white ring and **INPUT SNAPPED** or
**OUTPUT SNAPPED** identify the actual selected connection before confirmation.
Routes retain the exact socket position and departure/arrival direction.

Individual carriers, curves, branches, ramps and end caps also align to existing
conveyor outputs. Output adapters align to inputs; input/output adapters can snap
to the corresponding real building port. Rotation and height come from opposing
sockets, including the high end of ramps. Supports remain ground placements.
The command records `SnapToSocket` only for these exact single-part placements;
the construction service rechecks a currently owned, non-destroyed mate, terrain,
obstructions, supports and stock before charging. This does not bypass placement
rules or create a connection by proximity alone. Legacy commands omit the flag.

Construction and selection display cyan **IN** and amber **OUT** markers on
existing belts as well as buildings. Arrows use the actual flow direction and
larger 3m stems. Gameplay world labels are drawn by the normal HUD independently
of the development overlay, with readable backgrounds, overlap avoidance and a
64-label screen budget. Building and belt socket geometry each have a separate
64-port budget; offscreen ports do not consume it.

The conveyor hint panel reserves two rows for the current instruction or failure
and a separate row for controls. Changing the preview does not fade this panel
out. After choosing an output, the target phase starts only once the pointer
moves beyond a small 4px UI-scaled dead zone; a nearby input cannot immediately
take over the unchanged pointer. It carries the current session and tick so the
normal HUD freshness checks accept it. Rejected previews distinguish a building,
another belt, a vehicle/object and a loading lane, mark the failing part and offer
a corrective action. Missing stock shows required and available Steel. A rejected
click retains the preview and does not submit or pay for construction.

The hint bar names the mode, variant, tier and next click. Its placement context
shows validity/reason and exact Steel cost from the copied preview. World-space
cages use green/red plus text and IN/OUT arrows, so color is not the only cue.
The ghost is a structural wire preview rather than another set of solid material
meshes. Support cages show the actual ground footprint; carrier cages remain
elevated. Built instances use the existing authored meshes and PBR atlas, with
LOD0/1/2 at the existing 140/420m handoff distances.

During conveyor construction, visible owned buildings show **IN** and **OUT**
markers at their real fixed-purpose sockets. The input arrow points toward the
building and the output arrow points away. Command Core and Storage Depot
instead expose four physical **IN / OUT** positions each. An unused position
has one neutral marker; once attached, its marker resolves to **CONNECTED IN**
or **CONNECTED OUT**. Malformed double or misaligned attachments show **PORT
BLOCKED** and cannot be snapped to. This status comes from live, full-generation
adapter attachments, not decorative geometry. Labels and direction distinguish
roles without relying on color. Outside construction the selected or hovered
building retains its port markers. At most 64 building ports are drawn; focused
buildings have priority. Offscreen ports do not consume that budget. Unsupported
buildings do not acquire decorative ports.

The world-selection and tactical controllers yield to placement. Existing modal,
pause, focus, resize, session and HUD-pointer rules remain active. Pointer capture
clears the preview and never emits a world click. Only a matching request ID and
the current completed snapshot tick permit confirmation. One host/gateway request
may be outstanding; the controller waits for its actual correlation result before
another edit. Cancellation cannot retract a command that has already been submitted.

## Deterministic geometry decisions

### Automatic terrain height

New interactive routes keep both building sockets at their actual foundation
heights. The planner fits inclined straight carriers and adapters between them,
keeps corners level, and raises the intermediate span when terrain requires it.
Each support samples its own local ground height and extends to the carrier
mount. Connecting a mine output to a higher or lower smelter input therefore
does not require manual tiers, ramps or support placement.

Inclines retain the full 5m underside clearance at each terrain sample; the
validator uses the actual inclined underside instead of the lowest point of
the whole segment's bounding box. The bounded height fitter permits grades up
to 1:4 and support heights up to 32m. Roads, loading lanes, build areas, occupied
volumes and support intervals retain their placement checks. Unsupported cliffs
can still reject a route, with an explicit support/slope-limit message.

Carrier rise and support height are authoritative geometry shared by rendering,
collision bounds, sockets, payment, replacement and save/replay reconstruction.
Longer inclined carriers and extended supports include their material cost in
the confirmed Steel quote. The normal light/raised support meshes scale from
their ground pivots; carriers rotate about the deck center to meet both socket
heights. No new mesh or texture family is introduced.

New UI commands explicitly record `AutomaticHeight`. Its default is false and
is omitted from historical command JSON, preserving the previous planar routing
and fixed-footing decisions when old saves and replays reconstruct commands.

### Shared routing rules

- Automatic routing respects the outgoing source tangent and incoming destination
  tangent, including existing belt sockets. A bounded deterministic set of
  cardinal routes is ordered by length and piece count and checked through the
  normal placement rules. A bend consumes 4m on each leg and uses the authored
  left/right curve without negative scale. This is a local construction planner,
  not an unrestricted terrain or obstacle pathfinder.
- Straight runs use ordinary 8m/4m carriers and length-adjusted short carriers
  where necessary. Their actual length drives sockets, bounds, rendering,
  selection, supports and authoritative state. A shorter carrier still costs
  the normal short-carrier Steel amount; no invisible gap or free transfer is
  introduced. Existing fixed-size pieces and manually selected modules retain
  their dimensions.
- An automatic route is bounded to 4–256m including terminal adapters. A match
  contains at most 4096 constructed pieces, including feet. Unsupported terrain,
  invalid sockets and physically obstructed candidates remain explicit placement failures.
  New routes check their consecutive directed sockets and reject self-crossings.
- Merger/splitter modules are explicit single placements. They retain all three
  typed/directed sockets and can receive separately drawn branches. They are not
  inferred from crossing routes.
- Ramp up/down are explicit 8m modules with a 2m rise/fall and base tier 0.
  Tier-1 carriers use the actual raised support master. Feet remain at ground;
  the lower support is never translated upward. Higher tiers are rejected.
- Auto feet are proposed at carrier sockets, shared only for the same owner,
  location and support height. Footings on road corridors are skipped only where
  the remaining support coverage is within the bounded 8m radius of each carrier
  center (up to a 16m straight interval). This is a placement bound, not a claim
  of physically qualified structural loading. Manual terrain/vehicle review is
  still required. Explicit support placement on a road is rejected.
- Constructed pieces retain stable module identity, ground-reference pivot,
  cardinal rotation, tier, paid Steel, optional attachment entity/generation and
  buffer occupancy guard. Render extraction alone applies the source scale18.

The validator samples footprint edges and interior with spacing at most 0.5m,
including both boundary edges. It requires at least 5m beneath the carrier
underside and ground variation within 0.2m under each rigid foot. Automatically
inclined carriers use their actual underside plane; explicit ramp modules retain
their conservative lowest underside. Spatial obstruction checks cover world objects, existing pieces
and units intersecting the actual elevated or support volume. New footings also
respect both designed and occupied vehicle loading corridors. Road centerline
protection uses a 5m half-corridor plus the 0.4m footing radius.

Only column/support bounds enter dynamic ground navigation. Suspended route
AABBs are explicitly excluded. Navigation is refreshed after successful edits
and observes subsequent support lifecycle changes through the existing static
obstacle synchronizer. The collision and selection render envelope is distinct
from navigation; elevated selection cages do not become full ground barriers.

## Building ports and connectivity

V1 explicitly authors four exterior sockets, in unrotated building ground space:

| Building / expected physical scale XYZ | Input position / outward | Output position / outward |
| --- | --- | --- |
| Storage Depot / 14,8,14 | (-7,5.8,0) / west | (7,5.8,0) / east |
| Logistics Hub / 18,9,18 | (-9,5.8,0) / west | (9,5.8,0) / east |

`ConveyorConstructionService.CapturePorts` is the runtime source of this typed
content. It accepts only owned, completed, living buildings with the expected
physical scale and transforms these fixed sockets by the actual building pose.
The simulation package adds the exact further sockets and inventory bindings listed
in [its activated port table](ConveyorSimulationAndVehicleInterop.md). These two rows
remain the original construction baseline, not the complete current port catalog.

Clicking the visible building selects its matching output or input. Near an
exposed port or belt socket, a screen-space snap chooses one nearest authorized
endpoint with stable tie-breaking. The endpoint position, building identity and
tier are selected together; a later belt candidate cannot leave an unrelated
building identity attached to the request. Stale and foreign snapshots are not
used for endpoint selection.

A building route automatically includes
the correct 4m building-to-belt or belt-to-building adapter, with complementary
facing and height. Its building-side end may touch only its explicit target;
feet are not placed inside that wall. Both ownership and the current building
entity generation are rechecked when the command executes.

The automatic planner allows horizontal differences that are not multiples of
4m. Endpoints must still be within 0.02m in carrier height; use the existing
explicit ramps for supported tier changes. The former start-relative 4m lattice
remains only for legacy recorded requests. Existing connections are not moved
when a new route is planned. A port already occupied by an adapter cannot gain a
second coincident connection; extend a free belt end or place a splitter instead.
The subsequent simulation package adds resource transfer, throughput, drive power and
arbitration to this paid geometry. Regional trucks retain their existing road system.

Physical connectivity is derived from stable socket positions/facings, with
owner filtering. Capped output is Blocked; an unmatched end is Disconnected;
intact matched geometry is NoInput. A nonempty guarded buffer with a closed or
missing output is Congested. Damage/critical/destruction overrides those cues.
These copied statuses select the existing material/model state, appear in
hover/selection and in owned Operations logistics rows. Actual measured flow and
snapshot-driven animation now come from the conveyor simulation. Health and private
buffer data are not published for foreign entities; their visible models follow
the existing identification boundary via IntelligenceSignature.

## Paid edits and command flow

Steel costs are provisional construction balance: short belt 8, standard 14,
light/raised foot 4/6, merger/splitter 24, cap 4, other modules 16. The plan sums
all new feet and modules and excludes reused feet. The Build-menu entry shows a
minimum; the world preview shows the whole request. Tool entries remain openable without stock so Remove and
Repair are reachable; only the complete authoritative preview authorizes a build.
Construction is immediate
at the accepted simulation tick; there is no separate conveyor building timer.

`SubmitConveyor` uses the existing bounded PlayerCommandGateway and host channel.
`ConveyorConstructionCommand` carries the typed edit, endpoints, orientation,
tier, bend preference, routing mode, source/target IDs and payer. Its execution
reevaluates the full plan against current ownership, terrain, collisions, stock
and entity generations.
Only then does one atomic Steel removal pay for the complete plan before any
pieces are created. Rejected requests create nothing and charge nothing.

Remove requires an empty finite buffer and removes the selected piece without
a salvage refund. Auto feet are removed only when no remaining carrier socket
uses them; hand-placed feet remain explicit infrastructure. A used support cannot
be removed directly. Replace keeps the existing pivot, tier, rotation and adjusted
short-carrier length and requires exactly the same world socket positions,
directions and roles. It pays the full new module cost and preserves shared feet;
occupied targets cannot be
replaced. An upgrade requiring different sockets is a new route/ramp construction,
not a silent topology rewrite. Repair costs 4 Steel and restores a damaged living
piece's HealthState; healthy targets are rejected. Occupied damaged parts may be
repaired. Destroyed parts retain cargo and can be repaired for their original paid
module Steel cost; nonfatal repairs remain 4 Steel. None of these
edits consumes, returns or invents cargo stock.

ReplayCommandKind/PlayerCommandKind 14 encode the complete command, including
generation-bearing source and target IDs. Save restoration already reconstructs
the match by replaying recorded commands; the new codec schedules them against
that match's conveyor service. Authoritative ConveyorPiece, transform, health and
paid inventory changes are included in the normal ECS/inventory snapshot hash.
`AutomaticRouting` is omitted when false, and an adjusted carrier's `SpanMeters`
is omitted when zero. New calculated rendering helpers are excluded from state
JSON. Old requests therefore retain their original planner and serialization;
new commands reconstruct their selected geometry through the same authoritative
service. This preserves old saves in the updated application, not playback of
new automatic routes in older executables. Old empty
matches gain no new component store simply by opening the conveyor UI. No new
wall-clock input, random selection or presentation-owned gameplay state is used.
Acceptance must include real delivery and matching save/replay continuation,
not only a successful geometry preview.

## Automated acceptance

The routing acceptance uses real paid construction and finite inventory flow.
`ConveyorAutomaticRoutingTests` covers all sixteen building-orientation pairs,
fractional placement, small lateral offsets, extension from an existing output,
occupied/wrong-height/foreign endpoint rejection and replacement of an adjusted
carrier followed by another delivery. `ConveyorPersistenceTests` adds an actual
paid MatchRuntime opening, delivery to a freely placed depot, rejected-command
history and Save/Replay hashes immediately and after further ticks.

`ConveyorConnectionUxTests` covers the two-click controller flow, actual building
picking, port/socket competition, current owner/visibility/generation boundaries,
ramp tiers, rotated marker direction and the on-screen marker budget. These are
managed interaction/geometry checks, not native D3D12 visual qualification.

The existing conveyor flow, distribution, battlefield supply, presentation bounds
and extraction checks remain part of focused acceptance. No required CI gate,
assertion, reference hash, material cost or resource rule is relaxed.

## Manual acceptance checklist, not executed

1. Build the full runtime asset catalog and client for native acceptance. Automated
   acceptance and hosted CI evidence must identify their actual source revision.
2. Open Build → Conveyor / Draw route. Place straight 4/8/12/32m routes and both
   bend orders in all four directions. Cancel after the first click and during
   pointer capture; inspect that no stock or entities changed before acceptance.
3. Verify exact preview cost versus accepted stock, missing Steel, stale previews,
   rapid clicks, queue rejection, focus loss, pause, resize and save-session change.
4. Place both ramps and tier-1 carriers, merger/splitter branches, caps and shared
   feet. Inspect physical port mating, bounds, LODs, arrows and masks in native
   1080p/1440p/4K presentation. Review support loading and the conservative 16m cap.
5. Click freely positioned and rotated source/destination buildings, including
   fractional horizontal coordinates. Check automatic adapters, directed
   continuity and actual delivery. Verify markers, existing belt-end extension,
   occupied-port rejection, 0.02m height tolerance, wrong owner and removed buildings.
6. Cross a road with free span and outside feet; reject feet on road, truck bays,
   occupied approaches, buildings, units, steep terrain and out-of-area locations.
   Drive real trucks below spans and confirm only actual feet block navigation.
7. Remove/replace empty parts; retain shared feet and remove the last unused auto
   feet. Reject used-foot removal, nonempty/invalid buffers and socket-changing
   replacement. Damage a living piece and repair it; reject healthy/wreck repair.
8. Confirm Disconnected, capped/Blocked, NoInput and damage surfaces, Operations
   focus and hover advice. Check fog-of-war, foreign stock and ownership boundaries.
9. Save and load after build/remove/replace/repair, with commands pending and after
   entity-ID reuse. Replay the recording and compare authoritative hashes, costs,
   piece order, attachments and navigation. These are required manual checks,
   not validated results reported by this implementation.

## Connections in paid construction templates

The Build menu's two small paid templates share this construction authority.
Their immutable relation is first building OUT to second building IN, with two
cardinally rotated buildings 48m apart. Completed-tick design capture reuses the
bounded automatic route proposals, terrain fitting, support, road, existing
socket, build-area, obstruction and stock checks without creating temporary ECS
entities. It additionally checks the proposed buildings and their truck lanes.
The future endpoint proposal cannot accept work or bypass real ports.

After the two correlated building acceptances and their actual completion, one
ordinary directed route is submitted with full source/destination identities.
That command revalidates the current real ports and pays its complete route
normally. A rejected link leaves accepted buildings intact and is never retried.
A preview is not a stock reservation; actual route cost can change before
submission. No template connection bypasses inventory, topology or transport.
See [paid construction templates](BuildingPlacementAndConstruction.md#paid-construction-templates)
for controls, combined costs, partial results, interruption and persistence.
