# Prototype Battlefield: Central Divide

## Purpose

`Central Divide` is the canonical FORGELINE vertical-slice battlefield. It is a 3,072 × 3,072 meter two-player map designed to force the production, logistics, intelligence, terrain, disruption, and combined-arms systems to interact in one scenario.

The map is authored as typed game-content data through `PrototypeBattlefieldDefinition`. `ForgeLine.MapCompiler` now captures that definition into the versioned `BattlefieldMapArtifact`, reloads the compiled payload, and runs operational-geography qualification. Gameplay does not depend on editor-only state; the typed definition remains the current runtime source while the artifact establishes the compiler handoff for later editor-authored maps.

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

The content validator verifies that both starts have nearby access to Ferrous Ore, Volatiles, and Silicates and that contested resource pressure exists. Construction is additionally restricted to explicit start, strategic-site, and resource-mining build zones; arbitrary open terrain is not globally buildable.

## Command Core Objectives

Each side has one stable Command Core objective definition.

`MatchObjectiveSystem` owns match completion state in simulation. When a Command Core is attached as an objective, it is also made a normal combat structure target if those combat components are not already present. A surviving opposing Command Core therefore wins only after the real target's entity has been removed through authoritative gameplay state.

The match state is created as `Loading`, becomes `Active` only after Command Core objectives are attached, and resolves to `Victory` or `Draw`. Player-facing read models derive `Victory` versus `Defeat` from the authoritative winner. A completed match can transition to `Ended` through a simulation command.

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

`BattlefieldOperationalGeographyValidator` additionally qualifies every resource and strategic-site route from the spawns, explicit buildability, expansion pressure, elevation range, and both navigation and road alternatives after either crossing is individually unavailable. CI runs this qualification through `ForgeLine.MapCompiler` and retains the compiled map/report as diagnostics.

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
- compiled map capture/load roundtrip
- explicit buildable versus non-buildable geography
- complete strategic reachability and alternate-route qualification

The presentation tests separately validate the battlefield debug overlay.

## Runtime world and infrastructure presentation

The prototype now combines its authoritative map data with compiled presentation assets. Non-crossing `GroundRoad` edges produce presentation-only road entities using the Directorate road material family. The functional North Bridge resolves intact, restoring/damaged, and disabled/destroyed runtime meshes directly from `StrategicInfrastructureState`; the South Ford remains a road-surface presentation.

These visual entities do not participate in logistics routing, capacity, navigation blocking, restoration progress, or crossing availability. Those facts continue to come exclusively from the existing logistics and strategic-infrastructure systems.

Final high-detail environment art, richer bridge repair/destruction effects, and production map-editor authoring remain future work. The gameplay topology and authoritative state are not placeholders.
