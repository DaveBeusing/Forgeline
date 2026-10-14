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

## Measured cohort and remaining qualification

The [portable measurement summary](qualification/world-environment.json) records paired master `378eed96f6afc80a0ac5dac752ed1b3839b4d596` and rendering source `436f1b62a5b4de8a5a55c5b55bd0f1413757c5d3` (0.1.358). Four matched Medium 1920×1080 Sweep cases retain identical final pixels and instance draw counts. Resident terrain drops from 144 eager chunks to 24 tactical/50 strategic chunks: geometry 11,066,112 to 1,844,352/3,842,400 bytes, controls 823,680 to 137,280/286,000 bytes. Shared material payloads are separate.

The initial 15% performance gate fails seven CPU distributions, including upload-related traversal work and large tail outliers. Lower measured GPU means are not a verified sustained improvement. That cohort predates deferred control uploads and per-frame event pumping; a new clean-source measurement is required to qualify the optimization. Appearance/traversal/density matrix and extracted-package qualification also remain pending for the final rendering source. The report retains the failed cohort rather than relabeling it as passed.
The local optimized tree builds with zero warnings/errors; architecture checks validate 42 projects and version checks retain one patch increment. Its broad suite records 1,901 tests: 1,890 passed, nine skipped, two failed in unchanged asset-recovery/video-decoder tests. Isolated reruns pass all seven asset-recovery and two video cases; the broad run remains failed. Four native texture contracts pass, including synchronous/deferred 13-slot pixel readbacks with actual surface dimensions.

A local development package passes two-publish manifest reproducibility, all 492 canonical payload hashes and a fresh extracted-install smoke at 1600×900 (32 submitted/presented frames, zero texture/material binding failures). Its runtime root contains 495 files and no authoring/temporary files. Provenance explicitly records sourceTreeClean=false because upload/diagnostic changes are still uncommitted. This verifies functional packaging, not the final committed release source; the smoke uses existing development stress primitives. Runtime shader compilation remains part of the established package contract.