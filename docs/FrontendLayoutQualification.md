# Frontend layout qualification

## Revision and package

Product 0.1.364, code revision `424d5a4879ddeeab0d638f0d53971ab8ae2f4690`, based on
master `0ce9d86830cab442acf9441c519b3939a2f67b0c` / 0.1.363. The following evidence commit
only adds documentation and captures. The runtime was published from a clean source tree;
two independent publish trees compared equal. The existing 492-asset catalog and font
redistribution notices passed verification. Archive SHA-256:

`a29aff89369c089445c296608aca524e73bcd4de709a8387dbed1c660cae23aa`

## Display and native results

The geometry tests cover 1280×720, 1600×900, 1920×1080, 1920×1200, 2560×1440,
3440×1440 and 3840×2160, each with 96/144/192 DPI and 0.75/1/2 UI scale. Both ordinary
and wrapped-controls layouts are checked: 126 layout combinations, including inside and
immediately outside rows/edits/actions, gaps, bounds and disabled/empty states.

The packaged D3D12 capture command qualified seven surfaces at all 63 display combinations:
main menu, new game, long save list, settings with a real binding conflict, final controls page,
loading and pause. All **441** captures repeated pixel-identically, consumed the published
geometry value, and reported zero missing/dropped glyphs and zero submission faults.
The capture also suspended a zero-sized GPU surface and restored it successfully.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, D3D12 feature level 12.1.
Requested matrix DPI is a deterministic layout input; it is not physical certification on
three monitors. Actual windowed 1280×720 and borderless 5120×2160 capture windows reported
144 DPI. Debug layer was not enabled for this capture. Separate automated native WARP
layout/resize tests pass and run in CI.

Packaged playable smoke passed in windowed 1600×900 and borderless 5120×2160 with 1,000
stress instances, rendered runtime meshes and terrain, settled/non-suspended surfaces and
zero material/texture binding failures. Packaged normal studio-to-menu startup also reached
Ready with zero dropped events. Peak resident texture payload was 54,106,824 bytes.
See [package summary](evidence/frontend-layout/package.json) and
[full pixel/layout report](evidence/frontend-layout/qualification.json). The checked-in images
are representative evidence, not newly accepted visual golden hashes.

| Example | Packaged capture |
| --- | --- |
| Minimum-resolution menu | [720p menu](evidence/frontend-layout/menu-1280x720-96dpi-1.00.png) |
| Disabled Apply after actual binding conflict | [720p / 192 DPI / scale 2 settings](evidence/frontend-layout/settings-1280x720-192dpi-2.00.png) |
| Scrolled controls, Back remains visible | [720p / 192 DPI / scale 2 controls](evidence/frontend-layout/controls-1280x720-192dpi-2.00.png) |
| Centered ultrawide safe area | [3440×1440 menu](evidence/frontend-layout/menu-3440x1440-96dpi-1.00.png) |
| High-resolution controls | [4K controls](evidence/frontend-layout/controls-3840x2160-96dpi-1.00.png) |

## Test results and limits

- Restore, solution Release build (zero warnings/errors), project-reference validation,
  bundled-font integrity, version grammar/allocation and exactly-one-PATCH validation pass.
- Initial focused interaction tests: 24 passed. Input-lifetime/enablement/focus/hover tests:
  4 passed. Renderer tests: 11 passed. Font failure/batching tests: 5 passed.
- Final full Client suite: **260 passed**, zero failures/skips.
- Full Presentation suite: 884 passed, seven existing opt-in skips, one unstable unchanged
  `DirectorateBuildingPresentationTests` allocation assertion under default tiered compilation
  (3,032 bytes). Its complete 26-test class passes with `DOTNET_TieredCompilation=0`.
  The default-mode full suite passes the shadow and new native layout tests.
- Full solution rerun with fixed compilation: 1,943 total, **1,932 passed**, nine existing
  opt-in skips, two failures in unchanged native/video tests under concurrent load:
  `StudioVideoDecoderTests.PackagedMasterDecodesAndCancelsWithoutWaitingForTheConsumer`
  and one shadow restore capture requiring a settled surface. The subsequent default-mode
  full Client suite passes the video test, and the default full Presentation suite passes
  the shadow test. No single completely green full-solution rerun is claimed.
- The earlier full-solution run exposed an old font-failure test expecting its prior fixed
  footer position. It now checks the named Feedback region and passes. No image hash was
  blindly updated. No production shadow/video/building code was changed to hide these results.

## CI and review

At the qualified code revision, the new adaptive frontend geometry/interaction/native WARP
step passes, as do build, typography, material/HDR, display, asset, environment, VFX and
native apphost reference checks. Frame hot paths and Scalability qualification pass.

The main CI remains in progress at Windows window-mode smoke. The inspected prior baseline
[PR #194 run](https://github.com/DaveBeusing/Forgeline/actions/runs/38562929066/job/115848283242)
is still in progress at the identical step; the
[current code run](https://github.com/DaveBeusing/Forgeline/actions/runs/38597509821/job/115967913612)
reaches that step too. Local packaged mode/capture/startup checks are recorded independently;
this is not reported as a successful whole CI run. Final evidence-head checks must be read
from the PR. No review threads or submitted reviews were present at inspection.
