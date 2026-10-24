# Save, Load, Replay, and Recovery

## Purpose

FORGELINE persists match recovery data from authoritative simulation state and deterministic inputs. Persistence never serializes renderer, window, UI, audio, or presentation ownership.

The current format is intentionally versioned for the current product stage. Unsupported older or newer formats fail explicitly rather than being guessed or partially applied.

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
| Save/replay payload | 7 | Configuration, recorded command history, and checkpoint/replay metadata |
| Authoritative simulation state | 8 | Deterministic reconstruction and checkpoint comparison |

New captures use payload schema 7 and authoritative schema 8. The reader accepts supported payload schemas 1 through 7, but discovery, loading and replaying also require authoritative schema 8. An older payload layout being readable does not establish simulation compatibility. The schema-1 composition migration still runs only after the envelope checksum is validated and does not upgrade an old authoritative checkpoint.

Authoritative schema 8 adds owned radar identification effects, artillery production capability gating, bounded paid opponent research and its supply-established fact. Valid schema-7 saves/replays are explicitly incompatible; reconstruction cannot silently reinterpret earlier production or sensing rules. Envelope 1 is retained; payload 7 records the compiled scenario identity.

The preceding authoritative schema 7 retains the completed-building combat contract, power-gated radar observations, and building destruction's economic, logistics, and navigation cleanup. It adds bounded raid intent, stable member/contact identities, the contact cursor and tactical delegation policy. These rules change reconstructed entities and simulation outcomes. Saves carrying authoritative state schema 6 and replays declaring `AuthoritativeSchemaVersion = 6` therefore fail with `IncompatibleVersion` before runtime reconstruction, even when their document and checkpoint checksums are valid. Earlier incompatible state schemas remain rejected as well.

The raid simulation-contract change introduced no payload fields. Compiled scenario loading subsequently adds ScenarioIdentity to configuration and raises the payload to 7; the envelope remains format 1 without changing the then-current authoritative schema 7. The research integration now requires authoritative schema 8. No migration rewrites historical checkpoint hashes or treats former combat/radar behavior as current behavior.

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

Operations controls retain the existing OperationsRule command codec and envelope versions. Their original schema-6 checkpoints now run under authoritative schema 8, which includes raid intent and technology progression. Corrected same-tick manual pause precedence is exercised by both command orderings; reconstruction retains normal state/hash verification rather than accepting an unverified history.

## Tactical supply and accepted convoy checkpoints

MatchPersistenceTests now exercises active grouped partial resupply, a replacing Hold order and subsequent navigation through supported recorded player adapters. A second explicit finite-stock composition issues two real stock policies, accepts a loaded convoy with an escort, records manual detachment, and completes both 200 Fuel and 200 Ammunition deliveries. Both cargo orders finish, source reservations return to zero and normal dock proximity releases the temporary group. Remote Reserved/Busy visits retain their normal queue/slot owner until physical approach.

Each active checkpoint serializes saves and replay documents, reconstructs with the matching composition resolver and compares complete authoritative hashes immediately and after 1, 8 and 20 cumulative continuation ticks. These cases add no persistence exception, command type or serialized component. Those tactical cases retained the then-current authoritative schema 6 and existing envelope/payload versions; the appended navigation budget failure uses the existing failure-reason enum field. Current authoritative compatibility is schema 8 as specified above.

The active-detour recovery fixture uses a reproducible 64 m flat battlefield with a wall across the direct path, normal player movement and the production navigation/movement systems. It verifies an active refined route outside the blocked span, save/replay hash equality immediately and after 1, 8, 20 and 620 cumulative ticks, then normal arrival with no pending or active route.


### Active logistics raid reconstruction

The bounded raid intent is an ordinary authoritative component. Member and target generations, contact cursor, last known/ordered coordinates and stable tick deadlines participate in the complete hash. Candidate rankings and debug caches are derived. Existing tactical player commands and internal opponent scheduling regenerate the intent without a new external command codec.

`SkirmishRaidMatchTests` serializes an active normal-stock raid through both Save and Replay, resolves the authored composition, and compares complete authoritative hashes and the intent after increments of 0, 1, 7 and 24 ticks. This also detects duplicate group creation during reconstruction. `PreRaidCheckpointsAreRejectedBeforeReconstruction` verifies explicit rejection of authoritative schema 6; readable payload schema 6 is not permission to reinterpret old simulation state. See [bounded raid behavior and its scene parameters](SkirmishOpponent.md#bounded-logistics-raids).
