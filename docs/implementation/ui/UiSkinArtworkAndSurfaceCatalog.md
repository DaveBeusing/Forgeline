# UI Skin Artwork and Surface Catalog

[Documentation](../../README.md) · [Complete catalog](../../DocumentationIndex.md)

## Goal

Implement a complete, reusable Directorate skin-art foundation for every existing player-facing UI and HUD surface that still relies on prototype geometric styling. Deliver clean production artwork, not another reference montage. Execute the remaining scope end to end through the existing GitHub connection without requiring a change of working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. This is evidence context, not permission to start from an old revision. `FrontendOverlayRenderer` mixes geometric panels with `FrontendMenuBackground` and `UiTextBatch`; `GameplayHudRenderer` composes separate resource, selection, action, information, tooltip and operations surfaces. Five original reference boards are imported and remotely verified in `assets/reference/ui/directorate/catalog.json`; revalidate that evidence at implementation start. Their labels, numbers and example gameplay are not canonical requirements.

## Scope

Revalidate the imported original PNGs, inventory existing surfaces, define the shared visual contract and author reusable panel/control artwork. Cover frame corners and edges, quiet panel fills, buttons, tabs, row backgrounds, input fields, checkboxes, toggles, sliders, scrollbars, dropdowns, dialogs, tooltips, progress tracks/fills, portrait frames and state overlays. Include frontend controls absent from the reference boards by extending the same visual language.

## Non-goals

Do not migrate live frontend/HUD consumers, implement new gameplay, redesign approved branding, bake labels into artwork, replace the font system, or register the reference sheets as runtime textures. Debug and asset-failure/bootstrap renderers may retain necessary procedural fallbacks, explicitly inventoried rather than hidden as unfinished migration.

## Prerequisites and dependencies

The original five PNGs have been imported and remotely verified. Revalidation of their recorded identities is part of this scope; do not duplicate the completed import. If any original remains inaccessible, identify it, continue independent inventory work, and leave artwork completion blocked rather than substitute a different illustration. Subsequent icon/portrait production uses this surface catalog and art direction; the atlas renderer consumes the production outputs.

## Mandatory repository pre-check

Before changing files, inspect in this order: current remote `master`; relevant remote branches; open and closed-but-unintegrated PRs and interrupted work; functionality already integrated; affected architecture/projects; existing tests; relevant CI; documentation and binding rules. Batch reads by affected directory. Reuse and synchronize viable work instead of duplicating it. A new branch starts only from current `master`, never `main`, remembered state or an unrelated branch.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/AssetPipeline.md`, `docs/CommandInterfaceVisualLanguage.md`, `docs/UiVisualReview.md` and `docs/ReleaseVersioning.md`. Preserve C# 14/.NET 10, Windows x64/D3D12 and simulation/presentation separation. `assets/reference/` is never a player dependency; production sources belong under `assets/source/ui/directorate/`, compiled output under `assets/runtime/`. Names throughout branches, commits, PRs, code, tests and documents must be purely technical/product-related, with no internal execution or production-tool markers. Preserve truthful provenance and do not invent license rights.

## Concrete implementation tasks

1. Verify the five unchanged originals and completed import metadata against the current repository. Decode each PNG and compare dimensions, alpha behavior, byte count, SHA-256 and Git blob identity. Reuse the completed import. If verification fails, report the exact mismatch and repair only from the authentic originals; never replace them with new illustrations. Cleanup uses separate derivatives.
2. Trace `FrontendOverlayRenderer.cs`, `GameplayHudRenderer.cs` and its six surface implementations. Add a surface inventory under `docs/` with owner, screen, current rendering path, interaction geometry, existing asset use, required sprites/states, migration owner and evidence status. Cover all current screens and menus, not only modules shown on the boards.
3. Translate the reference styling into semantic colors, spacing, corner treatment, border widths, fill opacity and state precedence. Keep `ForgeLineUiPalette` as the semantic authority. Retain the canonical tagline and existing marks. Treat the illustrated population cap, vehicle names, tiers and prices as non-authoritative.
4. Author separate, text-free PNG production sprites, with editable source where useful. Extract only sufficiently clean portions; redraw borders and controls where embedded labels, tiny resolution or merged shadows make cropping unsuitable. No stretched complete panel screenshots and no claimed detail gain from upscaling.
5. Provide normal, hover, pressed, keyboard-focus, selected, disabled and relevant warning/error layers. Support combined focus/selection without duplicating full-panel images. Specify content padding, minimum dimensions, nine-slice insets, edge repeat/stretch policy and transparent exterior bounds.
6. Validate alpha on dark and bright terrain backgrounds. Do not remove every black pixel as a transparency shortcut. Keep scratches and texture contrast away from text/value regions. Supply small-size variants when decorative detail collapses.
7. Produce a stable source inventory of actual derivatives and unresolved gaps. Coordinate the declarative skin/atlas metadata format with the renderer implementation; do not create competing runtime loaders. No production-ready claim until derivative pixel review and source checks pass.

## Intended commit boundaries

Use separate small commits for verified original import, surface inventory/art contract, reusable panel families, control/state families, source validation and documentation. Keep changes valid and traceable as they are completed. Apply exactly one PATCH increment relative to current `master` for the whole PR; do not increment per commit or rerun. Preserve individual commits and do not squash, tag or release automatically.

## Tests and validation

Validate every source's PNG decoding, meaningful alpha, dimensions, bounds, naming, duplicate IDs, insets and state coverage. Render contact sheets at intended sizes and inspect borders on light/dark backgrounds. Assert the five reference sheets cannot enter the runtime manifest or player package. Distinguish reference import, source correctness, runtime compilation and human visual acceptance.

## Build and CI requirements

Use the SDK and Microsoft.Testing.Platform selected by current `global.json`. Run `dotnet restore ForgeLine.sln`, `dotnet build ForgeLine.sln --configuration Release --no-restore`, then the relevant source checks and `pwsh ./build/Validate-ProjectReferences.ps1`. Compile any registered production sources using `pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild`. Run targeted tests first and the full repository CI gate before declaring merge readiness. Record unavailable Windows/GPU validation as not run. Do not suppress the pre-existing fence/shutdown failure or weaken acceptance to obtain green CI.

## Branch and pull request strategy

Reuse a relevant viable PR after synchronizing with current `master`; otherwise use a technical branch such as `art/directorate-ui-skin`. Maintain a draft PR while original import, required art, validation or review is incomplete. Inspect current-head CI and fix regressions introduced by this scope. Do not merge with failed or pending required checks.

## Documentation and recovery

Update the reference catalog, surface inventory and visual contract. At every logical commit, retain a technical checkpoint in the PR: base/head SHA, completed assets, remaining items, exact validation commands/results, source hashes, blockers and next action. On resumption, compare this record with Git, files and CI; an attempted upload or test is not proof of completion. Do not use temporary local paths as durable references.

## Definition of done

The five originals are verified in the remote repository; every existing UI/HUD surface is assigned; the reusable skin/control asset families are complete and readable; dynamic content remains separated; provenance and validation are accurate; the PR is reviewable.

## Concrete completion criteria

Report exact asset paths and hashes, control-state coverage, remaining explicit exceptions, commits/PR, validation results and review status. Zero silently missing original images, zero baked gameplay values in production skin sprites, and zero claims of final AAA quality without attributable review.
