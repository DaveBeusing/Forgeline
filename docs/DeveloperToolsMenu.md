# Developer tools menu

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Entry and scope](#entry-and-scope)
- [Display and requests](#display-and-requests)
- [Targets and diagnostics](#targets-and-diagnostics)
- [Host and adapter handoff](#host-and-adapter-handoff)
- [Input lifetime and remaining qualification](#input-lifetime-and-remaining-qualification)
- [Current integration and acceptance](#current-integration-and-acceptance)
- [Historical menu-package acceptance evidence](#historical-menu-package-acceptance-evidence)
- [Visualization checkboxes and retired shortcuts](#visualization-checkboxes-and-retired-shortcuts)

</details>

## Entry and scope

The live provider integration is included in master through PR #379
(merge 89086e68cc72798544b8b75d81c7d36d6bd13811). ClientApplication creates
ClientDeveloperMenuBackend for the current runtime, host and session access.
The factory hooks below remain optional composition overrides.

From the repository root, run:

~~~powershell
pwsh ./build/Start-DevelopmentClient.ps1
~~~

The launcher builds and runs Release with
`-p:ForgeLineEnableDeveloperTools=true` on both commands. This selects the
isolated Developer output and its compiled menu/providers, including when an
older Public build exists. Runtime assets retain their normal build path.
Session activation is supplied by the existing client composition; no saved
preference or extra runtime permission flag is needed.

In an activated Developer client, start or load a match and press unmodified
**F1** to open Visualization directly. Escape → DEVELOPER TOOLS remains an
alternative entry. F1 or Escape closes the developer surface back to Pause;
Escape from Pause resumes through the existing host pause boundary.
F1/F12 no longer have legacy help assignments. Shift+F12 belongs only to the
optional match guide. F3-F9 have no default gameplay/developer assignments and
cannot be reassigned.
The existing frontend renderer, geometry, fonts, detail controls and focus visuals
render the bounded scrollable surface. Use the SECTION choice to switch Session,
Parameters, Actions, Visualization and Presets. Up/Down or wheel move the focus;
Left/Right adjust a choice/stepper; Enter activates the explicitly focused control.
Pointer adjustment arrows change the local draft. APPLY sends that row once;
RESET requests restoration of the captured initial value. Escape/BACK returns to
the ordinary pause menu before a subsequent Escape can resume gameplay.

DeveloperToolsCapability.Current and ephemeral session activation gate entry.
Public builds compile neither menu implementation nor inspector implementation;
PauseMenuModel also refuses its developer entry without compiled capability.
ClientApplication applies the same session capability to the F1 menu, its
diagnostic checkboxes and unmodified F2 world-debug master. Visualization now
owns the former developer Shift+F functions: performance metrics, Rendering,
Navigation, World, Logistics, Sensors, Combat and Entities, plus the navigation
diagnostic panel and each of its six layers. The old live Shift+F1/F2/F4-F11
handlers are disconnected; report export is an explicit menu button.
Public builds have no F1 developer entry or metrics shortcut. F10/F11 retain
their normal player overlay/minimap behavior even when navigation diagnostics
are enabled. These changes grant no simulation action capability. A session activation
loss closes the menu, clears drafts/results/targets and disables development views.
Player preferences never activate this permission.

## Display and requests

All five provider statuses are shown, including precise Unavailable reasons.
The menu catalog is a real local provider and preserves supplied action entries.
The default Developer client connects session controls to ClientSimulationHost,
simulation actions and gameplay tuning to the Game dispatcher through the bounded
host queue, and presets to ClientDeveloperPresetSessionAdapter. Visualization
uses the existing DebugOverlayController. Unavailable providers disable their
controls and expose their reason; admission never reports simulation application.

Parameter descriptors supply names, units, defaults, bounds, steps, target kinds
and apply modes. Actual observations supply confirmed values and revisions;
a metadata default is never substituted for an unavailable confirmed value.
Each local draft belongs to the current session, parameter and target, and retains
its observed parameter revision. Provider status, focus, outstanding submission
and loading state gate edits. An external revision reloads actual values while
retaining a dirty draft as UNVERIFIED. Review it and explicitly APPLY again.
Tuning contributors additionally show Basis, Initial and Pending separately.
NextMatch values are confirmed pending rather than labeled as active changes.

A submission uses exactly one existing DeveloperRequest with a new correlation.
Accepted/Pending admission does not become Applied. Only a matching authoritative
DeveloperResult for that session, correlation and revision domain completes it.
Applied shows its actual tick; rejection explains revision conflict, invalid target,
missing capability/provider or other stable failure. A synchronous Applied/Rejected
provider response is handled with the same matching checks. Queue/submission errors
never manufacture success. Pending authoritative parameter results describe waiting
for a new match; the active confirmed value remains unchanged. Completed messages
are limited to 64 entries. Detail strings are rebuilt when observed/model state or
focus/input changes, rather than formatting a fresh history every frame.

## Targets and diagnostics

Actions require an explicit available catalog action and Global, a configured
Player, or exactly one currently observed full EntityId. A typed payload form from
the actual action adapter supplies bounded fields, defaults, ranges, increments and
optional named enum choices. Boolean values show ON/OFF. EXECUTE SELECTED ACTION
submits once on a new press; held input never repeatedly spawns or grants resources.
No action payload is guessed when its adapter is missing. A changed/disappeared
entity clears the action selection. Multiple selection is explicitly described.

The inspector reads only the existing immutable presentation snapshot. It shows
full index/generation, owner/type, captured tick and available selection health,
Fuel/Ammunition and supply data. Missing facts say Unavailable. It does not read
live ECS, infer hidden enemy data, or retain a target from another session.
Visualization changes the single existing DebugOverlayController. Only its selected
simulation categories are requested through PresentationInteractionState. Rendering
alone requests no heavy simulation debug capture. Missing snapshots say Unavailable;
captured diagnostics identify their tick and older data is labeled explicitly.
Opening a paused menu cannot itself produce a newer completed simulation snapshot.

## Host and adapter handoff

ClientApplication alone owns composition and input ordering. In developer builds:

~~~csharp
DeveloperProviderFactory : Func<MatchRuntime, DeveloperToolsProviders>
DeveloperMenuBackendFactory : Func<MatchRuntime, DeveloperToolsProviders,
    Func<MatchRuntimeSettings, bool>, IDeveloperMenuBackend?>
~~~

Without overrides, ClientDeveloperMenuBackend supplies the registered
session/action/parameter/preset providers. DeveloperProviderFactory can replace
that composition; the menu preserves supplied providers and merges their catalog
operations. A backend is an observation/preset client bridge, not another
simulation dispatcher. Its Capture returns a bounded immutable
DeveloperMenuBackendSnapshot for the actual current session: domain revisions,
correlated results, captured tick, actual host speed/remaining steps, preset entries,
load state and confirmed menu-pause ownership. It never creates authority from UI
state. Current physical host pause is read separately from ClientSimulationHost.

The session owner processes Pause/Resume/Step1/10/100 and speed0.25/0.5/1/2/4
through the existing IDeveloperSessionController. For Resume/Step it must transfer
the existing menu pause reason at the actual control boundary and publish
MenuPauseTransferred only after real ownership transfer. Window/focus suspension
is delivered with SetWindowBlocked; the adapter must retain independent pause
reasons and cancel/suspend incompatible stepping. ReleaseMenuOwnership cancels
outstanding menu control intent and restores the ordinary pause owner. While the
adapter owns control, the normal client pause loop does not overwrite its state;
world commands remain blocked by the visible menu. The menu never calls
AdvanceOneTick, TrySetPaused or edits simulation state.

ActionForms supplies at most32 operation forms with at most16 fields and at most64
named choices per field. Catalog and payload agree on the operation and target;
the real dispatcher remains responsible for ownership, payload and domain validation
at execution. The selected action retains its observed Simulation revision.

Preset TrySubmitPreset receives a correlation, intent and optional selected entry.
Import/export/compatibility and file ownership remain exclusively in the preset
service. Before restarting a selected entry, the menu invokes the existing
IDeveloperPresetService.Validate again and displays that actual compatibility result. Publish the matching bounded PresetOutcome to confirm success or failure;
submission alone remains Submitted and blocks another write. The restart callback
passed to the backend validates proposed settings, queues ClientSessionRequest.Restart
and returns through the normal session-loading loop. It creates no ad hoc runtime.
The service owns preserving the requested seed/identity and installing initial
profiles; its normal loader owns construction and failure handling.
The default backend delegates storage and validation to the preset adapter
and DeveloperPresetStore. It queues settings through ClientApplication as a
ClientSessionRequest.Restart; ClientSessionFactory owns the new runtime.

## Input lifetime and remaining qualification

The menu receives input before world selection, camera, build, unit and tactical
controllers. Its visible shell blocks those controllers even during developer
simulation playback. Resize/DPI/focus/owner/session transitions suppress held input
until release. Opening, closing, switching sessions and losing capability cannot
leak a trailing pointer press into the world. The existing pause/menu cancellation
path also clears selection/build/targeting gestures when ownership changes.

Implementation used affected product compiler checks only. Scoped acceptance starts
only after implementation and documentation are complete. The PR records actual
build/test/CI source and outcomes. Combined real-provider gameplay chains, complete
Public/Developer package exclusion, Save/Replay and native/DPI qualification remain
with the combined developer-tools qualification. A rendered menu with missing
providers does not qualify their functionality.

## Current integration and acceptance

The product paths above are integrated. Historical results below describe the
earlier menu package; the later combined source, native/package observations and
remaining failures are recorded in [Developer tools qualification](DeveloperToolsQualification.md).
They do not establish complete product acceptance. Manual user approval remains
outstanding. Documentation reconciliation introduces no new runtime state or
Save/Replay format.

## Historical menu-package acceptance evidence

Implementation commits ba34443, 99276432 and 58a9222b precede the explicit scoped
acceptance boundary. No tests, smokes or benchmarks ran during implementation.
Both affected product client variants compiled with zero warnings/errors before
acceptance (Developer31.95s, Public36.66s).

Canonical solution restore and the unchanged reference validator passed (47 projects,
including the two pre-existing ignored local probes; no cycles). The complete Public
Release solution build passed with zero warnings/errors (80.76s). The selected Public
client run passed3/3 and existing DebugOverlayState cases passed11/11, with no skips.
The isolated Developer client test build passed with zero warnings/errors (65.60s)
and its16/16 selected menu/capability cases passed (13.255s). They cover correlation,
revision conflict, session/capability loss, held-input/lifecycle suppression, full
entity generation, explicit typed actions, confirmed pause ownership, preset result
matching, bounded history and rendering-only capture isolation.

A narrow acceptance review then connected the existing preset validator before
restart: the affected developer build passed (zero warnings/errors,15.22s) and2/2
preset cases passed. A final help correction applies session activation to paused
Controls as well as compiled capability; both affected client test builds passed
with zero warnings/errors, and the capability case passed1/1 in each variant. No original assertion, price, golden, deadline or gate was
relaxed. The PR identifies the exact published source and required CI result.
No full native, package, save/replay or real-provider integration campaign is claimed.

## Visualization checkboxes and retired shortcuts

Visualization uses the existing frontend Toggle control, with a visible checkmark
and a single activation by pointer, Enter or Space. Debug master gates its checked
categories. Performance metrics and the navigation panel have separate checkboxes.
Navigation layer controls are available while that panel is enabled; turning the
panel off clears its layers. Its details up/down and report export are explicit
menu actions. Export validates the latest copied publication, current full
selected-vehicle identity, own visibility and session before writing a report;
missing/stale data produces a rejection message. No key chord exports a report.

The menu acquires the existing pause/input ownership before world controllers
can consume the opening press. Opening, closing, focus, layout and session
transitions suppress held input. Deactivation closes the menu and disables the
diagnostic views and metrics. Choices belong to the current match session and
are not stored in saves or a new settings authority.

Formation and direct building/rotation actions retain stable GameplayAction IDs
but start unbound. Buildings and paid templates remain in Build; Q/E still rotates
placement. The binding editor permits other valid keys, while F3-F9 are retired.
Loading older settings removes only gameplay overrides using those keys.
Retired camera slots are replaced with available ordinary default camera keys,
preserving other camera bindings and all unrelated display/audio/game settings.
The settings layout/schema is unchanged; the normalized configuration is saved.

Product compilation and manual review are distinct. Review Public/Developer and
activation loss, checkbox pointer/Enter/Space edges, held keys, focus/resize,
pause/resume and session replacement, navigation data/export rejection, all
retired keys/modifiers, old saved bindings, Build/Q/E and the sole guide shortcut.
