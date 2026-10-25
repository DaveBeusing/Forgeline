# Logistics navigation diagnostics integration handoff

## Delivery state

Partial implementation: an internal bounded report encoder and one-shot local writer are prepared. No diagnostics view, capture adapter, overlay layer, or export action is registered. Shared integration and manual acceptance are outstanding. See [LogisticsNavigationDiagnostics.md](LogisticsNavigationDiagnostics.md) for semantics, privacy boundaries, file output, controls, and acceptance constraints.

Original inspected base: `cdb60b1a926e770f25eb1ec350548bb00349ff49`, including PR #350 and PR #354. Current synchronized base: `master` at `574040b1cde354bc1414a692e2ec075968e0959a` after PR #357. Its conveyor changes are preserved and do not alter the report's source contracts. The prepared version is 0.1.474-dev, exactly one PATCH above this base's 0.1.473-dev. Required common contracts: PR #356 at `df9290ead9bd09b608a1720d992fac0163d8598a`, proposed revision 1, not present on this base. Recheck current `master`, the PR state, and the resulting contract revision before continuing.

## Implemented entry points

All new code is internal to `ForgeLine.Client`; there are no new public Game/Navigation interfaces.

```csharp
NavigationDiagnosticReportEncoder.TryEncode(
    PresentationSnapshot snapshot,
    SimulationSessionId expectedSession,
    SimulationTick expectedTick,
    PlayerId viewer,
    EntityId selectedEntity,
    string mapId,
    ulong seed,
    out byte[] report);

NavigationDiagnosticReportExporter.TryWrite(
    PresentationSnapshot snapshot,
    SimulationSessionId expectedSession,
    SimulationTick expectedTick,
    PlayerId viewer,
    EntityId selectedEntity,
    string mapId,
    ulong seed);
```

`TryEncode` returns success plus bounded UTF-8 JSON; it performs no file I/O. `TryWrite` returns `NavigationDiagnosticExportResult(Path, Failure)`, whose `Exported` property indicates a completed report. Failure values are `None`, `ObservationUnavailable`, `LocalDirectoryUnavailable`, and `WriteFailed`.

The caller must authorize every explicit export action and supply the latest copied snapshot and reconciled selection for the current session. The helpers cannot reauthorize against live simulation state. A full `EntityId`, including generation, is required. Do not retain an old snapshot to make identity checks pass after the selection or session changes.

The report is limited to 64 KiB with bounded strings. Configuration is currently limited to the supplied current map identity/seed and expected identity arguments; there is no user-controlled path or simulation configuration. Local output uses `LocalApplicationData/FORGELINE/Diagnostics`, creates a unique temporary file, flushes it, and moves without overwrite. There are no retries or background operations.

Holding/placement admission fields are optional: existing zero `queueGeneration` or `position` sentinels become null. Zero `waitingCount` and `changedAtTick` are legitimate copied values and remain zero. `SimulationTick.Zero` is allowed when all source identities agree. Tactical combat eligibility is unrelated to export authorization; the copied owned selection, current session/tick, and rendered ownership establish the supported observation boundary.

There is no retained mutable state and no Save-/Replay-schema requirement. All buffers and filesystem state are local to a single requested report. No simulation command, transform, inventory, cargo, fuel, queue, or reservation is modified.

## Required contract completion

Do not copy PR #356 types into this module. Its proposed `NavigationDiagnosticFrame` already owns the bounded, viewer-scoped copied-frame contract, and `NavigationReadAdapter` owns simulation-thread vehicle capture. Integrate and extend the common definition deliberately before adding consumers.

| Required addition | Why revision 1 is insufficient |
| --- | --- |
| Per-observation source tick and relevant revision | The frame has one publication tick; vehicle fields do not identify geometry, traffic, queue, dock, or reservation source versions. |
| Explicit availability of optional fields | Target, progress tick, queue position, and dock ownership are required primitive values; unknown values must not become zero or `false`. |
| Route and approach phase | The diagnostic vehicle has no selected copied route/corridor or explicit approach phase. |
| Wait origin and replan reason | Wait age and the reason for a replacement route cannot be reconstructed authoritatively from the current fields. |
| Yield/reservation and propulsion-fuel observation | These separate authoritative resources are not carried by the diagnostic frame. |
| Geometry and per-layer bounds | Actual corridor, oriented hull, authorized lookahead, conflict, reservation passage, and dirty-region observations need copied, bounded sources. |
| Viewer-scoped fleet aggregates | Search work, cache, workspaces, regional updates, maximum wait, and replan counts are absent. |
| Completed-delivery observation | `NavigationDiagnosticEvent` is a navigation event, not a delivery-completion event. |
| Optional instrumentation samples | A bounded rolling time/allocation window is not part of the proposed frame. |
| Captured configured limits and metadata | The frame receives limits to validate bounds but does not expose their captured values for a report; map/session metadata needs a matching source. |

`NavigationDiagnosticFrame` validates vehicle ownership and identity shape; live identity/generation and blocker visibility remain producer responsibilities. Its `MaximumNeighbors` vehicle bound also needs an explicit interpretation for fleet observation versus per-vehicle neighbors. Every producer must filter unauthorized data before copying, including dirty regions and visible-blocker identities.

## Registration ownership and order

The central integration change owns all shared call-site edits. None of the following methods are changed here:

1. **Authorization and input:** resolve a real developer-only host before adding an action. `ClientApplication.UpdateDebugOverlayToggles` is only a visibility toggle path. Existing `F2`, `Shift+F4`, and `Shift+F6` must not be repurposed as authorization or export shortcuts.
2. **Selection requests:** use `PresentationInteractionState.SetSelection`; carry diagnostic demand through `SetDebugState`. Keep the copied selection and session aligned.
3. **Simulation capture:** extend the established `PresentationExtractor.OnTickCompleted` path after `Interaction.Capture`/policy resolution and alongside `ApplyDebugCaptureState` and `CaptureDebug`, before publishing the matching `PresentationSnapshot` to the buffer. Produce copies only while authorized diagnostics are requested.
4. **Client reconciliation:** reuse `RtsSelectionController.ReconcileSelection` against the current `RenderWorld` and the owner/category `SelectionFilter`; validate the full entity identity, source tick/revision, and current viewer before formatting or drawing.
5. **Rendering:** extend `BuildDevelopmentDebugOverlay` after access, enabled-category, and valid-snapshot checks; preserve its disabled early return. Pass bounded copied geometry through `ClientRenderFrame`. Leave queue/dock rectangles to `LogisticsZoneSnapshot.Capture` and `LogisticsZoneOverlay`.
6. **Export:** invoke `NavigationDiagnosticReportExporter.TryWrite` once after a fresh authorized user action, using the latest reconciled copied observation and matching metadata. Display the returned success/failure without retrying, running a scenario, or changing simulation state.

`Navigation.LastCompletedPath` is a global completed path and must not become the selected vehicle's displayed route. Existing debug metrics and overlay visibility are not proof of developer access. The inspected client path has no verified developer-authorization boundary, so registration is explicitly blocked until that boundary is resolved.

## Completion sequence and acceptance

Next step: integrate the common contract baseline into `master`, complete the missing diagnostic producers/fields and developer-access host, then continue this branch against the updated `master`. Reuse these helpers rather than adding an alternative export path. Implement the Game adapter and Presentation consumer only against the authoritative merged contracts.

Before final integration, reconcile `Directory.Build.props` serially to exactly one PATCH increment over current `master`. Preserve existing CI, test guards, and central Save-/Replay schemas. Do not merge automatically.

Required final Release commands are recorded in the main document. At this implementation stage the local environment lacks SDK/PowerShell and suitable Windows support, so Game, Presentation, and Client Release compilations are **not executed**. Automated tests are not created, changed, or executed; no extra CI run or rerun is initiated.

Manual acceptance is **not executed**. After integration, verify owned Cargo/Supply selection in each state, unknown observations, full-identity changes, death/reuse, ownership/visibility changes, session turnover, overlay deactivation, bounded output, and a single report export. Exercise unavailable local output and failed writes without altering the simulation. Record actual outcomes and limitations; do not label an unintegrated or merely compiled module as accepted.
