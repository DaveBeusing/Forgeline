# Developer session controls

`ClientSimulationHost` is the sole simulation owner. Its real
`IDeveloperSessionController` is exposed as `host.DeveloperSessions` in Developer
builds. Public builds exclude `DeveloperTools/Implementation`; they expose an
unavailable Foundation provider and cannot activate the executor.

## Composition and authorization

Trusted client composition binds `DeveloperToolsCapability.Current.BindSession`
to the actual `SimulationSessionId` and passes that access to the host constructor
as `developerAccess`. The default is inactive. `TrySetDeveloperAccess` queues a
replacement binding or revocation (an inactive binding for the same session).
It is applied at an owner boundary. Tokens from other sessions are rejected.
The caller must retain the exact bound token; another active token does not grant
access. Flags, presets, saves and menu visibility do not activate this service.

The Foundation gate checks capability, activation, session, correlation-bearing
request, revision, registered operation, target and typed payload at admission and
execution. The host recaptures its own session revision; the caller's execution
context is never authority. Only the global target and no parameter are valid.
Every request has a nonempty correlation; duplicate outstanding correlations are
rejected. Effective changes advance the Session revision. Tick progression alone
does not invalidate an ongoing control request.

`SubmitDeveloper` returns a concrete receipt with the Foundation result, host
sequence and typed failure. The Foundation interface returns the same result.
Accepted means enqueued. `TryReadDeveloperCompletion` consumes the correlated
boundary outcome. Completions retain their capacity reservation until consumed;
player requests, queued controls, an active series and unread results share the
host's existing bounded capacity. Queue overflow does not evict prior results.
Each series occupies one request, not one request per tick. Pause-owner and
access changes use fixed coalescing mailboxes so a full queue cannot prevent a
window/loading blocker or revocation from stopping a series.

## Pause ownership and menu handoff

The compatible `TrySetPaused(bool)` controls only the Legacy reason. New
composition uses separate `MenuPause`, `WindowPause`, and `SessionLoadingPause`
handles. Each changes only its own reason, asynchronously between full ticks.
Developer Pause/Resume changes only Developer ownership. Terminal, fault and
stopping observations have precedence. An initialized paused restore retains
Legacy ownership until its existing caller releases it.

The Menu handle additionally offers `TransferToDeveloper(access, revision)` and
`RestoreFromDeveloper(access, revision)`. They submit ordinary correlated
Pause/Resume envelopes through the same bounded queue. Transfer requires actual
Menu ownership, then atomically replaces Menu with Developer. Restore cancels
remaining steps, restores Menu and releases Developer. Repeated transfers are
idempotent. `MenuPauseTransferred` becomes true only after the owner executes the
transfer; the UI must inspect the completion/status, not mark it on enqueue.
Developer Resume can then run while the menu remains open. Closing/releasing
the developer backend restores ordinary Menu ownership before the menu owner
decides whether to release that reason. Window/loading reasons always remain.
Revocation restores a transferred Menu reason, cancels remaining steps and
releases only Developer ownership.

The menu composition owner must obtain the host after its creation, register
`host.DeveloperSessions` in `DeveloperToolsProviders`, consume completions, and
map copied `CaptureControlStatus()` fields into its backend snapshot. The current
`DeveloperProviderFactory`/`DeveloperMenuBackendFactory` handoff needs access to
that actual host; constructing a separate simulation or a successful placeholder
is invalid. `ClientApplication` and `ClientSessionFactory` are intentionally not
changed here. Their existing boolean pause callers still need explicit mapping
to these handles. `SetWindowBlocked` maps to `WindowPause.TrySetPaused`, and
backend release maps to the Menu restore operation. Simulation actions and
presets remain separate providers; this host does not implement their dispatch.

## Steps and lifecycle recording

SingleStep takes exactly one unsigned value: **1, 10 or 100**. It requires
Developer ownership with no Legacy, Menu, Window or SessionLoading blocker.
Only one series can run. The owner executes this sequence:

1. Execute `SetMatchPausedCommand(false)` via `ExecuteControlCommand` at the
   current completed tick, retaining Developer ownership locally.
2. Advance complete normal simulation ticks, publishing the existing snapshots.
   Drain bounded host messages and coalesced blockers between ticks. Recheck
   activation, stop, terminal and ownership before the next tick.
3. On completion or cancellation, execute `SetMatchPausedCommand(true)` at the
   last tick unless terminal state already owns the lifecycle. Resume first ends
   the series, then releases Developer ownership and starts normal pacing if no
   other owner blocks it.

These lifecycle controls use the existing recorded control-command path and
pause codec. There is no extra replay history. Speed, pause-owner mask,
permission, revision, remaining-step counts and host sequence are client-local.
Normal steps and speed changes do not set `DeveloperModified`. The simulation
actions/replay owner must preserve this control ordering when integrating its
central dispatcher and save/replay composition.

Completion reports requested and actually completed ticks, including zero on
cancellation. Failed ticks never increment completed counts. Rejected results
can still report partial progress in the concrete completion. Terminal ends a
series at the terminal tick: when at least one tick completed, the Foundation
result is Applied with actual AdvancedTicks and concrete Terminal stop reason.
No subsequent gameplay tick runs. A stop waits only for a currently executing
full tick, then resolves the series and queued requests. A fault preserves the
original failure. Every newly constructed host starts at 1x without inherited
steps or access. Already initialized paused/terminal sessions republish at their
current tick without advancing an extra gameplay tick.

## Host pacing and copied status

The authoritative clock remains **20 Hz, 50 ms per tick**. Allowed host speeds
are **0.25x, 0.5x, 1x, 2x and 4x**, supplied as one real-number value. Host intervals
are respectively **200, 100, 50, 25 and 12.5 ms**. Monotonic `TimeProvider`
timestamps use checked arithmetic and positive intervals rounded up to the
provider's precision. An actual resume or speed change begins a full interval.
Identical requests keep the current deadline. Pausing accumulates no time debt.
Catch-up remains bounded to five ticks; controls are drained between ticks.

`CaptureControlStatus()` returns an immutable copied record: session/revision,
requested speed, measured achieved speed, completed tick count and last complete
tick, unchanged tick duration, pause reasons, stepping/requested/completed/remaining
counts, activation and completed Menu transfer. Achieved speed is the simulated
duration of paced completed ticks divided by monotonic elapsed host time; manual
steps are excluded. It is unavailable outside a running measurement interval.
Slow hardware can report less than the requested speed without changing rules.
Neither status nor results expose live entities or mutable host collections.

## Validation scope

Implementation compiler checks cover the affected Client project in Public and
Developer builds. Package acceptance supplies focused host checks after the
implementation is complete. Full menu operation on hardware, Save/Replay across
all developer functions, and exported Public/Developer packages belong to shared
qualification. Required PR CI remains mandatory and is reported separately.
