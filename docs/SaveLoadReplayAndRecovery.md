# Save, Load, Replay, and Recovery

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Purpose](#purpose)
- [Guided practice compatibility](#guided-practice-compatibility)
- [Recovery Model](#recovery-model)
- [Compiled Scenario Compatibility](#compiled-scenario-compatibility)
- [File Format](#file-format)
- [Save Payload](#save-payload)
- [Replay Payload](#replay-payload)
- [Supported Recorded Commands](#supported-recorded-commands)
- [Headless Usage](#headless-usage)
- [Failure Handling](#failure-handling)
- [Determinism Boundary](#determinism-boundary)
- [Validation](#validation)
- [Match composition migration](#match-composition-migration)
- [Intentional pause and host pacing](#intentional-pause-and-host-pacing)
- [Reliability evidence](#reliability-evidence)
- [Operations control checkpoints](#operations-control-checkpoints)
- [Tactical supply and accepted convoy checkpoints](#tactical-supply-and-accepted-convoy-checkpoints)
  - [Active logistics raid reconstruction](#active-logistics-raid-reconstruction)
- [Opponent progression compatibility](#opponent-progression-compatibility)
- [Conveyor routing compatibility](#conveyor-routing-compatibility)
- [Connected vehicle routing compatibility](#connected-vehicle-routing-compatibility)
- [Bridge combat and repair compatibility](#bridge-combat-and-repair-compatibility)
- [Queued unit orders and patrol compatibility](#queued-unit-orders-and-patrol-compatibility)
- [Fleet scheduling compatibility](#fleet-scheduling-compatibility)
- [Cover and visual occlusion compatibility](#cover-and-visual-occlusion-compatibility)
- [Committed inbound storage compatibility](#committed-inbound-storage-compatibility)
- [Cooperative replay verification](#cooperative-replay-verification)
- [Generated map package identity](#generated-map-package-identity)

</details>

## Purpose

FORGELINE persists match recovery data from authoritative simulation state and deterministic inputs. Persistence never serializes renderer, window, UI, audio, or presentation ownership.

The current format is intentionally versioned for the current product stage. Unsupported older or newer formats fail explicitly rather than being guessed or partially applied.

## Guided practice compatibility

Current captures use envelope 1, payload 12 and authoritative schema 20. Payload 12 adds optional PracticeProgress for the three registered guided practice compositions. Practice saves require matching lesson/version/player/tick and bounded completion timestamps; ordinary skirmish saves reject practice metadata. Practice replay requires payload 12. The existing authoritative compatibility rules for ordinary historical saves remain in force.

The loader reconstructs the normal commands and deterministic paid setup, then verifies derived objective status, failure and timestamps before returning the candidate. Practice snapshots are observations rather than a second authoritative checkpoint; their session identity is regenerated. Hints and reading state are local UI state and cannot certify saved completion. See [guided practice](GuidedStrategyPractice.md) and [the bounded observer contract](adr/BoundedGuidedPractice.md).

Loaded conveyor demolition introduces authoritative schema 19, retaining payload
12 and envelope 1. The owner-scoped bounded salvage inventory and its ECS owner
marker participate in regular inventory/entity snapshots and deterministic
command replay. Earlier schema-18 saves or replays are incompatible because a
previously rejected loaded-removal command now changes cargo ownership and the
world. Combat wreck creation still does not grant salvage.

The Refinery conveyor extension advances authoritative simulation schema to 20,
retaining payload 12 and envelope 1. The resource compatibility and reachable
routing rules now transport real Volatiles and Fuel through finite owned
inventories; old checkpoints are explicitly incompatible rather than silently
replaying newly eligible deliveries. Existing truck contracts and reservations
remain unchanged. No pipeline state or fluid network is introduced.

## Recovery Model

A save contains two complementary forms of evidence:

1. the deterministic inputs required to reconstruct the match: Vertical Slice configuration, seed, fixed-tick command history, and control commands applied at explicit tick boundaries;
2. an authoritative checkpoint captured from simulation state, including the simulation tick, RNG state, ECS entities/components, inventories, match lifecycle, logistics topology/state, distribution/cargo state, and faction-intelligence state.

Loading is transactional. The loader does not mutate the currently running match.

A fresh Vertical Slice runtime is created from the saved configuration. Queued commands are scheduled with their original target ticks and queue sequence. Control commands are applied at their original completed-tick boundary. The fresh runtime advances to the saved tick and must reproduce the saved RNG state and authoritative checkpoint hash.

Only a fully validated reconstructed scenario is returned. Corrupt, incompatible, incomplete, or divergent data disposes the temporary runtime and returns a controlled persistence failure.

The Windows client runs construction/restoration on a dedicated session-loading owner and keeps the frontend event pump active. `RestoreCancellable` exposes immutable configuration/assembly/replay/verification progress and cooperative cancellation at complete tick/control boundaries while preserving the existing `Restore` contract. Replay order, RNG and checkpoint/hash verification are unchanged. Failed or cancelled candidates return to the menu without modifying the source save; graphics and presentation readiness remain separate from reconstruction success. See [Asynchronous session loading](AsynchronousSessionLoading.md).

This model preserves stable entity identifiers and internal subsystem state through deterministic reconstruction while retaining a serialized authoritative checkpoint for corruption and compatibility diagnostics.

## Compiled Scenario Compatibility

Payload 7 captures map key, revision and complete artifact SHA-256, terrain recipe version, Directorate content key/catalog digest and starting-base/objective rule IDs. Restoration compares these with the resolved composition before allocating its terrain or runtime. Changed map/content/rules are IncompatibleVersion failures; no hash is rewritten to accept different geography. New canonical payloads without that identity are rejected.

Payloads 1–6 with current authoritative schema 7 may resolve the existing central-divide.directorate.v1 key only to the explicit frozen historical Central Divide identity. Full terrain/placement and tick-0/tick-20 parity against master cb58c3ad establish that compatibility. The historical target is separate from the current registration: a future map/catalog change cannot silently redefine legacy recovery. Earlier authoritative schemas remain incompatible. Existing custom in-process compositions retain their explicit host resolver; keys and any supplied identity must match, then full checkpoint/replay verification remains mandatory.

ClientSessionFactory passes the resolver through owned asynchronous loading and NewGame as well as Load. Restart requests carry the current identity, seed and participants and re-resolve before constructing a fresh session. Headless uses the selected registry for new/load/replay, including --scenario-root. See [compiled maps](StrategicMapAndOperationalGeography.md).

## File Format

`MatchPersistenceSerializer` owns format version 1.

Every save or replay document contains:

- the fixed `FORGELINE_MATCH` header;
- a format version;
- a document kind (`Save` or `Replay`);
- the serialized payload;
- a SHA-256 checksum of the payload.

Writes use a temporary file in the destination directory followed by atomic replacement. Save and replay share a **134,217,728-byte (128 MiB) UTF-8 document budget**, including the complete envelope, indentation, checksum and JSON escaping of the embedded payload. The bound measures the final bytes, not UTF-16 characters or unescaped payload size. In-memory serialization/deserialization share the bound; file reading also checks the opened length and rejects invalid UTF-8.

Content and size are validated before target replacement. Oversize output raises `DocumentTooLarge` with actual/allowed byte counts. The pause menu displays `SAVE FAILED - FILE TOO LARGE`; detailed byte counts remain in the save diagnostic. Validation, serialization, I/O and cancellation failure preserve the previous bytes; temporary files are cleaned in finally. `WriteSaveCancellable` / `WriteReplayCancellable` check cancellation before work, temporary output and replacement. Cancellation after the replacement commit point cannot undo a completed write. Command history is never truncated. Oversize input is corrupt.

The serializer and direct restore share runtime-free content/schema preflight. Required configuration, collection entries, entity/inventory identities, command metadata, finite coordinates/quantities, embedded checkpoint JSON and state checksum are checked before enabling Load. Positive infinite inventory capacity retains its existing unlimited-capacity meaning; quantities and command values must be finite. Custom composition keys remain opaque until an explicit resolver supplies their content.

The three version boundaries have separate responsibilities:

| Boundary | Current version | Contract |
| --- | ---: | --- |
| Document envelope | 1 | Header, document kind, serialized payload, and payload checksum |
| Save/replay payload | 11 | Configuration, recorded command history, and checkpoint/replay metadata |
| Authoritative simulation state | 18 | Deterministic reconstruction and checkpoint comparison |

New captures before developer action persistence use payload schema 7 and authoritative schema 9. The reader accepts supported payload schemas 1 through 11, but discovery, loading and replaying now require authoritative schema 18. An older payload layout being readable does not establish simulation compatibility. The schema-1 composition migration still runs only after the envelope checksum is validated and does not upgrade an old authoritative checkpoint.

Payload schema 8 adds typed developer simulation commands, an optional initial developer parameter profile and the ordered parameter audit. Developer action payload schema 1 records the operation, complete target identity, correlation, expected revision, typed finite values and whether execution was activated at its recorded tick. Commands remain in their original full sequence even when their later result is Rejected; replay therefore preserves rejected and pending command ordering. Developer initial parameters are separate from later command-driven changes, and recovery verifies the ordered audit and authoritative `developer.state.v1` contribution. Developer Save/Replay requires a compiled Developer host and explicit `RestoreDeveloper` or `PlayDeveloperReplay` capability. Public recovery rejects these payloads before handler execution; diagnostic flags and persisted markers do not create access. Developer persistence originally retained authoritative schema 9; later compatibility changes are listed below. See [Developer simulation actions](DeveloperSimulationActions.md) for the operation and host-binding contract.

Authoritative schema 8 adds owned radar identification effects, artillery production capability gating, bounded paid opponent research and its supply-established fact. Valid schema-7 saves/replays are explicitly incompatible; reconstruction cannot silently reinterpret earlier production or sensing rules. Envelope 1 is retained; payload 7 records the compiled scenario identity.

Authoritative schema 9 adds bounded transient combat-group lifetime and disposal of a destroyed unit's shared drive/ammunition inventory. Cargo reservation cleanup retains ownership of cargo inventories, and conveyor wreck retention is unchanged. Earlier authoritative checkpoints, including schema 8, are explicitly rejected rather than silently replaying different entity generations or stock ownership. Independent schema-9 captures qualify the seed-831/tick-4 presentation observer and compiled seed-2026/tick-0 and tick-20 checkpoints.

The preceding authoritative schema 7 retains the completed-building combat contract, power-gated radar observations, and building destruction's economic, logistics, and navigation cleanup. It adds bounded raid intent, stable member/contact identities, the contact cursor and tactical delegation policy. These rules change reconstructed entities and simulation outcomes. Saves carrying authoritative state schema 6 and replays declaring `AuthoritativeSchemaVersion = 6` therefore fail with `IncompatibleVersion` before runtime reconstruction, even when their document and checkpoint checksums are valid. Earlier incompatible state schemas remain rejected as well.

The raid simulation-contract change introduced no payload fields. Compiled scenario loading subsequently adds ScenarioIdentity to configuration and raises the payload to 7; the envelope remains format 1 without changing the then-current authoritative schema 7. Research introduced authoritative schema 8; current compatibility requires schema 18. No migration rewrites historical checkpoint hashes or treats former combat/radar behavior as current behavior.

## Save Payload

A save records:

- Vertical Slice scenario settings;
- deterministic seed;
- participant/start assignments;
- relevant runtime diagnostic flags and initial entity capacity;
- saved fixed tick;
- simulation RNG state;
- complete supported queued/control command history from match start;
- the authoritative checkpoint;
- the checkpoint SHA-256 hash.

The authoritative ECS snapshot is ordered by component type and entity identifier. Component values are serialized as stable checkpoint evidence rather than presentation snapshots.

Inventory snapshots retain identifiers, total capacity, total quantity, per-resource quantity, and reservations. This allows active production and logistics resource state to participate in recovery validation.

## Replay Payload

A replay records:

- initial Vertical Slice configuration and deterministic seed;
- all supported externally queued commands in original submission order;
- control commands at their original tick boundary;
- final tick and RNG state;
- final match lifecycle state;
- the authoritative simulation schema version;
- final authoritative-state checksum.

Replay playback creates a fresh runtime, schedules the recorded commands, applies recorded control commands at the matching tick, advances to the recorded final tick, and verifies the final lifecycle, RNG state, and authoritative checksum.

Internal fixed-tick system decisions are regenerated from authoritative state, configuration, and RNG rather than recorded as a second gameplay authority.

## Supported Recorded Commands

The current replay codec covers the player-facing command families used by the Vertical Slice:

- movement;
- building construction;
- processing production;
- unit production;
- logistics stock policies and resupply actions;
- tactical combat orders and artillery missions;
- surrender and match completion;
- match pause/resume control.

If a queued or control command type cannot be represented by the current replay codec, the recorder marks the command history incomplete. Save/replay capture then fails closed instead of producing a recovery file that could silently diverge.

## Headless Usage

Create both a save and replay from a bounded Vertical Slice run:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 5000 --seed 2026 --save-output artifacts/match.save.json --replay-output artifacts/match.replay.json
```

Validate a save by reconstructing it and comparing the authoritative checkpoint:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --load-input artifacts/match.save.json
```

Validate a replay by playing it to its recorded final tick:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --replay-input artifacts/match.replay.json
```

Persistence input validation is deliberately a dedicated headless mode. It is not combined with telemetry or diagnostic output generation.

## Failure Handling

`MatchPersistenceException` provides a stable failure category:

- corrupt document;
- incompatible version;
- invalid configuration;
- incomplete command history;
- unsupported command;
- authoritative state mismatch;
- RNG state mismatch;
- document too large (rejected output).

Malformed roots, null payloads, wrong schema types, missing required objects/collections and invalid persisted identities become controlled corrupt-document failures. Unsupported envelope/payload/state schemas are incompatible-version failures; invalid scenario settings use invalid-configuration. Discovery isolates these per file: healthy entries remain Available, incompatible entries disable Load, and other rejected entries are Corrupt with diagnostics. Preview neither deletes files nor mutates a live session.

Checksum/header/schema validation occurs before a runtime is exposed. Runtime reconstruction happens in a temporary scenario that is disposed on any failure.

Compatibility reports additionally identify high-level mismatches such as schema, battlefield, tick, RNG, lifecycle, entity count, component-store count, or inventory count before falling back to a general authoritative-state divergence.

## Determinism Boundary

Recovery guarantees are bounded by the project's current deterministic-friendly simulation guarantees and the matching repository/runtime schema.

A save is not a memory dump. It is a versioned authoritative checkpoint plus the deterministic history required to rebuild that checkpoint. This keeps platform/render/presentation objects out of persistence and makes the same recovery path usable by headless validation.

Future multiplayer persistence or replay protocols can build on the stable command/tick model, but they are not part of the current format.

## Validation

Repository validation covers:

- active-match save/load round trips;
- continued deterministic execution after restore;
- pending command recovery;
- replay reconstruction with recorded player commands;
- corrupted document rejection;
- format-version rejection;
- authoritative schema-5 save/replay rejection before reconstruction;
- tampered checkpoint rejection;
- RNG divergence rejection;
- repeated save/load resource stability;
- a headless save/replay recovery smoke in CI.

The full repository build, tests, and existing natural terminal-match validation remain required alongside the focused persistence checks.

`MatchPersistenceTests.PreIndustrialCombatCheckpointsAreRejectedBeforeReconstruction` covers schema-5 rejection for both saves and replays. Existing `MatchPersistenceTests` and `MatchCompositionTests` retain reconstruction, checksum, composition-resolution, and historical-format coverage. The building combat and radar behavior that motivates the new authoritative version is covered separately by `BuildingCombatInitializationTests`, `BuildingCombatDestructionTests`, and `RadarPowerTests`; changing the version does not replace those behavioral checks.

## Match composition migration

Composition identity and per-player opponent policies remain explicit payload inputs. The supported schema-1 layout migration supplies the preset composition identity and maps its opponent fields; it does not change authoritative simulation semantics. Historical checkpoints from before vehicle docking, physical queues, finite conveyor cargo, or the completed-building combat contract cannot be reconstructed under the current authoritative version. Their saved hashes and fixtures remain rejection coverage. Custom composition recovery requires an explicit key resolver and verifies the reconstructed state before returning it. See [Match Runtime and Scenario Composition](adr/MatchRuntimeAndScenarioComposition.md).

## Intentional pause and host pacing

An accepted paused-to-running transition rebases the monotonic deadline to **now + one full fixed-tick interval** (50 ms at 20 Hz), discarding the partial pre-pause interval. Repeated Resume while running does not postpone it. Paused time creates no catch-up debt; ordinary running lag retains the five-tick catch-up cap.

The application aggregates minimized/empty-window and pause-menu reasons before sending host transitions. Help within the pause shell follows that path. Terminal freeze remains independent of Resume. Recorded controls retain completed-tick order with no wall-clock timestamps.

Controlled-clock host tests cover long/repeated pauses, exact interval behavior, repeated Resume and ordinary lag. Synchronization timeouts retain the real system clock. Existing terminal, command-ownership and shutdown cases remain required. Physical Pause/Help/Minimize overlap, input capture and graphics acceptance remain manual.

## Reliability evidence

Base `master 4f1126dc` (0.1.462-dev) reproduces 10 failed / 4 passed initial boundary cases: malformed content escapes the persistence exception contract or missing members/schemas are accepted. With the original host deadline policy and only controlled clock injection, paused tick 1 becomes tick 3 before the post-resume assertion. The PR records fixed-suite results at their source revisions.

Coverage uses actual active production and pending commands (`MatchPersistenceTests`), loaded adapters with complete generation identity and continued save/replay hashes (`ConveyorEndpointRecoveryTests`), all reconstruction cancellation phases (`CancellableMatchRestorationTests`) and actual client candidate cancellation preserving an existing runtime/file before independent retry (`ClientSessionLoadingRecoveryTests`). Coordinator cases retain exactly-once transfer/disposal and joined worker ownership. These are distinct from physical D3D12 and human UI acceptance.

The original serializer, copied unchanged except for a class-name alias and an injected 512-byte reader budget, also reproduces the output-size defect: WriteReplay succeeds with a file above that budget and its own ReadReplay rejects the same file as CorruptDocument. The retained regression uses a small shared budget to test exact/one-byte-over boundaries and last-file preservation for both Save and Replay without a 128 MiB allocation fixture.

The recovery integration also includes the conveyor flow/vehicle-supply contracts merged in master 92e6a65a. Both focused CI groups and both measurement entry points are retained. Shared persistence validation does not replace conveyor ownership, accepted-load accounting or adapter replacement behavior. The added conveyor persistence cases exercise their production command payloads and deterministic continuation under the same schema-6 preflight. Historical measurements retain their original source revision.

## Operations control checkpoints

OperationsRuleControlsTests submits both existing rule templates through the production Operations UI request path, serializes saves/replays, reconstructs through the explicit composition resolver and compares full authoritative hashes and continued effects. The output case saves a real rule-owned pause plus a future downstream production command; normal Ammunition production consumes finite shared Steel/Electronics after loading, allowing the same paused Steel request to resume. Input policy creation remains distinct from physical supply. Local unapplied drafts, input timing and focus are presentation state and are discarded on session replacement.

Operations controls retain the existing OperationsRule command codec and envelope versions. Their original schema-6 checkpoint scenarios now run under authoritative schema 9, which includes raid intent, technology progression and lifecycle cleanup. Corrected same-tick manual pause precedence is exercised by both command orderings; reconstruction retains normal state/hash verification rather than accepting an unverified history.

## Tactical supply and accepted convoy checkpoints

MatchPersistenceTests now exercises active grouped partial resupply, a replacing Hold order and subsequent navigation through supported recorded player adapters. A second explicit finite-stock composition issues two real stock policies, accepts a loaded convoy with an escort, records manual detachment, and completes both 200 Fuel and 200 Ammunition deliveries. Both cargo orders finish, source reservations return to zero and normal dock proximity releases the temporary group. Remote Reserved/Busy visits retain their normal queue/slot owner until physical approach.

Each active checkpoint serializes saves and replay documents, reconstructs with the matching composition resolver and compares complete authoritative hashes immediately and after 1, 8 and 20 cumulative continuation ticks. These cases add no persistence exception, command type or serialized component. Those tactical cases retained the then-current authoritative schema 6 and existing envelope/payload versions; the appended navigation budget failure uses the existing failure-reason enum field. Current authoritative compatibility is schema 18 as specified below.

The active-detour recovery fixture uses a reproducible 64 m flat battlefield with a wall across the direct path, normal player movement and the production navigation/movement systems. It verifies an active refined route outside the blocked span, save/replay hash equality immediately and after 1, 8, 20 and 620 cumulative ticks, then normal arrival with no pending or active route.

### Active logistics raid reconstruction

The bounded raid intent is an ordinary authoritative component. Member and target generations, contact cursor, last known/ordered coordinates and stable tick deadlines participate in the complete hash. Candidate rankings and debug caches are derived. Existing tactical player commands and internal opponent scheduling regenerate the intent without a new external command codec.

`SkirmishRaidMatchTests` serializes an active normal-stock raid through both Save and Replay, resolves the authored composition, and compares complete authoritative hashes and the intent after increments of 0, 1, 7 and 24 ticks. This also detects duplicate group creation during reconstruction. `PreRaidCheckpointsAreRejectedBeforeReconstruction` verifies explicit rejection of authoritative schema 6; readable payload schema 6 is not permission to reinterpret old simulation state. See [bounded raid behavior and its scene parameters](SkirmishOpponent.md#bounded-logistics-raids).

## Opponent progression compatibility

Authoritative schema **10** changes opponent investment and recovery ordering:
unpaid active production inputs are protected from research/construction,
essential fleet replacement is separate from optional fleet growth, core assault
roles can reclaim an unpaid production slot, and optional expansion waits for the
live core force. Sensor Fusion yields to needed Mechanized Systems.

These decisions reconstruct different ordinary commands and entity histories.
Schema-9 and earlier saves/replays are explicitly incompatible; no historical
hash is rewritten and no old simulation is claimed reproducible. No new
authoritative component, command codec or payload member is introduced.
Envelope format 1 and payload schema 8 remain unchanged. Research/source debug
observations and bounded progression histories are derived and are not persisted.

## Conveyor routing compatibility

Splitter routing introduced authoritative schema **11**, payload schema **9**, envelope
format **1**. Payload 9 adds the typed ConveyorRouting command's preference and
expected revision. Its complete entity identity, issuer, original tick and sequence
use the existing command metadata. The ECS checkpoint includes ConveyorRoutingState
and its bounded successful-service cursor. Accepted and rejected command attempts
retain normal ordered reconstruction.

Schema-10 and earlier authoritative checkpoints/replays are explicitly incompatible;
their former fair-only rules are never silently reinterpreted as current priority
ordering. Readable older payload layouts do not waive that check. No historical
hash, registered scenario catalog digest or compiled map artifact is rewritten.
Repair retains the original policy/cursor; removing or replacing the full entity
removes the state. See [bounded priority routing](adr/BoundedConveyorPriorityRouting.md).

## Connected vehicle routing compatibility

Authoritative schema 12 adds the connected bounded Cargo/Supply routing decisions
and navigation.routing.v1 checkpoint: root/request counters, active-work fairness,
scoped congestion ages, suspended search stages and pinned cost penalties.
Pending and accepted bindings retain original goals, profiles and service
generations in the ECS checkpoint. Normal command replay reconstructs the same
fixed work slices and compares the complete checkpoint; no frontier is guessed
or silently restarted with a new budget on load. Earlier authoritative semantics
are explicitly incompatible, including schema 11 splitter/progression saves.
Envelope 1 and payload 9 remain unchanged. Historical hashes are not rewritten.
The existing total document-size and atomic replacement rules still apply.
See [the routing ADR](adr/BoundedVehicleRoutingIntegration.md). Manual recovery
and operation approval remain pending; no new test execution is claimed.

## Bridge combat and repair compatibility

Bridge combat introduced envelope **1**, payload **10** and authoritative schema **13**. Payload 10 adds BridgeRepair (command kind 17), preserving issuer, submitted/target ticks, queue sequence and source, complete engineer/bridge/material-source identities and expected infrastructure revision. Rejected and pending repair commands retain their original ordering. Execution validates the match-bound infrastructure service again; the UI cannot directly modify health, progress or stock.

The checkpoint includes the ordinary health, target and intelligence contracts plus BridgeCombat, any BridgeRepairWork and bridge-targeted projectiles' BridgeProjectileTarget. The projectile intent retains the full launch-target identity even after the firing unit changes orders or dies. BridgeRepairWork stores payer, engineer, paid material-source provenance, starting/expected health and pause reason; StrategicInfrastructureState owns the existing fixed-tick progress. Reconstruction creates the same neutral faction and bounded bridge contracts, replays weapon/repair inputs and compares the complete state/hash. Loaded cargo, orders, reservations and topology are reconstructed through their existing owners.

These contracts change tick-zero components and subsequent target/sensor/lifecycle behavior. Saves or replays declaring authoritative schema 12 or earlier are rejected with IncompatibleVersion before reconstruction. No historical map/content identity or checkpoint hash is rewritten. Reading an older payload layout does not make its old simulation state compatible. Raw host DisableStrategicInfrastructureCommand and noncombat RestoreStrategicInfrastructureCommand are still unsupported external history; ordinary bridge combat and RepairBridgeCommand are supported.

Manual acceptance remains outstanding, including recovery during disabled, actively restoring, engineer-paused and re-damaged states with cargo already travelling. No new test, smoke, benchmark or automated qualification was run for this integration.

## Queued unit orders and patrol compatibility

Queued-order integration introduced authoritative schema **14**, payload schema **11**, envelope **1**. The QueuedOrders command (kind 18) records the issuer, full unit identities, edit operation, Move/AttackMove/Attack kind, coordinate or target, formation and submitted tick. Replay reconstructs normal fixed-tick sequence progression. Patrol legs call existing commands directly on the simulation owner and do not grow the external command journal per cycle.

The ECS checkpoint includes the bounded per-unit queue's immutable entries, cursor, owner, repeating/active/wait/resume flags, status and issued tick. Limits are 16 retained entries per unit and 1,024 units globally. Current/next UI projections are observations, not additional persisted authorities. Queue attacks are revalidated through current faction intelligence on activation; historical knowledge cannot grant a hidden target.

Schema-13 and earlier authoritative saves/replays are explicitly incompatible. Existing payload layout reading does not waive that simulation check. QueuedOrders in payloads older than 11 fail validation before reconstruction. No migration invents pre-existing queues or patrol waypoints, changes old checkpoints or rewrites historical hashes. The preceding routing, inventory, reservation, dock, supply, ownership and lifecycle rules remain authoritative. See [queued orders and patrol](adr/BoundedQueuedOrdersAndPatrol.md).

No queue save/load/replay qualification is claimed from compilation. Manual review must include an active finite sequence, a multi-cycle patrol, an active resupply interruption, blocked routing, replacement/Retreat and subsequent continuation under the current formats.

## Fleet scheduling compatibility

Authoritative schema 15 supersedes the schema-14 routing semantics with the
connected fleet policy/admission ledger, exact suspended continuations and pinned
input publications. Arena initialization and total work remain correlated with
scheduler state. Derived route payloads retain exact cache order/expiry/access
state because lookup work can affect completion ticks. Pool warmth is not a
simulation decision. Authoritative JSON uses explicit data projections instead
of helpers whose constructors accept ReadOnlySpan, correcting the baseline
congestion-checkpoint metadata failure.

Normal recovery reconstructs these services through the original command/tick
path and compares the complete checkpoint. Earlier authoritative checkpoints are
explicitly incompatible. Envelope 1/payload 11, the 128-MiB document bound and
atomic replacement remain unchanged; no historical hash is rewritten.
See [the fleet scheduling ADR](adr/BoundedFleetNavigationScheduling.md).

## Cover and visual occlusion compatibility

Current captures use envelope **1**, payload **11**, authoritative schema **16** and registered map revision **2** / artifact format **4**. No new command fields are necessary: existing movement, attack and artillery inputs reproduce area occupancy, covered damage and permitted sensing. Current tick-derived trace admission adds no persisted cursor.

The map artifact and full registered digest include TacticalAreaDefinition entries. The immutable set is also captured as world.tactical_areas in the authoritative domains, so custom host reconstruction with different tactical geography cannot silently match a checkpoint. Radar Detected positions are rounded grid points, while an admitted Identified observation retains its precise permitted position and normal identity rules.

These are changed combat and perception semantics. Saves/replays declaring authoritative schema 15 or earlier are rejected with IncompatibleVersion before reconstructing a runtime, even if an older payload layout is readable. Format-3 and earlier compiled map artifacts are rejected explicitly. Map descriptors, JSON and sidecars advance together; historical map hashes, checkpoints and qualification evidence are not rewritten.

See [cover rules and manual acceptance](CoverAndVisualOcclusion.md). Product compilation could not run against the exact source tree in this environment, and no tests or qualification runs were performed for this integration. Manual Save/Replay acceptance remains outstanding.

The integrated fleet scheduler retains its explicit congestion, scheduler, cache and pinned-snapshot projections. Cover adds the world.tactical_areas domain without replacing those navigation authorities or changing their checkpoint fields.

## Committed inbound storage compatibility

Current authoritative captures use schema **17**, envelope **1** and payload
**11**. Accepted cargo across all resources now reduces shared destination
headroom for subsequent truck and conveyor admission. This changes dispatch
decisions, so schema-16 and earlier saves/replays are explicitly incompatible.
No historical checkpoint, map identity or hash is rewritten. Phase-local incoming
capacity is derived from live orders and actual remaining cargo, never persisted.
See [committed inbound admission](adr/CommittedInboundStorageAdmission.md).

## Cooperative replay verification

Headless load and replay now use the existing shutdown token. PlayReplayCancellable and PlayDeveloperReplayCancellable retain isolated reconstruction, capability and final full-hash checks, with optional completed-tick progress and bounded intermediate comparison samples. See [Deterministic command replay](DeterministicCommandReplay.md) for command admission semantics, sample normalization, stop behavior and determinism limits. Envelope 1, payload 11 and authoritative schema 18 remain unchanged.

## Generated map package identity

Explicit generated-map hosts first validate a GeneratedMapPackage with their expected asset content signature, then pass its CreateComposition result through the existing resolver. ScenarioIdentity.Generated binds generator version, map seed, gameplay/visual hashes, canonical manifest hash and asset identity. The complete identity is captured in saves/replays and compared before terrain/runtime allocation. Generated keys require this record; the default authored registry does not resolve them. No saved path, type or script is executed, and no fallback map is selected. Authored identities omit the optional field, retaining their existing bytes and compatibility gates. See [generated battlefield maps](GeneratedBattlefieldMaps.md) for package limits and CLI recovery examples.
