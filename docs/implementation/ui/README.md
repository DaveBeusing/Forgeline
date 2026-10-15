# Graphical UI and HUD Implementation

## Delivery status

This directory contains eight independently scoped implementation requests for the Directorate graphical interface migration. They are plans to execute, not completed game features.

**Original artwork import remains incomplete.** The five expected PNGs are recorded in the [reference inventory](../../../assets/reference/ui/directorate/README.md), but the images themselves have not been committed. Binary transfer was not completed. `catalog.json` intentionally retains `pending_binary_import` and null hashes. Do not report the artwork as present or begin dependent production work on the assumption that inventory entries are real files.

No UI, renderer or gameplay implementation is changed by this preparation. The current visual direction is selected; final production quality and native acceptance are not established.

## Execution order

Use each document as a complete implementation request. Follow capability dependencies rather than starting parallel rewrites of shared surfaces.

| Request | Complete scope | Prerequisite capability |
| --- | --- | --- |
| [UI Skin Artwork and Surface Catalog](UiSkinArtworkAndSurfaceCatalog.md) | Verify/import original PNGs, audit every existing surface and produce clean panel/control/state artwork. | Existing originals; import is explicitly part of this scope. |
| [UI Icon, Portrait and Marker Artwork](UiIconPortraitAndMarkerArtwork.md) | Actual resource/status/action families, real-roster portraits, tactical glyphs and cursor sources. | Verified originals, surface inventory and skin direction. |
| [UI Atlas Pipeline and Skin Renderer](UiAtlasPipelineAndSkinRenderer.md) | Compiler/runtime metadata, shared texture ownership, nine-slice skinning, sprite states, clipping and diagnostics. | Production artwork and semantic mappings. |
| [Graphical Frontend Interface](FrontendGraphicalInterface.md) | All existing menus, Settings, Controls, save/load states, loading, pause, dialogs and errors. | Completed shared skin renderer and assets. |
| [Graphical Battlefield HUD](BattlefieldHudGraphicalInterface.md) | Resources/power, selection/portraits, groups, command shell/states and shared tooltips. | Shared renderer and stabilized frontend integration. |
| [Graphical Production and Operations Interface](ProductionAndOperationsGraphicalInterface.md) | Build/production cards, queues, operations/logistics/power details, current technology and actionable alerts. | Completed battlefield HUD shell. |
| [Tactical Map, Cursors and World Indicators](TacticalMapCursorsAndWorldIndicators.md) | Minimap, permitted contact glyphs, cursor hotspots, selection/order/world indicators and live overlay styling. | Completed shared HUD and operations/alert integration. |
| [UI Visual and Runtime Qualification](UiVisualAndRuntimeQualification.md) | Full surface/state coverage, interaction, DPI/native images, resource lifetime, performance and source-less packaging. | All graphical integrations and source assets. |

New branches start from current remote `master` after inspecting reusable branches/PRs and already integrated functionality. Dependencies must be integrated or intentionally continued in their existing synchronized PR; do not create a chain of stale feature branches. The planning snapshot `563f8c79641cb6f227ae009cc3213e012c4e7fed` is context only.

## Shared ownership boundaries

The artwork requests own production source creation and semantic coverage. The renderer request owns the single shared atlas/skin contract and resource lifetime. Frontend migration owns menu controls. Battlefield HUD migration owns common dock decoration; production/operations migration follows with domain-specific cards. Operations/alert and tactical-map work may touch `RtsInformationHudSurface`, so perform them in the stated order rather than rewriting that surface concurrently. Final qualification repairs specific regressions in the actual owning component.

Every request includes repository pre-checks, scope/non-goals, implementation tasks, small commit boundaries, build/test/CI requirements, documentation, recovery checkpoints and concrete completion criteria. Each PR receives exactly one PATCH increment relative to current `master`; no per-commit increment, automatic tags/releases or squash.

## Visual and gameplay boundaries

The reference boards are not runtime atlases. They contain labels, sample values and noncanonical concepts. Production art must separate frames, fills, icons, portraits, state overlays and cursor metadata from live text and game data. Preserve the canonical tagline, actual roster/resources, existing input behavior and authorized information.

Minimap terrain, resource values, queue progress, network/range geometry, fog of war and hit testing remain dynamic. Procedural geometry required for live shapes, developer diagnostics or failure/bootstrap rendering is an explicit intentional exception, not an instruction to replace the game with static images. Player-facing surfaces outside those justified exceptions must be inventoried and migrated.

## Acceptance and recovery

Follow the [native UI review handbook](../../UiVisualReview.md) and [asset pipeline](../../AssetPipeline.md). Separate original import, source validation, runtime compilation, functional behavior, structural image repeatability, physical display testing, performance and actual reviewer approval.

Record exact commit/source/runtime identities and passed/failed/skipped/not-run states. Preserve interrupted capture roots and resume from verified checkpoints; do not regenerate completed work or manufacture acceptance. The known planning-baseline fence/shutdown failure remains a validation concern, not permission to disable existing gates or prioritize an unrelated redesign over the requested UI work.
