# Prototype Battlefield: Central Divide

## Purpose

`Central Divide` is the canonical FORGELINE vertical-slice battlefield. It is a 3,072 × 3,072 meter two-player map designed to force the production, logistics, intelligence, terrain, disruption, and combined-arms systems to interact in one scenario.

The map is authored as typed game-content data through `PrototypeBattlefieldDefinition`. The current map compiler remains a future asset-pipeline host; the prototype does not depend on an editor-only or map-script-only runtime path.

## Strategic Layout

The western and eastern players begin on opposite sides of a north-south central divide.

Each start area has nearby finite Ferrous Ore, Volatiles, and Silicates deposits so either side can bootstrap without immediately crossing the map. Richer finite deposits sit outside the safe starting areas and are intentionally tied to expansion pressure.

The map contains:

- two opposing start/build areas and Command Core objective positions
- four expansion sites
- two external mining-outpost sites
- four Forward Operating Base opportunities
- a central terrain divide that blocks direct east-west ground movement
- the North Bridge at approximately `(1536, 920)`
- the South Ford at approximately `(1536, 2200)`
- a real GroundRoad logistics graph linking both start regions through both crossings
- elevated overlooks around the northern and southern approaches
- contested deposits around the divide and outer expansion lanes

The North Bridge is the efficient high-capacity route. The South Ford is a longer, lower-capacity alternate. Losing the bridge therefore changes both route cost and ground-navigation geometry rather than only changing presentation.

## Terrain and Navigation

`PrototypeBattlefieldTerrainFactory` creates deterministic chunked terrain using the normal `TerrainWorld` model. The divide is formed from elevation plus explicit static navigation blockers. Only the two crossing gaps allow direct ground transit.

The interactive client builds the normal hierarchical navigation world over this terrain. Strategic-infrastructure state replaces the `NavigationWorld` with a new `NavigationVersion`; cached high-level routes are cleared and stale route results are rejected by the existing navigation system.

A disabled or restoring crossing contributes its passage bounds as a navigation blocker. Once restoration completes, that blocker is removed and a new navigation version is published.

## Logistics Corridor

`PrototypeBattlefieldRuntime` creates the battlefield corridor as real `LogisticsNetwork` nodes and `GroundRoad` edges. Crossing definitions hold stable references to their corresponding logistics edge.

`StrategicInfrastructureSystem` changes the actual edge availability. Disabling a crossing invalidates cached logistics routes and forces any reachable route to use another enabled crossing. Restoration re-enables the same topology rather than creating a separate scripted bypass.

## Strategic Infrastructure Lifecycle

Crossings use generic simulation-owned components:

- `StrategicInfrastructure`
- `StrategicInfrastructureState`
- `DisableStrategicInfrastructureCommand`
- `RestoreStrategicInfrastructureCommand`
- `StrategicInfrastructureSystem`

The authoritative states are `Operational`, `Disabled`, and `Restoring`. Restoration consumes fixed simulation ticks. While restoring, the crossing remains unavailable to navigation and logistics; only completion returns it to service.

This mechanism is intentionally not specific to Central Divide and can be reused by later bridges, tunnels, gates, road junctions, or similar infrastructure.

## Resources and Expansion

Every deposit is a normal finite `ResourceDeposit`. Starting deposits are smaller and non-contested. Expansion deposits are larger and placed away from the protected start areas.

The content validator verifies that both starts have nearby access to Ferrous Ore, Volatiles, and Silicates and that contested resource pressure exists.

## Command Core Objectives

Each side has one stable Command Core objective definition.

`MatchObjectiveSystem` owns match completion state in simulation. When a Command Core is attached as an objective, it is also made a normal combat structure target if those combat components are not already present. A surviving opposing Command Core therefore wins only after the real target's entity has been removed through authoritative gameplay state.

The current match state supports `Running`, `Victory`, and `Draw`.

## Debug Visualization

F2 world debugging in the Windows client includes the battlefield layer in addition to the existing navigation, logistics, resources, combat, and intelligence diagnostics.

The battlefield overlay shows:

- start and build areas
- resource locations and contested-resource distinction
- expansion, mining-outpost, and FOB areas
- the central barrier
- road/logistics links
- crossing bounds and operational/restoring/disabled state
- Command Core objective locations

All visuals remain diagnostic overlays; navigation and logistics behavior comes from the authoritative systems.

## Validation

`PrototypeBattlefieldValidator` fails loading for invalid dimensions, duplicate or missing stable keys, invalid road references, insufficient crossing data, missing bootstrap resources, invalid strategic sites, or objective/start mismatches.

`PrototypeBattlefieldTests` run headlessly and validate:

- canonical dimensions and strategic content
- finite resource-deposit creation
- real logistics topology
- normal navigation across the efficient crossing
- bridge disruption changing both logistics and navigation routing
- alternate South Ford routing while the North Bridge is disabled
- fixed-tick restoration recovering the original route
- simulation-owned Command Core victory state
- Command Core combat-target integration

The presentation tests separately validate the battlefield debug overlay.

## Placeholder Art

The prototype intentionally uses generic visual IDs and debug geometry for strategic infrastructure and objectives. Final terrain materials, bridge meshes, environmental art, destruction animation, and production map-editor authoring remain outside this vertical slice. The gameplay topology and authoritative state are not placeholders.
