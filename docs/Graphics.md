# Graphics

## Purpose

`ForgeLine.Graphics` owns the first ForgeLine Engine graphics backend for Windows x64. The backend is intentionally narrow and RTS-focused: it establishes stable Direct3D 12 device, swap-chain, frame-resource, synchronization, shader, resource, and diagnostics foundations without introducing terrain, unit rendering, presentation extraction, or gameplay rules.

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

Zero-sized surfaces are treated as suspended rendering. No back buffers are recreated while minimized.

When a positive client size is restored or changed:

1. wait for outstanding graphics work;
2. release references to the old swap-chain back buffers;
3. call `ResizeBuffers`;
4. reacquire every back buffer;
5. recreate RTVs;
6. reset per-frame fence bookkeeping;
7. resume rendering.

The client coalesces queued window-size-related events before requesting a graphics resize.

## Command Submission Boundary

`IGraphicsDevice.RenderFrame` owns frame begin/end and accepts an optional `IGraphicsCommandContext` callback.

The first command context exposes frame identity, viewport/scissor control, graphics-pipeline binding, and non-indexed draw submission. Pipelines own their root signature and pipeline state and are tied to the graphics device that created them. Later RTS rendering can extend command recording without moving swap-chain, allocator, or fence ownership into presentation/game code.

## Resource Foundation

`IGraphicsDevice.CreateBuffer` establishes explicit buffer ownership for:

- GPU-local default-heap buffers
- CPU-visible upload-heap buffers

The returned `IGraphicsBuffer` is caller-owned and disposable. Graphics resources must be released before the graphics device is destroyed. Debug live-object reporting helps surface lifetime violations.

Texture allocation, upload scheduling, descriptor-table management, and higher-level asset residency remain later work.

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

The Windows graphics smoke path compiles vertex and pixel shaders, creates a minimal root signature and graphics pipeline state, and submits a three-vertex triangle so CI validates the native DXC and D3D12 pipeline path.

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
- Windows graphics client smoke execution with root-signature/PSO creation and triangle draw
- the existing headless smoke and 10,000-entity stress validation
- complete solution tests

The Windows client smoke is a bounded clear/present and minimal triangle-pipeline validation. GPU timing thresholds are intentionally not used as CI gates.

## Deferred Rendering Work

This foundation deliberately does not implement:

- terrain rendering
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
