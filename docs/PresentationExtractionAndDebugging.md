# Presentation Extraction and Debugging

## Purpose

The presentation boundary converts mutable simulation/game state into immutable render-facing data. Rendering consumes only extracted snapshots and never queries or mutates live ECS storage.

The canonical flow is:

```text
Simulation / Game State
    ↓
post-tick extraction
    ↓
PresentationSnapshotBuffer
    ↓
PresentationSnapshot
    ↓
RenderWorld
    ↓
Terrain / Instance / Debug / Overlay Renderers
    ↓
ForgeLine.Graphics
```

This boundary keeps render frequency independent from the fixed simulation rate and preserves headless execution.

## Extraction Timing

`SimulationCoordinator` executes the complete fixed-tick phase order first, including `SnapshotEvents`. Registered `ISimulationTickObserver` instances run only after those phases complete.

`PresentationExtractor` is a presentation-owned observer. It reads ECS data at that controlled post-tick point and immediately copies render-relevant values into a new immutable snapshot.

Simulation owns the observer hook but has no dependency on `ForgeLine.Presentation`, `ForgeLine.Graphics`, Direct3D, or the Windows client.

## Snapshot Ownership

`PresentationSnapshot` owns its copied `RenderInstance` array.

A snapshot contains:

- completed simulation tick;
- configured simulation tick duration;
- simulation entity count at extraction time;
- ordered render instances.

A `RenderInstance` contains:

- stable `EntityId`;
- copied render transform;
- mesh/resource handle;
- material/pipeline handle;
- presentation visibility flags;
- optional debug identity.

No snapshot stores references into mutable ECS component storage. Mutating simulation state after extraction cannot change an already-published snapshot.

Presentation data is allowed to be lossy, filtered, or aggregated in future systems because it is not authoritative gameplay state.

## Buffered Handoff

`PresentationSnapshotBuffer` is a latest-value handoff.

Publishing uses `Interlocked.Exchange`; readers use `Volatile.Read`. The renderer therefore never blocks the simulation and the simulation never waits for a render consumer.

If rendering is slower than simulation, intermediate snapshots may be skipped. This is intentional: the renderer needs the newest complete presentation state, not every historical frame.

If rendering is faster than simulation, `RenderWorld` retains the previous and current complete snapshots for interpolation.

## Interpolation

The default simulation target is 20 Hz, or 50 ms per tick. Rendering can run independently at a much higher rate.

The client maintains a real-time accumulator and derives:

```text
alpha = accumulated render time / simulation tick duration
```

clamped to 0..1.

`RenderWorld` matches entities across the previous/current snapshots by stable `EntityId` and interpolates position, rotation, and scale. New entities without a previous state use the current transform directly.

Interpolated transforms are presentation-only. They never feed back into simulation state, command processing, collision, combat, or save/replay state.

## Current Test Entity Path

`ForgeLine.Game` provides the simulation-side state used by the current interactive movement path:

- `WorldTransform`;
- `MovementOrder`;
- `GroundMovement`;
- `GroundMovementState`;
- `GroundMovementSystem`;
- `VisualIdentity`.

`VisualIdentity` contains a stable numeric visual identifier and visibility flags only. It contains no GPU resources, graphics objects, pipelines, shaders, or presentation references.

The Windows client creates a configurable grid of controllable ground entities. Accepted movement commands are consumed by authoritative fixed-tick locomotion while the renderer displays smoothly interpolated extracted transforms. `LinearVelocity` and `LinearMotionSystem` remain narrow low-level fixtures for validating fixed-tick transform integration independently of RTS locomotion.

Use:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --render-stress 1000
```

to create a 1,000-instance development stress scene.

## Generic Instance Renderer

`SimpleInstanceRenderer` is the initial generic extracted-instance rendering path.

It currently uses one shared cube mesh and material pipeline to validate:

- render-world ownership;
- per-instance transforms;
- frustum culling;
- submission accounting;
- repeated-instance load;
- interpolation against terrain.

It deliberately does not implement final unit art, animation, asset streaming, batching, indirect drawing, or production material selection.

Those optimizations must be driven by measurements from representative workloads.

## Debug Draw

`DebugDraw` is presentation-only and supports:

- lines;
- axis-aligned boxes;
- circles/ranges;
- point markers;
- labels.

When disabled, primitive methods return before collecting geometry. Regression coverage verifies that repeated disabled primitive submission does not allocate on the calling thread.

`DebugDrawRenderer` uses a bounded reusable upload buffer and a line-list graphics pipeline. World labels reuse the development overlay text renderer after projection through the RTS camera.

The Windows client uses:

- **F1** — toggle the development metrics overlay;
- **F2** — toggle world debug visualization, including terrain chunk debug state, entity bounds, ground-movement velocity vectors, targets, and local steering neighborhoods.

## Development Overlay

The development overlay is intentionally lightweight and uses a small built-in glyph renderer rather than a production UI/text stack.

It exposes at minimum:

- FPS;
- frame time;
- CPU render time;
- simulation tick;
- last measured simulation tick time;
- simulation entity count;
- visible / total terrain chunks;
- draw/submission count;
- rendered / total extracted instances;
- aggregate job execution time when a scheduler is present;
- total allocated bytes;
- managed heap size;
- Gen 0 / Gen 1 / Gen 2 collection counts.

Metrics may be one frame or one diagnostic-sampling interval behind the active frame. They are diagnostic observations only and never affect simulation behavior.

## Performance Instrumentation

`FrameTimingTracker` records smoothed frame and CPU-render durations.

Terrain and generic instance renderers expose deterministic submission/culling counters. Debug rendering exposes line and draw-call counts.

GPU timestamp queries are deferred. The current graphics abstraction does not yet expose a clean timestamp-query lifecycle, readback ownership, or frequency conversion contract. GPU timing should be added only when that abstraction can be introduced without leaking D3D12-specific synchronization details into presentation code.

## Rendering Baselines

The rendering BenchmarkDotNet host includes:

- terrain mesh generation;
- visible terrain chunk submission;
- 1,000 near-field simple render instances;
- 5,000 total simple instances with a 4,000-instance far field intended to exercise frustum culling.

Run:

```powershell
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release
```

Benchmark timing is non-binding and hardware-dependent. Keep BenchmarkDotNet environment metadata with captured results and compare like-for-like runs.

CI compiles these benchmarks but does not enforce FPS or timing thresholds.

The Windows client smoke path can additionally exercise 1,000 extracted instances:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000
```

This is a stability/submission smoke workload, not a performance pass/fail threshold.

## Architecture Guards

`build/Validate-ProjectReferences.ps1` rejects graphics, presentation, UI, client, Windows platform, audio, and editor dependencies reachable from `ForgeLine.Simulation`.

It also rejects graphics/audio/UI/presentation/client/Windows dependencies from `ForgeLine.Headless`.

These checks preserve the invariant that a complete simulation remains runnable without presentation or graphics initialization.

Ground-movement debug capture is opt-in and copies immutable diagnostic values at simulation tick boundaries. Presentation never retains live ECS movement references. See [Ground Movement and Local Steering](GroundMovementAndSteering.md).
