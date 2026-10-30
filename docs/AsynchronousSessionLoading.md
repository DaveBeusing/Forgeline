# Asynchronous match session loading

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

## Construction and ownership

After New Game, Continue or Load Game, the Windows client starts one dedicated session-loading thread. It performs configuration validation, CPU scenario assembly, save reading and deterministic reconstruction. Independent startup products remain reused; session retries do not reload the runtime asset catalog.

For a new game, the worker creates the host-owned job scheduler. `ClientLoadedSession` owns both the complete runtime and that scheduler, disposing the runtime before the scheduler. Restore preserves the existing headless reconstruction scheduler policy. A result transfers once through the coordinator after construction succeeds; abandoned, cancelled or failed results are disposed after the construction owner completes. Concurrent disposers wait for that owner. No worker accesses mutable window state, UI controllers, D3D12 objects or renderer resources.

The platform owner binds the command gateway and presentation observers only after transfer. The simulation owner then publishes its first completed-tick snapshot. Simulation and gameplay graphics initialization are asynchronous; the platform continues pumping input, window events and copied resize/suspension state while waiting. The original graphics thread continues from frontend rendering into gameplay initialization using its existing device and swap chain. Frontend renderer resources retire on that same creating thread; the displayed loading frame remains until gameplay presents. No graphics device transfers between threads. Gameplay input is enabled after a non-occluded successful gameplay presentation with the requested simulation session identity. Renderer initialization, published requests and loading overlays alone cannot qualify activation.

## Progress and deterministic restore

Immutable progress identifies configuration, scenario assembly, replay, authoritative verification, presentation binding and renderer readiness. Configuration/assembly/verification/graphics stages are indeterminate. Replay progress is the actual completed tick divided by the saved target tick, reported initially, every 64 complete tick/control boundaries and at the final tick. A zero-tick checkpoint remains indeterminate. Progress is not inferred from elapsed time and does not alter simulation pacing or command order.

`MatchPersistenceService.RestoreCancellable` preserves the existing `Restore` API. Cancellation is checked before construction, during command scheduling and between complete replay tick/control boundaries. A running tick is completed rather than partially applied. The existing recording-order, scheduled-sequence, pause/resume control, RNG, checkpoint checksum and reconstructed authoritative hash checks remain mandatory. The replay-control correction from PR #132 is already integrated; no invalid controls are skipped. Source save files are read only and never silently repaired or overwritten.

## Cancellation and recovery

Press Escape during loading to return to the menu. Construction observes cooperative cancellation; the platform continues pumping while the worker finishes. During simulation/graphics preparation it requests owner shutdown and pumps until those owners stop, then disposes and joins them before returning. Window close cancels construction and joins all owners before releasing the runtime. CPU construction and graphics API calls that cannot be interrupted finish on their owners; no thread is forcibly terminated.

Corrupt/incompatible saves, replay failures, state verification, configuration, presentation and graphics errors have separate categories. A pre-activation failure disposes the candidate session and returns the interactive client to its existing menu, with a concise user-facing explanation. Full cause and exception details remain in the session failure log. A valid retry creates a fresh coordinator and runtime. Errors after gameplay activation preserve the existing terminal host failure behavior. Smoke mode propagates failures to its exit/report path rather than hiding them through menu recovery.

## Diagnostics and qualification

Each request/retry receives a new positive SessionId within the launch. Previous failure events remain historical; current session readiness resets to Pending. Application readiness from a previously presented interactive menu is retained while a candidate session fails or is cancelled. SessionRuntimeReady denotes completed verified CPU construction; FirstGameplayFrame/SessionReady denote actual matching gameplay presentation. Bounded diagnostic collection stores phase transitions, not one event per replay tick. See `StartupDiagnostics.md` for event definitions.

Local validation includes 18 client session-loading cases and six cancellable restore cases: single transfer/disposal, concurrent shutdown, both enabled/disabled instrumentation paths, deliberate worker stalls, cancellation at all reconstruction phases and during queued native graphics continuation, native loading-overlay exclusion, actual gameplay handoff, a 1,024-tick replay with continued event pumping, checkpoint hash equality and deterministic continuation. Corrupt, incompatible and divergent file loads preserve the exact original bytes and allow a separate valid retry; paused restoration resumes through the validated control path. The 11 existing persistence cases and all 179 client cases passed.

Both bounded Windows gameplay smoke paths passed after the retained-swap-chain handoff: default smoke arguments and `--skip-splash --render-stress 1000` with visual qualification. Each session used one graphics device throughout loading and gameplay. These smoke paths deliberately skip the intro and cannot qualify interactive splash/menu behavior.

The canonical Release solution build now passes with zero warnings/errors after repairing the application's truncated icon directory. The full-solution run after updating stale asset, overlay, tactical and technology fixtures records 1,074 total: 1,072 passed, no failures, and two opt-in GPU cases skipped. The research persistence fixture constructs its power supply through recorded gameplay commands; deterministic replay/hash checks remain mandatory. Windowed and borderless smoke checks retain one terrain draw per visible chunk and at most eleven instance batches. See [CI repair qualification](CiRepairQualification.md) for the follow-up validation.

Before release, complete interactive New Game/Continue/Load Game, Escape at each preparation stage, window close/minimize/restore, repeated invalid-save/valid-save attempts, and settings-driven restart on supported Windows hardware. The native integration tests exercise actual owner/presentation boundaries, but do not replace full-client visual/lifetime qualification or packaged-install checks. No startup speedup is claimed.

## Recovery preflight and active-time resume

Save discovery and actual restoration use the same content/schema validation before constructing a candidate. Corrupt/incompatible entries cannot suppress a healthy neighboring file. Candidate assembly, replay and hash verification remain separately cancellable; cancellation never transfers the candidate to presentation/render ownership. A retry creates a new session identity and leaves original save bytes and any independent active runtime unchanged.

Intentional pause in the running host resets its next deadline only when the accepted state actually resumes. It grants one full active tick interval; partial pre-pause time and paused wall time are discarded. Running lag still uses bounded catch-up. Menu/help and minimized/empty-window reasons remain aggregated by the application; interactive overlap, input capture and graphics ownership need physical acceptance beyond controlled-clock tests.

## Bounded reconstruction measurement

Reproduce from a Release build with the benchmark host:

```powershell
dotnet run --project benchmarks/ForgeLine.Simulation.Benchmarks/ForgeLine.Simulation.Benchmarks.csproj --configuration Release --no-build -- --recovery-costs artifacts/recovery-costs.json
```

The retained [raw report](RecoveryMeasurements.json) was produced from exact source/build `1466e26bd0bcb96946fa59f846bb4232acd9ae94`, `0.1.463-dev`, on 23 October 2026. Environment: Intel Core Ultra 9 285HX, 24 logical processors, Windows 10.0.26300 x64, .NET 10.0.13 and SDK 10.0.402 (permitted by global.json latestPatch). Power/thermal settings were not controlled. Asset compilation, focused tests and a separate validation match also ran during this observation. Timings are advisory single samples, not an isolated performance comparison.

One ordinary **Gameplay** match uses seed 2026, normal Standard starting stock, two normal computer participants and 20 Hz logical ticks. It runs continuously through all sample points; each restore creates a fresh runtime. Diagnostic/debug capture is disabled. Command counts are external recorded commands: zero here because internal opponent decisions are regenerated by simulation. No history is truncated.

| Saved tick | Logical minutes | Document bytes | Commands | Entities / inventories | Parse/preflight ms | Reconstruction ms |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 6,000 | 5 | 735,286 | 0 | 180 / 47 | 177.5301 | 6,631.5387 |
| 12,000 | 10 | 838,878 | 0 | 188 / 53 | 105.2004 | 22,196.3132 |
| 24,000 | 20 | 1,000,827 | 0 | 196 / 57 | 121.0915 | 40,594.9291 |

All three captures remain Running; saved/restored authoritative hashes and RNG match. Process-wide allocations during reconstruction are approximately 428 MB, 1.12 GB and 2.97 GB; these are cumulative allocated bytes, not retained/peak memory. Timing includes runtime creation, command scheduling, replay and final verification; string parsing/preflight is measured separately. No file I/O, render binding or first gameplay Present is included. JIT/caches are not reset between samples.

The retained recovery/loading and startup documents define no numeric restore-time product gate; therefore this is not reported as a passed or failed latency target. The observed roughly 41-second reconstruction for a 20-minute match is a concrete cost requiring product follow-up. Direct checkpoint reconstruction or a separate bounded-history/compression design would be a later architectural task. This change preserves complete deterministic reconstruction and does not claim a speedup.

Earlier loading-test counts above describe their historical implementation checkpoint, not current complete acceptance. Current source-specific results and final-head mandatory CI are recorded in the PR. Physical D3D12, live Pause/Help/Minimize overlap, input/graphics lifetime and human visual review remain separate evidence classes.

## Generated skirmish preparation

New Game validates generated maps on a separate instance of the existing construction coordinator before requesting a gameplay session. The frontend remains interactive and exposes cooperative Cancel, copied indeterminate stage state and actionable failures. A qualified result requires a separate Start action. Session reconstruction resolves the exact manifest identity from the host-owned package cache, checks current asset identity and builds CPU terrain/decoration presentation data before match allocation. Existing New/Load/Restart, scheduler, presentation and cancellation ownership remains in force. See [seeded skirmish selection](SeededSkirmishSelection.md) for input, cache, qualification and visual-acceptance limits.
