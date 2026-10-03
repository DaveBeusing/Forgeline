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

The current reader accepts only its current format/schema version. Unlimited backward compatibility is not implied.

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
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario vertical-slice --profile validation --ticks 5000 --seed 2026 --save-output artifacts/match.save.json --replay-output artifacts/match.replay.json
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
- tampered checkpoint rejection;
- RNG divergence rejection;
- repeated save/load resource stability;
- a headless save/replay recovery smoke in CI.

The full repository build, tests, and existing natural terminal-match validation remain required alongside the focused persistence checks.
