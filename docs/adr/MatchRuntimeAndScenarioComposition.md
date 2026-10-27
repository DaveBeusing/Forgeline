# Match runtime and scenario composition

## Decision

`MatchRuntime` owns one authoritative RTS match and its explicitly owned scheduler. It accepts `MatchRuntimeSettings` containing a `MatchComposition`, participants, seed, scheduler policy, diagnostics and explicit gameplay/validation rules. Both client and headless execution use this contract. Presentation reads completed snapshots and never owns simulation mutation.

`MatchComposition` identifies a reproducible composition and supplies authored `BattlefieldDefinition`, content catalogs, terrain construction, starting-base construction and system selection/registration order. These are inputs to the existing RTS systems, not a second simulation framework. Map dimensions, spawn/player IDs, catalogs and selected systems are not chosen by the shared runtime. Combat currently requires each participant's combat faction to equal its player ID; this existing rule remains validated, with arbitrary valid IDs supported.

`CentralDivideScenario` supplies the current Central Divide/Directorate preset, its two starts and its explicit gameplay and validation profiles. `CentralDivideBattlefield` and `CentralDivideTerrainFactory` are authoring conveniences. Productive composition comes from ScenarioRegistry and deployed format-3 map data, with a bounded version-1 terrain recipe; CentralDivideStartingBaseFactory remains the explicit executable bootstrap rule. Catalog validation specific to Directorate occurs in that preset. `BattlefieldValidator` validates reusable structural integrity rather than requiring a particular map size, resource layout or crossing count. Strategic geography and preset tests continue to qualify the authored map.

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

The envelope remains format 1. Payload schema 11 includes splitter routing commands, complete compiled scenario identity and typed developer action/recovery and paid bridge repair and queued unit-order records; authoritative state uses schema 15 for opponent progression, bounded splitter priority ordering, connected vehicle routing and fleet scheduling/cache work and bridge combat/repair and queued orders/patrol; earlier checkpoints are explicitly incompatible. Payload schemas 1–7 retain their supported layout migrations and require current authoritative semantics. Schema-1 policy/composition migration occurs after original checksum verification. Legacy Central Divide resolution is frozen to the terrain/map/content revision proven equivalent to master cb58c3ad; changed data requires historical resolution or explicit incompatibility. No historical state hash is rewritten. See [current recovery contract](../SaveLoadReplayAndRecovery.md).

Custom composition saves/replays require their host-supplied resolver. The resolved key must equal the saved key. Unknown keys fail closed; no silent Central Divide fallback is permitted. Reconstruction replays the commands in a temporary runtime and verifies tick, RNG and authoritative state before exposing it. Custom composition keys identify their map/content/system revision; their hosts must version changed inputs. Registered productive scenario keys remain stable and carry the separately versioned ScenarioIdentity. The resolver must keep older content revisions available for the desired compatibility window. Delegates and catalogs are not serialized into save files.

The canonical CLI preset is `--scenario central-divide`; `--scenario vertical-slice` remains a compatible alias. Canonical soak tooling is `build/Run-MatchSoak.ps1` and `.github/workflows/match-soak.yml`. The legacy script remains a forwarding entry point for existing automation. New headless execution reports use schema 2 and expose a participant array instead of West/East fields; report consumers must migrate to participant IDs. Historical authored-content descriptions and dedicated historical test fixture names are retained where accurate.

## Verification

Composition tests run an independent 256-metre three-start map with participant IDs 7, 11 and 19, empty catalogs, a custom spawn adapter, flat terrain and explicit objective-system selection. They verify participant ownership, intelligence domains, hosted/headless checkpoints, custom-resolver save/replay round trips and rejection of unknown/mismatched composition identities. Existing Central Divide profile, command, lifecycle, seeded progression and recovery suites continue to qualify unchanged gameplay behavior.

ClientSessionFactory accepts supplied MatchRuntimeSettings and threads the scenario resolver through new, owned asynchronous and restore paths. The setup selects a named registered scenario and disables unavailable content. Restart carries seed, participants and identity into a fresh resolved session with host-owned scheduling. Custom saves retain the explicit composition resolver.
