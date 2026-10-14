# Cinematic frontend qualification

The qualified implementation is revision `5e7d4f7f625ca7ea330dfd0ea86c6e23b59b5295`,
product 0.1.365 (development package). This evidence-only follow-up does not change
the qualified executable or assets. Policy and reproduction commands are in
[CinematicFrontend.md](CinematicFrontend.md).

## Visual review

The previous schematic had a small title and a large empty decorative region.
The new image uses the existing Directorate industrial buildings and three Main
Battle Tanks, with warm industrial lighting against cool graphite surroundings.
The larger title and visual-only artwork region retain the established navigation,
focus, action and footer geometry. This is authored title imagery made with current
production meshes; it does not supply new high-detail or film-quality models.

- [Before, 1080p](evidence/cinematic-menu/before-menu-1080p.jpg)
- [Main menu, 1080p](evidence/cinematic-menu/menu-1080p.jpg)
- [Main menu, ultrawide](evidence/cinematic-menu/menu-ultrawide.jpg)
- [Settings conflict, 720p / 192 layout DPI / 2x scale](evidence/cinematic-menu/settings-720p-high-dpi.jpg)
- [Controls, 720p / 192 layout DPI / 2x scale](evidence/cinematic-menu/controls-720p-high-dpi.jpg)

Previews are full-resolution JPEG re-encodings of native captures, without composition
edits. Original repeated RGBA hashes in the report describe the original native pixels,
not JPEG bytes. Originals can be reproduced with the capture command. Pause uses the
live battlefield and dark semantic panels, including feedback/footer surfaces tested
against a deliberately bright background. Settings shows a real binding conflict and
does not enable Apply while invalid.

## Packaged native coverage

[Native qualification report](evidence/cinematic-menu/qualification.json):
441 cases (seven screens, seven output sizes, three layout DPI values and three UI
scales), including menu, new game, load game, settings, controls, loading and pause.
Repeated output matched; missing/dropped glyph counts and submission faults were zero.
The image uploaded once and retained 3,145,872 GPU bytes. The qualification tool explicitly
waited 70.0043 ms for optional artwork after menu readiness; this is not a readiness delay.

Native window checks exercised windowed 1280x720 and borderless 5120x2160, plus zero-size
surface restoration. Actual window DPI was 144. Matrix values 96/144/192 exercise
layout inputs and do not certify three physical monitor configurations. The device
was an NVIDIA RTX PRO 5000 Blackwell laptop GPU with the debug layer disabled.

The same-device 1080p complete clear/menu/image/text frame comparison used 16 warmup
and 64 measured frames per path. Fallback GPU p50 was 0.594624 ms (62 available GPU
timestamps), cinematic p50 0.544448 ms (44 timestamps). CPU submit p50 was 9,622.7 us
and 9,531.7 us respectively, including waits/presentation. Vertex counts were 1,356
and 1,350. These are complete frontend workloads without a world, not isolated image
shader timings or evidence of a statistically significant performance improvement.

## Startup and playable package

[Package and startup evidence](evidence/cinematic-menu/package.json) records a clean,
reproducible two-publish package comparison. Archive:
`FORGELINE-0.1.365-dev-win-x64.zip`; SHA-256:
`87c7c385b784bb921bb53ed2761bdd04d4b51ba0644e1e11cf503d9dee0ad565`.
It contains 493 canonical compiled assets and the required notices.

Three ordinary studio-to-menu launches (without splash skip) reached Ready with zero
dropped events. Interactive menu timestamps were 17,081.5 / 15,747.4 / 14,856.6 ms;
the earlier 0.1.364 baseline sample was 14,582.7 ms. These sequential samples include
the studio presentation and different startup costs; they are not a controlled latency
benchmark. The optional image task is scheduled only after the first interactive menu
frame has presented. Tests enforce that ownership/order independently of these timings.

Packaged playable stress smoke passed in windowed 1600x900 and borderless 5120x2160
with zero material/texture binding failures and settled surfaces. Gameplay rendered
runtime meshes and terrain. Normal startup and gameplay did not invoke source authoring
or asset compilation.

## Automated validation

- Release solution restore/build: passed, zero build warnings/errors.
- Project graph, bundled fonts, version contracts and single PATCH increment: passed.
- Full Client suite: 261 passed, zero failed/skipped.
- Full Presentation suite: 890 passed, seven existing opt-in skips, zero failed.
- Full Assets suite: 98 passed, zero failed/skipped.
- Final focused presentation checks after the pause contrast adjustment: 16 passed.
- Native lifecycle/geometry checks: five passed, including repeated host ownership,
  secondary-page reuse, resize and quality selection.
- New bounded-loader/fallback/resource tests cover cancellation, corrupt/missing content,
  unsupported upload, mip selection, stable reuse and disposal.
- The packaged 441-case native qualification, three normal startups and both playable
  window modes passed on the final implementation revision.

GitHub Actions build, version/font/geometry/native typography/material/display,
canonical asset compilation and runtime process checks passed for the qualified code
revision when this report was written. The cinematic lifecycle step and subsequent
jobs were still running. Frame allocation contracts passed; longer frame and scalability
measurements remained running. Check the pull request for final current-head results;
this document does not assert completion of pending CI or a whole-solution test run.

