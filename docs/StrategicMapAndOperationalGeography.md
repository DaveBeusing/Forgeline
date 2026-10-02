# Strategic Map and Operational Geography

## Purpose

Central Divide is the canonical FORGELINE Vertical Slice battlefield. It exists to validate economy, expansion, logistics, combat, intelligence, strategic infrastructure, hierarchical navigation, and opponent behavior on one shared operational geography.

The stable map key remains:

`prototype.vertical_slice`

The compatibility-oriented key is retained even though Central Divide now serves as the production-quality Vertical Slice map baseline.

## Map Scale

Central Divide is a 3,072 m × 3,072 m chunk-based battlefield.

The terrain is simulation-owned and generated into the existing `TerrainWorld` representation. The map uses the normal 256 m world chunk contract and remains independent from graphics, UI, audio, and editor code.

## Strategic Geography

The map is divided by the Central Divide ridge.

The ridge creates two primary east-west crossings:

- North Bridge at approximately Z=920 m.
- South Ford at approximately Z=2,200 m.

Neither crossing is allowed to be the only viable strategic route. Operational qualification rebuilds navigation with each crossing unavailable in turn and requires a valid tracked route between opposing starts. The road graph is validated under the same single-crossing-loss condition.

This preserves meaningful chokepoints without reducing the battlefield to one corridor.

## Spawn and Base Areas

Two opposing start areas anchor the west and east sides of the map.

Each start defines:

- player ownership;
- initial operational position;
- Command Core position;
- an explicit buildable base area;
- nearby bootstrap Ferrous Ore, Volatiles, and Silicates.

The Command Core position must remain inside the owning player's buildable start area.

## Explicit Buildable Geography

Central Divide no longer treats the complete world bounds as buildable.

`BattlefieldBuildableAreaQuery` permits construction only inside explicit strategic zones:

1. the issuing player's own start/base area;
2. shared strategic-site build areas;
3. bounded mining zones around resource deposits.

Everything outside those zones is non-buildable.

Strategic sites include:

- expansion areas;
- mining outposts;
- forward operating base positions.

Resource mining zones remain available to both sides so contested deposits can change hands naturally.

The placement system still owns slope, terrain availability, collision, resource-deposit, and footprint validation. Buildable geography adds a map-policy boundary; it does not bypass normal placement rules.

## Resources and Expansion Pressure

Local bootstrap deposits are deliberately finite.

Richer contested reserves exist away from the starting bases for:

- Ferrous Ore;
- Volatiles;
- Silicates.

Operational qualification requires the richest contested reserve for each bootstrap resource to exceed the corresponding local reserve. This makes long-term industrial scaling dependent on leaving the starting area.

Rare Elements remain contested strategic resources and are not required for the initial bootstrap economy.

## Roads and Logistics

The existing battlefield road graph remains the authoritative regional logistics topology.

Road nodes connect:

- west start;
- west junction;
- north crossing approaches;
- south crossing approaches;
- east junction;
- east start.

Road edges expose routing cost and transport capacity.

Buildings may connect to the road network through the existing `PrototypeRoadAccessSystem`. The strategic-map work does not introduce a second logistics graph.

## Strategic Crossings

North Bridge and South Ford are represented by `StrategicInfrastructure`.

Disabling a crossing:

- disables its existing logistics edge;
- inserts its navigation blocker;
- invalidates the navigation version;
- rebuilds authoritative traversability through the existing navigation boundary.

Restoration uses the existing fixed-tick restoration state and reverses those effects only after completion.

No map-specific teleport, path override, or special-case logistics shortcut exists.

## Elevation

The deterministic terrain provides:

- a strong central ridge;
- northern and southern passages;
- elevated overlook positions;
- lower central terrain;
- broad background variation.

Operational qualification samples the complete map and requires at least 20 meters of terrain elevation range. This protects elevation as an actual map characteristic rather than a purely visual effect.

Normal navigation slope capability remains authoritative for Infantry, Wheeled, and Tracked movement.

## Hierarchical Navigation Qualification

The map qualification path uses the same `NavigationWorld` and `HierarchicalPathfinder` used by gameplay.

It verifies:

- both starts can reach all strategic sites with Tracked movement;
- both starts can reach all strategic sites with Wheeled movement;
- both starts can reach every resource deposit with Tracked movement;
- opposing starts are mutually connected;
- an east-west Tracked route still exists with either crossing individually unavailable;
- the road graph still has an east-west route with either crossing edge individually unavailable.

Endpoints may project to a neighboring traversable cell through the standard pathfinder endpoint-projection option. The route itself must still be produced by normal hierarchical navigation.

## Operational Geography Qualification

`BattlefieldOperationalGeographyValidator` is the headless qualification boundary for the canonical map.

A successful report includes:

- map key;
- number of critical reachability checks;
- alternate-navigation checks;
- alternate-road checks;
- shortest and longest qualified route lengths;
- minimum and maximum sampled terrain elevation;
- buildable-zone count;
- contested-resource count.

Qualification fails with actionable diagnostics when a critical location is unreachable, a crossing becomes a single point of failure, expansion pressure disappears, explicit buildability is inconsistent, or terrain loses meaningful elevation.

## Compiled Map Artifact

`BattlefieldMapArtifact` defines version 1 of the compiled Vertical Slice map artifact.

The artifact contains copied, immutable map data for:

- metadata;
- starts;
- resources;
- world presentation objects;
- strategic sites;
- road nodes and edges;
- crossings;
- objectives;
- static navigation obstacles.

The artifact is serialized as deterministic JSON-compatible data and is immediately loaded and validated again by the compiler. This provides a real compile/load contract without moving gameplay authority into tooling.

The current gameplay runtime continues to use the canonical in-code definition as its source while the compiled artifact establishes the stable handoff needed for later editor-authored map data.

## Map Compiler

Run:

```powershell
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
```

The compiler:

1. loads the canonical Central Divide definition;
2. creates the authoritative terrain;
3. runs operational-geography qualification;
4. captures a versioned map artifact;
5. serializes the artifact;
6. reloads it;
7. verifies it still matches the canonical definition;
8. writes the compiled map and qualification report.

A failure returns a non-zero process exit code.

## CI

Normal CI runs map compilation and qualification after the Release build.

The resulting map artifact and qualification report are uploaded with the other engine diagnostics.

The existing CI gates remain in force:

- project-reference validation;
- Release build;
- runtime-asset qualification;
- Windows visual smoke;
- headless diagnostics;
- lightweight stress;
- full tests;
- terminal Vertical Slice full-match validation.

## Benchmarks

`CentralDividePathfindingBenchmarks` measures complete-map hierarchical navigation for:

- direct west-to-east travel;
- travel to an expansion site;
- west-to-east travel after loss of North Bridge.

These measurements are optimization evidence and are not hardware-sensitive CI pass/fail thresholds.

## Architecture Boundaries

The strategic map does not change the fundamental ownership model:

- `ForgeLine.World` owns terrain/spatial data;
- `ForgeLine.Navigation` owns derived navigation and path search;
- `ForgeLine.Logistics` owns logistics topology and routing;
- `ForgeLine.Game` owns map gameplay composition and strategic infrastructure;
- presentation only visualizes resulting state;
- tools compile and qualify data but do not become gameplay authority.

Headless execution remains fully supported.

`ForgeLine.Editor` remains a bootstrap host in this stage. Interactive map-editor authoring is not introduced by this change; the compiled artifact and qualification contract are the stable boundary that a later editor can target.
