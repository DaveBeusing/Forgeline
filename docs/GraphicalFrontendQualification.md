# Graphical frontend qualification

## Source and review status

Base master: `fd1a4c0d5869136a43ff5ae7306c9254a60881cf` (0.1.375).
Branch `ui/graphical-frontend`, [draft PR #210](https://github.com/DaveBeusing/Forgeline/pull/210), one PATCH to 0.1.376-dev.

Production source captured and packaged: `ac0c9c50f98fab897d5b6b4b610a23612871ccd3`.
Test measurement correction: `6257abea3ec3b7260d6efd0b5688b1090f0b6d3d`.
The latter changes only native tests to consume actual capture dimensions and adds
an explicit 1028-pixel alpha case; production code and assets are unchanged after
the native/package source. Subsequent evidence/documentation commits do not change
the player. See [scope and ownership](GraphicalFrontend.md).

Automated structural and regression results do not grant art approval, physical
monitor/accessibility approval, live-motion/listening approval or an isolated
performance budget. Complete regression and required CI acceptance remain blocked
by the failures described below. Human visual acceptance remains pending; keep
the PR draft. No merge, tag, release or accepted golden is implied.

## Coverage

All normal frontend surfaces use the shared production skin: main/pause menu,
new/load match, empty/broken saves, loading/error, General/Graphics/Audio/Bindings,
Controls, Credits, disabled actions, binding conflicts, display trial and feedback.
Live labels/values, settings persistence, unsaved tabs, existing +/-/keyboard input,
rebinding validation and the controller-owned 15-second rollback remain intact.
The Controls screen retains its two-column table. Credits retains only studio,
lead developer Dave Beusing, Forgeline version and engine, plus navigation chrome.
Paused settings use an opaque menu/footer backing with battlefield context beside it.

Independent exceptions: studio playback/embedded fallback; the initial bootstrap
frame before asset readiness; explicit required-skin failure/recovery; unsupported
fake graphics test backends. Optional background artwork retains its own diagnostic
fallback while the required skin remains validated. The battlefield HUD is outside
this migration. No unavailable role or concept board is substituted into a widget.

## Verification

| Check | Result |
| --- | --- |
| Release solution build at test source | 0 warnings, 0 errors |
| Project reference boundaries | 43 projects passed |
| Targeted Client suite | 298 passed, 0 failed/skipped |
| Native frontend production skin suite | 4 passed; repeated sessions/resource release, pause backing, reveal and missing-skin recovery |
| Native atlas/alpha suite after measurement correction | 7 passed, including 1280 and 1028 requested widths; original RGBA thresholds retained |
| Isolated full solution at test source | 2,048 total: 2,038 passed, 1 failed, 9 skipped; 8m25.607s. Complete run is FAILED; see below |
| Canonical runtime qualification | 1,337 assets, compiler 1.10.0; unchanged content |
| Final hardware raster matrix | 1,080 structural passes; two equal captures each |
| Reproducible source-less package | Two publish trees identical; qualified runtime/font/audio/skin payloads |
| Independent ZIP verification | All 1,403 manifest files and 1,337 runtime payloads hash/size checked; authoring/source files excluded |
| Packaged native rendering | 24/24 cases exactly match development pixels |
| Packaged fresh-settings smoke | Exit 0; Application Ready, Session Ready, 0 dropped events |

Runtime content signature:
`b6cbe90cbbd4dfc780abb28f5d42f7bc3b469ae0cf4e663f594975a2bfd219d9`.
Native catalog fingerprint (a separate identity):
`7E9E2DF8E7569BD0F8992ED30488040A2CBC4ED7DE071F54A7853D2EC9AD4E3C`.

Local ZIP: `artifacts/graphical-frontend-package/FORGELINE-0.1.376-dev-win-x64.zip`,
39,971,508 bytes, SHA256
`41bf99e7d9e49f42a3848d9a38856f8a1a1139856d91bbda2ef6a540f9337dfa`.
Its provenance records the clean production source above. No source PNG, SVG,
reference board or authoring tool is required by the packaged player.

## Native matrix and retained evidence

| Actual backbuffer | DPI inputs | UI scale inputs | Cases |
| --- | --- | --- | --- |
| 1280x720 | 96 / 144 / 192 | .75 / 1 / 2 | 216 |
| 1920x1080 | 96 / 144 / 192 | .75 / 1 / 2 | 216 |
| 2560x1440 | 96 / 144 / 192 | .75 / 1 / 2 | 216 |
| 3440x1440 | 96 / 144 / 192 | .75 / 1 / 2 | 216 |
| 3840x2160 | 96 / 144 / 192 | .75 / 1 / 2 | 216 |

Each combination captures 24 states: Menu, MenuFocus, MenuDisabled, MenuReveal,
MenuHover, MenuPressed, Loading, LoadingError, NewGame, LoadGame, LoadEmpty,
LoadBroken, SettingsLong, ControlsLong, Pause, Credits, SettingsAudio,
SettingsGraphics, Controls, SettingsGeneral, SettingsBindings, BindingConflict,
DisplayTrial and PauseSettings. Capture uses Medium/NormalGameplay, three warmup
frames, eight sample frames and two readbacks. All source/build/runtime identities,
PNG hashes, actual output dimensions, repeated pixels, required skin/font readiness,
missing assets/glyphs, dropped geometry and submission/texture-binding faults were
independently checked. Menu/focus/disabled/reveal/hover/press distinction gates remain.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, hardware D3D12
feature level 12.1. The host window is 1280x720 at reported DPI144; requested raster
DPI is recorded separately. Rendering a native 4K backbuffer does not qualify a
physical 4K monitor. Timing samples were collected alongside other workloads and
are diagnostic only; no sustained frame-rate or isolated UI-cost claim is made.

[Evidence ledger](qualification/graphical-frontend/evidence.json) binds original
source locations and byte hashes. The original 15 complete native manifests, the
base manifest and packaged manifest are preserved without newline normalization
inside [native-manifests.zip](qualification/graphical-frontend/native-manifests.zip).
Individual entry hashes are in the ledger. Retained original PNGs include all 24
1080p/DPI96/scale1 states, 15 high-DPI/high-scale representatives and all 13 base
images. The complete 1,080 original images remain in the immutable local artifact
roots recorded by the ledger. The ZIP and staged Git blobs are verified against
that ledger. No resized/recolored image is acceptance evidence.

Examples:
[main menu](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiMenu-Scale1.00.png),
[graphics](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiSettingsGraphics-Scale1.00.png),
[audio](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiSettingsAudio-Scale1.00.png),
[Controls](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiControls-Scale1.00.png),
[pause settings](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiPauseSettings-Scale1.00.png),
[Credits](qualification/graphical-frontend/BattlefieldCombat-1920x1080-Medium-NormalGameplay-Dpi96-UiCredits-Scale1.00.png).

## Resources and recovery

All 1,080 cases report one 4-MiB core texture, one preparation upload, zero missing
skin assets/dropped quads, and zero missing/dropped text glyphs. Skin geometry ranges
from 216 to 2,322 vertices, 6,912 to 74,304 uploaded bytes and 1-7 ordered draw calls
per captured frame. At most 27 controls are skinned in these fixtures. The existing
font uses 8 MiB; the bounded skin batch reserves 3 MiB of frame upload buffers.
These figures exclude separately owned world/background/colored/text resources.
The frontend does not prepare icon/portrait atlas groups or create textures each
frame. Native tests repeat all kinds across three sessions and return textures and
shader-resource descriptors to zero after disposal.

Required-skin failure remains explicit and actionable, with usable independent
emergency rendering. Native production capture rejects a degraded skin rather
than counting it as a pass. Optional art diagnostics and application/session
readiness remain separate from skin readiness.

## Earlier attempts and corrections

The valid base root `artifacts/graphical-frontend-before-all/` has 13 native states
from current-master base. An earlier shell-array attempt has only one combined
scenario and is not counted. Initial candidate root
`artifacts/graphical-frontend-candidate/` has 16 states; image inspection prompted
full slider value text and authoritative settings list extents. Those captures
remain immutable and are not final acceptance.

The first final attempt at source `18d60a95` failed MenuReveal/Menu pixel distinction
at scale1. A one-pixel separator was invisible. `ac0c9c50` uses the compiled progress
fill as a four-pixel reveal indicator and adds a native regression test. The failed
attempt and its original [failure report](qualification/graphical-frontend/reveal-failure.json)
are retained; the corrected complete matrix uses new roots. No distinction
assertion or approved image was changed.

[Before/after comparison](qualification/graphical-frontend/before-after-comparison.json)
contains 13 matched, changed-source comparisons and 59 additional keys. It records
pixel differences and pending human review; it is not a same-source golden gate.

The first complete solution attempt ran while hardware capture and multiple test
modules were competing: 2,029 reported, 2,019 passed, 9 skipped, one Client session
handoff timeout; the Graphics module also terminated with a fence timeout before
reporting all cases. Its failed log is retained. The complete rerun isolates test
modules with `--max-parallel-test-modules 1`; no assertion, timeout, native write
guard, shutdown/fence gate or terminal-match gate is relaxed.

The first isolated full rerun reported 2,048 cases: 2,038 passed, 9 skipped,
and one existing unit-presentation allocation assertion observed 312 instead of
zero bytes for self_propelled_artillery. That unchanged class then passed all 12
cases in a separate investigation. Its original zero-byte assertion is retained;
the second complete isolated run is recorded separately rather than replacing
the failed evidence. That second run also failed one existing zero-allocation
assertion, this time DirectorateBuildingPresentationTests / command_core:
1,136 instead of zero bytes, while the earlier unit assertion passed. The building
class then passed all 26 cases separately. Both catalogs and both test classes are
unchanged from base. These observations do not prove a cause, do not constitute a
complete successful regression run, and remain an explicit acceptance blocker.

CI run 38794917406 at `ac0c9c50` failed the existing native alpha test, which assumed
1280-pixel row stride while its CI surface was actually 1028 pixels wide. The
6257abea test-only correction reads actual capture width for pixel indexing and
actual dimensions for frontend layout; original blend thresholds remain, and the
explicit 1028-width case passes locally. CI [38798845220](https://github.com/DaveBeusing/Forgeline/actions/runs/38798845220)
at 6257abea passed atlas/alpha, package integrity, graphical frontend/pointer,
cinematic lifecycle, UI geometry, software/native WARP UI and apphost completion.
It then failed the existing window-mode qualification catalog-budget gate:
99,608,050 bytes versus 58,720,256 bytes (56 MiB). Asset sources, compiler and
`Invoke-WindowModeQualification.ps1` are unchanged relative to base; the base and
current captures have the same runtime fingerprint. This is a baseline catalog/gate
mismatch, not a successful CI run. The 56-MiB limit and counted assets remain
unchanged. See [checkpoint](qualification/graphical-frontend/ci-checkpoint.json).
Final-head required CI remains a blocker; keep draft until it is resolved.

## Reproduction

Use the SDK/Microsoft.Testing.Platform in `global.json`:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --project tests/ForgeLine.Client.Tests -c Release --no-build
dotnet test --project tests/ForgeLine.Presentation.Tests -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.FrontendProductionSkinTests
dotnet test --project tests/ForgeLine.Presentation.Tests -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.UiSkinBatchTests
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/graphical-frontend-package-new
```

Run graphics qualification without a competing native test suite. Source must be
committed/rebuilt and every output root new. Pass arrays directly to the script:

```powershell
$states = @('Menu','MenuFocus','MenuDisabled','MenuReveal','MenuHover','MenuPressed','Loading','LoadingError','NewGame','LoadGame','LoadEmpty','LoadBroken','SettingsLong','ControlsLong','Pause','Credits','SettingsAudio','SettingsGraphics','Controls','SettingsGeneral','SettingsBindings','BindingConflict','DisplayTrial','PauseSettings')
foreach ($display in @('1280x720','1920x1080','2560x1440','3440x1440','3840x2160')) {
    foreach ($dpi in @(96,144,192)) {
        & ./build/Invoke-RtsReferenceQualification.ps1 -Capture -ClientPath src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64/ForgeLine.Client.exe -RuntimeAssetRoot assets/runtime -Scenes BattlefieldCombat -Profiles Medium -Zooms NormalGameplay -Displays @($display) -RasterDpi $dpi -UiScales @(.75,1,2) -UiScenarios $states -CinematicFrontend -Warmups 3 -Samples 8 -RepeatCaptures 2 -OutputRoot "artifacts/graphical-frontend-new-$display-dpi$dpi"
        if ($LASTEXITCODE -ne 0) { throw 'Native qualification failed; retain the root.' }
    }
}
```

For packaged evidence use its apphost/runtime directory and a new output root,
then compare common keys and raw pixel hashes with the development manifest.
Keep `-GateImages` separate: it requires an explicit, complete human review of the
exact baseline manifest and cannot turn this report into automatic art approval.
