# Strategic Map and Operational Geography

The offline [procedural strategic layout stage](ProceduralStrategicGeography.md)
uses the same definition, navigation and qualification boundaries. It does not
change the authored maps or their runtime registration.

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

Buildings may connect to the road network through the existing `RoadAccessSystem`. The strategic-map work does not introduce a second logistics graph.

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
- both starts can reach a bounded traversable approach for every resource deposit with Tracked movement;
- opposing starts are mutually connected;
- an east-west Tracked route still exists with either crossing individually unavailable;
- the road graph still has an east-west route with either crossing edge individually unavailable.

Strategic-site endpoints may use the standard pathfinder endpoint-projection option. Resource qualification resolves a bounded tracked approach around the deposit because transport and construction interact with a deposit from its perimeter rather than requiring a vehicle to occupy the deposit center. The approach search is capped by the deposit footprint plus 128 meters; absence of a traversable approach fails qualification. The route itself must still be produced by normal hierarchical navigation.

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

## Compiled Map Artifact and Registration

BattlefieldMapArtifact format **4** carries map revision, copied placement/topology data, TerrainVisual, authoritative terrain recipe version **1**, Directorate catalog digest explicit starting-base/objective rule IDs and up to 32 explicit TacticalAreaDefinition entries. Formats 1 through 3 are rejected rather than implicitly acquiring new tactical rules. ToDefinition copies every collection and material array; edits to the source artifact cannot mutate the reconstructed definition.

ScenarioRegistry registers Central Divide and [Split Basin](SplitBasinBattlefield.md). The composition key remains `central-divide.directorate.v1`, the map key remains `prototype.vertical_slice`, and the default remains Central Divide. The descriptor binds the map key/revision, SHA-256 of the complete deterministic JSON, terrain version, Directorate content key/digest and rule IDs. The content digest serializes the eight actual catalogs in stable ID order, including technology definitions, costs, weapons and armor. Changing content requires a newly qualified map revision and explicit compatibility handling.

The reviewed runtime input is `assets/maps/runtime/central-divide.flmap.json` with its `.sha256` sidecar. Game project content items copy exactly these files to `maps/` beside client/headless executables, test assemblies and published binaries. The runtime resolves from AppContext.BaseDirectory, independently of the working directory. Git attributes preserve the map bytes across Windows/Unix checkouts. Sparse headless checkouts include only assets/maps/runtime in addition to code and tests; rendering sources remain unnecessary. Both the sidecar and bytes must match the registered digest. Missing or changed content fails explicitly; productive composition never falls back to CentralDivideBattlefield.Create or CentralDivideTerrainFactory. Those remain authoring/test conveniences.

External input is capped at 4 MiB, depth 24, 500,000 tokens, 2,048 UTF-8 bytes per string and 16,384 elements per array before materialization. Typed limits further restrict starts/objectives to 16, resources to 4,096, sites to 256, road nodes to 2,048, road edges to 8,192, crossings to 128 and obstacles to 4,096. Validation checks finite coordinates, areas, resource extents, unique keys/owners, references, one objective per start and supported content/rules. Map dimensions are at most 8,192 m per axis. Registered relative paths must remain beneath the content root; symbolic links and reparse points within that path are rejected.

## Authoritative Terrain Recipe

Version 1 fixes the floating-point evaluation order for three broad waves (X sine, Z cosine, diagonal sine), a Gaussian ridge attenuated by the maximum of two pass profiles, and three Gaussian surface features. All frequencies, amplitudes, centres, widths and grid settings are serialized. This is a bounded numeric data contract; it cannot name code, types or scripts. Unknown recipe versions fail before terrain allocation. Sample density is at most 129 per chunk side and total height samples are capped at 4,000,000; dimensions must exactly cover whole chunks.

Central Divide retains 256 m chunks and 65 samples per side. CompiledTerrainTests compares all **608,400** height samples with a fingerprint captured from master cb58c3ad before replacing the evaluator:

`d9ff8d68132b7c8a48936cf751552044ca6befe8b4359b1dd0b736de2bc67a33`

CompiledNavigationParityTests freezes every navigation cell at 16 m and 32 m resolution, both with normal crossings and with either crossing individually disabled. Each fingerprint includes height, slope, terrain/blocker flags, traversability and traversal cost for Infantry, Wheeled and Tracked. Those six fingerprints were captured from the unchanged pre-transfer master.

CompiledScenarioTests also freezes the validation-profile seed-2026 authoritative hashes at ticks 0 and 20, compares all authored placement/topology data, and runs the full loaded-map operational geography qualification (71 reachability checks, two alternate-navigation and two alternate-road checks). A separately registered test fixture changes a finite resource quantity and observes that exact value on the real ResourceDeposit entity. It is not a second productive map.

## Map Compiler

```powershell
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release -- --output artifacts/maps/central-divide.flmap.json --qualification-output artifacts/map-qualification.json
```

The compiler captures authoring data, serializes and reloads it, reconstructs the definition and generates terrain through the shared BattlefieldTerrainFactory. It qualifies the loaded definition/recipe, verifies authoring equality and the registered runtime digest, then writes JSON, its SHA-256 sidecar and the qualification report. A differing authored map fails until its qualified runtime data and registration are deliberately updated together. Compiler and runtime therefore qualify the same reconstructed data.

## Setup, CLI and Packaging

The existing New Game setup displays registered scenario names. Up/Down selects a scenario; clicking the battlefield row advances selection. Left/Right continues to adjust the seed. Unavailable/invalid content disables Start with an error, and choices are refreshed when entering setup. Selecting a choice never mutates a live session.

Headless accepts `--scenario central-divide`, its existing `vertical-slice` alias, and the stable `central-divide.directorate.v1` key. `--scenario-root <path>` overrides the compiled-map root for registered data; integrity and content checks remain mandatory. The same resolver is passed through new match creation, save restoration and replay. Client new/owned/load/restart paths preserve selected identity; a match restart retains seed and participants and reloads/verifies the registered revision with fresh scheduler ownership.

Publish-PreAlpha qualifies the map through MapCompiler before export. Validate-ScenarioPackage requires exactly the four registered map files and runs `dotnet ForgeLine.Client.dll --validate-scenarios` from the exported folder. That installed-client entry point constructs the real runtime and validates save/replay reconstruction using deployed files only, without source files or the compiler. The package provenance includes its map digest. See [save compatibility](SaveLoadReplayAndRecovery.md) for payload identity rules.

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

## Revision-2 tactical areas

Both registered battlefields now carry an immutable, bounded World TacticalAreaSet through capture, validation, reconstruction and authoring comparison. Map definitions expose their revision explicitly. Region bounds must fit the map, keys are unique, numbers finite and cover multipliers between 0.5 and 1. No scenery classification creates an area.

Runtime map JSON and mandatory sidecars are registered together at revision 2. Central Divide digest: `0f64c67943ddd0e399f7cc7579f65c43feded449e6bb43182de3bdd9ff4d0682`; Split Basin digest: `635ca23e5fa63285c4bb6b7e4b00b8615804af67cf638ee48e36dda21573a36e`. Terrain recipes/catalogs are unchanged. These prepared content bytes are not a claimed MapCompiler qualification; earlier maps and terrain fingerprints retain their historical revisions.

Sensors/All overlays copy public outlines and labels; occupancy and hidden entity metrics are absent. [Cover and visual occlusion](CoverAndVisualOcclusion.md) specifies combat/sensing consumers, budget boundaries, schema-16 recovery and outstanding manual acceptance.

## Offline balance audit

The existing compiler qualification now includes deterministic per-start opening, expansion, movement-class connectivity and road bottleneck observations. The registered-map audit, conservative criteria, explicit warning exceptions and optional developer previews are documented in [Strategic map balance audit](StrategicMapBalanceAudit.md). These measurements do not certify fairness or replace runtime/scenario acceptance.
