# Graphics quality and display settings

The client defaults to Medium. Settings offers Low, Medium, High and Ultra, with independent overrides for implemented features. Selecting a preset clears its overrides while retaining manual exposure, color grading, display, camera and input preferences. `PROFILE` means inherit the selected preset. Simulation and headless execution do not consume these settings.

| Preset | Shadow size | Particles / fill areas | Atmosphere | Surface detail / vegetation motion | Object LOD diameter | Texture detail | Spatial AA |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Low | 1024 | 512 / 0.5 | Disabled | Disabled | 0.8 | Quarter | Disabled |
| Medium | 2048 | 2048 / 1 | Height | Enabled | 1 | Full | Low, 20% |
| High | 4096 | 4096 / 2 | Height | Enabled | 1.1 | Full | High, 30% |
| Ultra | 4096 | 4096 / 2 | Height | Enabled | 1.25 | Full | High, 30% |

All presets default to 100% world resolution. Ultra retains more object detail; it shares the bounded High shadow/particle/AA resources. It does not enable additional effects. Vegetation motion controls wind animation, not vegetation visibility or gameplay coverage.

## Independent controls and persistence

`RenderQualitySettings` owns nullable overrides for `resolutionScale` (finite 0.5–1), `antialiasing` (Disabled/Low/High), `shadows` (Disabled/Low/Medium/High), `textures` (Full/Half/Quarter), `objectDetail` (Low/Medium/High/Ultra), `surfaceDetail`, `vegetationMotion`, `atmosphere` (Disabled/Distance/Height) and `particles` (Disabled/Low/Medium/High). The frontend offers resolution steps 50/75/100%; fixture requests can use the full validated range. AA is the existing bounded spatial filter; no temporal history is introduced. Decals and other unimplemented methods have no settings row.

Texture detail selects the supplied mip chain at renderer construction, dropping at most one or two top levels. Small textures retain a usable mip; compressed top levels must preserve legal BC7 block dimensions. GPU payload decreases without changing runtime formats, material transfer functions or the compiler. This is not texture streaming, and CPU source payload is still loaded.

Schema 3 stores preferences in `%LOCALAPPDATA%\FORGELINE\settings.json`, outside the installation. `--settings-root` supplies an isolated qualification directory. Schema 1/2 migrations preserve display, camera, bindings, grading and non-default effect choices. Legacy Low retains its previous full textures and vegetation motion through explicit overrides until the preset is selected again. A malformed graphics group is quarantined and reset independently of valid display/input preferences. Other recovery follows the existing settings store contract.

Labels and descriptions live in `ForgeLine.UI/GraphicsSettingsText.resx`; the neutral resource is English and can be localized without changing the input model. The details view scrolls its supported rows, reuses bounded vertex buffers and explains when settings apply. [Native settings capture](qualification/graphics-settings.png) records the 1280×720 software-rendered UI, not an accepted art golden.

## Applying and reverting

| Change | Application |
| --- | --- |
| UI scale | Immediate frontend preview; saved on Apply |
| Window size / windowed or borderless | Immediate unsaved trial, then explicit Keep |
| Graphics features / preset | Next session or match, after retiring the previous renderer resources |
| Camera and binding preferences | Existing validated frontend/session application |

No exposed graphics option requires an executable restart. There is no quality mutation of an active match. Apply validates a complete candidate before writing it. Display trials last 15 seconds; Keep is available only after the requested surface has successfully presented and the window is not minimized. During a trial, other settings edits are frozen. Escape, cancellation, expiry and disposal restore the previous live dimensions/mode. A failed restore attempts a safe windowed configuration. Failed trials never save the candidate. Borderless uses the current monitor; windowed choices are 1280×720, 1600×900, 1920×1080, 2560×1440 and 3840×2160.

Win32 display changes run on the window owner. The frontend render owner queues surface updates and applies the existing fence-synchronized resize. A successful Present publishes dimensions for confirmation. Low-level scene-output reconfiguration rejects changes while recording, retires outstanding work, rebuilds dependent targets and restores the previous configuration on failure. A failed restoration becomes a controlled session fault.

## Capabilities and effective policy

Adapter selection remains D3D12 feature level 11_0 or newer, with maximum supported feature level in diagnostics. Composition additionally probes FP16 texture/render-target/blend/shader-load and R32 depth shader-load support. Software adapters cap optional features to Low, including Quarter textures, no fog/AA/wind and Low shadow/particle budgets. The requested settings remain visible in diagnostics.

Software-cap diagnostics also report the reason when Low was already requested: explicit Full/High overrides may still be capped even though the profile name does not change.

Construction checks available local budget, when available, against scaled scene/depth, optional native overlay depth, shadow textures and constants, particles and a conservative 64 MiB Full texture allowance (16 MiB Half, 4 MiB Quarter). Pressure degrades Ultra → High → Medium → Low; explicit overrides cannot bypass degraded caps. Unsupported composition or insufficient Low budget selects direct native output with neutral grading and optional AA/shadows/fog/particles disabled. The allowance is a policy estimate, not a guarantee of physical VRAM residency. Runtime allocations and device removal still use controlled fault handling. Quality is stable during a session; there is no continuous adaptive budget polling.

At reduced world resolution, world color and depth use ceil(output × scale), while HUD, selection and command indicators retain native output dimensions. The existing composition draw interpolates four display-linear samples and transfers nearest world depth to a native overlay depth target. Spatial AA can require up to 20 scene loads, versus five at native resolution. Thus reduced resolution saves target memory but is not guaranteed to improve frame time. See the [decision and ownership contract](adr/ValidatedGraphicsQualityAndDisplayPreview.md).

## Reproduction and measured evidence

Build the exact clean source and qualify canonical assets before capturing. Run hardware workloads sequentially; do not overlap builds or GPU tests. Existing development environment overrides remain available, including `FORGELINE_RENDER_QUALITY`. Prefer typed fixture settings for reproducible per-feature comparisons; manifests record requested settings, effective profile, fallback and actual resource dimensions.

Development overrides use `FORGELINE_RENDER_SCALE` (invariant decimal 0.5–1), `FORGELINE_ANTIALIASING`, `FORGELINE_SHADOW_QUALITY`, `FORGELINE_TEXTURE_QUALITY`, `FORGELINE_OBJECT_DETAIL`, `FORGELINE_ATMOSPHERE_QUALITY`, `FORGELINE_PARTICLE_QUALITY`, `FORGELINE_SURFACE_DETAIL` and `FORGELINE_VEGETATION_MOTION`. Enum values match the typed controls above; boolean values are `true`/`false`. Invalid values fail validation before renderer construction, and environment choices do not overwrite saved preferences.

```powershell
dotnet build ForgeLine.sln --configuration Release
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes @('IndustrialBase','BattlefieldCombat') -Profiles @('Low','Medium','High','Ultra') -Zooms @('CloseTactical','NormalGameplay','Strategic') -Displays 1920x1080 -Warmups 32 -Samples 128 -RepeatCaptures 8 -OutputRoot artifacts/quality-profiles
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes @('IndustrialBase','BattlefieldCombat') -Profiles Medium -Displays 1920x1080 -ResolutionScale .75 -TextureQuality Half -Antialiasing High -Warmups 32 -Samples 128 -RepeatCaptures 8 -OutputRoot artifacts/quality-overrides
& ./build/Invoke-ReferencePerformance.ps1 -Profiles Medium -ResolutionScale 1 -OutputRoot artifacts/quality-performance
```

The capture runner also accepts `-ShadowQuality`, `-AtmosphereQuality` and `-ParticleQuality`; use its validated parameter names for enum values. Pass PowerShell arrays directly when selecting multiple profiles/scenes. The performance comparer ignores informational requested-settings metadata but rejects changed effective features, scales, assets or fixture workloads. Full raw manifests retain sampled frames; the [portable qualification summary](qualification/graphics-quality-settings.json) retains distributions, pass means, allocations, draws, LODs, logical resources, DXGI budget observations and native image hashes.

Measured rendering source is `be90ae0939255ac48c0e2fda107d0b0540fb1b29`, version 0.1.357; baseline is exact master `e12e913be4bfd61873545c5a19f195511e331e01`. Hardware is NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653, Windows 10.0.26300, .NET 10.0.12, Release, VSync off. Paired Medium camera runs use 32 warmups/128 CPU samples and 126 distinct completed GPU fences. The matched 15% gate passes mean/p50/p95/p99 in all four cases. CPU mean deltas range −1.18% to +0.59%; GPU −2.92% to +0.60%. These are bounded observations, not evidence of a speedup.

| Native 1920×1080 NormalGameplay | CPU mean ms | GPU mean ms | Instance draws | Logical textures bytes | DXGI local usage bytes |
| --- | --- | --- | --- | --- | --- |
| Industrial Low | 20.229 | 17.051 | 37 | 4,049,384 | 162,308,096 |
| Industrial Medium | 25.855 | 24.697 | 37 | 52,707,564 | 250,912,768 |
| Industrial High | 29.401 | 27.209 | 37 | 52,707,564 | 402,780,160 |
| Industrial Ultra | 31.464 | 29.591 | 39 | 52,707,564 | 402,796,544 |
| Combat Low | 22.293 | 21.071 | 30 | 4,049,496 | 162,373,632 |
| Combat Medium | 26.854 | 26.535 | 30 | 52,708,956 | 251,043,840 |
| Combat High | 27.993 | 27.489 | 30 | 52,708,956 | 402,845,696 |
| Combat Ultra | 28.950 | 28.609 | 32 | 52,708,956 | 402,862,080 |

The 24 profile cases and six 75%/Half/High-AA cases pass eight exact readbacks each, with zero binding/submission faults or fallback meshes. Eight additional CombatSupportRepeated cases exercise four profiles at tactical/strategic zoom with the production 1,000-object fixture (short 8-warmup/32-sample functional runs). The WARP request Ultra/50%/Full/High shadows/particles resolves to Low/Quarter/Low shadows/particles, with the reason and original overrides retained; two repeat readbacks agree.

The Windows x64 development package from the same clean rendering source passes two-publish manifest reproducibility and an extracted-install smoke run. All 492 canonical asset payload hashes agree; the runtime root also contains its manifest and the two established startup media files, with no authoring or temporary files. An extracted-package 75%/Half/High-AA combat reference passes eight exact readbacks and matches the source PNG SHA256 `6C782216DAFBE17E5383EED4A7A5742BD980AD78803747DAD9AF880A337096E2`. Normal windowed and borderless smoke runs present 48/36 frames at 1600×900/5120×2160 with zero texture/material binding failures. The separate extracted-package smoke presents 103 frames at 1600×900. Synthetic smoke stress uses existing development primitives; the production 1,000-object reference is the no-fallback asset test. These functional results do not qualify sustained frame rate or physical-monitor legibility.

At 75%, the 1920×1080 output has 1440×810 world targets: scene payload 27,993,600 bytes, world depth 4,665,600 and native overlay depth 8,294,400. Native scene/depth are 49,766,400/8,294,400; net target savings are 17,107,200 bytes. Half texture payload is about 13.78 MB. The scaled High-AA cohort is slower in these short runs; its composition can exceed 10 ms. Lower memory is verified, a performance improvement is not.

Release builds, architecture/version checks, policy, migration, native scaling/depth/resize, settings UI and display-trial tests pass. The broad local suite recorded 1,886 tests: 1,876 passed, nine skipped and one unchanged intermittent 1,984-byte presentation-allocation assertion failed. A subsequent complete presentation run passed 837 tests with seven skips, and the new native settings capture passes. This does not turn the original full-suite run into a clean result. CI supplies the exact final-head check. Static images are not accepted goldens: physical-monitor readability, long resize/occlusion soak, real device removal, camera-motion review and sustained 60 FPS with live simulation remain unqualified. The recorded Medium GPU times still exceed the 16.67 ms engineering target.
