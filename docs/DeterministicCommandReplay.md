# Deterministic command replay

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The existing versioned journal and headless playback are the replay foundation.
Use the current matching scenario/content and authoritative simulation schema;
the journal is not a renderer recording or a cross-hardware lockstep guarantee.

## Existing recording and identity

MatchReplayRecorder starts before tick zero. SimulationCoordinator validates
queue admission before publishing CommandSubmitted. Each supported admitted
envelope retains target tick, queue sequence/source, stable issuer and complete
entity index/generation, plus typed command data. RecordingOrder also orders
completed-tick control commands. The existing codec covers movement, combat,
production, logistics, research, construction, operations, conveyors, bridge
repair and queued orders.

An admitted command can later fail domain validation or remain pending.
Those entries must stay in the journal: filtering them by their later result
would change sequence numbers, pending work and deterministic reconstruction.
Internal fixed-tick decisions are regenerated from configuration/state/RNG.
Unsupported command types mark history incomplete and prevent unsafe capture.
This change does not relocate recording or introduce another journal.

Replay headers retain initial configuration, seed, participants, exact scenario/
map/content/rule identity, payload and authoritative schema, final tick/RNG,
lifecycle and complete authoritative SHA-256. Envelope format 1, payload 11,
authoritative schema 18 and the 128-MiB complete escaped UTF-8 file bound remain
unchanged. Integrity and compatibility checks precede exposing a candidate.

## Headless load, playback and stop

After the canonical Release build:

```powershell
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile validation --ticks 128 --seed 2026 --replay-output artifacts/short.replay.json
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --replay-input artifacts/short.replay.json
```

Both --replay-input and --load-input now participate in the existing console
shutdown scope. Ctrl+C requests cooperative cancellation. A cancelled candidate
is disposed, no verified runtime is returned, the input file is unchanged and
the application prints Headless execution cancelled. The application cancellation
path returns code 2; a parent shell receiving the same console interrupt can
report its own interrupted-command exit instead. Corrupt/incompatible/divergent
recovery still uses the existing controlled failure path.

The library retains PlayReplay and PlayDeveloperReplay. New
PlayReplayCancellable and PlayDeveloperReplayCancellable accept a token and
optional MatchRestorationProgress observations, matching existing save recovery.
Phases are Configuration, ScenarioAssembly, Replay and HashVerification.
Replay reports at the initial boundary, every 64 completed ticks and the final
boundary, after all controls at that tick. Cancellation is checked before
construction, during existing assembly/scheduling, at tick/control boundaries,
around requested comparisons and before ownership transfer. Reading/parsing and
individual hash computations remain bounded synchronous operations.

The public path still rejects developer interventions. The Developer path
requires the same compiled capability and verifies the same ordered parameter
audit. Diagnostics/persisted markers cannot grant permission. Neither API
mutates an unrelated live session.

## Optional intermediate comparisons

A reproduction owner can sample MatchReplayCheckpoint.Capture(runtime) on its
simulation owner after a completed tick and its complete control batch, then
pass the samples to PlayReplayCancellable through checkpoints. No samples are
collected by default or added to normal match hot paths.

```csharp
var samples = new List<MatchReplayCheckpoint>();
// At selected completed-tick/control boundaries on the original owner:
samples.Add(MatchReplayCheckpoint.Capture(runtime));
// After the original run has completed:
var journal = MatchPersistenceService.CaptureReplay(runtime);
using var verified = MatchPersistenceService.PlayReplayCancellable(
    journal, cancellationToken, checkpoints: samples);
```

Schema-1 samples contain tick, RNG state and a comparison SHA-256. Up to 4096
strictly increasing, unique ticks from zero through the recorded final tick are
accepted. Missing/null entries, malformed hashes, duplicates, out-of-range ticks
and excessive counts fail before content resolution or candidate creation.
Unknown sample schemas report IncompatibleVersion. Input collections are copied
before reconstruction.

Intermediate hashing uses the existing authoritative snapshot with only the
simulation.pending_commands count domain omitted. Existing playback pre-schedules
commands submitted later in the original run; this admission observation can
therefore differ at an earlier checkpoint while gameplay state is identical.
All other snapshot domains, ECS/inventories, lifecycle, tick and RNG remain in
the intermediate hash. This is a diagnostic comparison identity, not the full
saved checkpoint identity. No historical hash is rewritten or new snapshot
schema substituted.

Every requested comparison runs after the complete same-tick control batch.
A difference reports StateMismatch or RandomStateMismatch with the exact tick.
The final existing checks still compare the complete unfiltered authoritative
hash, final RNG, lifecycle and Developer audit. Passing optional samples cannot
waive final integrity or ownership/capability checks.

Samples are optional in-memory developer/headless verification inputs. There
is no new replay file format, automatic sidecar import, spectator/player export
or visibility policy. The API returns no enemy entity, position, inventory,
presentation frame or private state payload. Existing recovery data remains
internal reproduction data until a separate verified player visibility policy
permits broader export.

## Determinism and qualification

Stable tick/queue/control order and schema/content identity support repeatable
reproduction on the qualified build/runtime. Floating-point behavior, changed
runtime/platform, engine semantics or content can still diverge; detection is
explicit rather than a bit-perfect cross-machine guarantee.

Run the existing MatchPersistenceTests, PersistenceBoundaryTests,
CancellableMatchRestorationTests and new CancellableCommandReplayTests.
The new mixed-input fixture includes late submissions and same-tick pause/resume,
compares periodic samples and the unchanged full final hash, then continues
both runs. Existing coverage retains real combat casualties, loaded convoy/
resupply, destroyed IDs, malformed/truncated content, version mismatch,
atomic output preservation and pending commands.

See [executed qualification](CommandReplayQualification.md) and the
[replay verification decision](adr/CancellableCommandReplayVerification.md).
