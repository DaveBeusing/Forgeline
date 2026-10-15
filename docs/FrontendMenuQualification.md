# Frontend menu qualification

Implementation source: `5ce4553ad484ab966dcc682523193cfdf2e8f041`, version 0.1.371; base master `fa5902bb2e2d101cd3c08c98921b3fd3e20db6e0`.
[Exact native measurements and hashes](evidence/frontend-menus/native.json).

## Changes

- Settings now has General, Graphics, Audio and Bindings tabs, reachable by pointer or Tab/Shift+Tab in the main menu and paused match. Section changes preserve draft preferences. Audio immediately exposes the existing interface-sound toggle and volume; no unsupported audio channels are implied.
- Controls is a two-column Action/Input table with one action per row, 49 rows for the default mapping, and Up/Down/wheel scrolling. Remapped camera/gameplay values remain authoritative.
- Title art covers the full native viewport with navigation drawn above it. Wide-screen content uses edge margins rather than a fixed centered 16:9 canvas. Optional image failure retains the full-screen fallback.
- An embedded repository-authored vector wordmark replaces the FORGELINE text header. It uses no runtime font, texture upload or new third-party asset. Application icon and studio splash remain separate.
- Credits contains exactly studio Undefined Behavior Studios, lead developer Dave Beusing, the built FORGELINE product version and ForgeLine Engine. Platform/library entries, tagline and decorative artwork are excluded.

## Verification

- Complete Release solution build on the pushed implementation: zero warnings/errors.
- All 286 Client tests and all 27 focused frontend renderer/geometry tests pass. New tests cover Audio tab access/persistence, keyboard section changes, complete per-section field reachability, exact Credits entries, remapped Controls rows, vector bounds, full-viewport coverage through 5120×2160 and nonoverlapping table columns.
- Broader Presentation run on the pushed revision: 912 passed, seven existing opt-in skips, one failure in unchanged `RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame(smelter)` (1,712 bytes observed versus zero). The same class then passed all 26 tests in isolation. An earlier broader run observed supply_depot at 1,984 bytes. These failures remain disclosed; the complete broader run is not claimed green. No allocation assertion or visual baseline was relaxed.
- Architecture: 42 projects, no cycles/boundary violations. One-PATCH version policy and whitespace checks pass.
- 80 native cases: Menu/Controls/Credits/SettingsAudio/SettingsGraphics × 1280×720, 1920×1080, 3440×1440, 3840×2160 × scale 1/2 × raster DPI 96/192. Both complete source-bound requests pass structural/repeatability checks. All 160 readbacks match their case repeat, native text has zero missing/dropped glyphs, and no failure marker exists. The optional cinematic menu asset is Ready. Actual HWND DPI is 240, recorded separately.
- Native 720p Audio, Controls and Credits images, high-scale 192-DPI Audio/Controls and the ultrawide title composition were inspected. Pending visual-approval metadata remains pending; no approved golden is fabricated.
- Actual borderless menu startup used isolated settings, occupied the physical monitor's full 3840×2400 client/swapchain at 240 DPI, and reached ApplicationReadiness Ready with SessionReadiness NotRequested. Runtime catalog validates 493 assets; logo embedding adds no runtime-catalog entry.

Raw immutable manifests and PNGs are retained locally under `artifacts/frontend-menus-native-dpi96` and `artifacts/frontend-menus-native-dpi192`. The checked-in compact JSON binds every PNG to source/build, assemblies, runtime fingerprint and original hashes. These short functional captures do not establish sustained performance or human multi-monitor acceptance. Current-head CI and human live playthrough remain pending.
