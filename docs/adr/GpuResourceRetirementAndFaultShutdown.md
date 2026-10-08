# GPU Resource Retirement and Fault Shutdown

## Status

Accepted for the single-owner Direct3D 12 renderer.

## Ownership matrix

| Object | Owner | Retirement condition |
| --- | --- | --- |
| Device, queue, fence and event | Creating render thread | GPU idle or confirmed removal |
| Swap chain, back buffers, depth target | Render thread | Idle before resize/shutdown; recreate before completing resize |
| Frame allocator, timestamps, frame upload buffers | Render thread, indexed by back buffer | Wait for index's previous submission before callback entry |
| Upload/default buffer, pipeline/root signature | Creating device/render thread | Wrapper invalidates immediately; native release follows last binding fence |
| Texture and descriptor | Creating device/render thread | Retire together after last binding fence |
| Texture staging resource, allocator and upload list | Render thread | Upload fence completion; retained by device on fault |
| Command context | Current callback | Invalidated at callback exit, including exceptions |
| Renderer and asset caches | Render thread | Release partial initialization acquisitions; device owns native retirement |
| Copied render/surface intent | Platform publishes, rendering consumes | Admission and stopping share a gate |
| Simulation messages/state | Platform publishes, simulation executes | Finish active work, reject remaining submissions once, join |
| Scenario, workers and window | Platform composition owner | Join both hosts before dependency disposal |

## Decision

Bindings track use during recording. After execution and successful signaling, the device stamps those uses with the actual submission fence. Synchronous texture uploads inside the callback cannot falsely complete recorded frame uses through intervening fence values.

Disposal invalidates wrappers immediately. Recorded-but-unsubmitted resources cannot retire. Native objects release after their last-use fence; texture descriptors remain occupied until native texture release. Device teardown also invalidates undisposed wrappers.

Pending retirement is capped at 4,096. Collection runs at frame entry, including suspended/occluded frames, disposal and idle transitions. Capacity pressure outside recording may synchronize. Exhaustion during recording reports `retirement-capacity` without dropping ownership. Ordinary frames add no frame-global idle wait. CPU writes reject recorded or in-flight buffers. Instance-buffer growth is already protected by index reuse fences.

Signal precedes Present, so presentation failure cannot omit the submission fence. Recording and submission/presentation/resize failures terminate the device session. Escaped contexts, cross-thread graphics use, nested rendering, idle, resize and disposal during recording are rejected. Resource creation remains available for existing lazy loading.

## Recovery and shutdown

Zero dimensions suspend without submission. Latest resize intent wins; restoration synchronizes swap-chain resources and commits its generation only after recreation. Occlusion uses DXGI test presentation with the host's existing 16 ms wait.

Removal reports `device-removed`, HRESULT, removal reason and adapter; other presentation failures report `presentation-failed`. Fence waits inspect completion/removal every 100 ms and report `fence-timeout` after ten seconds. The removal sentinel is never successful completion.

Automatic restoration is unsupported: GPU objects and asset caches need coordinated recreation. Failures propagate to the platform owner and end the session. Native release is safe after confirmed removal. If a live GPU cannot retire during shutdown, ownership remains rooted until process exit; new device creation reports `gpu-shutdown-pending`. Restart the process.

Shutdown stops admission, joins rendering and retires GPU resources, joins simulation, then disposes scenario, workers and window. Concurrent disposers serialize and wait for completion. Self-join is rejected. Faulting submissions publish one failed completion; remaining submissions report `Faulted`.

## Diagnostics and validation

`GraphicsDiagnostics.Health` reports live, pending/peak retirement, releases, fence waits, frame faults and failure reason. `ClientRenderHost.Health` reports thread state, published/completed frames, last completion timestamp and captured fault as a value snapshot.

CPU tests cover recorded disposal, exact-once release, out-of-order retirement, capacity, write rejection, teardown, concurrent disposal, fault completion and partial initialization. Existing surface tests cover resize/suspension intentions.

Native lifetime validation is opt-in:

```powershell
$env:FORGELINE_GPU_LIFETIME_TESTS = '1'
dotnet test --project tests/ForgeLine.Graphics.Tests/ForgeLine.Graphics.Tests.csproj --configuration Release
```

Requires Windows x64, D3D12 hardware or WARP, DXC and Windows Graphics Tools/debug layer. An opted-in run fails if the layer is unavailable. It draws growing buffers, disposes recorded buffers/pipelines, checks escaped contexts and eventual reclamation, and checks debug errors. Existing resize integration tests and `build/Invoke-WindowModeQualification.ps1` cover surface transitions and active-render exit.

Real device removal and a multi-hour graphics soak remain manual qualification; short tests establish neither. Rendering benchmarks measure CPU presentation/terrain work. No throughput improvement is claimed without a comparable baseline.
