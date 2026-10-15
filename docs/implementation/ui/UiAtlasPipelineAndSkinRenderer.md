# UI Atlas Pipeline and Skin Renderer

## Goal

Implement the shared compiled-asset path and graphical skin renderer that lets all existing frontend and HUD surfaces use production artwork without flattening their live content. Complete contracts, compiler support, rendering, lifecycle, tests and a small integration fixture through the existing GitHub connection without changing working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. `FrontendOverlayRenderer.cs` already owns a graphics pipeline, reusable vertex buffers, `UiTextBatch` and optional `FrontendMenuBackground`. `GameplayHudRenderer.cs` composes six surfaces; some receive `RuntimeAssetCatalog` and others do not. `docs/AssetPipeline.md` defines source declarations, compiled `.flasset` payloads, manifest integrity and a player that never compiles source assets. Extend these paths rather than creating another UI framework or renderer device.

## Scope

Implement deterministic UI atlas/skin metadata, compile-time packing or validated authored atlas regions, runtime loading, shared texture/resource ownership, nine-slice/tiled panel drawing, sprite states, clipping, batching, dynamic fill regions and diagnostics. Integrate a representative control fixture and the shared plumbing, not every screen.

## Non-goals

No new general-purpose UI framework, second graphics device, source parsing in the player, duplicated font renderer, global gameplay migration, balance changes, broad GPU-shutdown rewrite or unrelated CI redesign. Do not render the reference boards as full-screen UI textures.

## Prerequisites and dependencies

Require the skin artwork/surface contract and initial production icon mappings from `UiSkinArtworkAndSurfaceCatalog.md` and `UiIconPortraitAndMarkerArtwork.md`. The reference inventory currently records a pending binary import; verify actual source availability rather than assuming filenames exist. Synchronize a reusable existing PR or require prerequisite capability on current `master` before creating a dependent branch.

## Mandatory repository pre-check

Inspect, in this order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; integrated functionality; architecture/projects; existing tests; CI; current documentation and rules. Reuse viable work and avoid duplicate atlas contracts. Start a new branch only from current `master`, never `main`, cached code or an arbitrary dependency branch. Batch focused reads around Assets, AssetCompiler, Graphics, Presentation and UI.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/Architecture.md`, `docs/AssetPipeline.md`, `docs/UiTypography.md`, `docs/CommandInterfaceVisualLanguage.md` and `docs/ReleaseVersioning.md`. Assets owns runtime data contracts; the compiler owns authoring/packing; Graphics owns D3D12 resources; Presentation owns skin composition; UI/input continue using shared geometry and validated actions. Simulation and Headless acquire no UI/graphics dependencies. Use purely technical naming without internal execution or production-tool markers; preserve accurate provenance.

## Concrete implementation tasks

1. Audit existing image, font-atlas and runtime texture support before adding contracts. Define one bounded, versioned skin/atlas contract: stable role/asset IDs, page references, pixel regions, source/logical dimensions, padding, nine-slice insets, supported states, texture semantics and cursor metadata ownership. Reject duplicate or invalid IDs, missing dependencies, out-of-page regions and impossible insets.
2. Extend the existing compiler to produce deterministic runtime UI metadata and texture pages. Reuse supported texture formats and the sealed manifest/container integrity path. Packing order must be stable. Prevent edge/mipmap bleeding through validated padding/extrusion or an explicit no-mipmap policy for the affected pages. Validate compression quality for thin lines and alpha; do not apply world-texture defaults blindly.
3. Load required pages during the appropriate frontend/session preparation, not on every draw. Share resources across surfaces through an explicit owner. Report missing/corrupt required assets through existing startup/session diagnostics; keep an asset-independent emergency fallback. An ordinary missing production icon must not silently become a successful fallback qualification.
4. Add a reusable skin batch supporting ordinary sprites, fixed-corner nine-slice frames, documented edge tiling/stretching, dynamic clipped progress fills and separate state overlays. Handle small rectangles without negative geometry or inverted UVs. Reuse `UiTextBatch` for all live labels and values.
5. Define the alpha contract explicitly and apply premultiplication exactly once when required. Match texture color space, tint, blend state and output space. UI remains unlit and full-resolution after world-resolution scaling; avoid world tone mapping, temporal effects or post-process blur affecting HUD text/art.
6. Batch only compatible contiguous draw commands. Preserve painter order, scissor transitions, text/sprite interleaving and modal layering; sorting globally by texture is not valid when translucent elements overlap. Reuse bounded CPU/upload buffers and descriptors. No source reads, image decoding, resource creation or unbounded allocations in steady-state frame paths.
7. Preserve Graphics resource retirement/fence ownership during startup cancellation, menu/match transitions, resize, DPI changes, minimize/restore and teardown. Do not share a device-owned resource beyond its lifetime or synchronously wait for GPU completion per control. Extend the existing resource owner, not a parallel disposal scheme.
8. Expose actual page/texture bytes, draw counts, vertices, uploads, missing/fallback assets and cache/resource counts. Define measured memory/draw budgets separating core skin, icons and portraits; do not hide unlimited texture growth behind an atlas label.
9. Integrate one representative panel/button fixture through the same geometry consumed by hit testing. Migrate shared plumbing needed by later surface integrations and document the API; leave domain-specific surface migration to its own scope.

## Intended commit boundaries

Separate contracts/validation, deterministic compiler support, runtime loading, skin batch/nine-slice rendering, resource lifecycle integration, tests/fixture and documentation. Commit valid logical increments continuously. Use one PATCH increment per PR relative to current `master`, not per commit; preserve individual commits and do not auto-tag, release or squash.

## Tests and validation

Add round-trip/integrity, deterministic packing, malformed metadata, boundary UV/inset, missing/deleted texture, clipping, alpha/composition, state selection and draw-order tests. Verify repeated menu/match entry does not accumulate descriptors or resources. Exercise cancellation, resize, disposed-owner rejection and missing required skin. Run native readback over bright/dark backgrounds and retain exact images. Confirm text metrics, pointer geometry and headless dependency tests remain unchanged.

## Build and CI requirements

Use current `global.json` and Microsoft.Testing.Platform. Run restore, Release build, project-reference validation and `Build-RuntimeAssets.ps1 -Mode development -NoBuild`. Use targeted tests under `tests/ForgeLine.Graphics.Tests`, `tests/ForgeLine.Presentation.Tests` and existing asset/compiler test projects first; discover exact test names in current source. Then run `dotnet test --solution ForgeLine.sln --configuration Release --no-build` and required CI. Record native tests not run separately. The observed baseline fence-timeout is not permission to disable lifecycle gates or claim this feature fully qualified.

## Branch and pull request strategy

Reuse a synchronized relevant PR or start `ui/compiled-skin-rendering` from current `master`. Inspect current-head CI and fix regressions introduced here. Keep draft while required capability, validation or review is incomplete; no merge with failing/pending required checks.

## Documentation and recovery

Update Asset Pipeline, architecture ownership, UI visual contract and public diagnostic meaning. Checkpoint base/head SHA, contract version, completed commits, source/runtime hashes, exact tests/results, pending native evidence, blockers and next action in the PR. Resume from verified remote state; do not duplicate a partially integrated compiler or renderer.

## Definition of done

Compiled production skin assets render through the existing device with correct scaling, clipping, text, state and ownership behavior. Source/runtime integrity and Headless separation hold; the fixture and relevant tests pass; performance and resource counts are measured.

## Concrete completion criteria

Report exact contract/source paths, supported draw/state behavior, compiled asset identities, measured resource costs, passed/failed/not-run checks, lifecycle findings, commits and PR. No source-format dependency in the player and no whole-board screenshot used as a functional control.
