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

## File Format

`MatchPersistenceSerializer` owns format version 1.

Every save or replay document contains:

- the fixed `FORGELINE_MATCH` header;
- a format version;
- a document kind (`Save` or `Replay`);
- the serialized payload;
- a SHA-256 checksum of the payload.

Writes use a temporary file followed by atomic replacement. A partially written temporary file is never treated as the requested save path.

The three version boundaries have separate responsibilities:

| Boundary | Current version | Contract |
| --- | ---: | --- |
| Document envelope | 1 | Header, document kind, serialized payload, and payload checksum |
| Save/replay payload | 6 | Configuration, recorded command history, and checkpoint/replay metadata |
| Authoritative simulation state | 6 | Deterministic reconstruction and checkpoint comparison |

New captures use payload schema 6 and authoritative schema 6. The reader accepts supported payload schemas 1 through 6, but loading or replaying also requires authoritative schema 6. An older payload layout being readable does not establish simulation compatibility. The schema-1 composition migration still runs only after the envelope checksum is validated and does not upgrade an old authoritative checkpoint.

Authoritative schema 6 includes the completed-building combat contract, power-gated radar observations, and building destruction's economic, logistics, and navigation cleanup. These rules change reconstructed entities and simulation outcomes. Saves carrying authoritative state schema 5 and replays declaring `AuthoritativeSchemaVersion = 5` therefore fail with `IncompatibleVersion` before runtime reconstruction, even when their document and checkpoint checksums are valid. Earlier incompatible state schemas remain rejected as well.

This simulation-contract change adds no payload fields or command kinds, so the payload remains at schema 6 and the envelope remains at format 1. No migration rewrites historical checkpoint hashes or treats former combat/radar behavior as current behavior.

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
- RNG state mismatch.

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

## Tactical supply and accepted convoy checkpoints

MatchPersistenceTests now exercises active grouped partial resupply, a replacing Hold order and subsequent navigation through supported recorded player adapters. A second explicit finite-stock composition issues two real stock policies, accepts a loaded convoy with an escort, records manual detachment, and completes both 200 Fuel and 200 Ammunition deliveries. Both cargo orders finish, source reservations return to zero and normal dock proximity releases the temporary group. Remote Reserved/Busy visits retain their normal queue/slot owner until physical approach.

Each active checkpoint serializes saves and replay documents, reconstructs with the matching composition resolver and compares complete authoritative hashes immediately and after 1, 8 and 20 cumulative continuation ticks. These cases add no persistence exception, command type or serialized component. Authoritative schema 6 and the existing envelope/payload versions remain unchanged; the appended navigation budget failure uses the existing failure-reason enum field.
