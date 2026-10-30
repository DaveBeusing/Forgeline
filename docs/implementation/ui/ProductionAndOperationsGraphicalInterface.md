# Graphical Production and Operations Interface

[Documentation](../../README.md) · [Complete catalog](../../DocumentationIndex.md)

## Goal

Integrate production artwork into the live build/production cards, queues, logistics/operations views, current technology presentation and actionable alerts. Preserve physical economy and command semantics. Complete implementation, tests, evidence and PR handling through the existing GitHub connection without changing working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. The HUD is modular, with `PlayerActionDockHudSurface`, `OperationsHudSurface` and `RtsInformationHudSurface` as relevant entry points. The documented operations UI exposes stock, production, power and logistics bottlenecks; alerts provide authorized focus and filtered operations navigation. The reference panel board illustrates these families but its values, names, population cap and technology layout are not game data. Original PNG import was pending during planning.

## Scope

Migrate existing build/unit-production/recipe cards and queue visuals, operation details and rows, supply/logistics/power diagnostics, existing technology presentation, alerts and domain-specific tooltips. Add production portraits/icons and graphical blocked/pending/disabled feedback while keeping all information live.

## Non-goals

No new technology tree, railway gameplay, invented production recipes, resource grants, delivery shortcuts, remote inventory transfers, new simulation metrics or static graph screenshots. Do not change minimap/cursor/world-indicator rendering, replace the completed shared HUD shell, or redesign computer-opponent rules.

## Prerequisites and dependencies

Require verified artwork/semantic mappings, the shared atlas/skin renderer and completed graphical battlefield HUD shell. This scope changes command-card content after the earlier shell migration in `PlayerActionDockHudSurface`; do not rewrite that helper in parallel. It may also touch `RtsInformationHudSurface` for alerts; tactical-map work follows this scope to avoid overlapping edits. Prerequisite capability must be on current `master` or intentionally continued within a synchronized existing PR.

## Mandatory repository pre-check

Inspect in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; integrated functionality; architecture/projects; tests; CI; documentation and binding rules. Reuse/synchronize viable work instead of creating parallel implementations. New branches start only from current `master`, not `main`, remembered source or an arbitrary dependency branch. Batch reads around the current dock, operations, alert and production views.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/ProductionAndLogisticsOperations.md`, `docs/ActionableAlertsAndStrategicMap.md`, `docs/CommandDockQualification.md`, `docs/CommandInterfaceVisualLanguage.md`, `docs/UiVisualReview.md` and `docs/ReleaseVersioning.md`. Inspect current surface/view/controller ownership before changing code. UI consumes copied facts and submits validated commands; it never owns resources, routes, completion or supply state. Use purely technical names throughout the repository, without internal execution or production-tool markers.

## Concrete implementation tasks

1. Map current command modes, cards, queues, operations details and alerts to the common surface catalog. Bind current stable resource/unit/building/action identities to production artwork. Do not add absent actions simply because a reference icon exists.
2. Skin build and unit/recipe cards using actual portraits, separate icon layers and dynamic costs, prerequisites, hotkeys, progress and availability. Preserve current compact layout and existing command controls; a reference grid is not permission to replace the interaction model wholesale.
3. Render queue entry backgrounds, cancel/reorder controls only where supported, empty slots, current work and blocked states with clear hierarchy. Keep time/progress sourced from the existing view. Distinguish queued, started, stalled, completed and rejected work; an accepted command does not guarantee production completion.
4. Apply production art to current logistics/stock/power/production operation rows and inspectors. Preserve sorting, filters, scrolling, selected facility, underlying causes and authorized navigation. Graphs and network views remain data-driven; show unavailable or unmeasured information explicitly rather than inventing throughput.
5. Reskin currently implemented technology presentation without adding research mechanics or imported reference nodes. Preserve material/prerequisite requirements and unavailable states. Retain future-system art as reference-only where no runtime feature exists.
6. Skin alerts by severity and category with dedicated icons, structural markers and live text. Preserve critical core/supply priority, bounded history and exact actionable regions. Empty rows, decorative shadows and gaps must not capture world input. Preserve existing FOCUS/OPS/MORE behavior and knowledge filtering.
7. Extend domain tooltips with clean frames and real descriptions/reasons/costs. Preserve dynamic values, text scaling and measured layouts. Do not place information exclusively in color or tiny decorative detail.
8. Exercise updates during shortages, loss of power, destroyed buildings, unavailable suppliers and selection changes. The skin must not cache stale actionable IDs or expose hidden inventories. Keep allocation and texture residency bounded through the shared renderer.

## Intended commit boundaries

Separate command-card/portrait integration, queue/state visuals, operations/technology views, alerts/tooltips, regression tests and documentation/evidence. Commit complete logical changes continuously. Use exactly one PATCH increment relative to current `master` for the PR; preserve individual commits, no per-commit increments, automatic tags, releases or squash.

## Tests and validation

Extend existing operations, action-panel, production, actionable-alert and Client interaction tests after locating their actual current classes. Test insufficient resources/power, unavailable prerequisites, queue cancel/reorder where supported, full/empty queues, completed/destroyed producers, changing selection, rejected commands, tooltip clipping and keyboard/pointer hit alignment. Test hidden/enemy facility facts stay absent and decorative/empty alert regions pass input through appropriately.

## Build and CI requirements

Use current pinned SDK/Microsoft.Testing.Platform. Restore/build Release, validate references, compile runtime assets, run targeted tests and then `dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1` plus complete required CI. Capture current named `QueueBlocked`, `Technology`, `ResourcePressure`, `LowPower`, `SupplyCritical` and dock-result states through existing reference tooling. A bounded headless production/logistics scenario must retain authoritative outcomes. Do not remove the natural terminal-match gate or suppress existing GPU lifecycle failures.

## Branch and pull request strategy

Reuse a synchronized relevant PR or start `ui/graphical-operations` from current `master`. Inspect current-head CI, fix introduced regressions and preserve unrelated work. Keep draft until required functionality, tests and visual review are complete; no merge with failed/pending required checks.

## Documentation and recovery

Update production/operations/alert documentation and surface coverage. At each logical commit record base/head SHA, completed modes/surfaces, remaining edge states, exact tests/results, source/runtime hashes, capture identities, blockers and next action in the PR. On resumption verify current views and remote changes before continuing; never replay a completed migration blindly.

## Definition of done

Every existing in-scope economic/operational surface uses the shared graphical skin, remains live and truthful, preserves current controls and physical economy, and has regression and visual evidence for normal and failure states.

## Concrete completion criteria

Report mode/queue/operations/alert coverage, any intentionally unavailable reference concepts, icon/portrait mappings, authoritative scenario results, native evidence, measured overhead, passed/failed/not-run validation, commits and PR. Zero fabricated metrics, zero new gameplay from concept art and zero loss of actionable error reasons.
