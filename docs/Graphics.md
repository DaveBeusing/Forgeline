# Graphics

Terrain surface refinement retains the existing four-layer/thirteen-texture pipeline, thirteen-sample ceiling, 63-DWORD direct root signature and 18-DWORD buffered shadow receiver. Pixel-footprint-filtered color/normal detail and Rock-only slope-adaptive coordinates share existing textures and passes. Construction-time settings validate before native allocation; no new GPU target or descriptor class is introduced. Vegetation uses copied, viewer-authorized instance transforms with the same phase in World and SunShadowDepth. Authoring ranges, culling/LOD policy and qualified viewpoints are described in [World Asset Presentation](WorldAssetPresentation.md); the architectural choice is recorded in [Bounded Terrain Surface Refinement](adr/BoundedTerrainSurfaceRefinement.md).

## Purpose

`ForgeLine.Graphics` owns the ForgeLine Engine graphics backend for Windows x64. The backend remains intentionally narrow and RTS-focused: it provides Direct3D 12 device, swap-chain, frame-resource, synchronization, shader, geometry-buffer, texture, descriptor, sampler, depth-buffer, indexed-submission, and diagnostics foundations while terrain/world ownership remains outside the graphics layer.

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
- shader-visible SRV descriptor heap and descriptor allocation/reuse
- immutable GPU-local 2D texture resources and their staged uploads
- centralized static world/clamp sampler policy

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
signal frame fence and stamp recorded resource uses
    ↓
present swap chain
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

The command context exposes frame identity, viewport/scissor control, graphics-pipeline binding, multi-slot vertex/index buffer binding, vertex root constants, pixel texture binding, and indexed, indexed-instanced, or non-indexed draw submission. Vertex declarations distinguish per-vertex and per-instance input rates while keeping the D3D12 classification inside `ForgeLine.Graphics`. Pipelines own their root signature and pipeline state and are tied to the graphics device that created them. Texture slots are declared by the pipeline contract and become SRV descriptor tables in the D3D12 root signature. The bounded engine contract permits up to sixteen pixel SRV slots; the production terrain path deliberately uses thirteen. Terrain and world presentation use this boundary without exposing D3D12 objects to world or simulation code.

## Production static-mesh material path

The production non-terrain mesh path consumes runtime mesh version 2. Static mesh vertices carry Position, Normal, UV0, and a tangent `float4` whose W component is the tangent-space handedness. Material texturing no longer depends on mesh vertex color as surface albedo; Base Color, Normal, ORM, and Emissive come from the resolved runtime material. Instance color remains an intentional per-object/team/readability multiplier.

The textured D3D12 input layout is:

```text
POSITION  float3 @  0
NORMAL    float3 @ 12
TEXCOORD0 float2 @ 24
TANGENT   float4 @ 32
```

The vertex shader transforms the normal/tangent basis with the instance world basis. The pixel shader orthogonalizes the tangent, reconstructs the bitangent from `Tangent.w`, converts the sampled normal from [0,1] to tangent-space [-1,1], and transforms it through the TBN basis before the current lightweight material response. A deterministic orthogonal tangent is used only for materials that do not require a normal map and therefore receive the flat-normal fallback.

Runtime mesh material IDs come from compiler-stable asset references. A one-material mesh can therefore preserve its source material identity through compiler output, GPU buffer creation, batching, and draw submission. Meshes explicitly marked as migration fallback do not enter the textured path; they remain visible through the existing untextured development path and are diagnosed by asset compilation.

Debug line/procedural rendering remains independent and is not forced through the production 48-byte mesh vertex format.

## Production terrain material path

The production terrain path uses one RGBA control texture and four active material layers per visible chunk. The fixed binding layout is:

```text
t0       chunk control map
t1-t4    layer Base Color
t5-t8    layer Normal
t9-t12   layer ORM
```

Terrain material coordinates come from world X/Z rather than chunk-local repeating coordinates, so neighboring chunks sample the same material field at their shared edge. Each material applies its explicit tile scale. The control map remains chunk-local and clamp-sampled.

The shared `s0` world-material sampler provides anisotropic filtering and trilinear mip transitions for tiled material textures. `s1` provides linear clamp filtering for the control map. Normal maps are decoded and renormalized after weighted blending; ORM is blended linearly. Vertex color is a low-frequency macro modifier only and is no longer the primary terrain albedo.

The shader sample/binding budget is deliberately bounded at thirteen textures per draw, with a maximum of four active material layers. Material GPU textures are cached and reused, and each terrain chunk owns one persistent control texture. No descriptors are allocated during ordinary per-frame terrain submission.

## Resource Foundation

`IGraphicsDevice.CreateBuffer` establishes explicit buffer ownership for GPU-local default-heap buffers and CPU-visible upload-heap buffers. `IGraphicsBuffer.SetData` provides bounded initialization of upload buffers. The terrain renderer creates persistent per-chunk vertex and index buffers once and reuses them across frames. Repeated world presentation uses one upload instance stream per swap-chain frame index so transform/tint data can be refreshed only after that frame resource has been synchronized for reuse.

The returned `IGraphicsBuffer` is caller-owned and disposable. Graphics resources must be released before the graphics device is destroyed. Debug live-object reporting helps surface lifetime violations.

`IGraphicsDevice.CreateTexture` accepts validated immutable 2D texture data containing width, height, format, color space, and a complete supplied mip set. D3D12 textures are created in the default heap, every supplied mip is copied through D3D12 copyable footprints, the resource transitions from `CopyDest` to `PixelShaderResource`, and the staging resource remains alive until the upload fence completes. Static textures are not uploaded per frame.

Every live texture owns one shader-visible SRV descriptor. Descriptor indices are allocated from the graphics-owned heap and returned for reuse only when the texture is disposed after GPU retirement. Raw D3D12 descriptor handles never cross into simulation or gameplay state.

Pipelines that declare pixel textures receive centralized static samplers: `s0` is the world-material sampler using wrap addressing, trilinear mip filtering, and anisotropic filtering; `s1` is a deliberate linear clamp sampler. Presentation code selects stable material/asset IDs and binds `IGraphicsTexture` objects; it does not create D3D12 sampler or descriptor state.

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

## Scene Lighting, Exposure, and Output Transfer

World presentation uses one explicit scene-lighting configuration composed by the client render host and consumed by both terrain and repeated-instance rendering. The baseline is deliberately stable for an RTS camera rather than camera-reactive:

- one normalized directional light with an explicit color and intensity;
- a hemisphere-style ambient contribution that keeps shadow-facing geometry readable;
- ambient occlusion as a bounded local visibility modifier rather than a substitute for lighting;
- one manual exposure value;
- an ACES-fitted tone-mapping curve;
- explicit linear-to-sRGB output transfer for lit world surfaces.

Base Color textures marked as sRGB are decoded by the D3D12 SRV before material math. Normal and ORM textures remain linear data. The swap chain intentionally remains R8G8B8A8_UNorm. Linear quality profiles encode final world color to sRGB once in the shared composite; terrain/object shaders retain equivalent direct output for fallback. This avoids the previous path where linear material values were written directly to the UNorm surface and appeared substantially darker than intended.

The client quality profile defaults to Medium with linear scene composition. World lighting and particles enter frame-indexed FP16 targets; one shared fullscreen triangle applies fog, manual exposure, bounded grading, ACES, spatial AA and output transfer. Overlay UI/selection/command colors retain full-resolution output-space rendering. Direct output remains the capability/pressure fallback with neutral grading and no AA. General post chains and HDR monitor output remain deferred.

SceneLightingSettings is presentation-owned and contains the canonical defaults. The client settings file exposes sceneExposure (validated from 0.25 through 4.0) and composes that value into the immutable scene-lighting state before the render host starts. This gives development and qualification runs an explicit persisted exposure control without making exposure simulation state. Scene lighting never enters simulation state and cannot influence deterministic gameplay.

The terrain shader already uses thirteen SRV descriptor tables. D3D12 root signatures are limited to 64 DWORDs, so terrain per-layer Base Color factors are packed as RGB10 values and roughness/metallic factors as paired UNorm16 values before scene-lighting constants are appended. The resulting terrain root-signature cost remains within the existing hardware limit without adding descriptors or draw calls.

The optional directional depth pass adds cast shadows and bounded local contact occlusion to this lighting path. Its coverage, quality budgets and qualification are described below.

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

Runtime surface diagnostics expose current dimensions, frame index, suspended/occluded state, pending-resize state, resize generations, submitted-frame count, and successful-Present count. The frame counters form a low-overhead qualification heartbeat: submitted frames show that command submission continues, while successful Presents distinguish an active presentation path from a renderer that is only producing GPU work.

Resource diagnostics expose current and peak loaded GPU texture count, current and peak resident texture bytes represented by supplied mip payloads, current and peak SRV descriptor usage/capacity, cumulative successful texture uploads/releases, and invalid texture-binding attempts. D3D12 frame GPU time is measured with timestamp queries resolved into a readback buffer only after the corresponding frame fence completes. When the debug layer is enabled, warning/error counts are collected through the D3D12 info queue. Presentation additionally reports resolved material count, loaded asset texture count, material/texture fallback failures, terrain control-texture count, fixed terrain texture bindings/samples per draw, CPU terrain submission time, the active scene-light direction, directional/ambient intensity, manual exposure, and tone-mapping mode.

Present and resize failures include the HRESULT, D3D12 device-removed reason, and selected adapter name.

## Validation

The repository validates the foundation through:

- full solution restore/build
- project-reference architecture validation
- focused DXC success/failure tests
- Windows graphics client smoke execution with terrain shader/PSO creation, depth buffering, persistent chunk geometry, frustum culling, and indexed draws
- the existing headless smoke and 10,000-entity stress validation
- complete solution tests

The Windows client smoke is a bounded terrain-rendering validation. CI records CPU frame timing, D3D12 timestamp GPU timing when supported, terrain draw/texture/descriptor pressure, residency peaks, texture lifetime counters, and debug-layer cleanliness. Hardware-specific GPU timing is not a pass/fail threshold on the Microsoft Basic Render Driver. Structural budgets remain gated. The terrain shader budget stays fixed at one control sample and four samples each for Base Color, Normal, and ORM per pixel before ordinary hardware anisotropic filtering behavior.

## Deferred Rendering Work

This foundation deliberately does not implement:

- unit/building rendering
- model loading
- render extraction from ECS/game state
- fog-of-war rendering
- advanced culling
- indirect rendering
- compute workloads
- general-purpose post-processing effects and HDR display output
- editor rendering
- Vulkan


## Render-thread ownership

See [GPU Resource Retirement and Fault Shutdown](adr/GpuResourceRetirementAndFaultShutdown.md) for resource retirement, upload-write safety, bounded health counters and fatal device-loss policy.

The interactive client gives all D3D12 lifetime and submission to one dedicated render owner. Device creation, swap-chain creation, pipelines, buffers, renderer objects, `RenderFrame`, resize, `WaitForIdle`, and disposal all execute on that owner.

`GraphicsDeviceFactory.CreateForWindowTarget` accepts a copied `GraphicsWindowTarget` containing the opaque native handle, initial client dimensions, and suspended state. This lets graphics create its swap chain without reading mutable `IWindow` properties from the render thread.

Subsequent resize/minimize/restore state crosses from the platform owner as copied dimensions. A zero-sized target suspends rendering; restoration to a positive size performs the existing idle/recreate path on the render owner.

The renderer consumes immutable presentation snapshots and copied camera/debug/UI frame state. No graphics object, mutable window object, ECS registry, inventory, or simulation system crosses into the render owner.

A slow render frame can delay later GPU submissions, but it cannot execute or block authoritative simulation ticks. A slow simulation tick leaves the renderer free to reuse the newest completed snapshot.

See [Client Execution Ownership](adr/ClientExecutionOwnership.md).


## Player interaction overlays

Graphics pipelines expose opt-in non-premultiplied alpha blending; opaque rendering remains the default. The RTS information overlay opts in for marquee fill and outlines. The player world overlay reuses the bounded debug geometry source with optional screen-space line expansion, soft alpha edges and one triangle batch. The render owner applies the active DPI/UI scale. Developer line rendering retains its existing path. See [Selection and Command Interaction](SelectionAndCommandInteraction.md) for marker semantics and budgets.

## Explicit passes and optional linear scene output

`IGraphicsCommandContext.BeginPass` marks World and Overlay in their original submission order. Graphics performs optional composition at the boundary; World submission after Overlay is rejected. The render host places terrain/objects in World and all debug lines, interaction geometry, HUD, development metrics and frontend surfaces in Overlay. Loading and splash paths enter Overlay directly.

The client resolves persisted Low/Medium/High quality before world renderer construction. `FORGELINE_RENDER_QUALITY` overrides that profile for qualification; the legacy `FORGELINE_LINEAR_SCENE_OUTPUT=1` can still explicitly request composition, while 0 does not override the selected quality profile. Requested and actual quality plus any capability/budget fallback are published. Native allocation/removal failures retain the actionable session-fault path.

Scene targets use R16G16B16A16_FLOAT, one target and SRV per swap-chain index, and the existing frame reuse fences. Resize recreates them only after all frames retire. Minimize/occlusion retain the existing no-submission behavior; device loss and shutdown retain the existing ownership policy. Pipeline target formats are checked against the active pass. Only opaque world pipelines are supported in the scene target. Clear/terrain debug pixels carry an output-space alpha tag, which composition preserves. Details and future-effect constraints: [Explicit Frame Passes and Scene Composition](adr/ExplicitFramePassesAndSceneComposition.md).

`GraphicsDiagnostics.Frame` and client qualification `metrics.FramePasses` expose the target plan, CPU submission identity, completed GPU fence, World/composition/Overlay timings and absent-feature reasons. CPU measurements cover command recording, with clear/setup included in World; they exclude allocator waits and Present. GPU measurements are delayed until fence completion and partition the measured frame. Old-size/configuration measurements are cleared. Target bytes are logical texel payload, not total driver VRAM. At three buffers, scene payload is 21.1 MiB at 1280×720, 47.5 MiB at 1920×1080 and 189.8 MiB at 3840×2160. Direct output adds zero scene bytes and zero composition draws.

Native graphics tests perform readback only on explicit qualification captures, comparing linear/sRGB material transfer, alpha, overlays, enabled/disabled composition, resize and suspension on hardware and forced WARP. Production presentation tests compare terrain, procedural objects, debug lines and terrain debug palettes at three camera distances. FP16 world precision is bounded to one 8-bit step; the tested overlay pixels are exact. New native presentation fixtures run separately from parallel CPU allocation fixtures. Existing allocation assertions remain unchanged.

The isolated hardware comparison is opt-in:

```powershell
$env:FORGELINE_PASS_BENCHMARK_OUTPUT = Join-Path (Get-Location) 'artifacts/scene-pass-performance.json'
$env:FORGELINE_BENCHMARK_DRIVER = '<installed driver version>'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ScenePassPerformanceQualificationTests
```

Observed on NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, 1280×720, three buffers, VSync off, nine fixed representative terrain chunks, exposure 1.15, ACES, 64 warmups and 512 CPU samples per mode. GPU statistics sample distinct completed fences; no concurrent asset build or validation command ran during measurement.

| Camera distance | Direct GPU mean ms | Scene GPU mean ms | Composition GPU mean ms | Direct CPU mean ms | Scene CPU mean ms |
| --- | --- | --- | --- | --- | --- |
| 25 | 0.897 | 1.193 | 0.169 | 7.466 | 3.615 |
| 90 | 0.993 | 1.145 | 0.141 | 3.512 | 3.442 |
| 240 | 1.786 | 1.572 | 0.105 | 4.235 | 3.753 |

CPU figures include frame reuse/Present overhead in the benchmark's outer frame call and differ from pass-recording diagnostics. Sequential samples remain sensitive to clocks, scheduling and warmup; no CPU improvement or portable GPU threshold is claimed. This fixture excludes gameplay, objects and HUD. WARP establishes functional correctness only. Sustained full-client throughput, physical-monitor readability, extended resize/occlusion soak and real device removal remain separate acceptance work.

## Directional shadows and contact occlusion

Set `FORGELINE_SHADOW_QUALITY=Low`, `Medium` or `High` before starting the client. Omit it or use `Disabled` for the default path with no shadow allocation or extra depth draws. Unknown profiles fail explicitly. Shadows work with direct output and optional linear scene composition; UI/debug overlays remain outside lighting. Profiles are composed before renderer construction; runtime profile changes require rebuilding the renderers.

| Profile | Map resolution | Radius (m) | Maximum caster submissions | Maximum depth draws | Depth + receiver payload, three buffers |
| --- | --- | --- | --- | --- | --- |
| Disabled | 0 | 0 | 0 | 0 | 0 |
| Low | 1024² | 180 | 1024 | 256 | 24 MiB |
| Medium | 2048² | 450 | 4096 | 256 | 96 MiB |
| High | 4096² | 750 | 8192 | 256 | 384 MiB |

Each enabled profile uses one stable camera-relative orthographic map, the measured alternative to cascades. Coverage remains fixed as zoom changes, X/Y placement snaps to whole light-space texels, and receivers fade near the map/radius boundary. Strategic zoom therefore has deliberately limited shadow distance. Terrain uses at most 128 depth draws; objects consume the remaining budgets. VFX and hidden entities do not cast. Caster order follows the immutable authorized snapshot; dense scenes can omit later casters when capped.

The graphics device owns R32 typeless targets with D32 depth/R32 shader-resource views and one SRV per back buffer. Fence reuse, configure/disable, suspension, resize and fatal device-loss ownership follow the established frame contract. Fixed-resolution targets survive window resize. Buffered per-draw constants keep terrain within the root-signature budget; the fixed arena adds 3 MiB at three buffers, independently from depth texel payload. Neither figure is total driver VRAM.

Four bilinearly weighted depth comparisons implement PCF; receiver-plane correction and bounded bias address sloped receivers. Shadows attenuate direct light only. Local contact occlusion uses actual depth-blocker separation, fades to zero within 1.5 metres and affects ambient by at most 15% (12% default). It is separate from material AO and is not SSAO/GTAO. Hemisphere ambient, emissive, exposure and output transfer remain intact. See [Directional Shadow Depth and Contact Occlusion](adr/DirectionalShadowDepthAndContactOcclusion.md).

`GraphicsDiagnostics.DirectionalShadows` and client qualification `metrics.directionalShadows` report profile, resolution, one-map count, target bytes, descriptors, successful CPU submission identity, draw/caster counts, CPU recording cost and completed-fence GPU shadow time. `Frame.Plan.Shadow` includes the target/lifetime contract. World pass timings exclude the depth pass; total GPU frame timing includes it. Asset texture residency excludes render targets, which are reported separately. Visual qualification retains the existing terrain/material budgets and adds only the explicit shadow draw/descriptor counts.

Hardware qualification command:

```powershell
$env:FORGELINE_SHADOW_BENCHMARK_OUTPUT = Join-Path (Get-Location) 'artifacts/directional-shadow-performance.json'
$env:FORGELINE_BENCHMARK_DRIVER = '<installed driver version>'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.DirectionalShadowPerformanceTests
```

Observed on NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, 1280×720, three buffers, VSync off, nine representative chunks and 1000 opaque instances. Each sequential Disabled/Low/Medium/High pair uses 64 warmups and 256 samples. GPU statistics use distinct completed fences. No concurrent asset compilation, build or validation command ran during the measurement.

| Camera distance | Disabled total GPU ms | Low total / depth GPU ms | Medium total / depth GPU ms | High total / depth GPU ms |
| --- | --- | --- | --- | --- |
| 120 m | 1.67 | 3.76 / 0.72 | 4.19 / 1.20 | 4.17 / 1.22 |
| 420 m | 2.93 | 4.02 / 0.77 | 4.46 / 1.31 | 4.55 / 1.29 |
| 1000 m | 2.68 | 3.74 / 0.88 | 4.20 / 1.27 | 4.17 / 1.20 |

Disabled CPU means were 8.33/4.00/3.92 ms; enabled means ranged 4.48–5.31 ms and include frame reuse/Present. Sequential clocks/scheduling make the first result unsuitable for claiming CPU improvement. These are workload-specific costs, not a sustained full-client FPS guarantee. The initial pre-change terrain-only scene-pass baseline is recorded separately and is not numerically compared against this denser workload.

WARP regressions cover enabled/disabled pixels, hidden-caster invariance, identical static frames, direct/linear output, disable/re-enable, resize/suspend, descriptor reuse, invalid matrices and pass/configuration faults. Reference zooms 120/420/1000 m and raster scales 100/150/200% pass; raster scaling does not qualify physical monitor transitions. A 10000-instance test confirms the Low 1024-caster cap. Native production-client windowed/borderless qualification also exercises real Directorate assets with Medium shadows and linear output. Physical-monitor legibility, moving-unit/shimmer review under arbitrary camera motion, long occlusion/resize soak and actual device removal remain manual acceptance work.

## Atmospheric fog and scene depth

Atmospheric fog is optional visual state, separate from tactical fog of war. Set `FORGELINE_ATMOSPHERE_QUALITY=Distance` or `Height`, or persist `sceneAtmosphere` in the client settings file. `Disabled` is the default. Invalid profiles/ranges fail explicitly; older files retain disabled defaults. An enabled profile selects the existing linear scene output automatically. Disable atmosphere before disabling that output through the graphics API.

The persisted controls are `quality`, `colorRed/Green/Blue` (linear RGB, 0–4), `density` (0–0.01 per metre), `startDistance` (0–2000 m), `baseHeight` (-1000–10000 m), `heightFalloff` (0–0.1 per metre), `maximumOpacity` (0–0.65) and `zoomCompensation` (0–1). Defaults are RGB 0.58/0.66/0.76, density 0.0012, start 100 m, base 0 m, falloff 0.02, opacity 0.5 and compensation 0.75. Density zero preserves the original scene output.

| Profile | Extinction | Additional depth targets | Depth SRVs | Additional composition draws over linear output |
| --- | --- | --- | --- | --- |
| Disabled | None | 0 | 0 | 0 |
| Distance | Exponential distance after the start threshold | 0 | 1 | 0 |
| Height | Distance weighted by exponential height density | 0 | 1 | 0 |

A pixel-center depth read reconstructs world position using the copied inverse view-projection. Height fog uses a bounded analytic segment average with a stable near-horizontal limit; deduction of start distance uses that mean density. Density is compensated toward a 420 m RTS reference using a clamped 0.35–2 multiplier. The opacity ceiling retains at least 35% of surface contribution. Fog color is mixed into linear lighting before the one existing exposure/ACES/sRGB transform. There is no temporal history, blur, neighborhood sampling or volumetric lighting.

The graphics-owned depth buffer uses typeless R32 storage with D32 write and R32 sample views when enabled. The surface-owned depth payload is reused; ordered queue barriers unbind the DSV for composition, sample, return to depth-write state and restore it before overlays. Resolution changes retire/recreate the descriptor with the existing idle/fault policy. Clear-depth pixels, output-space clear/debug colors and a missing camera bypass fog. Missing camera data produces an explicit diagnostic reason; incomplete frame recording never publishes an applied submission.

Enabling fog from direct output still requires the FP16 scene targets: 21.1 MiB at 1280×720, 33.0 MiB at 1600×900 or 253.1 MiB at 5120×2160 for three buffers. There is no additional fog target beyond these. `GraphicsDiagnostics.Atmosphere` and client `metrics.atmosphere` distinguish zero additional depth-target bytes, required scene payload, one depth descriptor, effective density, successful CPU submission identity and completed-fence composition GPU time. The latter includes output transfer and fog; it is not a separate arithmetic-only fog timer. `Frame.Plan.DepthShaderReadable` declares the scene-depth contract.

HUD, minimap, tactical icons, warnings, debug lines and intelligence markers stay in the later output-space Overlay pass. Fog consumes only depth/color already rendered from authorized immutable visuals; it does not read faction data, expose hidden entities or change targetability. Existing opaque VFX keep their normal world-depth ordering. General transparent volumetric smoke/soft particles remain outside the opaque scene-composition contract. See [Atmospheric Scene Depth Composition](adr/AtmosphericSceneDepthComposition.md) and [Battlefield Intelligence](BattlefieldIntelligence.md).

Reproducible pixel comparisons:

```powershell
$env:FORGELINE_ATMOSPHERE_CAPTURE_DIRECTORY = Join-Path (Get-Location) 'artifacts/atmosphere-captures'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.AtmosphereIntegrationTests
```

The paired PPM captures use the production terrain/object renderers and actual 120/420/1000 m cameras. The test matrix covers 100/150/200% raster scales, static equality, hidden-object invariance, disabling and shadow compatibility. Native hardware/WARP tests compare distance extinction to an analytic reference, height extinction to independent numerical integration, and test exposure 0.25/1.15/4, exact overlay preservation, missing camera, zero density, resize/suspend, descriptor release and recording failure. Raster scaling does not qualify physical-monitor DPI transitions. Moving-camera/bridge/unit silhouettes, smoke art-direction, long occlusion/resize soak and real device removal remain manual acceptance.

Hardware comparison:

```powershell
$env:FORGELINE_ATMOSPHERE_BENCHMARK_OUTPUT = Join-Path (Get-Location) 'artifacts/atmosphere-performance.json'
$env:FORGELINE_BENCHMARK_DRIVER = '<installed driver version>'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.AtmospherePerformanceQualificationTests
```

Observed on NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, 1280×720, three buffers, VSync off, nine fixed representative terrain chunks, exposure 1.15 and ACES. Every sequential Direct/Linear/Distance/Height pair uses 64 warmups and 512 CPU samples; GPU statistics sample distinct completed fences. No build, asset compilation or second native qualification overlapped the measurement.

| Camera | Direct total GPU ms | Linear total / composition ms | Distance total / composition ms | Height total / composition ms |
| --- | --- | --- | --- | --- |
| 25 m | 0.92 | 1.21 / 0.19 | 1.24 / 0.23 | 1.29 / 0.25 |
| 90 m | 1.05 | 1.22 / 0.18 | 1.33 / 0.20 | 1.34 / 0.23 |
| 120 m | 1.27 | 1.44 / 0.17 | 1.48 / 0.24 | 1.43 / 0.26 |
| 240 m | 1.75 | 1.78 / 0.11 | 1.80 / 0.16 | 1.69 / 0.17 |
| 420 m | 1.82 | 1.80 / 0.10 | 1.58 / 0.15 | 1.84 / 0.17 |
| 1000 m | 1.45 | 1.48 / 0.09 | 1.48 / 0.09 | 1.61 / 0.10 |

Before the changes, equivalent 25/90/240 m direct GPU means were 1.02/1.10/1.49 ms and linear means 1.03/1.06/1.59 ms (composition 0.17/0.14/0.10 ms). Sequential total-time variation prevents claiming a total GPU or CPU improvement. The paired output-stage increment for fog is approximately 0.00–0.09 ms in this workload. CPU includes allocator reuse/Present, not only fog recording. This fixture excludes objects, simulation and HUD; WARP timings are never hardware performance evidence.

Production-client windowed/borderless qualification passed with Height fog, Medium shadows and 1000 extra instances, zero material/texture binding failures and 186 SRVs (179 asset, three scene, three shadow, one depth). Observed completed-frame GPU total/composition timings were 14.69/0.22 ms at 1600×900 and 51.53/8.61 ms at 5120×2160. These short-run samples are functional evidence, not controlled before/after comparisons or sustained FPS guarantees. High resolution materially increases output cost; Disabled retains the direct fallback.


## Depth-aware GPU particles

Graphics exposes an explicit bounded particle submission within World, after opaque terrain/instances and before scene composition/Overlay. `GpuParticleRenderer` owns fixed frame-indexed upload streams and an indexed quad. Its vertex shader evaluates seeded analytic motion and lifetime; no compute/UAV or indirect API is introduced. `ConfigureParticleDepth` recreates the existing depth surface as typeless at an idle boundary and adds/shares its SRV. No extra render target or texture is allocated.

Particle pipelines require alpha blending, reserve t15 for scene depth, forbid depth writes and fit a 33-DWORD root signature (32 vertex constants plus one depth table). Their submission unbinds the DSV, transitions DepthWrite→PixelShaderResource and restores the world depth state before output/overlay. Linear blending is supported only by this explicit depth-aware contract; unrelated opaque pipeline rules remain. FP16 blend alpha does not preserve the negative clear/debug sentinel.

Two particle timestamps per synchronized frame measure the combined analytic update/render workload and publish only after the associated fence completes. Resize resets samples, and disposal/device-removal follows existing native lifetime rules. Presentation owns admission, visual policy and copied event identity; Graphics owns resources, barriers, recording order and retirement. See [Bounded analytic GPU particles](adr/BoundedAnalyticGpuParticles.md), [Combat and logistics VFX](CombatAndLogisticsVfx.md) and [qualification](GpuParticleQualification.md).

## Final image and quality profiles

The shared scene composite has a 40-DWORD vertex constant block, one scene texture table and an additional shared depth table only for atmosphere (41/42 root DWORDs). Color decoding/lighting precede FP16 world and transparent particle drawing. Atmospheric extinction, exposure/relative white balance, ACES, bounded contrast/saturation and spatial filtering precede a single sRGB output transfer. Clear/debug sentinel centers and the subsequent full-resolution Overlay pass bypass those transforms.

AA reads the center once plus four clamped neighbors, chooses a lower-gradient direction and retains at least 70% of the original color. Disabled AA reads one scene sample; enabled AA reads five, with at most five corresponding depth samples under fog. There are no intermediate/history targets, temporal resources, sharpening or resolution scaling. Existing completed-fence composite timing measures the entire final image, including fog/AA.

One presentation settings resolver owns Low/Medium/High defaults and construction-time budget fallback. Software selects Low; available local budget, if queryable, bounds optional scene/shadow/constant-arena/particle payload. Insufficient or unsupported composition falls back to the direct image and existing mesh effects. The profile remains stable during the session; existing LOD hysteresis and zoom-dependent fog/particle behavior are retained. Graphics diagnostics report actual AA, scale 1, scene payload/count, zero intermediate/history targets, sample ceiling and completed-fence GPU time. The client qualification snapshot reports requested/active profile and fallback reason.

Profile tables, measured costs, native color/UI/resize tests and capture reproduction are in [Postprocessing Qualification](PostProcessingQualification.md). The ownership and spatial-AA tradeoffs are recorded in [Bounded Spatial Postprocessing](adr/BoundedSpatialPostprocessing.md).

Explicit `IGraphicsDevice.CaptureFrame` qualification reads the native swap-chain RGBA output before Present using the existing owner-thread readback and fence retirement. It rejects suspended, pending-resize and occluded surfaces. Ordinary frames allocate/copy no readback. The client exports unscaled PNGs and verifies actual surface size, source build identity, repeated pixels and compiled runtime hashes; see [Native visual reference evidence](VisualReferenceEvidence.md). Readback stalls are excluded from timed samples and cannot qualify sustained motion.

Capture input fingerprints cover dynamic upload bytes and vertex, shadow-frame and composite constants while explicit readback is active. They exclude GPU addresses and are diagnostic evidence rather than a complete command-stream attestation. Optional `captureShadowDepth: true` reads the native shadow depth for investigation; ordinary captures leave this memory empty. Both resources remain fence-owned, and a skipped backbuffer copy rejects capture.

Directional depth transitions from depth-write to copy-source after casting. Each frame copies it once into a plain R32 receiver texture, which transitions from pixel-read to copy-destination and back before world sampling. This separates sampled depth from depth-attachment layout. A High reference case on the recorded hardware produced two differing edge pixels across frame targets despite identical upload fingerprints; a diagnostic depth copy eliminated the difference. A combined read state alone passed an isolated128-repeat test but failed after broader allocation reuse, so it was insufficient. The driver-internal mechanism remains unproven. The materialized receiver path applies to ordinary frames, includes both texture sets in quality budgeting/frame plans, and is covered by native repeatability, hidden-caster, resize, resource-release and performance qualification. The single copy is included in shadow GPU timing.
