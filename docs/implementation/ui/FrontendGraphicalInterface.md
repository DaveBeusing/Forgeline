# Graphical Frontend Interface

## Goal

Replace prototype-style frontend panel/control decoration with the Directorate production skin while preserving every existing screen, setting and interaction. Implement and validate the real application integration end to end through the existing GitHub connection without changing working environment.

## Current situation

The planning baseline is `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. `FrontendOverlayRenderer.cs` already uses `UiTextBatch`, shared `FrontendGeometry`, a compiled cinematic background and geometric panel helpers. The integrated frontend includes settings categories, a scrollable Controls table, full-viewport artwork and restricted Credits content. Do not recreate or regress those capabilities. The new reference catalog still records pending original PNG import; verify completion and production skin availability at execution time.

## Scope

Migrate all existing menu, new/load game, loading, empty/broken-save, settings, Controls, pause, Credits, confirmation, error and feedback surfaces, plus their buttons, rows, tabs, switches, sliders, scrollbars, field borders and focus/disabled states. Extend the inventory when current `master` exposes additional player-facing frontend surfaces.

## Non-goals

Do not replace approved studio/product branding or already suitable background art, add game modes, redesign input bindings, change save formats, render settings/labels as bitmaps, or migrate the battlefield HUD. Preserve the bootstrap/emergency path that can explain an asset or device failure without loading the skin.

## Prerequisites and dependencies

Require the production skin/icon sources and completed shared compiled atlas/skin renderer. Begin only when those capabilities are integrated into current `master`, or deliberately continue their existing synchronized PR without creating competing implementations. Coordinate changes to shared palette/geometry helpers with the renderer scope. Battlefield HUD migration follows this shared frontend integration to avoid overlapping helper changes.

## Mandatory repository pre-check

Inspect in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; already integrated functionality; architecture/projects; tests; CI; current documentation and rules. Reuse/synchronize viable work first. New branches must start from current `master`, never `main`, remembered state or an arbitrary feature branch. Read affected files in focused batches.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/FrontendLayout.md`, `docs/CommandInterfaceVisualLanguage.md`, `docs/UiTypography.md`, `docs/UiMotionAndAudio.md`, `docs/UiVisualReview.md` and `docs/ReleaseVersioning.md`. Inspect `FrontendOverlayRenderer.cs`, `FrontendGeometry`, `FrontendSurfaceView`, frontend controllers in Client/UI and their tests. Preserve existing device ownership, shared geometry, English product text, font metrics, semantic palette, reduced motion and sound preferences. Keep repository names purely technical/product-related; no internal execution identifiers or production-tool markers.

## Concrete implementation tasks

1. Map each inventoried frontend control to existing live view data and the new skin role. Preserve existing layout regions and control IDs unless an explicit necessary adjustment updates drawing and input together.
2. Replace geometric decoration in normal production paths with reusable sprite/nine-slice surfaces. Use separate text and value layers. Do not resize entire reference panels, obscure the existing full-viewport title artwork, or display unapproved slogans/emblems from the concept boards.
3. Migrate navigation cards, all Settings categories, the Controls action/input table, load-game lists, dialogs and feedback. Cover normal/hover/pressed/focus/selected/disabled/error and appropriate combined states. Retain reason text for unavailable actions and all keyboard/wheel navigation.
4. Preserve unsaved settings while switching tabs, current Apply/Cancel semantics, audio preferences, rebinding conflicts and the 15-second display-confirmation/rollback behavior. Use the actual existing timeout/controller, not a second timer inside the artwork renderer.
5. Keep loading progress, save metadata, version, hotkeys and errors dynamic. Distinguish asset readiness, application readiness and match readiness. A polished loading panel must not falsely report a completed session.
6. Preserve paused battlefield context and a continuous quiet backdrop preventing HUD text from leaking through menu gaps. Respect modal input capture; decoration is not a hit target and must not eat clicks outside actual modal policy.
7. Validate compact screens, long labels and 0.75-2.0 UI scales. Clip scrolling rows and keep action buttons/feedback reachable. Use actual viewport and DPI; no fixed 16:9 frame, content overlap, distorted corner artwork or low-resolution text.
8. Keep a deterministic controlled fallback for failed required skin preparation and existing optional artwork behavior. Missing skin remains an explicit failure/degraded state, not a hidden pass. Do not create textures or decode image files per frame.

## Intended commit boundaries

Separate frontend skin wiring, navigation/list controls, settings/detail controls, modal/loading/pause states, regression tests and documentation/evidence. Keep logical commits small and valid. Apply one PATCH increment per PR relative to current `master`; no extra version for follow-up commits or CI attempts, no squash, automatic tag or release.

## Tests and validation

Preserve and extend current `FrontendOverlayRendererTests`, `FrontendLayoutHardeningTests`, `FrontendPointerInteractionTests`, `FrontendControlAffordanceTests`, `FrontendInteractionTests` and related settings/lifecycle tests after verifying their current names. Test click-center/edge alignment after scaling, keyboard-only navigation, unsaved preference persistence, disabled reasons, rebinding, display rollback, scroll bounds, missing assets, cancellation and menu/match transitions. Cover absent/empty/broken saves and loading errors. Do not replace meaningful assertions or approved goldens to hide differences.

## Build and CI requirements

Use the SDK/Microsoft.Testing.Platform selected by current `global.json`. Restore/build Release, validate project references, compile runtime assets, run targeted tests and `dotnet test --solution ForgeLine.sln --configuration Release --no-build`. Use current MTP syntax rather than stale legacy VSTest examples. Capture named frontend states through `build/Invoke-RtsReferenceQualification.ps1` as documented in `docs/UiVisualReview.md`, first at 1080p, then compact/1440p/ultrawide/4K and DPI variants. Inspect current-head CI and distinguish inherited fence/shutdown failures from new regressions; do not weaken the gate.

## Branch and pull request strategy

Reuse a relevant synchronized PR or start `ui/graphical-frontend` from current `master`. Complete all existing frontend surfaces in this scope, not a single representative screenshot. Leave the PR draft while required functionality, tests or visual review is pending; do not merge on failed/pending required checks.

## Documentation and recovery

Update frontend layout/visual documentation and the surface coverage inventory. Record base/head SHA, migrated screens, remaining controls/states, exact tests/results, native capture identities, source/runtime hashes, blockers and next action in the PR after each logical commit. Resume by verifying current code and CI before continuing.

## Definition of done

All normal frontend surfaces use the shared production skin, retain dynamic text/state and their established behavior, fit supported layouts and have attributable regression and visual evidence. Emergency fallbacks remain usable.

## Concrete completion criteria

Return screen/control coverage, explicit fallback exceptions, before/after captures, measured resource impact, passed/failed/not-run validation, review status, commits and PR. No baked functional text, no input/render geometry divergence, no missing Settings functionality and no unreviewed final-quality claim.
