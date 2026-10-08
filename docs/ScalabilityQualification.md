# Scalability qualification

The 20 Hz simulation and 60 FPS rendering targets imply 50 ms per tick and 16.67 ms per frame. They are analysis budgets, not achieved scale promises or CI timing gates. A fast synthetic tick does not qualify production gameplay; null graphics does not qualify GPU time or client frame pacing.

## Workload matrix

| Named workload | Scale / state | Measured operation |
| --- | --- | --- |
| Lightweight empty / medium / large | 0 / 1,000 / 10,000 entities; four component-iteration systems in nonempty cases | One logical 20 Hz tick, CPU phase samples |
| Active movement | 1,000 movers; orders and positions reset before samples | One tick and a separately labeled ten-tick batch; reset excluded |
| Combat | 1,000 shooters plus 1,000 targets; ample finite ammunition and target health | One direct-fire tick |
| Logistics | 500 physical deliveries with navigation; fresh setup before every cycle | Complete bounded cycle, at most 300 ticks; not a tick percentile |
| Scheduler | 65,536 items, 1,024-item batches, at most four workers | Parallel range plus wait; execution/wait counters |
| Central Divide gameplay | Seed 2026, two opponents, production profile; actual entity counts recorded | One tick plus phase timings; no graphics |
| Central Divide validation | Same seed, accelerated validation profile | Separate tick evidence; never interpreted as production performance |
| Navigation | Central Divide cross-map, expansion and bridge-loss alternate routes | Cache-warmed tracked route search |
| Renderer empty / medium / large strategic | 0 / 1,000 / 10,000 mixed instances; no rendered terrain / 49 / 289 total chunks | Terrain, instance and total submission timings; actual visibility/draws |
| Renderer effects/debug UI | 1,200 mixed instances including VFX; 49 chunks; 128 debug lines; development text overlay | Submission with optional debug/UI work; excludes complete gameplay HUD |
| Rapid camera | Same mixed scene, deterministic pan/yaw and distance motion | Visibility, LOD distribution, submission/upload tails and retention |
| LOD boundary | One tank at the target, distance alternates 139.99 / 140.01 m across its 140 m LOD0/1 boundary | Alternating visible LODs are required; current selection is stateless, without hysteresis |
| Scene reload | Fresh instance renderer, first submission, disposal, 1,000 mixed instances | Lazy mesh/material load and upload cost; native live resources return to baseline |
| Extraction/publication | Existing fixed `--frame-hotpaths` scenarios | Separate extraction and publisher timing/allocation; no complete main-thread stall claim |

The native renderer uses the same scenes at a 1600x900 surface, VSync off, hardware-only D3D12. Each frame completes through `WaitForIdle` before GPU diagnostics is read, avoiding repeated stale asynchronous timing samples. Total serial frame time includes submission, presentation and completion waits. This mode measures throughput/capacity and recovery; it does not reproduce the asynchronous client or prove smooth 60 FPS. GPU time is total timestamp-query time; per-pass GPU timestamps are not available. Opt-in backend memory diagnostics reports DXGI local/nonlocal usage and OS budgets for the actual device adapter. These are application video-memory usage/budgets, not a proof that every committed byte is resident. Unsupported queries report unavailable with a reason; default graphics configuration does not query memory.

## Capture

Use the pinned SDK from `global.json`, a Release solution build and compiled runtime assets. The helper builds unless `-NoBuild` is supplied. CPU qualification runs without creating graphics resources in the simulation/navigation hosts; rendering is a separate host with null graphics.

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release --no-build -- --source assets/source --runtime assets/runtime --clean --qualification-output artifacts/asset-qualification.json
pwsh ./build/Invoke-ScalabilityQualification.ps1 -NoBuild -Samples 1024
pwsh ./build/Invoke-ScalabilityQualification.ps1 -NoBuild -Samples 4096 -Gpu
pwsh ./build/Invoke-ScalabilityQualification.ps1 -NoBuild -Samples 1024 -Soak -SoakSamples 16384
```

Each individual benchmark host also accepts `--scalability <output.json> [samples]`. Rendering additionally accepts `--gpu-scalability`; simulation accepts `--scalability-no-phase` for instrumentation-off comparisons. Sample counts are bounded to 16–65,536. Movement, delivery and reload cases explicitly cap samples because they reset/load substantial state; their report records the actual count. Extended sampling is a bounded evidence run, not an hours-long stability qualification. Use recorded wall duration, not logical accelerated simulation time, when reporting soak duration.

Set `FORGELINE_HARDWARE_REFERENCE`, `FORGELINE_CPU_MODEL`, `FORGELINE_GPU_DRIVER` and `FORGELINE_CLOCK_SETTINGS` before capture. Record driver version, active power plan, clock locking, affinity, thermals and background load separately where available. Missing metadata stays null and unqualified. Revision/dirty-tree markers and measured assembly SHA-256 values identify the actual build. CI records its checked-out PR integration tree, which may be a synthetic merge commit.

Reports retain raw samples in execution order, nearest-rank p50/p95/p99, maximum, budget misses, >100 ms hitches, process allocations and GC. Four sampling windows plus a starting snapshot record process private bytes, working set and managed memory. Entity/tick counts accompany simulation windows; native resource/descriptor/DXGI memory windows accompany renderer samples. Process memory includes fixed measurement arrays and previously retained reports, so compare within-case windows and identical matrices. GPU texture payload residency excludes buffers, heaps and driver allocations; it is not OS residency. DXGI usage and budget meanings follow [Microsoft's query contract](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info). Instance payload throughput excludes terrain/debug uploads and disk streaming.

Warmup is 128 operations, or four fresh setups for reset cases and four reloads. GC settles once after warmup, outside samples; subsequent sampling follows natural GC. Process allocation is counted around the action, excluding preparation, observation and reporting. GC deltas include the entire loop and other threads. Phase and renderer clocks are opt-in qualification overhead. Phase timing separates input/systems and tick observers; it includes job waits inside a phase and does not measure isolated worker critical paths. Extraction/publication uses its existing longer warmup protocol.

## Comparisons and regression policy

Keep revision, assets, scenario/profile, seed, viewport, runtime, sample counts and instrumentation configuration explicit. Compare baseline/candidate binaries in alternating ABBA order without simultaneous builds or other qualification runs. Use multiple independent processes on a reference machine. The comparison helper refuses mismatched workloads, hardware/runtime metadata, phase configuration or entity counts:

```powershell
pwsh ./build/Compare-ScalabilityReports.ps1 -Baseline baseline.json -Candidate candidate.json -Output comparison.json
```

Reference matrix entries remain pending until controlled evidence exists:

| Reference tier | Required capture | Acceptance policy |
| --- | --- | --- |
| Shared CI CPU | Deterministic scenario/report contracts, raw matrix reports | Correctness gates only; timing advisory |
| Local laptop | Actual adapter/driver/power metadata, repeated CPU/GPU cases | Exploratory evidence; no global thresholds |
| Designated Windows D3D12 reference | Locked/recorded clocks, stable thermals, several ABBA runs at target resolution | Establish per-scene median and noise envelope before enabling gates |
| Minimum supported hardware | Product-approved configuration and resolution | Not established; no compatibility/performance promise |

A proposed reference acceptance requires production-profile tick p99 within 50 ms and full asynchronous client frame p99 within 16.67 ms, with documented hitch tolerance and memory ceilings for that specific machine/scene. Neither the serial renderer nor component tests can satisfy the full client criterion. First establish repeatable baselines and noise; then evaluate both absolute budget misses and relative changes beyond that noise envelope. Do not hide a failed target by changing profiles, seeds, cameras or counts.

Residency qualification requires stable live resources/descriptors after warmup and return to baseline after unload. Managed/process growth must be explained against entity counts, GC cycles and diagnostic retention; a high-water process allocation alone is not a leak. Retain raw reports for baseline and candidate. Investigate resource accounting failures as correctness failures; leave noisy time and memory ceilings advisory until qualified.

## CI and native coverage

Client visual qualification stops and joins the render owner before writing its report. After the existing GPU idle wait, that owner refreshes the last snapshot's GPU timing and debug counts. CPU/scene counters remain from the last measured frame, and no timing is manufactured when queries or completed frames are unavailable. This fixes short smoke reports that previously captured an available query system before any timing result had been retained.

`Scalability qualification` runs measurement/authority contracts and the deterministic CPU matrix on PRs, uploading reports independently of the main CI's native gate. Manual dispatch can request an extended CPU run and a native lane on `[self-hosted, Windows, X64, forgeline-performance]`. That label does not establish or provision a reference machine. Operators must install the debug layer and supply hardware/driver/clock metadata. The main `build-test` gate remains unchanged.

Native qualification exercises surface suspend, 800x450 resize and 1600x900 recovery. Actual window minimize/occlusion, complete gameplay HUD states, asynchronous frame pacing, intermittent terrain streaming, GPU per-pass analysis and an hours-long match/residency soak remain additional reference-host qualification. Run existing window-mode qualification and native resize/lifetime tests alongside the new lane; zero debug counts when the debug layer is disabled are not debug-layer evidence.

Optimize only a repeatable bottleneck with a scoped before/after trace. Current candidates include delivery-cycle allocation, high-density strategic GPU work and cold scene reload latency. GPU total time alone does not identify terrain, material sampling or overdraw as the cause. No advanced renderer redesign or performance optimization is justified solely by target aspirations.
