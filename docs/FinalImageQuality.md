# Final image quality review

## Visual targets and baseline

Integration baseline is master a4eb694 (0.1.343), including the material,
world dressing, mechanical motion and battlefield-effect refinements.
Low/Medium/High retain their existing shadow, atmosphere, LOD and particle
budgets. Directorate targets are neutral steel/concrete, muted industrial
paint, warm directional light with cool ambient fill, restrained atmosphere
and distinct silhouettes. Critical state uses the existing text/symbol/state
contracts as well as color. There is no authored night scenario to qualify.

Native baseline review of BattlefieldCombat CloseTactical and IndustrialBase
NormalGameplay shows legible full-resolution HUD/resource symbols and muted
world colors. Terrain repetition and saturated fallback bursts are existing
content limitations. These observations do not justify changing shared
exposure, white balance, fog density or introducing bloom/sharpening. Neutral
grade and 1.15 exposure remain the reproducible controls.

The current five-sample AA uses luminance alone to detect/select edges.
An equal-luminance red/green boundary can consequently evade filtering;
a single-pixel diagonal blends the full 30% at High. Native synthetic
regressions isolate both cases without relying on scene-shadow variation.
The refinement keeps partial edge coverage while preserving more
of a local single-pixel peak. Full-resolution overlays remain outside the
world composite.

## Evidence policy

Reference capture now permits explicit raster DPI 96/120/144/192 at any
supported native output size. Zero retains the established height-derived
DPI. Explicit DPI is recorded in the capture key and row; it changes the
production HUD raster/layout input only. Camera, native output size, gameplay
input/hit testing and physical Windows display scaling are separate facts.
Native 3840x2160 and 3440x1440 readback can prove pixel dimensions and raster
clarity; physical-monitor readability still requires display inspection.

Repeated fixed states must remain pixel-identical; failed High shadow cases
retain failure status. Adjacent camera poses do not establish temporal
stability, sustained animation or frame pacing. No accepted golden or human
sign-off is inferred from successful structural checks. TAA requires motion
vectors, reset/disocclusion/alpha evidence and a separate justified budget;
no such evidence currently warrants temporal resources here.

## Qualification at 0.1.344

The refinement retains five scene reads, at most five depth reads, the
40-DWORD constant block, one output transfer, existing frame-indexed FP16
scene targets and zero intermediate/history targets. Maximum High blending
is still 30%; isolated details use 15%. Native 320x240 WARP regression shows
an equal-luminance edge neighbor changing from RGB (0,148,0) to (108,138,0)
and a bright one-pixel diagonal changing from (219,219,219) to (238,238,238).
Axial white lines and flat gray pixels remain byte-identical. Dark details,
warning hue, overlay exclusion, neutral transfer, resize and retirement use
the focused Graphics regression contracts; four PostProcessingTests pass.

Baseline native capture uses a4eb694; final scene capture uses ee69e47.
Both use the same source/runtime content, NVIDIA RTX PRO 5000 Blackwell
Laptop GPU, driver 32.0.15.9653, VSync off, 8 warmups, 32 timed frames and
two repeated readbacks per state. Six fixtures cover Low/Medium/High and
120/420/1000m at 1920x1080, 2560x1440, 3840x2160 and 3440x1440.
The final 216-state matrix has 214 pixel-stable rows. DaylightTerrain and
BattlefieldCombat High CloseTactical at 1920x1080 fail repetition; the
matrix remains structurally failed. Baseline already failed three of 54
Full-HD rows, including these two. This is evidence of an unresolved
pre-existing category, not proof of its GPU cause or golden acceptance.

All 18 Low Full-HD outputs are byte-identical to baseline. In the inspected
BattlefieldCombat Medium CloseTactical pair, the resource bar and operations
panel regions are byte-identical while world edges change. Native 4K HUD
1:1 crops show sharp glyph/icon rasterization. Eight additional
IndustrialBase Medium NormalGameplay captures cover explicit DPI
96/120/144/192 at both exact 4K and Ultrawide; all eight pass structural
checks. They were built at 24c5121. Raster DPI is not physical-monitor
inspection. Peak resident texture payload in the scene matrix is
52,708,956 bytes; no source assets or target counts changed.

Matched isolated High-AA timing uses the existing nine-chunk terrain-only
workload, 1280x720, FP16/ACES exposure 1.15, 64 warmups and 512 samples,
with completed-fence GPU timestamps and no concurrent build/GPU workloads.
It uses 25/90/240m cameras rather than the full-scene RTS distances.
Composite GPU milliseconds before/after are 0.554/0.538, 0.432/0.417 and
0.220/0.225. Whole-frame GPU milliseconds are 1.661/1.738, 1.533/1.465
and 1.805/1.983. After CPU means are 4.386/3.756/4.334 ms, p99
8.532/7.610/8.056 ms. Mixed frame results and driver variance do not
establish a general speedup. Logical scene payload stays 22,118,400 bytes.
Across full fixtures, the highest row GPU means per display are
25.052/32.506/50.068/38.137 ms; those short authored states do not qualify
60 FPS or natural-match throughput.

The background smoke now samples running frames while unfocused and checks
completion before the unfocused input exit. Ordinary input cancellation,
pause and simulation rules retain their existing path. The owned hidden
qualification process completed both client runs. Windowed 1600x900 passed
all canonical checks (49 submitted/presented frames, 11 instance draws,
zero binding failures). Borderless 5120x2160 closed and wrote its report
(62 submitted/presented frames, zero binding failures), but the unchanged
canonical draw guard rejected 13 instance batches against its limit of 11.
It remains a failed qualification; no budget was relaxed. Observed windowed
and borderless rates were 8.75 and 15.38 FPS, respectively.

Local evidence: artifacts/image-baseline-native-v2, image-final-native,
image-dpi-{96,120,144,192}, image-edge-final, image-aa-{before,after}.json,
image-{window,borderless}.json and mechanical-logs/image-*.log. Generated
captures are review evidence rather than shipped or automatically accepted
goldens. The full regression and hosted build-test results are recorded in
PR #175; these remain required gates before integration.

## Remaining acceptance limits

No golden sign-off is claimed. High repeated-pixel variance, the native
5120x2160 borderless instance-draw budget, physical-monitor readability,
sustained camera/LOD/battle motion and real device removal remain open.
The optional local debug layer was unavailable; zero binding failures are
not a debug-layer-clean claim. Fixed-age VFX still omit the short muzzle
phase and do not qualify dense fallback appearance. Texture repetition,
sparse strategic silhouettes and bright saturated mesh fallback remain
content/readability limitations. Sharpening, bloom and temporal resources
are not justified by these observations. Neutral exposure/grade, restrained
fog and final-resolution overlays remain the supported baseline.

## Software adapter regression

Hosted window qualification exposed Microsoft Basic Render Driver selected
through normal adapter enumeration with its software flag unset. Its
Microsoft vendor/device identity 0x1414/0x8c is specified in the
[DXGI overview](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/d3d10-graphics-programming-guide-dxgi).
Classification now excludes that identity from hardware selection as well
as flagged software adapters and uses the existing allowed WARP fallback.
Integrated/virtual hardware is not classified from zero dedicated memory
or a display name. This restores the existing software-Low quality policy
without changing quality limits or the stale-rate guard. Hardware capture
selection and the rendering shaders are unaffected. Six identity regressions
cover unset/remote flags and unrelated Microsoft, Intel and NVIDIA devices.

The complete local solution run at 6a372a0 passed 1,704 tests with nine
opt-in cases skipped (1,713 total). The later adapter-classification change
requires its focused/native graphics and quality regressions plus the
current hosted required build-test; PR #175 records their final outcomes.
Adapter revision ed18513 builds with zero warnings/errors. Direct executable
runs pass 67 graphics tests (two opt-in skips) and 722 presentation tests
(seven skips). A client run with writable temporary files outside the repo
passes 239/240; one startup timing assertion fails. The isolated startup
class recheck passes all 20 cases, so this intermittent assertion remains
visible rather than reporting a clean complete client run. Earlier attempts
hit sandbox named-pipe access or temporary-path/repository discovery errors.
The full solution result above predates only the adapter correction.
Publishing this correction is pending explicit user authorization following
an automatic approval-review rejection; hosted checks have not tested it.
