# Graphics

## Purpose

`ForgeLine.Graphics` owns the ForgeLine Engine graphics backend for Windows x64. The backend remains intentionally narrow and RTS-focused: it provides Direct3D 12 device, swap-chain, frame-resource, synchronization, shader, geometry-buffer, depth-buffer, indexed-submission, and diagnostics foundations while terrain/world ownership remains outside the graphics layer.

Simulation and headless execution remain independent from graphics.

## External Bindings

The backend uses the centrally pinned Vortice.Windows packages:

- `Vortice.Direct3D12` 3.8.3
- `Vortice.DXGI` 3.8.3
- `Vortice.Dxc` 3.8.3

These packages use the MIT license and provide the maintained .NET bindings for D3D12, DXGI, and DXC used by this repository.

## Ownership Boundary

`ForgeLine.Client` owns application composition.

`ForgeLine.Platform.Windows` owns the HWND lifecycle and exposes only `IWindow.NativeHandle`.

`ForgeLine.Graphics` consumes that opaque native handle and owns:

- DXGI factory
- selected adapter metadata
- D3D12 device
- direct command queue
- one command allocator per swap-chain frame
- reusable graphics command list
- flip-discard swap chain
- RTV descriptor heap
- back-buffer resources and views
- frame fence values and wait event
- graphics buffer allocations created through the device
- DXC shader compilation
- root signatures and graphics pipeline state created through the engine-facing pipeline contract

Graphics does not own or mutate simulation state.

## Adapter Selection

Adapter selection is explicit and deterministic relative to DXGI enumeration:

1. enumerate DXGI adapters in reported order;
2. reject adapters marked as software;
3. choose the first hardware adapter that can create a D3D12 device at feature level 11_0 or newer;
4. record the maximum supported feature level for diagnostics;
5. if no suitable hardware adapter exists and software fallback is allowed, use the DXGI WARP adapter;
6. otherwise fail startup with an actionable platform-not-supported error.

WARP exists primarily to keep development, remote, virtualized, and CI environments able to validate the graphics lifecycle when a hardware GPU is unavailable.

## Debug Layer

Debug builds request the D3D12 debug layer before device creation. If the optional Windows Graphics Tools component is unavailable, startup continues with an explicit diagnostic message rather than silently pretending validation is active.

Debug shutdown requests live-device-object reporting after owned frame resources, command objects, swap-chain resources, and synchronization objects have been released.

## Swap Chain

The initial render surface uses:

- flip-discard presentation
- `R8G8B8A8_UNorm` back buffers
- three buffers by default
- one RTV per back buffer
- VSync presentation by default

The swap-chain owner is the graphics device. Higher layers do not own back buffers or DXGI interfaces.

## Frame Lifecycle

Each frame follows this lifecycle:

```text
wait for the selected frame allocator to be reusable
    ↓
reset frame command allocator
    ↓
reset reusable graphics command list
    ↓
transition back buffer Present → RenderTarget
    ↓
bind RTV
    ↓
set viewport and scissor from current client size
    ↓
clear render target
    ↓
invoke optional engine-facing frame recording callback
    ↓
transition RenderTarget → Present
    ↓
close and execute command list
    ↓
present swap chain
    ↓
signal frame fence
    ↓
advance to the swap chain's current back-buffer index
```

Synchronization waits only when a frame allocator/back buffer is about to be reused before its previous submission completed. Normal rendering does not insert a full-GPU idle wait every frame.

`WaitForIdle()` is reserved for ownership transitions that require complete retirement, such as swap-chain resize and shutdown.

## Resize and Minimize

The platform-to-graphics contract separates suspension from valid client dimensions. The platform retains the last valid non-zero client size while minimized, and the client forwards an explicit suspended surface state to the render owner.

`IGraphicsDevice.Resize` records graphics-owned surface intent instead of calling DXGI immediately. Zero or negative dimensions mark the surface suspended and never call `ResizeBuffers`. Positive resize requests are collapsed to the most recent requested size and are applied by the graphics device at the beginning of `RenderFrame`, before any command allocator, back buffer, or render-target state is reused.

On restore or a completed window-mode transition, the render owner submits the final positive client dimensions. A positive resize is processed even when the dimensions match the pre-minimize size so a suspended device has an explicit path back to an active renderable surface.

When a pending positive resize is applied:

1. wait for all outstanding graphics work using the existing frame fence;
2. release the depth target and every old swap-chain back-buffer reference;
3. call `ResizeBuffers` with the final non-zero dimensions;
4. reacquire every swap-chain back buffer;
5. recreate RTVs and the resolution-dependent depth target;
6. refresh the current DXGI back-buffer index and validate it against the configured buffer count;
7. clear per-frame fence bookkeeping;
8. publish the new surface dimensions and applied resize generation;
9. resume normal rendering with viewport/scissor derived from the new dimensions.

Intermediate Win32 resize messages produced by a platform window-mode transition are not treated as independent graphics surface states; the final valid platform state drives graphics resize/recovery.

Surface diagnostics expose suspension, occlusion, pending-resize state, requested resize generation, and the last successfully applied generation. Resize requests, synchronization/application, successful recreation, Present failures, and recovery transitions emit structured console diagnostics. A resize or Present failure is escalated with the HRESULT and device-removal reason instead of leaving the client in a silent blank state.

The swap chain uses flip-discard presentation. Defensive occlusion handling still treats an occlusion status as recoverable: rendering pauses while occluded and the graphics layer probes presentation readiness before resuming, without making occlusion a sticky terminal state.

## Command Submission Boundary

`IGraphicsDevice.RenderFrame` owns frame begin/end and accepts an optional `IGraphicsCommandContext` callback.

The command context exposes frame identity, viewport/scissor control, graphics-pipeline binding, multi-slot vertex/index buffer binding, vertex root constants, and indexed, indexed-instanced, or non-indexed draw submission. Vertex declarations distinguish per-vertex and per-instance input rates while keeping the D3D12 classification inside `ForgeLine.Graphics`. Pipelines own their root signature and pipeline state and are tied to the graphics device that created them. Terrain and world presentation use this boundary without exposing D3D12 objects to world or simulation code.

## Resource Foundation

`IGraphicsDevice.CreateBuffer` establishes explicit buffer ownership for GPU-local default-heap buffers and CPU-visible upload-heap buffers. `IGraphicsBuffer.SetData` provides bounded initialization of upload buffers. The terrain renderer creates persistent per-chunk vertex and index buffers once and reuses them across frames. Repeated world presentation uses one upload instance stream per swap-chain frame index so transform/tint data can be refreshed only after that frame resource has been synchronized for reuse.

The returned `IGraphicsBuffer` is caller-owned and disposable. Graphics resources must be released before the graphics device is destroyed. Debug live-object reporting helps surface lifetime violations.

Texture allocation, staged GPU-local upload scheduling, descriptor-table management, and higher-level asset residency remain later work.

## Shader Compilation

`DxcShaderCompiler` is the repository-standard HLSL compiler path.

The current foundation:

- compiles Shader Model 6.0 vertex, pixel, and compute shaders;
- uses HLSL 2021;
- enables strictness;
- treats shader warnings as errors;
- uses debug-friendly optimization settings in Debug builds and `-O3` in Release builds;
- exposes in-memory and file-based compilation;
- returns immutable engine-facing shader bytecode metadata;
- reports DXC diagnostics through `GraphicsShaderCompilationException`.

The Windows graphics smoke path now compiles the terrain vertex/pixel shaders, creates the terrain root signature and pipeline state, creates a depth target, binds indexed chunk geometry, and submits visible terrain so CI validates the native DXC and D3D12 world-rendering path.

## Diagnostics

Startup diagnostics include:

- adapter name
- hardware/software adapter state
- maximum reported feature level
- dedicated video memory
- debug-layer state
- back-buffer dimensions
- swap-chain buffer count
- present mode

Runtime surface diagnostics expose current frame index and suspended state.

Present and resize failures include the HRESULT, D3D12 device-removed reason, and selected adapter name.

## Validation

The repository validates the foundation through:

- full solution restore/build
- project-reference architecture validation
- focused DXC success/failure tests
- Windows graphics client smoke execution with terrain shader/PSO creation, depth buffering, persistent chunk geometry, frustum culling, and indexed draws
- the existing headless smoke and 10,000-entity stress validation
- complete solution tests

The Windows client smoke is a bounded terrain-rendering validation. GPU timing thresholds are intentionally not used as CI gates.

## Deferred Rendering Work

This foundation deliberately does not implement:

- unit/building rendering
- model loading
- render extraction from ECS/game state
- fog-of-war rendering
- selection outlines
- advanced culling
- indirect rendering
- compute workloads
- post-processing
- editor rendering
- Vulkan


## Render-thread ownership

The interactive client gives all D3D12 lifetime and submission to one dedicated render owner. Device creation, swap-chain creation, pipelines, buffers, renderer objects, `RenderFrame`, resize, `WaitForIdle`, and disposal all execute on that owner.

`GraphicsDeviceFactory.CreateForWindowTarget` accepts a copied `GraphicsWindowTarget` containing the opaque native handle, initial client dimensions, and suspended state. This lets graphics create its swap chain without reading mutable `IWindow` properties from the render thread.

Subsequent resize/minimize/restore state crosses from the platform owner as copied dimensions. A zero-sized target suspends rendering; restoration to a positive size performs the existing idle/recreate path on the render owner.

The renderer consumes immutable presentation snapshots and copied camera/debug/UI frame state. No graphics object, mutable window object, ECS registry, inventory, or simulation system crosses into the render owner.

A slow render frame can delay later GPU submissions, but it cannot execute or block authoritative simulation ticks. A slow simulation tick leaves the renderer free to reuse the newest completed snapshot.

See [Client Execution Ownership](adr/ClientExecutionOwnership.md).
