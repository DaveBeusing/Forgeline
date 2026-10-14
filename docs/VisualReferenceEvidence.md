# Native visual reference evidence

The optional capture lane extends Invoke-RtsReferenceQualification.ps1 and the production client's explicit D3D12 readback. Normal gameplay frames never allocate a capture target, copy pixels or serialize evidence. Immutable reference snapshots are produced after 32 completed extraction ticks with seed2026. These are authored presentation acceptance fixtures, not a balance preset or a running full-match performance benchmark.

Six states share the canonical Central Divide terrain and committed world objects: untouched daylight, an operational industrial asset layout, mixed formation/convoy, the authored first crossing and logistics corridor, damaged/wreck units and a critical factory, and a combat-effects layout with identified and hidden foreign units. Existing intelligence/sensor/extraction contracts filter foreign units; persistent damage/wreck effects use the existing presentation extractor. Combat bursts at known visible positions are explicitly authored effects. Resource/power HUD values are fixed fixture data. No gameplay content, inventories, production rates or combat rules change.

The full matrix contains six scenes, three reference zooms, four displays and four requested profiles: 288 native images, each checked against seven further captures by default. `-RepeatCaptures` permits2..128 total readbacks; the count is recorded per capture and all pixels must agree exactly. Reference angles are45° yaw,-55° pitch,45° vertical FOV; distances120/420/1000m. Native output sizes are1920x1080,2560x1440,3840x2160,3440x1440. Raster DPI96/144/192 and UI scale1 are recorded. The swap-chain output is explicitly resized and its actual dimensions checked. The host window remains1280x720 and may display a scaled/clipped view: full native backbuffer evidence does not certify a physical4K monitor or monitor-DPI transitions.

Typed `graphicsSettings` fixture overrides and the runner's resolution/texture/AA/shadow/atmosphere/particle parameters resolve through the production policy. Requested overrides, effective scale/textures/vegetation state and fallback are retained in each profile. Lower world resolution leaves the native output and HUD unchanged. See [graphics settings](GraphicsQualitySettings.md) for ranges, application and the recorded four-profile cohort. Historical evidence below retains its original source identity and matrix size.

Each QHD Medium normal view also exports eight adjacent shifted/yawed camera poses. Readback waits interrupt frame pacing; these inspect spatial shimmer and camera changes, not sustained judder or a live motion acceptance.

Build this exact committed revision and compile/qualify assets before capture. Use a new output root every run. Source commit must equal the client assembly's SDK informational revision; runtime payloads are hashed before/after. No image from another build is silently reused.

```powershell
dotnet build ForgeLine.sln --configuration Release
dotnet run --project tools/ForgeLine.AssetCompiler/ForgeLine.AssetCompiler.csproj --configuration Release --no-build -- --source assets/source --runtime assets/runtime --clean --qualification-output artifacts/visual-assets.json
pwsh ./build/Export-VisualAssetInventory.ps1 -OutputPath artifacts/visual-inventory.json
pwsh ./build/Invoke-RtsReferenceQualification.ps1 -Capture -OutputRoot artifacts/visual-reference-hardware
pwsh ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Software -Scenes BattlefieldCombat -Profiles Low -Zooms NormalGameplay -Displays 640x360 -Warmups 3 -Samples 8 -OutputRoot artifacts/visual-reference-warp
pwsh ./build/Compare-VisualReferenceEvidence.ps1 -CurrentRoot artifacts/new-reference -BaselineRoot artifacts/visual-reference-hardware
```

The manifest records commit/build/runtime/driver/adapter, source fixture hash, runtime payload hash, seed/tick, camera, requested and actual surface, host size, requested/active profile/fallback, lighting, raster DPI, native PNG and pixel SHA256, repeat-pixel equality, draws/LODs/particles/HUD vertices, resource/memory/health snapshots and sampled CPU/GPU/FPS distributions. PNG rows come directly from native RGBA readback without resizing. Existing scene/depth/shadow/fence lifecycle remains authoritative; unsupported capture throws. Minimize, pending resize and occlusion reject capture. No state is written back to simulation.

CPU timings include submission, Present and frame reuse, excluding capture/encoding/export; GPU statistics use distinct completed fences after warmup. The default32 warmups/128 samples are bounded short measurements, not sustained FPS or total CPU simulation time. VSync is off. Idle and combat fixtures run sequentially on the same device/settings; builds, compilation and other GPU tests must not overlap. Software measurements are functional evidence only. The WARP CI lane uses a native640x360 backbuffer to keep software rasterization within the existing fence timeout; this diagnostic size is rejected on hardware and cannot satisfy any of the four acceptance resolutions. The1920x1080 WARP attempt exceeded the existing ten-second fence wait on the hosted runner. Logical texture/target/upload residency and adapter-budget observations are distinct from physical total VRAM.

Compare checks manifest completion, unique keys, actual dimensions, RGBA PNG header/hash, repeated pixels, zero binding failures and no fallback meshes. Baselines remain read-only. Matching file checksums are exact-byte evidence; changed fixtures/hardware are classified, not silently compared as equivalent. No accepted goldens or art-quality thresholds exist. Optional bounded native-pixel and matched performance gates are described in [Graphics reference performance](GraphicsReferencePerformance.md); they do not grant human art acceptance. Failures return nonzero and a failure.json after output initialization; incomplete progress manifests cannot qualify. Request/binary mismatch or an existing output directory is rejected before native startup without overwriting evidence.

On unequal fixed pixels, both native PNGs and per-frame upload fingerprints are retained. Optional native shadow-depth investigation has a separate API flag; the normal reference matrix performs no shadow-depth readback and records null shadow fingerprints. The complete matrix checks eight successive readbacks across the existing frame resources, without a pixel tolerance. Shadow receiver derivatives are evaluated before varying coverage branches, and High LOD scaling preserves the near-camera sentinel without overflow.

Each scene/zoom/display/profile case constructs fresh production renderers before warmup. This prevents LOD hysteresis and emitter history from depending on matrix order. Particles are first prepared at zero elapsed time, then advanced through two bounded0.1s preparation steps and held paused; authored new bursts therefore have0.2s visual age. Persistent effects retain their fixed-tick source age in addition. Construction, asset upload and these preparation steps precede the timed sample window.

For complete investigation evidence, add `-ContinueAfterMismatch`. All eight readbacks are still compared exactly; mismatches retain native images, hashes and channel/delta counts. Failed cases remain false in `repeatPixelsEqual`. The final manifest is `structural-failure`, failure.json lists failed keys, the process exits nonzero and baseline comparison rejects it. Dimensions, source/runtime attestation and unsafe resource failures still abort immediately. This mode collects a complete failing matrix; it never relaxes acceptance. A High shadow-edge repeatability defect observed on the recorded hardware remains a precise native qualification blocker rather than an accepted golden.

`-Scenes MaterialFoundation` adds optional analytic steel, painted armor, rubber, concrete and rough-terrain controls with three roughness rows. It uses the production BRDF and camera bookmarks without changing the six default fixtures or replacing catalog assets. See [inputs and measured qualification](PbrFoundationQualification.md).

## Review rubric

Use `-RasterDpi 96`, `120`, `144` or `192` with explicit `-Capture` to
inspect production HUD rasterization independently of native display size.
Zero retains the height-derived default. Explicit settings add a `-DpiN`
capture-key suffix and record the actual raster DPI. These are pixel/layout
checks; they do not attest physical Windows monitor scaling or readability.

Two optional material galleries extend the same capture path without changing
the six default fixtures. `-Scenes DirectorateUnits` shows all seven producible
unit identities at their catalog visual scales and ground offsets; Combat
Engineer retains the established Rifle Squad mesh. `-Scenes
DirectorateBuildingStates` shows Command Core, Vehicle Factory and Power Plant
in three rows: operational, unpowered and critical. The latter states pass
through the existing completed-tick extractor, not renderer overrides. Use
`IndustrialBase` for the full thirteen-building roster. Record gallery captures
at all three reference zooms alongside the unchanged default fixtures. The
request permits at most eight distinct scenes. No gallery is an accepted golden
until native output and the rubric have been reviewed.

Record pass/fail/pending per scene/profile/zoom/display, with image filename, region and observed defect. Inspect full-resolution frames and relevant1:1 crops; contact sheets are navigation aids.

| Criterion | Observable acceptance |
| --- | --- |
| Silhouette | Visible unit/building families remain distinguishable; narrow elements retain contrast at the reference zoom. |
| Separation | Units, warning colors and selection/command marks remain distinguishable from nearby terrain, shadows and effects. |
| Shadow attachment | Supported profiles attach contact shadow to feet/decks; no detached dark halo, gross acne or clipping around visible casters. |
| Material consistency | The same asset keeps recognizable color/roughness across zoom/LOD; no missing textures, inverted faces or unexplained factor-only fallback. |
| Terrain | Inspect repeated surface patterns, blur and oblique sampling; adjacent camera poses must not reveal a distracting change of material identity. |
| Foliage | Branch/coverage silhouettes remain legible; distant coverage should not dominate or erase nearby units. |
| Fog and knowledge | Hidden enemy has no mesh/effect/contact marker; identified enemy may appear. Minimap unknown/explored/visible states follow the copied intelligence. Existing public terrain is not a new world fog-mask implementation. |
| VFX | Smoke/dust/impacts retain appropriate contrast without hiding every selection/critical silhouette; no hidden-enemy effect is injected. |
| Motion judder | Review sustained live camera motion separately; readback pose sequences cannot establish frame-pacing acceptance. |
| LOD popping | Review continuous live zoom separately, then locate visible changes using reference poses; no unreviewed LOD is declared accepted. |
| UI | Text and symbols remain readable and clipped within safe areas at the declared raster DPI; physical-monitor readability needs physical inspection. |
| Frame fidelity | Native dimensions/payload/hash match manifest and source/runtime identity; repeated fixed states agree. |

Structural-pass never means visual acceptance. Review all six scenes before accepting any golden; keep reviewer observations and acceptance decisions separate from immutable renderer metadata. No baseline update is automatic. Known missing or ambiguous visual capabilities should become prioritized follow-up work rather than hidden by a numerical score.

Long animated combat, natural unit/production/logistics workloads, continuous LOD transitions, physical monitor/DPI, HDR monitor output and actual device removal/recovery are unqualified by these static fixtures. Existing native lifecycle tests and production windowed/borderless smoke remain separate gates. Optional local debug-layer unavailability must be disclosed in the manifest; zero reported debug messages without an enabled layer is not a debug-clean claim.

## World environment integration

Traverse uses path version 2 with changing target, yaw and distance. Optional haze, authored surface marks and environment/vegetation inputs are recorded and participate in workload comparison. Residency settings are excluded only as the measured resource implementation; review pixel, draw and memory equality independently. See [World environment rendering](WorldEnvironmentRendering.md).
