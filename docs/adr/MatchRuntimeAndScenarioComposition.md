# Match runtime and scenario composition

## Decision

`MatchRuntime` owns one authoritative RTS match and its explicitly owned scheduler. It accepts `MatchRuntimeSettings` containing a `MatchComposition`, participants, seed, scheduler policy, diagnostics and explicit gameplay/validation rules. Both client and headless execution use this contract. Presentation reads completed snapshots and never owns simulation mutation.

`MatchComposition` identifies a reproducible composition and supplies authored `BattlefieldDefinition`, content catalogs, terrain construction, starting-base construction and system selection/registration order. These are inputs to the existing RTS systems, not a second simulation framework. Map dimensions, spawn/player IDs, catalogs and selected systems are not chosen by the shared runtime. Combat currently requires each participant's combat faction to equal its player ID; this existing rule remains validated, with arbitrary valid IDs supported.

`CentralDivideScenario` supplies the current Central Divide/Directorate preset, its two starts and its explicit gameplay and validation profiles. `CentralDivideBattlefield`, `CentralDivideTerrainFactory` and `CentralDivideStartingBaseFactory` hold authored geometry, terrain and bootstrap content. Catalog validation specific to Directorate occurs in that preset. `BattlefieldValidator` validates reusable structural integrity rather than requiring a particular map size, resource layout or crossing count. Strategic geography and preset tests continue to qualify the authored map.

Runtime bases and opponent policies are keyed by participants. Snapshot intelligence domains, gameplay telemetry and headless reports iterate participants. No shared runtime assumes players 1/2 or West/East. The system selector receives the existing ordered RTS system set; it may select, extend or reorder that set. Compositions must preserve dependency ordering appropriate to their selected gameplay systems.

## Canonical names

| Former reusable contract | Current contract |
| --- | --- |
| VerticalSliceScenario | MatchRuntime |
| VerticalSliceRuntimeSettings / Services | MatchRuntimeSettings / Services |
| VerticalSliceScenarioSettings / Profile | MatchScenarioSettings / Profile |
| VerticalSliceAuthoritativeSnapshot | MatchAuthoritativeSnapshot |
| PersistedVerticalSliceConfiguration | PersistedMatchConfiguration |
| PrototypeBattlefieldDefinition / Runtime / Validator | BattlefieldDefinition / Runtime / Validator |
| PrototypeBattlefieldTerrainFactory | CentralDivideTerrainFactory (authored preset) |
| PrototypeRoadAccessSystem | RoadAccessSystem |

## Persistence and compatibility

The envelope format remains 1. New save/replay payloads use schema 2 and include a composition key and per-player opponent policies. Schema 1 remains readable: after verification of the original envelope payload checksum, missing composition identity maps to `central-divide.directorate.v1`, and WestOpponent/EastOpponent migrate to policy keys 1/2. The authoritative checkpoint schema remains 1; default participant ordering, domain names, map key `prototype.vertical_slice`, asset IDs and command discriminators are retained so existing checkpoint hashes remain comparable. Public C# type names were never serialized as discriminators.

Custom composition saves/replays require their host-supplied resolver. The resolved key must equal the saved key. Unknown keys fail closed; no silent Central Divide fallback is permitted. Reconstruction replays the commands in a temporary runtime and verifies tick, RNG and authoritative state before exposing it. A composition key identifies its map/content/system revision; callers must change the key when those inputs change. The resolver must keep older content revisions available for the desired compatibility window. Delegates and catalogs are not serialized into save files.

The canonical CLI preset is `--scenario central-divide`; `--scenario vertical-slice` remains a compatible alias. Canonical soak tooling is `build/Run-MatchSoak.ps1` and `.github/workflows/match-soak.yml`. The legacy script remains a forwarding entry point for existing automation. New headless execution reports use schema 2 and expose a participant array instead of West/East fields; report consumers must migrate to participant IDs. Historical authored-content descriptions and dedicated historical test fixture names are retained where accurate.

## Verification

Composition tests run an independent 256-metre three-start map with participant IDs 7, 11 and 19, empty catalogs, a custom spawn adapter, flat terrain and explicit objective-system selection. They verify participant ownership, intelligence domains, hosted/headless checkpoints, custom-resolver save/replay round trips and rejection of unknown/mismatched composition identities. Existing Central Divide profile, command, lifecycle, seeded progression and recovery suites continue to qualify unchanged gameplay behavior.

ClientSessionFactory accepts supplied MatchRuntimeSettings and retains host-owned scheduling; the current interactive front end selects the Central Divide preset. Custom saves can be restored through the same supplied composition resolver.
