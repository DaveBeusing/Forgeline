# Conveyor construction, placement and UX

Implementation baseline: master `d7c45792` (0.1.450), including the conveyor model
and material packages. This work prepares 0.1.451. The Release client and its
dependencies were built; automated tests, CI, native interaction/camera smokes
and save/replay round trips were deliberately not run at the user's request.
All manual acceptance below remains pending.

## V1 interaction and layout

Open the existing Build menu and select **Conveyor / Draw route**, or select a
specific short/standard belt, curve hand, ramp, merger, splitter, I/O adapter,
end cap or support. These entries use menu-only IDs starting at 2000, intercepted
before building placement; they do not masquerade as building definitions or go
through SubmitBuild. The existing paginated Build menu handles the longer list.

| Gesture while conveyor placement owns input | Behavior |
| --- | --- |
| first world click in Draw route | anchor start; no command or cost yet |
| pointer movement | current-tick authoritative plan, part cages and directional IN/OUT arrows |
| second world click | request the complete route only when its preview is current and valid |
| click with a special module selected | request that individual module and automatic feet |
| Q / E | rotate special modules by 90 degrees |
| Tab | choose X-first or Z-first bend for a drawn route |
| T | select carrier tier 0 or 1; endpoint snapping adopts the starting socket tier |
| V | cycle Build → Remove → Replace → Repair; point at the owned part to edit |
| Escape / right click | cancel the unsubmitted plan and leave placement |

The hint bar names the mode, variant, tier and next click. Its placement context
shows validity/reason and exact Steel cost from the copied preview. World-space
cages use green/red plus text and IN/OUT arrows, so color is not the only cue.
The ghost is a structural wire preview rather than another set of solid material
meshes. Support cages show the actual ground footprint; carrier cages remain
elevated. Built instances use the existing authored meshes and PBR atlas, with
LOD0/1/2 at the existing 140/420m handoff distances.

The world-selection and tactical controllers yield to placement. Existing modal,
pause, focus, resize, session and HUD-pointer rules remain active. Pointer capture
clears the preview and never emits a world click. Only a matching request ID and
the current completed snapshot tick permit confirmation. One host/gateway request
may be outstanding; the controller waits for its actual correlation result before
another edit. Cancellation cannot retract a command that has already been submitted.

## Deterministic geometry decisions

- Routing uses 4m increments, cardinal headings and one optional 90-degree bend.
  It prefers 8m straights, then a 4m remainder. A bend consumes 4m on both legs
  and selects the separately authored left/right curve, without negative scale.
- A route is bounded to 4–256m after terminal adapters. A match contains at most
  4096 constructed pieces, including feet. Unsupported lengths, fractional
  endpoint phases and tier jumps fail closed; no invisible stretch piece is used.
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
including both boundary edges. It requires at least 5m beneath the conservative
carrier envelope and ground variation within 0.2m under each rigid foot. Ramps
use their conservative lowest underside; arbitrary terrain-following deformation
is not offered. Spatial obstruction checks cover world objects, existing pieces
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
Other building types remain without ports; the earlier port matrix is still a
future content plan for them. These sockets are a construction connection, not
an inventory source/sink implementation or automatic across-wall transfer.

Route starts snap within 2m to an output socket and ends to an input socket.
Existing belt sockets snap within 1.5m. A building route automatically includes
the correct 4m building-to-belt or belt-to-building adapter, with complementary
facing and height. Its building-side end may touch only its explicit target;
feet are not placed inside that wall. Both ownership and the current building
entity generation are rechecked when the command executes.

The 4m lattice is relative to the selected start port; a destination port must
share its phase. Ports at different phases or more than 0.02m apart in carrier
height are rejected with Port mismatch. Position buildings with compatible port
coordinates; V1 does not fill a 2m gap or an arbitrary grade with a scaled mesh.
No resource transfer, throughput, power, arbitration or inventory flow system is
added here. The existing regional truck economy remains authoritative.

Physical connectivity is derived from stable socket positions/facings, with
owner filtering. Capped output is Blocked; an unmatched end is Disconnected;
intact matched geometry is NoInput. A nonempty guarded buffer with a closed or
missing output is Congested. Damage/critical/destruction overrides those cues.
These copied statuses select the existing material/model state, appear in
hover/selection and in owned Operations logistics rows, and never claim actual
flow animation in the absence of a transport simulation. Health and private
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
tier, bend order, source/target IDs and payer. Its execution reevaluates the full
plan against current ownership, terrain, collisions, stock and entity generations.
Only then does one atomic Steel removal pay for the complete plan before any
pieces are created. Rejected requests create nothing and charge nothing.

Remove requires an empty finite buffer and removes the selected piece without
a salvage refund. Auto feet are removed only when no remaining carrier socket
uses them; hand-placed feet remain explicit infrastructure. A used support cannot
be removed directly. Replace keeps the existing pivot, tier and rotation and
requires exactly the same world socket positions, directions and roles. It pays
the full new module cost and preserves shared feet; occupied targets cannot be
replaced. An upgrade requiring different sockets is a new route/ramp construction,
not a silent topology rewrite. Repair costs 4 Steel and restores a damaged living
piece's HealthState; healthy and destroyed targets are rejected. None of these
edits consumes, returns or invents cargo stock.

ReplayCommandKind/PlayerCommandKind 14 encode the complete command, including
generation-bearing source and target IDs. Save restoration already reconstructs
the match by replaying recorded commands; the new codec schedules them against
that match's conveyor service. Authoritative ConveyorPiece, transform, health and
paid inventory changes are included in the normal ECS/inventory snapshot hash.
The additive optional replay field is omitted for legacy commands. Old empty
matches gain no new component store simply by opening the conveyor UI. No new
wall-clock input, random selection or presentation-owned gameplay state is used.
Replay/save/load round-trip acceptance is explicitly pending manual execution.

## Manual acceptance checklist, not executed

1. Build the full runtime asset catalog, build the client, then run the existing
   test suites manually. This task has not launched any tests or CI workflows.
2. Open Build → Conveyor / Draw route. Place straight 4/8/12/32m routes and both
   bend orders in all four directions. Cancel after the first click and during
   pointer capture; inspect that no stock or entities changed before acceptance.
3. Verify exact preview cost versus accepted stock, missing Steel, stale previews,
   rapid clicks, queue rejection, focus loss, pause, resize and save-session change.
4. Place both ramps and tier-1 carriers, merger/splitter branches, caps and shared
   feet. Inspect physical port mating, bounds, LODs, arrows and masks in native
   1080p/1440p/4K presentation. Review support loading and the conservative 16m cap.
5. Connect the explicit Storage/Hub ports with compatible phases. Check direction,
   0.02m height tolerance, wrong owner, absent/removed building, incompatible phase,
   other building types and absent resource-transfer authority.
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
