# Command replay qualification

## Source

Base master: 8749198f89ce1f1c90684c0bcb3f8a3398e28f45.
Product/CI source: ca26caa373cbfa66bf2abcf4f09c1e570a032184.
Later commits add documentation only. PATCH: 0.1.504 to 0.1.505, once for this PR.
SDK 10.0.402; C# 14; Windows x64 headless execution.

A fresh archive plus the exact 34 incoming non-version Git blobs reconstructs
the current master source without copying other feature branches. The local
qualification directory has no independent Git source attribute; the published
source and binary hashes below identify the producer.

## Existing functionality and repaired gap

The current master already has a versioned command journal, stable queue/control
ordering, initial configuration/seed/scenario/content identity, bounded
checksummed UTF-8 documents, isolated headless reconstruction and complete
final lifecycle/RNG/authoritative-state verification. The integrated journal
and persistence implementations are reused.

Before this change, Headless returned from --load-input/--replay-input before
installing its shutdown handler. PlayReplay exposed no token/progress API.
The new entry points share existing recovery phases and cleanup, route CLI
shutdown through the same owner, and optionally compare bounded intermediate
diagnostic samples. No journal, authoritative schema or save format is replaced.

## Executed validation

- Full public Release solution build: passed, zero warnings/errors.
- Developer Headless restore/build: passed, zero warnings/errors, using its
  separate Developer output/intermediate directories.
- New CancellableCommandReplayTests: 8 passed, 0 failed/skipped.
- Existing MatchPersistenceTests, PersistenceBoundaryTests,
  CancellableMatchRestorationTests, ScenarioPersistenceTests,
  ConveyorPersistenceTests and AutomaticProcessingPersistenceTests:
  77 passed, 0 failed/skipped.
- Total public: 85 distinct selected cases. Developer configuration additionally
  runs the same eight new cases and four DeveloperSimulationActionsTests:
  12 passed, 0 failed/skipped. Repeated cases are not presented as distinct.
- Project reference graph: 46 projects, no cycles or architecture violations.
- Existing Test-CompiledScenarioLoading passes real both-map startup/save/replay,
  final hashes and independent missing-content rejection.

The new mixed fixture records five entries across movement, logistics, tactical
and same-tick pause/resume controls, including later submissions. Nine samples
at ticks 0,16,...,128 compare successfully; the unchanged full final hash matches
and both simulations continue another 16 ticks with equal full hashes. This
checks that pre-scheduling future inputs does not create a false intermediate
divergence. Existing cases retain real combat casualties, loaded convoy/escort
replacement, resupply, timed docks, pending commands and deterministic continued
save/replay operation.

Cancellation is verified at Configuration, ScenarioAssembly, initial Replay,
mid-Replay tick 64 and HashVerification. Every cancelled attempt preserves the
source replay and the independent live state, then supports an independent
verified retry. Bad intermediate state/RNG samples report the exact tick;
duplicate/out-of-range/null/partial/oversized samples and schema mismatch fail
before the composition resolver. Valid samples cannot waive a bad final hash
or Developer capability checks.

Existing boundary tests retain empty/malformed/checksum/version rejection,
invalid identity/nonfinite payload preflight, exact escaped-UTF-8 budget edges,
last-good output preservation, cancellation and output failures. No assertion,
runtime policy, document bound, schema or terminal gate was relaxed.

## Actual console cancellation

A local qualification copy of an existing public replay had its final tick
extended to 1,000,000 and its envelope checksum recomputed. The final state hash
was intentionally left unchanged: completing that synthetic run would fail
final verification. This fixture tests interruption, not successful replay.

The real public Headless executable entered playback; Ctrl+C produced
Headless execution cancelled. and stopped promptly. The console host's interrupted
PowerShell command reported exit 1. The application cancellation branch returns
2, but a separate native process exit code was not captured in that console
qualification; no claim equates the host code with the application code.
The original replay remains untouched. No renderer/window/audio was initialized.

## Headless recovery identities

Seed 2026, tick 20, matching registered deployed maps:

| Scenario | Complete authoritative SHA-256 |
|---|---|
| Central Divide | 14D58A81A68CC2437CA714001BB323859E04A9F4D759D77BBF16F614A2606E99 |
| Split Basin | 12347A57B4A8196FCAB21E07D2D296DBF8880C1E2C9B1FF3A22579C7F4FC3AFB |

These match the synchronized-master recovery control from the existing compiled
scenario qualification. Intermediate hashes are explicitly different identities:
only their pending external-command count domain is omitted. Full final and
save/replay hashes remain unfiltered.

## Producer hashes

| Binary | SHA-256 |
|---|---|
| Public Game | 7c61d6e24a09562e52aff099b646a14f6398e967cbd05c316e2a70da913624d7 |
| Public Headless | 392cb73003974ddd13a71a5e251207ac3918202d192f608ff9d40910c6860d3b |
| Developer Game | e86e7c9c9c1e6da20de55708c092bafe9e430349734c9144d1dbf40d41978c1b |
| Developer Headless | 72d3cd3e0cc0e64c397262d02005f303ecb3f2e120db20488827c9ff5524bb45 |

Local logs: replay-restore.log, replay-build.log, replay-new-tests.log,
replay-existing-tests.log, replay-cli-recovery.log, replay-architecture.log,
replay-developer-build.log and replay-developer-tests.log.

Initial compilation identified cancellation-parameter ordering/performance
analyzer requirements and two fixture references to the wrong existing
component/runtime members. They were corrected using the actual owner contracts;
final builds pass with every analyzer enabled.

## CI and remaining acceptance

At product source, CI 40674560494 and four qualification workflows were running
with no failed steps; build-test was checking out the repository. Baseline
master CI 40667689410 was also still running. Its navigation, movement, conveyor
and source-audit workflows passed. These baseline observations are not results
for this feature's final head.

The early CI replay step now runs new cancellation coverage plus existing
journal/integrity/save-cancellation cases. Final documentation-head required CI
must pass independently. Local selected suites do not replace hosted full
solution, asset/package/native or natural-terminal gates. Cross-machine
bit-perfect determinism and a player-export visibility policy are not established.
Keep draft pending required CI and final review. No merge, tag or release.
