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

Runtime byte counts are the reproducible size of generated .flasset files. They are not presented as resident GPU-memory measurements. A future renderer-residency system may add GPU allocation and residency telemetry when the graphics abstraction owns that information authoritatively.

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

The canonical integrated smoke scene is the normal Windows client running the actual VerticalSliceScenario.

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
- total measured world/debug draw calls;
- visible and total render instances;
- high-detail and reduced-LOD instance counts;
- active VFX, pool capacity, and dropped VFX count.

gpuMilliseconds is intentionally nullable and currently remains unavailable. ForgeLine Graphics does not yet expose a validated Direct3D 12 timestamp-query/readback lifecycle. GPU time must not be estimated from CPU frame time.

## Rendering Benchmarks

ForgeLine.Rendering.Benchmarks contains synthetic rendering baselines plus representative mixed-content submission cases.

Run the presentation qualification benchmarks with:

~~~powershell
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release -- --filter "*PresentationBenchmarks*"
~~~

The representative scene contains repeated Directorate armor and reconnaissance, Command Core and Vehicle Factory instances, resource deposits, vegetation, industrial props, and combat VFX.

Two camera regimes are measured:

- tactical view at normal gameplay distance;
- strategic view at long distance.

The benchmark is intended to reveal regression in submission cost, allocations, culling behavior, batching, LOD reduction, and VFX distance policy. BenchmarkDotNet results must be compared on equivalent hardware/runtime configurations before treating a difference as an optimization result.

Terrain-specific baselines remain available through TerrainBenchmarks.

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
