# Graphical Battlefield HUD

[Documentation](../../README.md) · [Complete catalog](../../DocumentationIndex.md)

## Goal

Integrate the Directorate production skin into the live resource/power strip, selection inspector, command-dock shell, combat-group presentation and shared gameplay tooltips. Retain authoritative values and existing RTS interaction. Complete implementation, tests, evidence and PR handling through the existing GitHub connection without changing working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. `GameplayHudRenderer.cs` composes `ResourcePowerHudSurface`, `SelectionInspectorHudSurface`, `PlayerActionDockHudSurface`, `RtsInformationHudSurface`, `WorldHoverTooltipSurface` and `OperationsHudSurface`. The documented resource strip exposes seven authoritative resources; power is separately described by generation/demand. `WaitingForSnapshot` and `WaitingForPlayerData` are real states. Recheck the current implementation and production asset availability; reference PNG import was pending during planning.

## Scope

Migrate the HUD shell, resource/power cells, selection identity/portraits, health/supply/readiness displays, group-strip/card decoration, command mode tabs, action-control states and shared tooltip surfaces. Preserve existing game data and input semantics. Mark every migrated surface in the common coverage inventory.

## Non-goals

Do not implement new resources, population caps, balance rules, simulated inventories, production/operations card content, alerts, minimap markers or world-space indicators. Production and alerts have a subsequent scope; tactical maps/cursors have their own scope. Developer diagnostics remain independent of final art availability.

## Prerequisites and dependencies

Require the verified source artwork, semantic icons/portraits and completed shared atlas/skin renderer. Complete shared frontend skin integration first so helper/palette changes are stable. Start only from current `master` containing the prerequisites, or continue a deliberately reused synchronized PR. `PlayerActionDockHudSurface` is shared with production/operations work: implement its shell/state contract here first, then integrate domain cards there. Avoid parallel rewrites.

## Mandatory repository pre-check

Inspect, in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; functionality already integrated; architecture/projects; tests; CI; current documentation/rules. Reuse viable work and synchronize it. New branches start exclusively from current `master`, never `main`, cached state or an unrelated feature branch. Read only the affected HUD, layout, view and test files unless dependencies require expansion.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/Architecture.md`, `docs/CommandInterfaceVisualLanguage.md`, `docs/BattlefieldHudQualification.md`, `docs/CommandDockQualification.md`, `docs/UiVisualReview.md` and `docs/ReleaseVersioning.md`. Retain `GameplayHudLayout`, published hit geometry, snapshots and validated command submissions. Presentation cannot own resource or combat truth. Keep English UI labels, current font atlas and semantic palette. Repository naming must be technical/product-related, without internal execution identifiers or production-tool markers.

## Concrete implementation tasks

1. Audit actual owners of the resource, selection, group and command shell surfaces. Bind existing view fields to production skin roles; do not reconstruct simulation state in the renderer.
2. Skin resource cells and the separate power region with clean frames and semantic icons. Keep every currently exposed resource visible, including the compact two-row layout. Distinguish totals from rates, unavailable data from zero, and power generation/demand from stock. Never copy reference-board numbers or the illustrated supply/population cap.
3. Add correct catalog-bound portraits to single-unit/building selection. Keep names, ownership, counts, health, ammunition, fuel, supply, readiness and work dynamic. Fit portraits without stretching. Omit facts not authorized by the current read model; do not infer hidden enemy health or identity.
4. Skin multi-selection filters, group summaries, damaged/critical-supply focus and formation controls. Preserve existing aggregate semantics, control-group shortcuts, unit cycling and stable selection IDs. Do not turn decorative figures or submeshes into new simulation entities.
5. Migrate the command-dock shell, mode tabs, structural hierarchy, hotkey controls and state overlays. Preserve the existing command content contract for subsequent production integration. Keep Pending distinct from Accepted/Rejected and from actual task completion; an accepted order is not a completed delivery or build.
6. Use separate clipped fill sprites for bars and bounded state overlays for warnings/focus. Keep quantities, percentages and hotkeys in `UiTextBatch`; preserve color-plus-shape/text semantics. Use quiet backgrounds and do not let decorative borders consume functional space.
7. Migrate shared gameplay tooltip backplates and optional player-facing guidance. Preserve fitted width/height, current delay/input behavior and accurate control hints. Developer telemetry remains readable through its asset-independent path.
8. Verify modal/world input boundaries, empty selection, missing snapshots, compact scales, long names, rapidly changing selections and paused/resumed matches. Normal HUD art/text remains at native resolution regardless of world render scale.

## Intended commit boundaries

Separate resource/power integration, selection/portraits, group presentation, command shell/states, tooltips, regression tests and evidence/documentation. Commit valid logical increments continuously. Use exactly one PATCH increment relative to current `master` per PR; preserve individual commits and do not auto-tag, release or squash.

## Tests and validation

Preserve and extend current `GameplayHudRuntimeTests`, `ResourcePowerAlertHudTests`, `SelectionInspectorHudTests`, `HudInformationLayoutTests`, `GameplayHudLayoutIntegrationTests`, `GameplayHudVisualQualificationTests` and relevant Client interaction tests after verifying actual names. Test all resource visibility, zero/unavailable distinctions, correct portrait mapping, live progress, read-model privacy, group actions and pointer/keyboard alignment after DPI changes. Validate the missing-snapshot path without invented totals.

## Build and CI requirements

Use current pinned SDK and Microsoft.Testing.Platform. Restore/build Release, validate project references, compile runtime assets and run targeted HUD/interaction tests, then `dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1` and required CI. Use existing capture tooling for `ResourcePressure`, `LowPower`, `SupplyCritical`, `MixedSelection`, `DockFocus`, `DockHover`, `DockPressed`, `DockPending`, `DockAccepted` and `DockRejected` where present. Retain source/runtime/capture identities. Existing fence/shutdown failures remain disclosed and cannot be suppressed to pass this scope.

## Branch and pull request strategy

Reuse a synchronized related PR or start `ui/graphical-battlefield-hud` from current `master`. Inspect current-head CI and fix introduced regressions. Leave draft while required migration, validation or review is pending; no merge with failed/pending required checks. Do not change frozen geometry/goldens without explicit justified review.

## Documentation and recovery

Update HUD/command documentation and the surface coverage inventory. At each logical commit, retain base/head SHA, migrated surfaces, remaining states, exact test results, source/runtime hashes, capture paths, blockers and next action in the PR. Resume from verified Git and current checks instead of repeating completed integrations.

## Definition of done

Every in-scope normal HUD surface uses the shared production skin with correct dynamic information, identities and controls. Required resource/selection states remain readable and input-aligned; no simulation behavior changes or new asset lifetime regressions occur.

## Concrete completion criteria

Report covered surfaces, portrait/icon mappings, retained fallback exceptions, native before/after evidence, resource/draw costs, passed/failed/not-run tests, review status, commits and PR. Zero baked gameplay values, zero lost resource cells and zero unauthorized intelligence in the HUD.
