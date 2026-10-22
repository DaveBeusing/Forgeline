# Directorate raised conveyor visual kit

**Scope:** visual design, blockout geometry, authored sockets and module identities.  
**Status:** proposed V1 art contract, ready for review; **not** live gameplay or compiled runtime assets.  
**Canonical source:** [`conveyor-kit.v1.json`](../assets/source/infrastructure/directorate/conveyors/conveyor-kit.v1.json).  
**Reference:** [Base conveyor logistics](BaseConveyorLogistics.md), [building port matrix](BaseConveyorPortMatrix.md), [Directorate material review](DirectorateMaterialQualification.md), [asset pipeline](AssetPipeline.md).

The earlier logistics design already fixed a 4 m short unit, 8 m standard unit,
cardinal-only 90° corners and a **minimum 5 m free vehicle clearance**. This art
contract gives those decisions exact module envelopes and authored port frames.
It does not change Cargo/Supply Trucks, route authority or real inventory rules.

## Engine space, dimensions and tolerances

Measurements are world metres; engine coordinates are **+Y up, +Z forward,
+X right**. Default movement is from local negative Z to positive Z. A module's
pivot is at its plan/grid reference at world ground (local `[0,0,0]`), not at
belt height. Rotation is a whole 90° yaw about +Y; authored curve handedness
requires a verified mirrored source mesh, not a negative runtime scale.

| Parameter | V1 value | Acceptance meaning |
| --- | ---: | --- |
| Horizontal construction grid | **4 m** | Endpoints coincide at cardinal grid coordinates |
| Short / standard straight | **4 m / 8 m** | Only V1 linear increments |
| Usable belt width | **2.00 m** | Physical moving face, not a 2 m road obstacle |
| Total raised carrier width | **2.80 m** | Edge guard/rollers/frame, not support foundations |
| Belt travel surface, tier 0 | **Y = 5.80 m** | Port centerline and material elevation |
| Lowest underside, tier 0 | **Y = 5.20 m** | Provides 0.20 m nominal buffer above 5.00 m |
| Guard top, tier 0 | **Y = 6.30 m** | Guard is visibly above the moving face |
| Road minimum clear span | **5.00 m** | Sample local traversable terrain, not just module pivot |
| Raised tier increment | **2.00 m** | Tier 1 belt top = 7.80 m |
| Ramp length / rise | **8.00 m / 2.00 m** | Low tier at one port, high tier at the other |
| 90° curve centerline radius | **4.00 m** | Exactly one 4 m turning radius, both hands |
| Belt-to-belt snap position error | **≤ 0.02 m** | Compare in *world* coordinates after placement |
| Belt-to-belt yaw error | **≤ 1°** | Facing vectors must oppose |
| Supported horizontal turns | **90° only** | No unrestricted 45° curves in V1 |

A straight/ramp/curve carrier by itself does **not** register ground collision
along the entire elevated footprint. Foundations and low structural members
are the only ordinary ground obstructions; clearance is measured from the
highest sampled driveable terrain directly beneath the actual crossing span.
Where grade or an obstacle reduces clearance below 5 m, placement must fail or
use a different valid raised tier. The current 3.0 × 2.0 × 6.0 m Cargo Truck and
3.2 × 2.2 × 6.4 m Supply Truck visual envelopes are smaller than the planned
vertical clearance, **but native navigation/collision qualification remains
required** before claiming vehicles can pass underneath in the game.

## Shape vocabulary and silhouettes

DIRECTORATE remains grounded industrial military science fiction. Every module
shares thick trapezoidal support beams, square reinforced end cheeks, exposed
roller spacing, rubber center face, bolted guards and limited amber maintenance
paint. Broad low shapes and exposed service structure take precedence over
futuristic shells or glowing rails. Reuse the existing Directorate material
atlas vocabulary; no new 4K texture family or material dependency is authorized
by this contract.

- **Straight**: two parallel cantilevered rails and at least two asymmetric
  chevrons per 4 m; roller mouths reveal direction with the belt still.
- **Curve**: continuous 4 m radius belt with chevron turn marks, a longer outer
  guide rail and a visible inside-corner guard; source-author both hands.
- **Ramp**: continuous rise, prominent low/high bearer difference and no
  ground-scraping end; `UP`/`DOWN` are clear from camera.
- **Merger 2:1**: two incoming funnels converging into one **single wide central
  throat**; incoming faces at local X -4/+4 m and outgoing at X 0 m.
- **Splitter 1:2**: a clear **central input nose and two lateral output chutes**;
  a directional Y silhouette distinguishable from the merger at RTS zoom.
- **I/O adapters**: mechanically different building intake and discharge
  hoppers, with one clearly belt-facing jaw and one building-facing jaw.
  A physical adapter must align to a *separately authored* building port;
  no proximity-created cross-wall links.
- **Supports**: thin single stanchion or heavier two-leg cross-braced frame,
  separate from elevated belt meshes. Repeatable 4/8 m structural supports
  allow truck passages between columns. Keep foot collision separate from
  mesh and from the full suspended rail envelope.
- **End cap**: transverse stop-bar and bolted roller shield; blocks the physical
  end. It **cannot** be an invisible inventory consumer or a valid flow sink.

See the [dimensioned greybox board](images/conveyor-greybox.svg) for plan and
elevation silhouettes. The board is a design reference, not a screenshot or
native visual acceptance image.

## Port and placement conventions

Each record in the canonical JSON lists `slug`, stable `assetId`, authored
footprint, transport role, local port name, `kind`, `role`, local XYZ coordinates,
cardinal-facing outward normal and relative height tier. Inlet normals point
*away* from the module toward the upstream carrier; outlet normals point
downstream. A connection requires exact complementary roles, opposite world
normals, socket coincidence, same elevation, permitted resource class, ownership
and an active mechanical link. Physical adjacency is not a transfer rule.

| Module | Inlet local XYZ / normal | Outlet local XYZ / normal | Footprint / identity |
| --- | --- | --- | --- |
| Short straight | (0, 5.8, -2) / S | (0, 5.8, +2) / N | 2.8 × 4 m |
| Standard straight | (0, 5.8, -4) / S | (0, 5.8, +4) / N | 2.8 × 8 m |
| 90° curve, left source | (-4, 5.8, 0) / S | (0, 5.8, +4) / E | radius 4 m; swept frame 6.8 × 6.8 m, center (-2, +2); opposite hand authored |
| Ramp up | (0, 5.8, -4) / S | (0, 7.8, +4) / N | +2 m tier |
| Ramp down | (0, 7.8, -4) / S | (0, 5.8, +4) / N | -2 m tier |
| Merger 2:1 | (-4, 5.8, -4), (+4, 5.8, -4) / S | (0, 5.8, +4) / N | 10.8 × 8 m; two → one |
| Splitter 1:2 | (0, 5.8, -4) / S | (-4, 5.8, +4), (+4, 5.8, +4) / N | 10.8 × 8 m; one → two |
| I/O input (building → belt) | building source at (0, 5.8, -2) / S | belt output at (0, 5.8, +2) / N | 2.8 × 4 m |
| I/O output (belt → building) | belt input at (0, 5.8, -2) / S | building sink at (0, 5.8, +2) / N | 2.8 × 4 m |
| Light/heavy support | *no material ports* | structural load attachment only | localized foundation collision |
| End cap | capped mechanical interface at (0, 5.8, -0.4) / S | *no flow output* | 2.8 × 0.8 m |

For tier `n`, add `2n` to each socket Y in the authored port frame;
do not scale module geometry or terrain collision along that axis. Distinct
building port coordinates are planned by building stable ID in the
[port matrix](BaseConveyorPortMatrix.md), not populated speculatively here.
The builder must reject unsupported material classes: only explicitly allowed
solid/packaged ResourceIds can enter. Fuel, Volatiles, gas, liquids, vehicles
and infantry are not belt cargo.

**Port mating examples**, expressed as module-center XYZ and local yaw:

1. Short straight centered at `(0,0,-6)` feeds standard straight at
   `(0,0,0)`: both endpoint centers are `(0,5.8,-4)`, opposite N/S normals.
2. Standard straight centered at `(-4,0,-4)` feeds a left curve
   centered at `(0,0,0)`, then an eastbound straight with
   **yaw +90°** centered at `(4,0,4)`.
3. Standard straight at `(0,0,-8)` feeds ramp up at `(0,0,0)`;
   a tier-1 straight centered at `(0,2,8)` receives at Y = 7.8 m.
4. Two standard input belts at X = -4 and +4, Z = -8 feed merger at
   `(0,0,0)`, which forwards to a standard belt at `(0,0,8)`.

These are planar port continuity examples, not proof that placement is enabled.

## State language and player readability

| Actual future operating observation | Visual rule | Movement indication |
| --- | --- | --- |
| Flowing | Moving belt/rollers; repeat chevrons | Active |
| Idle / no input | Empty face, exposed still rollers | Static |
| Disconnected | Open mechanical jaw / broken link | Static |
| Congested | Visible tightly queued parcels, output throttling | Static |
| Blocked | Physical closed gate, striped barrier and amber lamp | Static |
| Unpowered | Dormant motor housing and engaged brake | Static |
| Damaged | Bent outer plate/guard and localized effects | Conditional |
| Critical | Distorted bearer plus concentrated smoke/sparks | Conditional |
| Destroyed | Collapsed local span and visibly interrupted route | Static |

Normal, inactive, blocked, damaged and critical variants are required on the
shared part family; destroyed source variants are planned, but a compiled
`*.asset.json` state mapping must be implemented and validated by the later
asset-export work. **Do not claim material flow, damage rules, ownership
shader parameters or animation are currently implemented.**

Ownership markers remain small (≤8% of painted surface), and painted chevrons
encode direction even on a monochrome screenshot. State uses mechanical
geometry, motion and an optional small status lamp rather than hue alone.
Enemy operations and exact carried quantities must respect the existing
faction-visibility boundary. No fog-of-war information may leak from animated
parts or hidden cargo state.

### Variant identity and reuse

The JSON lists a stable `assetId` for each module and a separate planned
`assetId + "." + state` for every state presentation. The nine material-moving
module families have explicit **Flowing, Idle, Disconnected, Congested, Blocked,
Unpowered, Damaged, Critical and Destroyed** variants. The two supports and
physical end cap have only the meaningful **Structural Intact, Damaged, Critical
and Destroyed** variants. A shared mesh with bounded procedural changes may
provide several of those states; the reserved variant IDs do not require a
unique high-poly model per state, do not grant flow authority and do not imply
a compiler-ready asset today.

## Materials, texture and LOD contract

Follow the current Directorate baseline (metallic/roughness):
paint 0/.72, bare steel .88/.56, gunmetal .85/.65, rubber 0/.94,
concrete 0/.91, warning paint 0/.78. Use existing
`assets/source/materials/directorate/surfaces/` as the reference.
Expected textures are BaseColor, Normal and ORM (R=AO/G=roughness/B=metallic),
plus bounded emissive only for a functional control point.

A standard mesh family should have **LOD0** for inspection, **LOD1** for normal
RTS view and **LOD2** preserving broad frame, arrow and branch silhouettes.
At distance, remove invisible fasteners and individual roller details before
removing directional information. Reuse roller geometry, beam profiles, support
bases and atlas regions across components; prefer batching/instancing to one
renderable material for every belt piece. No new shader, extra runtime material,
texture resolution budget or per-resource visual physics is established here.

Source masters are glTF 2.0 GLB or Blender-editable projects exported through
the existing [asset compiler](AssetPipeline.md). Use lowercase snake_case file
stems matching the twelve `slug` values and stable period-delimited
`assetId` values from the JSON. **This file is deliberately named
`conveyor-kit.v1.json`, not `*.asset.json`:** it is a preproduction contract
and must not be interpreted as a deployable runtime asset definition.

## Review and acceptance checklist

- [x] 12 bounded module identities and all normal, idle, blocked, damaged,
  critical plus supplementary visual states have authored requirements.
- [x] 4/8 m routing increments, 90° corner, 2 m tier and 5 m passage envelope
  have a single measurable source of truth.
- [x] Every material port has a role, directed normal, local XYZ and tier.
- [x] Greybox plan and elevation board is present for modelling reference.
- [ ] Pinned Blender 5.2.2 source meshes and compile-ready declarations exported.
- [ ] Native greybox/camera and roadway clearance qualification completed.
- [ ] Emissive/motion, damage-state geometry and faction identification reviewed
  from close, normal RTS and strategic distances.
- [ ] Dedicated conveyor gameplay, navigation and logistics tests passed.

The design gate enables independent mesh/texture authoring. **It does not
approve** physical asset delivery, real-world vehicle passage or shipment of
a gameplay conveyor system. No current gameplay economy, logistics, transport,
savegame schema or scenario is modified by this package.

## Model production handoff

[Conveyor models and belt motion](ConveyorModelsAndAnimations.md) supplies the
Blender/GLB state/LOD implementation and compiler declarations for this design.
The design baseline and original review checklist above remain historical; manual
visual, clearance and gameplay acceptance are still outstanding.
