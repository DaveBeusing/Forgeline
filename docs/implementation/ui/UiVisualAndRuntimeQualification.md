# UI Visual and Runtime Qualification

## Goal

Qualify the completed graphical frontend/HUD migration across visual coverage, interaction, accessibility, native rendering, resource lifetime, performance and distributable player assets. Close regressions in the migrated area and produce attributable evidence rather than declaring quality from a concept board. Execute through the existing GitHub connection without changing working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. Existing `docs/UiVisualReview.md` separates structural checks, native images, physical-monitor testing, performance and human review. The known baseline `build-test` failed in Windows window-mode qualification with a fence timeout during shutdown, leaving later full-suite and packaging gates unexecuted. Recheck the current state; do not carry an old failure or success forward without evidence. The reference inventory originally records pending binary import, not delivered runtime art.

## Scope

Complete the surface-coverage audit, native state matrix, behavioral regression suite, asset/package integrity checks, missing-content/lifecycle tests, matched performance measurements, review records and final documentation for the graphical UI. Fix regressions introduced by the migration in their actual owning components.

## Non-goals

No new gameplay systems, unrelated engine redesign, invented performance claims, automatic human art approval, unreviewed golden regeneration, assertion weakening, broad CI permissions changes or removal of terminal-match validation. Do not turn a reference-board checksum or screenshot montage into production acceptance.

## Prerequisites and dependencies

Require verified original artwork import, production skin/icon/portrait sources, compiled atlas renderer and completed frontend, battlefield HUD, production/operations and tactical integrations. Their capability must be present on current `master`, or deliberately continued in a synchronized existing PR. Read the common surface inventory and all outstanding acceptance records before running expensive matrices. Do not create a new branch from a stale dependency branch.

## Mandatory repository pre-check

Inspect in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; integrated functionality; architecture/projects; existing tests; CI; documentation and rules. Reuse/synchronize viable work and existing evidence when identities truly match. A new technical branch starts only from current `master`, never `main`, cached code or unrelated feature history.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/UiVisualReview.md`, `docs/VisualReferenceEvidence.md`, `docs/GraphicsReferencePerformance.md`, `docs/AssetPipeline.md`, `docs/Development.md` and `docs/ReleaseVersioning.md`. Preserve authoritative simulation, faction-limited snapshots, shared input geometry, existing font rendering and Graphics ownership. Repository naming remains technical/product-related, without internal execution identifiers or production-tool markers. Human acceptance needs a real reviewer decision and exact evidence identity.

## Concrete implementation tasks

1. Reconcile every inventoried existing UI/HUD surface with its actual normal render path. Require a production skin/asset mapping or an explicit justified dynamic/procedural, debug or emergency exception. Find remaining generic placeholder icons, unstylized control states, wrong portraits, baked values, silent fallbacks and uncovered menus. Repair omissions in the owning surface, not by hiding it.
2. Verify source and runtime integrity. Check all five reference originals and their hashes; production source IDs; atlas regions/states; resolved runtime dependencies; sealed manifest/container hashes; missing/corrupt asset diagnostics. No reference board, unused source master, temporary capture or authoring dependency may ship in the player package.
3. Capture the current named frontend states: menu/focus/disabled, loading/error, new/load game, empty/broken saves, settings/Controls with long content, pause and Credits. Capture HUD resource/power/supply pressure, mixed selection, every dock state, queue blockage, current technology, contacts/targeting and strategic minimap. Add narrowly scoped fixture states only for genuinely uncovered controls; do not overwrite accepted fixtures to obtain a pass.
4. Cover 1280x720, 1920x1080, 2560x1440, 3440x1440 and 3840x2160; raster DPI 96/144/192 and UI scales 0.75/1/2 where supported. Split the matrix into independently complete roots, record the exact union of case keys and never overwrite interrupted evidence. Record actual host-window DPI and output dimensions separately from requested settings.
5. Inspect original images at native pixel size for alpha fringes, atlas bleeding, nine-slice distortion, blurred HUD when world resolution is reduced, text contrast/metrics, clipping, focus, disabled reasons, icon semantics and excessive battlefield occlusion. Inspect over bright/dark terrain and in grayscale/color-vision views, keeping transformed inspection aids separate from original evidence.
6. Perform keyboard and pointer journeys through settings/apply/cancel/display rollback, load errors, selection, commands, production queues, operations filters, alerts and minimap. Check geometry alignment after resize/monitor/DPI changes and restore/alt-tab. Preserve privacy: no hidden/unidentified enemy facts through portraits, markers or tooltips.
7. Exercise missing/invalid runtime assets, cancellation during preparation, repeated menu/match/restart transitions, minimize/restore, device-fault handling and normal shutdown. Record descriptors, texture pages, cache counts and resource retirement before/after; prove ownership remains bounded. Reproduce any baseline fence failure separately and distinguish inherited failures from migration regressions. No weakening timeouts/assertions merely to clear a check.
8. Measure matched warm workloads with source/runtime/build, adapter/driver, resolution, preset, DPI/scale and scene identity fixed. Record CPU/GPU p50/p95/p99, allocations, draws, vertices, upload bytes and texture/descriptor residency. Separate isolated skin costs where measurable from whole-world/HUD capture costs. Set documented evidence-based budgets; do not claim sustained frame-rate targets from WARP or short readbacks.
9. Build and test a source-less packaged player, including first run, menu, match, restart and failure diagnostics. Prove no compilation or source-image reads occur at runtime. Run unchanged headless/command/recovery and natural terminal-match checks to confirm UI work has not altered authoritative results.
10. Produce before/after comparison records and pending human review exports using the existing scripts. Preserve original hashes and manifests. A real reviewer must explicitly accept the exact candidate; structural repeatability alone is not aesthetic, physical-monitor, live-motion or GPU-performance approval.

## Intended commit boundaries

Separate coverage findings/focused fixes, deterministic test additions, capture/diagnostic improvements, packaging/integrity corrections and documentation/evidence. Keep valid logical commits small. Use one PATCH increment per PR relative to current `master`, never per commit/rerun; preserve history without automatic tags/releases or squash.

## Tests and validation

Run targeted compiler/atlas/Graphics tests, Presentation/Client interaction tests and architecture boundaries before full solution validation. Add regression tests for each discovered defect. Preserve approved golden evidence and fail clearly on missing/pending/rejected acceptance rather than manufacturing approval. Report passed, failed, skipped, not run and human-review-pending as distinct outcomes.

## Build and CI requirements

Use the SDK and Microsoft.Testing.Platform selected by current `global.json`:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
```

Use exact current capture parameters from `docs/UiVisualReview.md`, `Invoke-RtsReferenceQualification.ps1`, `Compare-VisualReferenceEvidence.ps1` and `Export-VisualReferenceReview.ps1`. The complete `build-test` job remains the merge gate, including native, recovery, package and terminal-match steps. A green compilation subset is not a green pipeline. Keep any unperformed Windows/hardware tests explicitly not run.

## Branch and pull request strategy

Reuse a synchronized related PR or start `ui/graphical-interface-qualification` from current `master`. Inspect current-head CI and repair migration-caused failures. Keep draft if required checks or acceptance are pending. Do not merge, auto-merge or release while blockers remain; do not change repository protection to make integration possible.

## Documentation and recovery

Update the coverage inventory, UI handbook, asset pipeline, relevant screen docs and performance/acceptance records. Checkpoint exact base/head SHA, completed case keys, immutable output roots/hashes, tests/results, unresolved defects and next action at each commit. On resume, verify evidence identity and complete only missing cases; never count an interrupted run as finished.

## Definition of done

The complete current graphical UI is covered; required functional/native/package checks pass; production assets are present and source-less deployment works; measured resource behavior meets documented budgets; human decisions and outstanding limitations are explicitly attributable.

## Concrete completion criteria

Return final surface/state coverage, intentional procedural exceptions, all test/CI outcomes, source/runtime identities, native and physical-display evidence, performance/resource results, package checksums, review status, commits and PR. Do not report final AAA acceptance while any required art, behavior, hardware or reviewer gate remains pending.
