# World environment rendering

The production client combines bounded terrain GPU residency, existing four-layer materials, instanced vegetation and scene-depth atmosphere. Appearance consumes copied presentation inputs. Authoritative heightfields, navigation, collision, cover, simulation time, intelligence and Fog of War remain unchanged.

## Terrain residency

`TerrainResidencySettings.Default` enables a 128-chunk cache (validated range 1–4096). Call `TerrainRenderer.Prepare(camera, width, height, shadowFrame)` on its creating render thread before recording. Preparation covers the union of camera and shadow frusta, loads every required chunk and evicts least-recently-used nonrequired chunks. If coverage exceeds capacity, preparation fails before changing residency; it never silently drops visible terrain. A changed pose or viewport without preparation is rejected. The standalone constructor retains eager loading when the residency argument is omitted.

CPU world data remains available for all chunks. Geometry and normalized control mipmaps are created on admission and reused while resident; material textures remain shared in a bounded cache. Control uploads queue their copy and transition before subsequent direct-queue rendering. Their staging buffers, allocators and lists retire at the upload fence. Evicted geometry, textures and descriptors retire at their last submission fence. The existing 4,096-entry retirement bound can synchronize under exceptional pressure.

Diagnostics include required/resident chunks, geometry/control payload bytes, admissions, evictions and preparation CPU time. Payload bytes are logical data sizes, distinct from DXGI local-memory usage and its allocation granularity. Existing world-space macro/micro and slope refinement, normal blending, four-material palettes, normalized mipmaps and the 13-texture budget are retained.

## Authored surface wear and vegetation

`ITerrainSurfaceMarkSource` provides copied, public authored footprints. `TerrainSurfaceMarks` validates at most 64 marks; `WithSurfaceMarks` creates an immutable presentation profile. Tire, tracked-vehicle and road wear map to existing dirt, mud and industrial material families. Material priority preserves wear in affected four-slot palettes. The 33×33 control grid represents broad ground wear; sub-grid tire detail can disappear and should use the established authored texture decal quads. Existing roads, shoulders, concrete transitions and detailed quads remain the map integration. This interface does not generate live entity trails, reveal hidden movement or duplicate combat/wreck effects.

`VegetationRenderSettings` defaults to density 1 and maximum distance 4,000 m; accepted distance is 100–4,000 m. Identity-based density selection is stable across frames. It filters vegetation only, for world and shadow batches, before the existing frustum/LOD submission. Units and props retain their visibility behavior. Existing wind bounds and asset LOD distances apply. `VisibleVegetationInstances` makes the filtered workload observable.

## Appearance and haze

`WorldEnvironmentSettings` accepts finite daylight, cloud cover and wetness in [0,1]. Defaults 1/0/0 preserve the neutral lighting and shaders. Daylight controls a bounded dawn-to-day appearance with readable ambient illumination; zero is dawn, not night. Cloud cover uses static, broad world-space modulation of direct light. Wetness changes upward-facing nonmetal surfaces by bounded darkening and roughness adjustment. These are session appearance inputs; changing them rebuilds affected shader variants on the next session. There is no weather simulation, moving sky or dynamic rainfall.

Optional `GraphicsLocalHazeSettings` validates a finite center, radius 10–2,000 m and density 0–0.01. Scene-depth composition adds a compact quadratic density field integrated with three-point Simpson sampling after the existing fog start distance. Existing opacity remains capped at 0.65. This approximation can miss small volumes crossed by very long rays. It has no scattering, raymarch target or extra pass. Three evaluations occur per fog evaluation; spatial AA can multiply this to 15 at native resolution or 60 at scaled resolution per output pixel. Native tactical overlays remain after atmosphere composition. Haze never changes targeting, minimap or visibility extraction.

Software/pressure fallback Low disables cloud and wetness shader modulation while retaining requested settings and bounded daylight. Its disabled atmosphere also suppresses local haze. All inputs validate before GPU allocation. Graphics preferences remain additive schema 3 fields and use the existing graphics quarantine on invalid data; haze centers serialize their X/Y/Z fields.

## Reproduction

Build a clean committed Release source and canonical runtime assets. Run native captures sequentially without concurrent GPU tests or builds:

```powershell
& ./build/Invoke-ReferencePerformance.ps1 -Profiles Medium -Warmups 32 -Samples 128 -OutputRoot artifacts/world-performance
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes IndustrialBase,BattlefieldCombat -Profiles Medium -Zooms CloseTactical,NormalGameplay,Strategic -Displays 1920x1080 -WorldDaylight .3 -CloudCover .5 -Wetness .65 -LocalizedHaze -SurfaceTracks -OutputRoot artifacts/world-appearance
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes IndustrialBase,BattlefieldCombat -Profiles Medium -CameraPath Traverse -Displays 1920x1080 -OutputRoot artifacts/world-traverse
```

`Traverse` (path version 2) moves the target across multiple chunks while changing yaw and distance. Fixed/Sweep retain path version 1. Timed frames include terrain preparation and admissions; event pumping occurs outside the CPU measurement. Extraction remains frozen, so these runs do not qualify live simulation throughput. `-EagerTerrain` isolates residency from identical visual settings. Performance comparison excludes residency configuration as the implementation under measurement but rejects changed effective appearance, vegetation, haze, marks and other workload inputs. Pixel hashes, draw counts and resource diagnostics must also be reviewed.

Development overrides are `FORGELINE_WORLD_DAYLIGHT`, `FORGELINE_CLOUD_COVER`, `FORGELINE_WORLD_WETNESS`, `FORGELINE_VEGETATION_DENSITY` and `FORGELINE_VEGETATION_DISTANCE`, using invariant decimal values. They do not overwrite saved preferences. The existing graphics UI does not add weather controls.

Native automated checks cover cache traversal/eager pixel equality, shadow coverage, budget failure, resize, haze isolation, repeatability, neutral and altered shader variants, overlay preservation and settings persistence. Physical-monitor readability, long traversal/resize soak, real device removal and sustained 60 FPS remain separate acceptance work. Reference screenshots are evidence, not accepted art goldens.

## Measured qualification and limits

The [portable measurement summary](qualification/world-environment.json) retains the initial failed cohort and the renewed passing cohort. Rendering source is `663897d8e18657445b3552a2d86c305ff3943776`, version 0.1.358; matched master is `378eed96f6afc80a0ac5dac752ed1b3839b4d596`. Release hardware is NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, Windows 10.0.26300 and .NET 10.0.12, VSync off.

The renewed paired Medium 1920×1080 Sweep runs use 32 warmups and 128 CPU samples, with 126 distinct completed GPU fences. The 15% matched mean/p50/p95/p99 gate and the paired-run variance gate pass all four cases. All four final pixel hashes and instance draw counts match master. CPU mean deltas are +0.66% to +4.01%; GPU −0.16% to +2.16%. These observations do not establish a speedup. The initial seven CPU regressions remain in the report; deferred control uploads and event pumping remove their large tail outliers in the renewed window.

| Matched Medium Sweep | CPU mean ms | CPU p99 ms | GPU mean ms | Instance draws | Resident chunks |
| --- | ---: | ---: | ---: | ---: | ---: |
| Industrial tactical | 31.544 | 40.086 | 29.875 | 23 | 24 |
| Industrial strategic | 31.816 | 45.037 | 30.298 | 40 | 50 |
| Combat tactical | 36.733 | 50.885 | 35.582 | 24 | 24 |
| Combat strategic | 29.939 | 40.987 | 29.201 | 33 | 50 |

Terrain geometry drops from 11,066,112 eager bytes to 1,844,352/3,842,400 tactical/strategic bytes; controls from 823,680 to 137,280/286,000 bytes. Shared material payloads are separate. Strategic Sweep includes first admissions: mean allocation is about 10 KB/frame and p99 about 278 KB, not a zero-allocation stream.

The 24 neutral profile/zoom cases, six altered daylight/cloud/wetness/haze/wear cases and eight Medium/Ultra Traverse cases each pass eight exact readbacks with zero binding/submission faults and no fallback meshes. Traverse admits 65–101 chunks within the 128 bound. It still pays first-use material/upload/admission costs: CPU p99 reaches 95.344 ms, allocation p99 25,507,112 bytes. The smaller-cache native tests independently exercise eviction and exact eager equivalence. These results establish bounded functional traversal, not hitch-free sustained play.

Four short 8-warmup/32-sample production 1,000-object cases pass eight readbacks. That fixture contains vehicles and no plants: density 1 versus 0.5 retains the same vehicle submissions and image. Separate IndustrialBase plant captures use 32 warmups/128 samples. Tactical vegetation changes 9→3 while other visible objects remain 25; strategic vegetation 151→72 while other visible objects remain 109. Strategic GPU mean is 29.649→29.560 ms in these short samples; shared texture payload stays unchanged. No substantial frame-rate or VRAM gain is attributed to density. The 1,000-object tactical GPU means around 92 ms are well outside the 16.67 ms engineering target.

WARP requested Ultra with maximal clouds/wetness resolves to Low, zero cloud/wetness modulation, Quarter textures and disabled atmosphere; requested settings remain recorded. Its two repeat readbacks agree. Native haze/shader tests separately preserve the full-resolution overlay and validate changed world pixels.

Release builds have zero warnings/errors; architecture validates 42 projects and version checks retain one patch increment. The broad local suite records 1,901 tests: 1,890 passed, nine skipped and two failures in unchanged asset-recovery/video-decoder tests. Isolated reruns pass all seven asset-recovery and two video cases; the broad run remains failed. Four native texture contracts pass, including synchronous/deferred 13-slot pixel readbacks with actual surface dimensions.

The clean rendering-source Windows x64 development package passes two-publish manifest reproducibility, all 492 canonical payload hashes and a fresh extracted-install smoke at 1600×900 (132 submitted/presented frames, zero texture/material binding failures). Its runtime root contains 495 files and no authoring/temporary files. The extracted-package altered-weather combat reference passes eight readbacks and matches the source pixels and PNG. Provenance records sourceTreeClean=true and the exact rendering source above. The earlier dirty-tree functional package is retained as historical evidence only. Normal smoke uses existing development stress primitives; the reference capture uses production assets. Runtime shader compilation remains part of the established package contract.

Exact final-head CI supplies repository-wide checks. Physical-monitor readability, accepted art goldens, long traversal/resize soak, real device removal and sustained live 60 FPS remain unqualified. Documentation-only evidence updates after the rendering source do not change the measured binaries.


Short live tracks now use the shared presentation decal pool for vehicles owned by the viewing faction. They use existing movement history and eight-tick sampling, without regenerating terrain control maps or authoring enemy movement trails. Broad public surface wear remains owned by ITerrainSurfaceMarkSource. Impact marks align their plane with the sampled terrain normal; they do not deform height or navigation. See [combat and industrial rendering](CombatIndustrialRendering.md).
