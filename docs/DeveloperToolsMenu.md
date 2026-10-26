# Developer tools menu

## Entry and scope

The menu starts from master 0747a72205abd19505ecdeec6279ec5714acd4f6,
version 0.1.481-dev, with the foundation and gameplay tuning integrated. The PR
increments PATCH once to 0.1.482-dev. Existing client asset integration from
PR353 is already merged; no alternate client composition is copied from a branch.

In an enabled developer build, pause the match and choose DEVELOPER TOOLS.
No new keyboard chord replaces Help, metrics, gameplay bindings or the guide.
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
ClientApplication applies the same session capability to F2, Shift+F2 and
Shift+F4-F9 world diagnostics and the opt-in logistics navigation controller.
Public Controls and FixedBindings omit those entries. Public Shift+F1 metrics,
player strategic overlays, minimap and standalone qualification entries retain
their existing scope; they cannot grant developer permission. A session activation
loss closes the menu, clears drafts/results/targets and disables development views.
Player preferences never activate this permission.

## Display and requests

All five provider statuses are shown, including precise Unavailable reasons.
The menu catalog is a real local provider and preserves supplied action entries.
On this master, SessionControls, central SimulationActions and preset I/O remain
missing. GameplayTuning code is integrated but has no central dispatcher binding.
The default client therefore displays these missing services and disables their
controls. Visualization is genuinely connected to the existing DebugOverlayController;
no unavailable service reports successful simulation application.

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

Register actual already integrated domain providers with DeveloperProviderFactory.
The menu preserves these session/action/parameter/preset providers and merges their
catalog operations. A backend is an observation/preset client bridge, not another
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
service. Publish the matching bounded PresetOutcome to confirm success or failure;
submission alone remains Submitted and blocks another write. The restart callback
passed to the backend validates proposed settings, queues ClientSessionRequest.Restart
and returns through the normal session-loading loop. It creates no ad hoc runtime.
The service owns preserving the requested seed/identity and installing initial
profiles; its normal loader owns construction and failure handling. No preset
storage, parser, runtime swap, persistence format or replay adapter is implemented
inside this menu package.

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
