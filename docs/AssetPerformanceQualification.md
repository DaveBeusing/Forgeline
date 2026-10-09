# Asset Performance and Visual Qualification

## Purpose

FORGELINE qualifies the integrated Vertical Slice visual asset set as a system rather than accepting assets only because they compile or render in isolation.

Qualification covers:

- runtime asset integrity and loadability;
- runtime asset footprint by asset type;
- dependency, material, texture, collision, and LOD references;
- representative render submission and culling;
- tactical-camera and strategic-zoom LOD behavior;
- draw-call and visible-instance diagnostics;
- combat VFX population and distance reduction;
- integrated Direct3D 12 client startup;
- continued headless independence.

The process extends the existing asset compiler, presentation diagnostics, rendering benchmarks, and Windows client smoke path. It does not introduce a second asset pipeline or a graphics dependency into simulation or headless projects.

## Acceptance Model

A gameplay asset is qualified only when all applicable production conditions remain true:

- the stable asset ID is valid and unique;
- the source compiles into a valid runtime asset;
- every runtime payload referenced by the manifest exists and can be read;
- generic dependencies resolve;
- material references resolve to material assets;
- texture references resolve to texture assets;
- collision references resolve to mesh assets;
- LOD references resolve to mesh assets;
- LOD levels are positive and unique;
- LOD distances are finite, positive, and strictly increasing;
- mesh bounds, when present, are valid;
- the representative client can start and render the integrated scene;
- the headless runtime remains independent from visual assets;
- performance-sensitive changes are compared with measurements rather than visual intuition alone.

The compiler remains the earliest gate. Runtime qualification is deliberately a second gate so corrupted or missing generated payloads fail predictably even when the source metadata was valid at compile time.

## Runtime Asset Qualification

A successful Asset Compiler run automatically executes RuntimeAssetQualification.

Canonical command:

~~~powershell
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release -- --source assets/source --runtime assets/runtime --clean --qualification-output artifacts/asset-qualification.json
~~~

The JSON report records:

- total asset count;
- mesh, texture, and material counts;
- total runtime payload footprint;
- runtime footprint grouped by mesh, texture, and material asset type;
- runtime catalog load duration;
- full runtime asset read duration;
- the twelve largest generated runtime assets;
- qualification diagnostics.

Runtime byte counts are the reproducible size of generated .flasset files. They are distinct from resident GPU texture bytes. The graphics qualification report records both current and peak resident texture bytes represented by uploaded mip payloads, so compiled footprint and runtime residency remain separate measurements.

### Qualification Failure Codes

| Code | Meaning |
|---|---|
| ASSETQ000 | Runtime catalog could not be loaded. |
| ASSETQ001 | A declared runtime payload is missing, unreadable, or malformed. |
| ASSETQ002 | Published asset bounds are invalid. |
| ASSETQ003 | LOD levels are invalid or duplicated. |
| ASSETQ004 | LOD distances are invalid or not strictly increasing. |
| ASSETQ005 | A runtime reference is not a valid stable asset ID. |
| ASSETQ006 | A runtime reference points to an asset that is absent from the catalog. |
| ASSETQ007 | A typed runtime reference resolves to the wrong asset type. |

Any error makes qualification fail and returns a non-zero Asset Compiler exit code.

## Integrated Vertical Slice Visual Scene

The canonical integrated smoke scene is the normal Windows client running the actual MatchRuntime.

CI adds a representative repeated-entity load without replacing the real scenario:

~~~powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --smoke-test --render-stress 1000 --visual-qualification-output artifacts/visual-qualification.json
~~~

The run therefore exercises the real Vertical Slice composition, including terrain, Directorate base/gameplay presentation, world assets, fog/intelligence presentation, UI/information surfaces, and active scenario state while also adding a repeated rendering workload.

The JSON report captures the latest completed D3D12 render frame with:

- graphics adapter name;
- dedicated video-memory capacity reported by the adapter;
- smoothed frames per second;
- smoothed frame duration;
- smoothed CPU render duration;
- visible and total terrain chunks;
- submitted terrain triangles;
- terrain draw calls;
- instance draw calls;
- total measured world, gameplay-overlay, and developer-debug draw calls;
- visible and total render instances;
- high-detail and reduced-LOD instance counts;
- active VFX, pool capacity, and dropped VFX count;
- active scene-light direction;
- directional and ambient light intensity;
- manual scene exposure;
- active tone-mapping mode.

`gpuMilliseconds` is populated from Direct3D 12 timestamp queries recorded around the production graphics command list and read only after the owning frame fence completes. `gpuTimingAvailable` remains explicit; unsupported timing never falls back to CPU frame time.

The Windows qualification gate also requires finite positive directional-light/exposure state, non-negative ambient intensity, and the current AcesFitted tone-mapping baseline. This validates that the production scene is not silently rendered through an unconfigured lighting path. It is a structural qualification rather than an image-similarity gate.

Developer diagnostics are disabled by default in the canonical smoke scene. Player-facing selection/command/strategic overlay rendering is a separate path, while developer lines are depth-tested and category-gated. When diagnostics are enabled interactively, Shift + F1 metrics report rendered gameplay-overlay lines, rendered/dropped developer-debug lines, and measured developer-overlay CPU submission time. A disabled developer overlay must submit zero developer line draw calls.

## Rendering Benchmarks

ForgeLine.Rendering.Benchmarks contains synthetic rendering baselines plus representative mixed-content submission cases. The production qualification benchmarks load the compiled `assets/runtime` catalog so object and terrain cases exercise texture/material resolution and texture binding rather than the legacy untextured fallback. Compile runtime assets first when running the benchmark host outside CI.

Run the full rendering qualification matrix with:

~~~powershell
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release -- --filter "*" --job Short --artifacts artifacts/rendering-benchmarks --exporters BriefJSON
~~~

The representative mixed-content scene contains repeated Directorate armor and reconnaissance, Command Core and Vehicle Factory instances, resource deposits, vegetation, industrial props, and combat VFX. This scene also qualifies the current silhouette/readability geometry at tactical, normal RTS, and strategic camera distances, including the distinct clustered resource meshes and presentation-bound culling path. A separate road-readability workload submits the production road surface, shoulder, curve, T-junction, and cross-junction assets through the same runtime material path.

Object/material submission is measured at:

- close tactical distance;
- normal RTS gameplay distance;
- strategic zoom.

Road/infrastructure submission is independently measured at the same three camera regimes so added edge/junction readability can be compared without changing the historical mixed-content benchmark population.

Terrain submission is measured at:

- close tactical distance;
- normal representative coverage;
- strategic distance with high terrain coverage.

BenchmarkDotNet `Short` repeats measurement iterations so qualification is not based on a single timing sample. The output retains runtime and machine metadata and is published by CI under `artifacts/rendering-benchmarks`.

The benchmark is intended to reveal regression in submission cost, allocations, culling behavior, material resolution/binding, LOD reduction, terrain coverage cost, and VFX distance policy. BenchmarkDotNet results must be compared on equivalent hardware/runtime configurations before treating a difference as an optimization result.

## LOD and Strategic Zoom Qualification

Units and buildings use their catalog-defined LOD0/LOD1/LOD2 thresholds. World props, resources, and vegetation use the world-presentation high/reduced LOD policy. Combat VFX use explicit maximum render distances.

Qualification relies on both correctness tests and representative rendering measurements:

- catalog tests verify threshold transitions;
- runtime qualification verifies that referenced LOD assets actually exist and have valid chains;
- the representative tactical benchmark keeps gameplay-relevant detail active;
- the representative strategic benchmark forces distant content toward reduced representation;
- fine VFX is suppressed beyond its authored presentation range;
- strategic symbols, minimap information, and UI remain separate presentation mechanisms rather than forcing distant geometry to remain detailed.

Readability has priority over decorative geometry. An optimization that lowers cost but makes unit role, building function, resource identity, or critical state unreadable is not accepted.

## Current Engineering Budgets

The engine architecture defines the current prototype targets:

- at least 60 FPS on target hardware;
- therefore a nominal 16.67 ms total frame budget at 60 FPS;
- stable 20 Hz simulation;
- at least 1,000 active combat/logistics entities without architectural redesign;
- 10,000 lightweight simulation entities as an early stress target.

The visual qualification process does not invent per-asset polygon, texture-byte, or draw-call ceilings before representative measurements justify them.

Instead:

- the runtime report always exposes the largest asset outliers;
- instancing and draw-call diagnostics expose batching regressions;
- tactical and strategic rendering benchmarks expose submission regressions;
- the integrated D3D12 smoke exposes real client frame/CPU-render diagnostics;
- unusually expensive assets must be fixed or documented with measurement evidence before their cost becomes a production convention.

When stable hardware baselines are established, concrete warning and failure thresholds may be added without changing the measurement model.

## Accepted Textured Rendering Baseline

The qualification baseline is anchored to reproducible Windows CI evidence for the same Vertical Slice scene with 1,000 render-stress instances.

Historical qualification run 36770423081, before the production texture/material pipeline, reported on Microsoft Basic Render Driver:

- 22.253 FPS;
- 44.937 ms frame time;
- 34.784 ms CPU render time;
- 16 visible of 144 terrain chunks;
- 26 measured terrain/instance/debug draw calls;
- 783 visible of 1,055 total instances.

Textured `master` qualification run 37345829712 on commit `c1b41f4394ccc360e2cc9c83a5f4dde6227aa96a` reported on the same Microsoft Basic Render Driver class:

- 3.241 FPS;
- 308.559 ms frame time;
- 242.552 ms CPU render time;
- the same 16 visible terrain chunks;
- the same 26 measured draw calls;
- the same 783 visible of 1,055 total instances;
- 175 loaded GPU textures;
- 998,540 resident texture bytes;
- 175 of 4,096 SRV descriptors used;
- 13 terrain texture bindings / maximum texture samples per pixel;
- zero texture or material binding failures.

The unchanged draw-call, terrain-visibility, and instance counts are the useful structural comparison: the texture/material pipeline did not introduce draw-call fragmentation in the canonical scene. The large timing increase on Microsoft Basic Render Driver reflects the much heavier texture-sampling workload on a software rasterizer and is not accepted as a target-hardware performance verdict. Target-hardware comparisons must use the same scene, resolution, camera regime, runtime, and adapter class.

### Vertical Slice budgets

The following budgets are tied to the measured current Vertical Slice and are enforced by the Windows qualification path where they are hardware-independent:

| Metric | Accepted baseline / budget | Rationale |
|---|---:|---|
| Active terrain layers | 4 maximum | Production splat contract |
| Terrain texture bindings / samples | 13 maximum | 1 control + 4 Base Color + 4 Normal + 4 ORM |
| Canonical measured draw calls | 26 maximum | Matches both pre-texture and textured qualification evidence |
| Peak resident texture payload bytes | 4 MiB maximum | Concept surface completion measures 2,085,616 bytes in the canonical stress scene and 2,208,276 bytes in the full object/effect/state gallery |
| Peak SRV descriptors | 512 maximum | Current measured value is 175; retains substantial headroom inside the 4,096-descriptor heap |
| Compiled gameplay texture runtime footprint | 3 MiB maximum | Concept surface completion measures 2,404,260 bytes, excluding the separately budgeted studio splash |
| Texture/material binding failures | 0 | Invalid resource state is never an accepted baseline |
| D3D12 debug-layer warnings/errors | 0 when the layer is available | New renderer warnings require investigation |
| Static texture upload balance | uploads = live textures + released textures | Detects accidental repeated uploads or unbalanced lifetime accounting |

These are Vertical Slice qualification budgets, not final full-game limits. Deliberate content growth may revise them only with a measured before/after qualification and an updated rationale.

### Target-hardware frame budget

The renderer continues to target 60+ FPS on target hardware, corresponding to a 16.67 ms total frame budget. Hardware-sensitive frame and GPU timing are recorded but are not CI failure thresholds on Microsoft Basic Render Driver.

Target-hardware qualification must retain:

- adapter and dedicated-memory metadata;
- viewport resolution and window mode;
- close tactical, normal RTS, and strategic camera regimes;
- high terrain coverage;
- representative repeated-material industrial content;
- representative unit formations;
- the same asset/runtime build.

Use repeated BenchmarkDotNet runs and representative/median values rather than one noisy sample. GPU timing must come from D3D12 timestamps; CPU frame time is not a substitute.

### Resource lifetime and steady state

The graphics diagnostics record current and peak texture residency, current and peak SRV use, cumulative successful texture uploads, and cumulative texture releases.

For a steady-state client qualification:

- static textures must not upload once per frame;
- each live texture owns one SRV descriptor;
- disposal must return its descriptor and resident-byte accounting;
- upload count must equal live textures plus released textures;
- intentionally persistent material and terrain-control caches may remain resident for the session;
- repeated load/unload support must not show monotonic unexplained texture or descriptor growth.

### Mip, filtering, and readability review

Visual qualification must inspect the same production material path at close tactical, normal RTS, and strategic zoom plus oblique terrain views.

Accept only when:

- Base Color remains correctly sRGB interpreted;
- Normal and ORM remain linear data;
- complete compiler-generated mip chains are used;
- trilinear transitions remain stable during zoom;
- anisotropic sampling remains stable at oblique angles;
- no global negative mip bias introduces shimmer;
- distant normal detail does not create moire;
- material detail remains subordinate to silhouette, role, and gameplay readability;
- terrain control transitions do not expose chunk seams.

### Known measurement limits

The accepted baseline deliberately records the following limitations rather than inferring unavailable data:

- D3D12 timestamp queries currently measure the complete production graphics command list; terrain and object/material GPU time are not yet split into separate timestamp ranges.
- Terrain CPU submission time is available separately and remains useful for attributing CPU-side terrain regressions.
- Microsoft Basic Render Driver is suitable for functional D3D12, descriptor, lifetime, shader, and structural qualification, but its frame/GPU timing is not a target-hardware performance baseline.
- The repository does not yet provide a deterministic tolerance-based image comparison system. Visual mip, shimmer, anisotropic, and readability review therefore remains an explicit qualification activity rather than a brittle screenshot CI gate.
- The current client owns visual resources for the active session. Texture lifetime regression tests exercise repeated GPU texture ownership/release directly; broader multi-session residency comparisons should be added when supported in-process map/session reload becomes a production lifecycle.
- D3D12 debug-layer cleanliness is gated when the Windows environment exposes the layer. An unavailable debug layer is reported rather than treated as evidence of cleanliness.
- GPU memory diagnostics currently represent texture mip payload residency tracked by ForgeLine Graphics; they are not a complete accounting of driver allocations, render targets, depth buffers, or vendor-specific residency.

These limits are extension points for future diagnostics, not reasons to substitute estimates for measured values.

### Re-evaluation triggers

Texture streaming, virtual texturing, additional compression infrastructure, descriptor virtualization, or more complex material indirection remain deferred until measurements show that simpler policies are insufficient.

Re-evaluate only when one or more of the following occurs on representative target hardware/content:

- resident texture use materially exceeds the accepted Vertical Slice budget after resolution/reuse review;
- SRV usage loses the documented headroom;
- texture upload or load latency becomes visible during supported lifecycle transitions;
- larger authored maps make persistent terrain-control/material residency impractical;
- target-hardware GPU time exceeds the 16.67 ms total-frame direction because texture/material work is a measured dominant contributor;
- compiled texture footprint grows beyond the current budget despite shared materials, mip discipline, and sensible source resolution.

The first response to a measured regression remains reuse, batching, resolution correction, mip correctness, or redundant-sample removal rather than immediately adding virtual texturing.

## Visual Regression Policy

The repository does not currently provide a stable screenshot/reference-image comparison subsystem. A large unrelated visual-regression framework is therefore not introduced solely for this qualification pass.

Current visual regression coverage is provided by:

- deterministic presentation/catalog tests;
- runtime asset reference and payload validation;
- LOD-chain validation;
- the Windows D3D12 integrated smoke;
- representative tactical/strategic rendering benchmarks;
- explicit strategic/minimap semantic presentation tests.

Reference-image capture can be added later when the renderer has a stable deterministic capture path and comparison policy. At that point, capture configuration, tolerance, target hardware/driver expectations, and update procedure must be documented together.

## CI Qualification

The primary CI workflow performs these visual gates in order:

1. Release build.
2. Clean runtime asset compilation.
3. Runtime asset qualification and artifacts/asset-qualification.json.
4. Windows D3D12 Vertical Slice visual smoke with 1,000 stress instances.
5. artifacts/visual-qualification.json.
6. Headless diagnostic and 10,000-lightweight-entity stress runs.
7. Correctness/regression tests.
8. Vertical Slice full-match validation.

The existing artifact upload step publishes the JSON qualification reports with the other engine diagnostic artifacts.

## Headless Independence

Asset qualification must not move visual dependencies into simulation.

ForgeLine.Headless remains valid without:

- ForgeLine.Graphics;
- Direct3D 12;
- window creation;
- runtime visual asset loading;
- UI.

The headless smoke and stress runs remain CI gates after visual qualification specifically to catch accidental dependency leakage.

## Outlier Investigation Procedure

When a regression or expensive asset is found:

1. Reproduce with the same asset build and comparable hardware/runtime configuration.
2. Inspect the runtime qualification report for footprint outliers and reference anomalies.
3. Inspect tactical and strategic benchmark allocation/submission changes.
4. Inspect integrated client draw calls, visible instances, LOD distribution, terrain triangles, and VFX population.
5. Determine whether the cost originates in source density, material fragmentation, missing instancing, ineffective LOD reduction, unnecessary VFX, or renderer integration.
6. Change only the measured bottleneck.
7. Repeat the same qualification and compare evidence.
8. Record any intentionally retained high-cost outlier with its gameplay/readability justification.

This keeps optimization measurement-driven and prevents arbitrary content degradation.

## Concept surface completion measurements

The complete catalog contains 324 assets and 5,127,670 compiled bytes. Texture runtime footprint is 4,190,251 bytes including 1,785,991 studio-splash bytes; gameplay textures account for 2,404,260 bytes. The new bounded atlases provide all physical component surfaces, eight decals and the ten shared effect material families. The 3 MiB gameplay compilation budget and 4 MiB GPU residency budget reflect this measured content growth; studio splash retains its separate 3 MiB budget. Descriptor, terrain sample, upload balance and instance draw limits are unchanged.

A native D3D12 gallery renders all thirteen buildings, seven units, twenty-six world features, thirty-six effects, twenty-four infrastructure state cases and eight construction/operational/damage cases: 114 visible instances and 118 textured runtime mesh submissions including state attachments, zero fallback meshes and zero material/texture binding failures. It loads 39 materials and thirteen asset textures, with 2,208,276 resident bytes including renderer fallback textures. A six-object LOD0 closeup also renders the articulated tank turret and confirms visible exterior roofs, armor and decals. The canonical 1,000-instance stress run on NVIDIA RTX PRO 5000 Blackwell at 1600 x 900 measures eleven instance draws plus eighteen visible terrain draws, 2,085,616 resident bytes and 5.865 ms completed GPU frame time. Windowed and 5120 x 2160 borderless startup qualification pass; the larger borderless frame measures 19.689 ms GPU time and is not a 60 FPS performance claim. The requested local D3D12 debug layer is unavailable, so local reports do not claim native debug-layer qualification. Hosted graphics qualification remains required.
