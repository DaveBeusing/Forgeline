# Client Execution Ownership

## Status

Accepted for the Windows interactive client.

## Context

The original Windows client pumped Win32 messages, advanced the authoritative simulation, consumed presentation snapshots, and submitted Direct3D 12 work serially from one host loop. The simulation and presentation data models were already separated, but execution was not. A slow simulation tick could therefore delay event pumping and rendering, while minimize behavior paused simulation only because the serial render loop returned early.

The current presentation boundary already publishes immutable completed-tick snapshots through `PresentationSnapshotBuffer`, and player command results already use a separate bounded consumptive buffer. Those contracts provide the data boundary required for independent execution.

## Decision

The interactive client uses three explicit long-lived owners:

1. **Platform owner** — the thread that creates `WindowsPlatform` and `IWindow`. It alone pumps Win32 messages, drains input/window events, reads mutable window state, updates presentation-only interaction controllers, and publishes copied render/input intent.
2. **Simulation owner** — a dedicated `ForgeLine Simulation` thread. It alone advances `SimulationCoordinator`, calls `PlayerCommandGateway`, mutates ECS/game state after startup handoff, and applies terminal control transitions.
3. **Render owner** — a dedicated `ForgeLine Render` thread. It creates and disposes the D3D12 device, swap chain, renderers, graphics resources, command submission, resize handling, and GPU-idle shutdown.

The existing `JobScheduler` remains a persistent worker pool below the simulation owner. Worker completion remains an explicit boundary inside a complete simulation command/system invocation; workers do not own platform or graphics APIs.

## Startup and handoff

Startup remains composed on the platform owner while authoritative state is not yet running:

1. create Windows platform/window;
2. create shared vertical-slice runtime and host-owned job scheduler;
3. perform optional render-stress fixture population before simulation execution begins;
4. register command-result and presentation tick observers;
5. create presentation-only interaction/camera state;
6. start the simulation owner, which performs the first complete tick and publishes the first immutable snapshot;
7. copy the HWND, initial client size, and suspended state into `GraphicsWindowTarget`;
8. start the render owner, which creates all graphics resources from that copied target.

After the simulation owner starts, the platform/render owners do not directly mutate or query live ECS, inventories, or simulation systems.

## Command and control transport

Platform-authored gameplay submissions cross a bounded host queue. Each accepted host message receives a monotonic host sequence and carries the expected `SimulationSessionId`. The simulation owner is the only thread that invokes `PlayerCommandGateway`; therefore `SimulationCoordinator.SubmitCommand` remains single-owner rather than becoming a generally concurrent API.

A queued gameplay submission is processed at most once. The gateway assigns the authoritative future target tick and existing simulation command sequence when the simulation owner drains it. Host admission fails explicitly when the bounded queue is full or stopping. Old-session and post-terminal submissions are rejected at the host boundary instead of touching the new/terminal simulation.

Submission receipts are copied back through a separate consumptive queue. Authoritative command results continue to use `PlayerCommandResultBuffer`; they are never stored in the latest render snapshot and therefore cannot be lost when visual snapshots are superseded.

Pause/resume and terminal acknowledgement are explicit control messages rather than side effects of render flow.

## Fixed-tick pacing

The simulation owner paces the interactive runtime against a monotonic host clock while preserving the configured logical tick duration. A tick always executes all phases atomically from the host's perspective.

Catch-up is bounded to five complete ticks per scheduling pass. When the host is farther behind, wall-clock debt is discarded by moving the next pacing deadline forward; logical ticks, accepted commands, phase order, and simulation randomness are not skipped or partially executed.

Headless execution remains unchanged and unpaced.

## Snapshot and render lifetime

`PresentationSnapshotBuffer` remains a latest-complete-snapshot exchange. The render owner maintains its own `RenderWorld`, so a slow simulation tick does not block rendering of the most recent completed state and a slow render frame does not execute simulation work.

The platform owner also maintains its own presentation-side `RenderWorld` for picking and UI. Both consume immutable snapshots independently.

Camera state crosses to rendering as copied `RtsCameraState`. Debug lines/labels and action/tactical views are copied into each render request. Mutable camera/controller/debug objects are not shared with the render owner.

Window resize/minimize state crosses as copied dimensions. D3D12 never reads the mutable `IWindow` from the render thread.

## Pause and terminal behavior

Minimize or a zero-sized client area sends an explicit pause request to the simulation owner at its next execution boundary. Restore sends resume. Rendering independently suspends its swap chain on a zero-sized target. Headless simulation is unrelated to window state.

After a completed tick publishes Victory/Defeat/Draw, the simulation owner freezes normal tick advancement. Rendering/event pumping continue against the terminal snapshot.

Return/exit applies `EndMatchCommand` as a simulation-owned control transition at the current completed tick through `SimulationCoordinator.ExecuteControlCommand`. This transition does not advance the logical clock or run the normal gameplay phase pipeline, so acknowledgement cannot hide another gameplay tick behind the result screen.

Restart disposes the current execution owners and shared runtime before constructing a fresh `SimulationSessionId`.

## Failure and disposal

Simulation and render owner failures are captured with their original exception and surfaced on the platform owner. The platform loop checks both owners every iteration.

Shutdown order is:

1. stop accepting/publishing new platform work;
2. stop/join render owner and retire GPU work/resources;
3. stop/join simulation owner after the current complete tick/message;
4. dispose the shared vertical-slice scenario;
5. drain/dispose the host-owned job scheduler;
6. dispose the platform window/platform.

No owner is abandoned in the background.

## Alternatives considered

### Keep the serial client loop

Rejected because immutable snapshots alone do not isolate scheduling. Slow simulation/render work would still delay platform progress and minimize would still implicitly alter simulation execution.

### Make SimulationCoordinator generally thread-safe

Rejected. That would widen the synchronization surface around ECS, command scheduling, systems, observers, and deterministic ordering. A single simulation owner with bounded message exchange is simpler and preserves current authority.

### Run each simulation tick/job on a new OS thread

Rejected. ForgeLine already owns a persistent bounded `JobScheduler`; creating threads per tick/job would increase scheduling overhead and weaken resource lifetime control.

### Unbounded channels

Rejected because UI/input bursts must not create unbounded retained command/result history. Backpressure is explicit.

## Consequences

- interactive simulation no longer depends on render/event-loop cadence;
- D3D12 resource lifetime has one execution owner;
- mutable platform/window state stays on its native owner;
- command latency can include one bounded host-queue handoff before the normal future tick;
- rendering may intentionally skip presentation snapshots and still retains essential command results separately;
- debugging must distinguish platform, render, simulation, and worker failures;
- headless semantics remain unchanged.

## Re-evaluation criteria

Revisit this ownership model if networking/replay requires a different command-ingestion clock, multiple render surfaces require a dedicated render scheduler, the job system gains cross-tick tasks, or measurements show the bounded handoff itself is a material bottleneck.
