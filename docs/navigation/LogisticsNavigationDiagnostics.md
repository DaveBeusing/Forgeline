# Logistics navigation diagnostics

## Status and scope

This change prepares a bounded manual report encoder and local writer. It does not register a new diagnostics panel, export action, or navigation layer. The complete selected-vehicle diagnostic view remains blocked on shared contracts, authoritative producers, and an authorized developer host.

Implementation started from `cdb60b1a926e770f25eb1ec350548bb00349ff49` and is synchronized with `master` at `574040b1cde354bc1414a692e2ec075968e0959a` after PR #357. The incoming conveyor changes do not alter the report's copied source contracts or the inspected developer-access limitations. Navigation recovery from [PR #354](https://github.com/DaveBeusing/Forgeline/pull/354) and the adjacent supply changes from [PR #350](https://github.com/DaveBeusing/Forgeline/pull/350) are already integrated. The shared contracts in [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356), inspected at `df9290ead9bd09b608a1720d992fac0163d8598a`, remain unmerged at this baseline. Their proposed contract revision is 1; neither `Navigation/Contracts` directory is present on this `master`.

No replacement shared contract is introduced. The independent implementation uses existing copied presentation data and existing client build identity only. No Game or Presentation diagnostic consumer is added. The integration inventory and remaining work are in [LogisticsNavigationDiagnosticsHandoff.md](LogisticsNavigationDiagnosticsHandoff.md).

## Data ownership and visibility

`NavigationDiagnosticReportEncoder.TryEncode` accepts a `PresentationSnapshot`, expected session and tick, viewer, full selected `EntityId`, map identity, and seed. It produces a bounded UTF-8 JSON report in memory. `NavigationDiagnosticReportExporter.TryWrite` accepts the same inputs and writes one report. Both types are internal client implementation details; they are not navigation-module interfaces.

The future caller must first authorize the explicit developer action, reconcile the current selection, and obtain the latest published copy for the current session. Pass the full entity identity, including generation. Snapshot identity checks cannot establish current authorization when a caller deliberately reuses an old snapshot and matching old arguments.

Only one currently selected own Cargo Truck or Supply Truck is eligible. The experience, action, tactical-selection, and render copies must agree on the selected identity and current tick/session. The rendered truck must be selectable, visible in the world, owned by the viewer, and not a wreck; its copied health must be finite and positive. A missing selection, wrong viewer, stale session or tick, mismatching entity generation, or missing eligible observation rejects the report. The encoder does not query `EntityRegistry`, movement systems, live navigation dictionaries, or search frontiers.

A queue observation is included only when the experience and matching action copies agree and the associated building is present as a selectable, visible, owned building in the same snapshot. Invalid queue values or an unavailable building make the queue unavailable without inventing replacement data. The copied `changedAtTick` means the last observed queue-state change; it is not a wait-start tick or a basis for calculating wait age.

Existing holding observations can use zero as an unavailable admission generation or queue position. The report encodes those `queueGeneration` and `position` fields as null. A registered but not yet placed entry may have a known generation and a null position. Real zero values for `waitingCount` and `changedAtTick` remain zero; a matching observation at simulation tick zero is valid.

The report is not a savegame. It does not enumerate other vehicles, inspect hidden blockers, serialize world state, or infer private information. The global `Navigation.LastCompletedPath` is not the selected truck's route and must not be substituted for one. General supply values must not be relabeled as authoritative propulsion-fuel restrictions.

## Report format and local output

The encoder imposes a hard 64 KiB UTF-8 JSON limit, including escaping. The buffer cannot expand; exceeding it rejects the report rather than truncating JSON. `mapId` must contain 1-128 ASCII letters, digits, periods, underscores, or hyphens. Build fields are capped at 256 characters each.

The format is:

| Field | Content |
| --- | --- |
| `reportSchemaVersion` | `1`, the client report format version. |
| `reportKind` / `coverage` | `logistics_navigation_partial_observation` / `owned_selection_and_queue_only`. |
| `build` | `informationalVersion`, `sourceRevision`, `buildId`, and `channel` from `ClientBuildIdentity`. |
| `mapId`, `seed`, `sessionId`, `sourceTick`, `viewer` | Caller-supplied matching map metadata and validated observation identity. |
| `diagnosticContractRevision`, `activeModules`, `configuredLimits` | Explicitly unavailable until the common producers are connected. |
| `selectedVehicle` | Full entity index/generation, unit identity/kind, source tick, optional copied queue, and explicitly unavailable navigation fields. |
| `selectedVehicle.queue` | When available: full building identity, optional queue generation/position, waiting count, service, dock status, wait/block reasons, source tick, and `changedAtTick` with its meaning. Zero admission-generation/position sentinels become null. Otherwise the entire queue is an unavailable object. |
| `fleet`, `geometry` | Named required counters and layers, all explicitly unavailable in this partial format. |
| `gameplayTimingWindow`, `gameplayAllocationWindow` | Explicitly unavailable; no new instrumentation is connected. |

An unavailable observation has `availability: "not_available"`, `value: null`, `sourceTick: null`, `revision: null`, and a machine-readable `reason`. Available selected/queue observations have `availability: "available"` and their copied source tick; `navigationRevision` remains null because it is not present in the existing source. Unknown values are never serialized as zero or a healthy status.

The report format is a client artifact format. It does not declare a new shared navigation contract or claim that revision 1 is connected. Active navigation-module state and configured navigation limits remain unavailable until a producer can supply their actual copied values.

Output uses the existing client diagnostics directory:

```text
%LOCALAPPDATA%\FORGELINE\Diagnostics\navigation-<unique-id>.json
```

The directory comes from `Environment.SpecialFolder.LocalApplicationData`. Map names and other report fields never form filesystem paths. A missing or non-absolute local application-data root returns `LocalDirectoryUnavailable`.

The writer creates a unique `.navigation-<unique-id>.tmp` using `FileMode.CreateNew`, writes the bounded report, flushes it, then moves it to the final JSON name without overwriting an existing file. An unsuccessful write attempts to remove only its own temporary file. Failed cleanup can leave a `.tmp` file; that file is not a completed report. No background task, retry loop, automatic export, or trace stream is installed.

`NavigationDiagnosticExportResult` returns `Path`, `Failure`, and `Exported`. Failure values are:

| Failure | Meaning |
| --- | --- |
| `None` | A final report path is available. |
| `ObservationUnavailable` | The copied selected observation or required identity is unavailable or unsuitable. No file is written. |
| `LocalDirectoryUnavailable` | The existing local diagnostics root cannot be resolved safely. |
| `WriteFailed` | Directory creation, temporary-file writing, or final publication failed. |

The future UI must display the result once for the user action. It must not retry automatically or reinterpret a failure as a successful export.

## Required status semantics

These are requirements for the later connected view, not observations supplied by the current encoder:

| State | Meaning and display constraint |
| --- | --- |
| `WaitingForBudget` | Search or planning work is waiting for its configured work allowance. This is not evidence that the world is impassable. |
| `WaitingForTraffic` | An observed traffic conflict currently restricts movement. Reveal a blocker identity only when permitted for this viewer. |
| `WaitingForDock` | The authoritative queue or exclusive dock slot delays service or approach. Preserve existing FIFO and occupancy semantics. |
| `OutOfFuel` | The authoritative propulsion-fuel state prevents movement. Do not infer it from missing data or a general supply percentage. |
| `Replanning` | A replacement route is being sought for the current command generation; retain the original target and explicit reason. |
| `Unreachable` | An authoritative completed result reports no valid route under the applicable capabilities and revision. |
| `BlockedGeometry` | A current geometry observation blocks movement or the permitted corridor. |
| Unavailable | The module is disconnected, no valid observation exists, or its identity/source revision is stale. This is neither success nor one of the preceding failure states. |

Each connected observation must carry its source tick and relevant geometry, traffic, command, queue, dock, or reservation revision. A frame publication tick alone does not prove that every field was observed at that tick. Unknown booleans and counters must remain distinguishable from `false` and zero.

The selected-vehicle view still requires original target, route and approach phase, movement status, last progress tick, permitted blocker, queue position, dock ownership, yield/reservation state, wait age, replan reason, and propulsion-fuel restriction. It must not promise an arrival or delivery time from heuristic data.

## Overlay and aggregate contracts still required

The later view should offer separately enabled layers for the actual driving corridor, oriented vehicle hull, authorized braking/lookahead area, observed conflict, reserved passage, and dirty regions. Every layer needs a copied source with viewer scope, full identity where applicable, source tick, relevant revision, and an explicit producer-defined element bound. Reject invalid or stale observations before building geometry; cap output before appending lines or labels.

Do not synthesize a hull from velocity or a corridor from the last unrelated completed path. Do not invent empty queue positions. Existing `LogisticsZoneSnapshot.Capture` and `LogisticsZoneOverlay` own the queue/dock rectangles; the new layers must not duplicate them. Dirty regions and conflict geometry must not reveal hidden foreign state.

Required aggregate observations are waiting and active searches, actual search work, maximum wait ticks, cache hits and bytes, active workspaces, regional updates, completed deliveries, and replan count. Their producer must define the viewer scope and observation window. Count completed deliveries only from existing authoritative delivery events; reaching a route endpoint is not a delivery.

Optional time and allocation observations may come only from bounded instrumentation of normal gameplay. Use fixed buffers and finite rolling windows. Wall-clock measurements must never influence simulation decisions. No synthetic benchmark or scenario execution is part of diagnostics.

## Existing controls and future operation

The existing controls, read from `ClientApplication.UpdateDebugOverlayToggles`, are:

| Control | Existing behavior |
| --- | --- |
| `F2` | Toggle the world-space debug overlay master. |
| `Shift+F4` | Toggle the Navigation category. |
| `Shift+F5` | Toggle the World category. |
| `Shift+F6` | Toggle the Logistics category. |
| `Shift+F1` | Toggle the separate metrics overlay. |

These are visibility controls, not developer authorization. `BuildDevelopmentDebugOverlay` checks whether the overlay is enabled; its name is not an access-control policy. No verified developer-only authorization boundary is present in the inspected input/render path. This change therefore exposes no new report button, command, or hotkey.

After the integration prerequisites are satisfied, the intended operation is: open the authorized developer diagnostics surface, enable the desired navigation layers, select one own Cargo Truck or Supply Truck, read the cause together with its source tick/revision, and activate a single explicit report action. The integration change must document the actual surface and action labels. There is currently no user-accessible route to the new exporter.

No new FPS display or simulation-parameter editor is added. Preserve the repository's existing English UI language and formatting conventions when the future view is connected.

## Integration sequence

The shared integration change owns the following sequence:

1. Integrate the common contracts and complete the missing producer fields described in the handoff. Retain one source of truth for each public contract.
2. Resolve a real developer-authorization boundary before enabling capture, display, layers, or export. Overlay state must not grant access.
3. Carry copied selection through `PresentationInteractionState.SetSelection` and diagnostic demand through the existing `SetDebugState` request path.
4. In `PresentationExtractor.OnTickCompleted`, capture viewer-filtered diagnostics on the simulation thread beside the existing interaction capture, `DebugOverlayPolicy`, `ApplyDebugCaptureState`, `CaptureDebug`, and snapshot publication. Include matching session/tick and source revisions. Do not add UI reads of live Game state.
5. On the client, call the existing `RtsSelectionController.ReconcileSelection` with the current `RenderWorld` and owner/category `SelectionFilter`. Match the complete selected identity against the new copied observation; clear the display on mismatch, death, ownership loss, or session change.
6. Extend `BuildDevelopmentDebugOverlay` only after authorization, enabled-category, viewer, identity, and snapshot-validity checks. Keep its disabled early return and bounded line/label output. `ClientRenderFrame` continues receiving copied line/label arrays.
7. Bind one explicit developer export action to `NavigationDiagnosticReportExporter.TryWrite` using that action's latest copied snapshot, current session/tick, viewer, selected entity, and matching map/seed. Surface the returned result; do not enqueue a gameplay command or start a simulation scenario.

These shared call sites are identified for handoff only and are unchanged by this implementation.

## Lifecycle and disabled cost

The encoder and writer have no retained state, subscriptions, timers, rolling traces, or background workers. Their report buffers exist only for a direct call. Because no call site is registered, this change adds no per-tick vehicle-string or overlay-geometry construction. This is a structural property of the unregistered implementation, not a measured performance claim.

Future layer settings and rolling observation buffers belong to presentation/diagnostic state. They must reset when the session or viewer changes and require no save/replay changes. Navigation, transforms, inventories, cargo, fuel, queues, reservations, and service authorization remain under their existing owners.

## Validation and manual acceptance

The baseline SDK is .NET `10.0.401` with `latestPatch` roll-forward. The current environment has no `dotnet` or PowerShell and is not a Windows client-build environment. All three required Release compilations are **not executed**:

```powershell
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release
dotnet build src/ForgeLine.Presentation/ForgeLine.Presentation.csproj --configuration Release
dotnet build src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

Run the final targeted compilations in a suitable environment using repository build rules; restore is permitted. Do not remove platform targets or suppress existing warnings. Compilation alone does not establish gameplay correctness or measured performance.

No automated tests are created, changed, or executed. No benchmark, soak, smoke, headless, or visual-reference run is performed. Existing CI and test guards remain unchanged; ordinary push/PR-triggered CI may run, but no additional test run or rerun is requested.

Manual acceptance remains pending until the complete integration is available. It must cover each wait state, unavailable producers, selection changes, death and entity-index reuse, ownership/visibility changes, stale ticks and session changes, disabling all new layers, valid and rejected manual exports, and local write failures. Verify that no report contains hidden foreign information and that successful reports represent a single explicit request.
