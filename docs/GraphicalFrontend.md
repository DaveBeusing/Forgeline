# Graphical frontend

## Scope and ownership

Based on master `fd1a4c0d5869136a43ff5ae7306c9254a60881cf` (0.1.375),
including the integrated compiled skin renderer. Version 0.1.376-dev migrates all
normal frontend surfaces: main/pause menu, new/load match, empty/broken saves,
loading/error, all four Settings sections, Controls, Credits and detail actions.
Studio playback retains its existing independent video/embedded fallback path.
The battlefield HUD remains outside this migration.

`FrontendOverlayRenderer` prepares the existing core atlas group once and uses
`FrontendSkinnedRenderer.cs` for production composition. Runtime assets and their
IDs/pixels are unchanged. Frontend preparation runs after the initial interactive
bootstrap presentation; session preparation also prepares the pause renderer.
The canonical product wordmark/tagline, studio identity and optional compiled
full-viewport menu image retain their owners. No reference board becomes a widget.

## Surface and state coverage

| Live surface | Compiled roles |
| --- | --- |
| Full-output canvas, navigation shell, details, pause | panel.primary / panel.dialog / panel.compact |
| Main and pause menu cards, actions | control.button; independent hover/press/focus/disabled layers |
| Settings tabs | control.tab; selected plus hover/press layers |
| Save/settings/credits lists, Controls table | control.row / control.row_compact; focus/error; live columns |
| Choices and bindings | control.dropdown / control.input; original live value strings |
| Toggle values | control.toggle_track / control.toggle_thumb; typed effective boolean plus live ON/OFF or profile text |
| Slider values | control.slider_track / control.slider_thumb; normalized live position and full separate value text |
| Adjustments | control.button_compact; disabled directions remain visibly disabled and nonactionable |
| Scroll extent | control.scrollbar_track / control.scrollbar_thumb; published list offset/count |
| Loading progress/errors and feedback | panel.inset / panel.banner, progress track/fill, error/warning overlays |

Overlay combinations preserve focus together with hover/press and selection;
disabled controls suppress active layers. Unavailable Load or Apply actions retain
empty enabled command strings while exposing a separate disabled visual label.
Error reasons remain live controller text. No asset contains functional labels,
save values, readiness or settings truth.

`FrontendDetailLineView.ValueKind`, `IsOn` and `NormalizedValue` are passive copied
presentation data. The Client adapter maps every actual settings enum field to
its semantic visual type. Rendering never parses English ON/OFF strings or owns
settings decisions. Profile-derived values retain their original textual qualifier.
Slider/toggle visuals use the existing keyboard and +/- adjustments; they add no
drag/click targets or input binding. Choice/binding shells retain the same editing
workflow rather than inventing a second dropdown or rebinding controller.

## Geometry, input and live state

Drawing and input continue consuming the same published `FrontendGeometry`.
`WithMenuPointer` copies hover/press flags for menu cards and tabs without moving
focus or activating a command. Stepper direction flags distinguish the real +/-
regions and cannot enable a disabled direction. Tab/Shift+Tab, keyboard focus,
wheel, Enter/Escape and half-open edge behavior remain in UI/Client.

Published `TotalDetailRows` and `DetailRowOffset` expose the existing settings/save
window. The renderer draws its scrollbar and count from these copied facts;
there is no second scroll owner. Controls uses its existing full-list scroll.
Rows clip to Details; text uses the existing measured widths and bounded ellipsis.
Slider tracks sit below the live value, avoiding unnecessary loss of profile text.
Action/footer regions stay outside the list. Decorations remain visual-only.

Unsaved settings, audio enable/volume, binding conflicts, Apply/Cancel and the
15-second display trial remain in their existing models/controllers. The renderer
does not start a timer. Loading progress/status is copied from the existing
loading state, including a typed error flag. Asset, application and match readiness
remain distinct; drawing a complete frame does not declare a session ready.

`PauseContext` identifies settings/controls opened over gameplay. An opaque backing
under the skin blocks underlying HUD text inside the menu and footer while leaving
the battlefield visible beside the menu. Input suppression and modal routing are
unchanged. Opaque backing and canonical wordmark share one colored upload written
before binding, then ordered around the skin; the native write guard is preserved.
All skin commands use the existing device, one bounded flush and existing text.
No per-frame texture read, decode or graphics-resource creation is introduced.

## Controlled recovery and qualification

`PrepareSkin` validates the complete required frontend role set during preparation.
Missing/corrupt required content produces a logged actionable failure and the
visible `UI SKIN ERROR - REINSTALL FORGELINE` message. The existing independent
geometry/text path remains usable for bootstrap, explicit unsupported test backends
and recovery. `SkinState` distinguishes Bootstrap, Ready and Failed; `SkinReady`
also requires the normal font. Native production capture rejects degraded output.
Optional menu artwork retains its own Ready/loading/fallback diagnostics.

`SkinDiagnostics` reports actual pages, textures, shared leases, draws, vertices,
uploads and drops; `LastSkinnedControlCount` counts drawn controls. Core page
residency remains 4 MiB and one texture descriptor per shared device group, plus
the existing 8 MiB font. The frontend does not prepare icons/portraits. A default
skin batch reserves 3 MiB of bounded frame upload buffers; existing colored/text
buffers remain separately owned. Actual capture measurements and limits appear
in [qualification](GraphicalFrontendQualification.md).

Native fixtures add menu hover/press, General/Bindings, an actual binding-validation
conflict, display trial and pause settings to the existing reference lane. The
display-trial fixture freezes a copied label for inspection; real rollback tests
continue exercising the controller's timer. All native source/images are retained
without alteration; no automated result grants human art/DPI/accessibility approval.
