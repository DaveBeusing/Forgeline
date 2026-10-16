# Gameplay UI usability qualification

Historical evidence for 0.1.370. Its permanent FPS/TPS display contract is
superseded by [responsive HUD qualification](ResponsiveHudQualification.md).

Source implementation: `e80e9feecffd1e59fc501b40a9c88881c50e06f1`, product 0.1.370.
Base: master `947d27d834913948f02dfb76381a869c95609878`.
[Source-bound native and smoke measurements](evidence/gameplay-ui-usability/native.json).

## Reported behavior

- Shift+F1 toggles detailed stats. Generic native Shift/Control key messages preserve left/right identity. Brief Shift+F1 taps retain their modifier until the frame consumes them.
- FPS and measured simulation TPS remain visible independently of detailed stats.
- F1/unmodified F12 no longer open a gameplay modal. Controls is available from Pause.
- Shared HUD telemetry, numeric and tooltip text uses a 14 px base at unit scale. Onboarding uses 18 px headings, 16 px body, wrapping and 23 px line spacing, without the old fixed 1.25 scale cap. Available viewport height remains a fitting constraint.
- Only the system mouse cursor remains. Drag-selection, world targeting feedback and minimap markers retain their meaning.
- Both settings defaults and the actual bootstrap start new matches at the 120 m close tactical bookmark, rather than 420 m.
- Enabled action cards activate immediately. Building cards enter placement on click; a world click selects the location. Enter/Place is an alternative, not another required confirmation. Disabled/pending cards and pagination do not submit commands.
- Pause includes Settings. UI scale, guide, UI sound and camera-control preferences apply to the active match on save. Graphics and key bindings are saved for the next match. Back returns to Pause; Resume retains the active match.
- Display trials use the existing 15-second rollback. Keep requires a newer matching presented surface. Escape, timeout, loss of focus and disposal restore the prior live display.

## Automated checks

- Exact pushed revision: Release solution build, zero warnings/errors.
- Full solution regression run before the final cursor-only test: 1,983 passed, nine existing opt-in skips, one failure in unchanged `RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame(electronics_plant)`: 1,712 bytes observed versus zero. The assertion was not changed.
- The subsequent unchanged standalone Presentation suite passed: 903 tests, seven existing opt-in skips. This includes the cursor regression and the complete guidance viewport/DPI/scale cases. The full-run failure remains disclosed; this is not a claim that the full parallel run passed.
- Exact pushed revision: all 281 Client, 22 Input and 41 Platform tests passed. Tests cover modifier-side mapping, brief taps, immediate card activation and release, disabled activation, paused settings save/discard, display timeout/focus rollback and fresh-presentation confirmation.
- Architecture validation: 42 projects, no cycles or boundary violations. One-PATCH validation and whitespace checks pass. No screenshot/geometry golden or allocation assertion was relaxed.
- One intermediate build overlapped the asset tests and encountered their file lock. After tests exited, the exact pushed revision rebuilt cleanly.

## Native evidence

72 cases: Onboarding, Pause, Menu and DockHover × 1280×720/1920×1080/3840×2160 × UI scale .75/1/2 × raster DPI 96/192.
Both complete requests pass the existing reference comparator's structural gate. Every case has two identical native readbacks, native dimensions, complete atlas text, zero missing/dropped glyphs and no retained failure marker. Three warmups and eight measured frames per case are short functional samples, not sustained pacing qualification.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU; Windows build 26300; .NET 10.0.12. Native host HWND DPI was 240, recorded separately from requested raster DPI. Requested DPI captures do not certify physical monitor transitions.
The evidence JSON binds every PNG to source/build, assemblies, runtime fingerprint and original image/pixel hashes; the raw immutable manifests and PNGs remain in local `artifacts/ui-usability-native-dpi96` and `artifacts/ui-usability-native-dpi192`.
The 720p onboarding and pause images were inspected in native output. Human visual acceptance and comparison against an approved golden remain pending.

The isolated real gameplay smoke (no synthetic stress) reached the close tactical start at 120 m, rendered the HUD, presented all 236 submitted frames at 1600×900/240 DPI, and reported zero material/texture binding failures. Brief observed FPS does not establish sustained match performance. Optional D3D12 debug layer was unavailable.

Current PR CI must still complete. Physical multi-monitor interaction and a human live playthrough remain manual acceptance work.
