# Postprocessing and quality qualification

## Contracts and reproduction

The normal client uses one persisted `renderQuality` settings object. Settings → GRAPHICS QUALITY chooses Low/Medium/High for the next match; manual scene exposure is retained. The grade object validates relative white balance/tint (±0.1), contrast (0.95–1.08) and saturation (0.95–1.05), with neutral defaults. Quality owns AA. `FORGELINE_RENDER_QUALITY` selects a profile for development qualification; explicit existing shadow/atmosphere/particle overrides remain available. Non-default saved effect settings are retained. Snapshot diagnostics contain the actual configured profile and graphics post state.

| Profile | Directional shadow / cascades | Atmosphere | Particle population / fill areas | Terrain detail | Object LOD diameter factor | AA blend |
| --- | --- | --- | --- | --- | --- | --- |
| Low | 1024 / 1 | Disabled | 512 / 0.5 | Disabled | 0.8 | Disabled |
| Medium | 2048 / 1 | Height | 2048 / 1 | Enabled | 1 | 20% |
| High | 4096 / 1 | Height | 4096 / 2 | Enabled | 1.1 | 30% |

All profiles retain resolution scale 1, three FP16 scene targets with the default frame count, one fullscreen composition draw, zero additional postprocessing intermediate/history targets and no sharpening/upscaling. Scene payload at 1280×720 is 22,118,400 bytes. Current shadow texture payload for three frames is 25,165,824 / 100,663,296 / 402,653,184 bytes, including caster depth and materialized R32 receiver textures, with the existing 3,145,728-byte constant arena shared by the shadow/receiver contract. Particle upload/index payload is 98,316 / 393,228 / 786,444 bytes. These are logical resource payloads, not exact physical VRAM allocations; heap alignment, driver allocation and other texture residency remain separate diagnostics. Historical measurements below preceded the separate receiver texture; current native evidence must measure the added copy and payload.

Software adapters use Low. Available local budget, when queryable, is checked against scene + shadow textures/constant arena + particle payload before session construction. High→Medium→Low degradation is stable; insufficient Low budget or unsupported postprocessing uses direct output with optional shadow/fog/particles off and existing mesh fallback. Native allocation/device faults remain controlled session failures. Budgets are not continuously polled for adaptive quality; resize does not silently alter the chosen profile.

Build Release and compile the canonical source assets first. Run hardware workloads sequentially, without builds, compilation or other GPU tests. WARP establishes behavior, never target-GPU performance.

```powershell
dotnet build ForgeLine.sln --configuration Release
$env:FORGELINE_PASS_BENCHMARK_OUTPUT = "$PWD/artifacts/post-neutral.json"
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ScenePassPerformanceQualificationTests
$env:FORGELINE_POST_QUALIFICATION_AA = 'High'
$env:FORGELINE_PASS_BENCHMARK_OUTPUT = "$PWD/artifacts/post-aa-high.json"
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ScenePassPerformanceQualificationTests
Remove-Item Env:FORGELINE_PASS_BENCHMARK_OUTPUT,Env:FORGELINE_POST_QUALIFICATION_AA
$env:FORGELINE_QUALITY_QUALIFICATION_OUTPUT = 'artifacts/post-quality.json'
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.RenderQualityQualificationTests
Remove-Item Env:FORGELINE_QUALITY_QUALIFICATION_OUTPUT
```

Set `FORGELINE_POST_REGRESSION_OUTPUT` to an absolute output directory when running Graphics `PostProcessingTests` to export native 320×240 thin diagonal, warning-line and overlay regression captures. Remove it for the regular solution suite. Hardware qualification refuses software adapters and emits PPM captures plus JSON states, resources and completed-fence timing records.

## Matched baseline and isolated AA

Pre-change master `6f81805d44fc4dad04f9971d508e15b079ab15a9` was built and measured before graphics changes. Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, feature level 12_1, 25,309,478,912 dedicated bytes reported. All measurements use 1280×720, VSync off, exposure 1.15/ACES, 64 warmups and 512 CPU samples; only unique completed GPU fences contribute. CPU includes Present and frame reuse. Run-to-run driver scheduling/thermal variance is not a portable FPS or speedup result.

The existing nine-chunk representative terrain workload is identical before/after, with default target (0,0,0), yaw 0°, pitch −50° and 25/90/240m cameras. There are no objects, shadows, fog, particles or gameplay UI in this isolated comparison. Native direct/composed regression tests establish neutral color equivalence within one UNorm byte; the new AA/grade are neutral for the changed-neutral comparison.

| Distance | Baseline linear total GPU ms | Changed neutral total GPU ms | Changed High-AA total GPU ms | Baseline / neutral / High-AA composite GPU ms |
| --- | ---: | ---: | ---: | ---: |
| 25m | 1.610 | 1.486 | 1.569 | 0.376 / 0.283 / 0.513 |
| 90m | 1.698 | 1.539 | 1.508 | 0.409 / 0.332 / 0.410 |
| 240m | 1.767 | 1.807 | 1.975 | 0.122 / 0.150 / 0.241 |

Observed additional composite cost for High AA against changed neutral is 0.230/0.078/0.091ms. Whole-frame differences include variance and must not be attributed solely to AA. CPU means baseline / neutral / High-AA are 3.935/3.818/3.848, 3.949/3.836/3.828 and 4.248/4.391/4.389ms. No new texture/target payload accompanies AA. The full JSON includes direct-path control measurements and complete frame plans.

## Complete profile workload and visual evidence

The canonical 144-chunk Central Divide is rendered at industrial target (650, sampled terrain height, 520), with 16 copied production smoke/dust/spark/building-explosion IDs at fixed 0.2s visual age, eight narrow infrastructure proxy meshes, one excluded hidden effect, and full-resolution red/green selection circles and a blue command line. Actual 120/420/1000m views use the same target, yaw 0° and pitch −50°. Shifted/yawed captures (+2m X/Z, +0.035 radians yaw) additionally exercise camera change and have deterministic repeated pixels; they are outside the timing samples. JSON records both complete camera states.

| Quality / distance | Total GPU ms | Composite including fog/AA GPU ms | CPU frame mean ms | Active particles |
| --- | ---: | ---: | ---: | ---: |
| Low / 120m | 4.043 | 0.175 | 6.673 | 80 |
| Low / 420m | 6.861 | 0.793 | 7.788 | 76 |
| Low / 1000m | 10.330 | 0.396 | 11.657 | 32 |
| Medium / 120m | 6.413 | 0.597 | 7.558 | 160 |
| Medium / 420m | 8.841 | 1.027 | 10.218 | 152 |
| Medium / 1000m | 14.007 | 0.892 | 14.558 | 64 |
| High / 120m | 8.100 | 0.552 | 9.860 | 160 |
| High / 420m | 10.572 | 1.367 | 12.021 | 152 |
| High / 1000m | 16.488 | 0.733 | 17.010 | 64 |

These compare whole profile settings, including different shadow coverage/resolution, not isolated AA costs. Terrain visible chunks/draws remain 4/22/56 and resident texture payload 44,864,276 bytes for every profile. Postprocessing adds no textures. The workload has no running simulation, units or natural combat-event distribution and cannot establish full-match FPS. The fixed particle population is below the respective caps; dedicated particle churn/cap tests remain the saturation evidence.

Native captures were inspected at tactical/normal/strategic zoom. Low fog was removed after the initial comparison obscured the thin proxies. The final Low/High captures retain silhouettes and full-resolution selection/command colors. AA terrain captures alone do not establish text sharpness; native overlay regression compares all pixels in the overlay region byte-for-byte under AA and grading, and production client qualification exercises the existing HUD. Thin diagonal tests retain center contrast, expose partial edge coverage and preserve warning hue. No temporal history exists to retain stale imagery after camera/session/resize changes.

Graphics tests cover invalid grade/configuration, direct/composed texture transfer, fog/depth/particle composition, missing view, recording guards, minimize/settled resize and descriptor retirement. Presentation covers fixed quality budgets, software/unsupported/pressure fallback and existing instancing. Client tests cover menu navigation, persistence, preserved grade/camera settings and invalid-grade recovery. The new native image fixture runs in an isolated collection to avoid overlapping its window/GPU lifecycle with other native fixtures.

Long animated battle/camera sequences, physical monitor/DPI legibility and real device-removal recovery remain manual acceptance. Spatial AA is a conservative edge filter, not temporal reconstruction or complete subpixel recovery; motion shimmer remains possible. The optional local debug layer is unavailable, so native success is not a debug-layer-clean claim. Existing CI thresholds, timeouts and acceptance budgets are retained. See [architecture decision](adr/BoundedSpatialPostprocessing.md).
