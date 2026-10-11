# GPU particle qualification

## Reproduction

Build Release and compile the normal source assets through the canonical asset compiler before starting GPU measurement. Run only one hardware workload at a time, without concurrent builds, asset compilation or other rendering tests. WARP tests establish behavior and resource lifetime; their timings are not hardware-performance evidence.

```powershell
dotnet build ForgeLine.sln --configuration Release
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release --no-build -- --source assets/source --runtime assets/runtime --clean --qualification-output artifacts/particle-assets.json
$env:FORGELINE_PARTICLE_QUALIFICATION_OUTPUT = 'artifacts/particle-disabled.json'
$env:FORGELINE_PARTICLE_QUALIFICATION_ENABLED = '0'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ParticlePerformanceQualificationTests
$env:FORGELINE_PARTICLE_QUALIFICATION_OUTPUT = 'artifacts/particle-after.json'
$env:FORGELINE_PARTICLE_QUALIFICATION_ENABLED = '1'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ParticlePerformanceQualificationTests
```

The opt-in fixture refuses software adapters, emits native PPM captures and JSON camera/resource/timing records. Remove the two qualification environment variables before the regular solution test run. For production client qualification, `FORGELINE_PARTICLE_QUALITY` accepts Disabled, Low, Medium or High and overrides persisted `particles.quality` for the new render session.

## Measured baseline and workload

The pre-change production mesh renderer was measured from master `c3ec45d0f618c555a3caac72d2dc15d387fd117e` with the qualification fixture added before graphics changes. Native disabled-path captures from the changed implementation are byte-identical to this baseline at all three zooms.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, feature level 12_1, 25,309,478,912 dedicated bytes reported. Resolution 1280×720, VSync off, FP16 scene composition, exposure 1.15/ACES. Camera target (0,0,0), yaw 0°, pitch −50°, actual distances 120/420/1000m; JSON includes complete camera state. The same copied 16×16 grid at 4m spacing contains 64 each of production smoke-plume, dust-cloud, spark-emission and building-explosion IDs, plus one excluded Debug-only instance. There is no terrain, shadow or simulation workload in this isolated timing comparison.

Each viewpoint uses 64 warmups and 512 CPU samples, collecting unique completed GPU fences. CPU mean includes Present, driver scheduling and frame-index fence reuse. Particle-enabled captures freeze the burst at 0.2 visual seconds and repeatedly prepare with zero delta, so age, position and opacity do not change during sampling. This measures bounded admission/upload/sorting, analytic GPU motion and transparent drawing at one controlled age; it is not a full animated-match performance guarantee.

| Distance | Mesh baseline total GPU ms | Particle-enabled total GPU ms | Particle update/render GPU ms | Baseline/enabled CPU frame mean ms | Particle population | Mesh fallback effects | Total baseline/enabled effect draws |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 120m | 0.782 | 1.496 | 0.079 | 7.782 / 6.824 | 112 | 248 | 4 / 5 |
| 420m | 0.767 | 1.315 | 0.370 | 3.017 / 3.655 | 2,048 | 34 | 4 / 2 |
| 1000m | 0.606 | 1.215 | 0.296 | 2.971 / 3.459 | 1,024 | 0 | 1 / 1 |

The close dense grid reaches the conservative fill budget before the population budget, so most effects retain mesh fallback. The normal view saturates the 2,048-particle Medium cap. Strategic distance preserves building explosions and suppresses smaller families under existing distance policy. Particle drawing itself uses one batch at every populated viewpoint. The sum includes remaining mesh batches and can be one draw above baseline at close range; batching contracts are not widened or silently changed.

The enabled runtime adds exactly 393,228 logical upload/index bytes at the default three frame streams, fixed regardless of effect churn, plus small timestamp bookkeeping and the shared depth descriptor. Texture residency in both workloads is exactly 1,039,300 bytes; particle rendering adds no textures or targets. Existing FP16 scene payload at this resolution is separately 22,118,400 bytes. Upload payload excludes native heap alignment/driver allocation and must not be labelled dedicated VRAM. Peak particle/buffer counts, texture residency and adapter memory diagnostics are separate facts.

Sequential timing variance affects both CPU and GPU totals. The observed whole-frame GPU cost increases, while draw reduction at normal/strategic ranges and bounded visual motion are the measured benefits. No portable speedup or target-client FPS claim follows from these isolated numbers. A compute/indirect implementation was not justified by this population or measurement; analytic shader evaluation keeps the current engine resource contract narrow.

## Functional and visual evidence

Native comparison captures retain production mesh fallback and show procedural particle clusters at matched positions. Close/normal/strategic views use identical cameras. The strategic explosion minimum radius is capped at eight metres and cannot exceed the tier's conservative fill budget. The synthetic grid is deliberately dense and does not represent a natural distribution of combat events.

WARP integration additionally renders the actual Central Divide terrain with Height atmosphere and an explosion above terrain, confirming identical pixels with/without hidden copies and clean binding/retirement. Graphics tests cover direct and linear depth occlusion, soft intersections, minimize/restore and changed dimensions. Presentation tests cover stable identities after snapshot reorder, copied burst age, deterministic pause/session behavior, corrupt asset fallback, partial allocation failure, sustained 600-effect churn under Low quality and zero allocations over 128 warmed steady preparations. Existing mesh VFX and instance tests remain intact.

Native production-client window-mode qualification and complete solution validation remain the integration gates. The existing general CI, frame-hotpaths and scalability workflows cover this change; no acceptance thresholds, test timeouts or dependency versions are weakened. Long animated battles, physical-monitor/DPI readability, transparency ordering between moving sources, fog approximation, repeated occlusion/resize and actual device-removal recovery remain manual acceptance. The local optional Windows debug layer is unavailable, so native success is not a debug-layer-clean claim.
