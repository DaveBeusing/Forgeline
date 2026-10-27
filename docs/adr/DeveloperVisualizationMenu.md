# Developer visualization menu and function-key retirement

## Decision

Reuse the existing capability-gated developer menu and its Visualization section.
Unmodified F1 opens it directly only for an active compiled Developer session.
Render existing frontend Toggle controls with checkmarks; pointer, Enter and
Space activate one checkbox once. DebugOverlayController remains the category
owner. Client-local metrics and NavigationDiagnosticsController expose bounded
switch/action adapters, not another authoritative state or simulation dispatcher.

Move the former developer Shift+F functions to those controls. Report export
becomes a deliberate menu button, preserving latest-publication/full-identity
validation. Shift+F12 exclusively controls the optional guide. F10/F11 always
retain normal strategic-layer/minimap behavior. Unmodified F2 remains the debug
master shortcut. Legacy F1/F12 help constants and visible associations are removed.

## Retired keys and settings

F3-F9 have no gameplay or developer bindings and cannot be assigned. Keep the
stable GameplayAction identities for compatibility; Formation, direct building
placement and rotation start unbound. Build and template menus and Q/E placement
remain connected. Other permitted custom bindings remain supported.

Normalize old saved F3-F9 gameplay overrides away before validation. Retired
camera slots receive unused ordinary default keys, avoiding collisions with
preserved camera slots. Save the normalized settings without resetting unrelated
preferences or changing their schema. Multiple unbound actions do not conflict
and never enter the cached key-substitution dictionary.

## Ownership and limits

F1 entry acquires the existing host menu-pause ownership, suppresses the opening
gesture and blocks world controllers. Escape/F1 closes to Pause. Focus/layout,
held-input, activation and session transitions keep their normal boundaries.
Deactivation disables debug, navigation diagnostics and metrics. Options are
session-local and do not change Save/Replay schemas or registered gameplay data.

At most sixteen injected visualization switches and four commands are accepted.
The normal bounded frontend rows and focus/scroll geometry render them. Checkbox
reads observe their existing local owners. Only enabled simulation debug
categories request copied capture; paused menus cannot manufacture a newer tick.

## Validation boundary

Compile both Public and Developer product paths. Keep native visual/input,
checkbox edges, activation loss, pause/resume, retired saved keys, selection/
session changes, navigation report freshness and guide behavior open for manual
review. Product compilation does not imply behavioral or native qualification.
