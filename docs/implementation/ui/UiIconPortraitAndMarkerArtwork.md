# UI Icon, Portrait and Marker Artwork

## Goal

Produce the complete clean icon, portrait, cursor and tactical-marker families needed by the existing game, using the Directorate reference artwork. Deliver reusable production sources and semantic mappings, not labeled presentation sheets. Execute the remaining work end to end through the existing GitHub connection without changing working environment.

## Current situation

The planning snapshot is `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15; recheck current `master` before implementation. The reference inventory at `assets/reference/ui/directorate/catalog.json` currently records five PNGs awaiting binary import. The resource/status, command/cursor and strategic-marker boards contain embedded captions and illustrative concepts. The current runtime roster, actions and resources, not the boards, define required playable identities.

## Scope

Author resource/status symbols; commands and unit/building role glyphs; actual unit/building portraits; operational badges; frontend utility glyphs; minimap/strategic symbols; cursor sources and hotspots; selection/order marker sprite components. Supply distinct small-size tactical glyphs and larger illustrated inventory/portrait treatments where needed.

## Non-goals

Do not invent resource economies, unlock rail or aircraft gameplay, replace in-world models, bake unit values into portraits, author static minimap terrain, migrate UI consumers, or add a parallel runtime asset system. A future-system symbol can be retained as reference-only, but must not become an enabled action.

## Prerequisites and dependencies

Require the verified original import, surface inventory and skin visual contract described by `UiSkinArtworkAndSurfaceCatalog.md`. These must be available on current `master` or deliberately continued within their existing synchronized PR. Do not start a new implementation branch from a stale dependency branch. Renderer integration follows the shared atlas contract; this scope owns artwork and semantic source mapping only.

## Mandatory repository pre-check

Check, in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; functionality already integrated; affected architecture; tests; CI; documentation and binding rules. Reuse viable work, synchronize it, and create a new technical branch from current `master` only if necessary. Batch inspection around current catalogs and UI sources rather than rescan the entire repository.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/AssetPipeline.md`, `docs/CommandInterfaceVisualLanguage.md`, the new surface inventory and current Directorate building/unit production documents. Existing stable IDs and game rules are authoritative. Keep references outside runtime; production PNG/TGA files and supported editable sources belong under `assets/source/ui/directorate/`. The player cannot parse SVG or source formats merely because editable vector masters exist. Preserve purely technical names throughout repository metadata and truthful provenance; no internal execution identifiers or production-tool traces.

## Concrete implementation tasks

1. Enumerate current resource, status, action, unit, building and information-layer identifiers from code and catalogs. Create a coverage table binding each real identifier to an intended visual and migration consumer. Distinguish runtime-required, intentionally unavailable and reference-only concepts.
2. Extract usable resource/status pictograms without their labels or label plates. Re-author unsuitable edges, ambiguous silhouettes and insufficient-resolution material. Preserve the originals. Check that health, damage, fuel, ammunition, supply, maintenance, power and unavailable states remain distinguishable at actual HUD size.
3. Complete commands actually supported by the game, including their rejected/invalid, pending and unavailable presentation. Prepare role and utility glyphs for existing settings, controls, navigation, audio, alerts, close/cancel, expand/collapse and scrolling. Different commands cannot silently share an ambiguous icon.
4. Produce portraits for the actual unit and building roster, preferably from deterministic native captures of current production models with fixed camera/lighting. Match identities and silhouettes; never label a tank portrait as a rifle squad. Keep captions, cost, progress, damage, selection and hotkeys in separate runtime layers. Maintain small card and inspector variants without distortion.
5. Produce friendly/visible-enemy/detected-contact and infrastructure symbols. A detected contact must not reveal an unidentified unit class. Encode allegiance and critical state by shape as well as color. Avoid highly detailed illustration for 16-32 pixel map markers.
6. Separate cursor graphics from command-button graphics. Define actual pixel bounds, hotspot, intended display sizes and light/dark contrast outline for each supported cursor. Provide selection corners, ring sections, target and order components; terrain-conforming geometry and range shapes remain dynamic.
7. Define asset IDs, logical roles, intended sizes, scale variants, alpha/color-space requirements and source relationships in the shared source contract. Reuse existing IDs when appropriate. Do not introduce a second incompatible atlas schema; coordinate with `UiAtlasPipelineAndSkinRenderer.md`.
8. Review the complete family at 16/24/32/48/64 pixels where applicable, and portraits at their actual fitted UI size. Label any visually indistinguishable or blurry output as a gap, not as finished art. Do not claim upscaling has recovered missing source detail.

## Intended commit boundaries

Separate semantic coverage mapping, resource/status glyphs, action/utility glyphs, real-roster portraits, tactical/cursor sources, validation and documentation. Commit completed logical groups continuously. Use one PATCH increment per PR relative to current `master`; preserve individual commits, with no per-commit increments, automatic tags or releases.

## Tests and validation

Check source decoding, nonempty visible bounds, alpha behavior, exact mappings, missing/duplicate IDs and cursor hotspot bounds. Verify every current roster entry has the correct portrait and every supported action has a visual or a documented intentional exception. Review all icon families over bright/dark terrain and grayscale/color-vision inspection. Confirm reference-only concepts are absent from runtime registrations.

## Build and CI requirements

Use current pinned SDK and Microsoft.Testing.Platform. Restore/build Release, validate project references and compile/qualify registered source assets through `Build-RuntimeAssets.ps1 -Mode development -NoBuild`. Run targeted compiler/catalog tests, then required complete CI. No source discovery or portrait rendering at interactive startup. Record native portrait capture, hardware inspection and any unavailable tests separately; never turn WARP output into physical-GPU acceptance.

## Branch and pull request strategy

Prefer an existing reusable PR; otherwise start `art/directorate-ui-icons` from current `master`. Inspect current-head CI, correct regressions in this scope and leave a draft while required sources, identity checks or review are missing. No merge on failing/pending required validation and no unrelated CI redesign.

## Documentation and recovery

Update semantic mapping and provenance in the source inventory. Record completed source groups, exact hashes, roster coverage, capture identity, base/head SHA, test commands/results and next incomplete item in the PR at each commit. Resume by verifying actual files and integrated work, never by repeating an already completed asset family.

## Definition of done

Every currently implemented role is mapped to clean, readable production art; portraits represent actual content; cursor and tactical metadata are complete; source assets compile; review and coverage evidence remain truthful.

## Concrete completion criteria

Return the exact new/reused asset IDs, portrait coverage, unavailable/reference-only exclusions, source paths/hashes, test and review results, commits and PR. No embedded labels or invented gameplay values in runtime pictograms and no hidden-information leakage through icon identity.
