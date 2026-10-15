# Tactical Map, Cursors and World Indicators

## Goal

Integrate the Directorate tactical artwork into the live minimap, strategic legends, cursors, selection/target/order indicators and world-space operational displays while preserving actual map geometry and faction-limited intelligence. Complete implementation, tests, evidence and PR handling through the existing GitHub connection without changing working environment.

## Current situation

Planning inspected `master` at `563f8c79641cb6f227ae009cc3213e012c4e7fed` on 2026-10-15. `GameplayHudRenderer` receives the actual camera, world bounds, information-layer and tactical-targeting views, layout and optional gameplay overlay. `RtsInformationHudSurface` is an entry point for information display. `docs/ActionableAlertsAndStrategicMap.md` and the current HUD documentation describe fitted map layers, shape legends and knowledge restrictions. Reference boards contain marker/cursor ideas, not executable geometry or permissions. Their original PNG import was pending during planning.

## Scope

Migrate minimap frame/control art, live contact/infrastructure/objective sprites, layer/legend glyphs, supported cursor variants, selection/hover/target/order visuals and player-facing world health/supply indicators. Apply the visual language to existing route/range/coverage overlays through reusable sprite/line/material components.

## Non-goals

Do not replace live minimap terrain with the illustrated map, bake fog/coverage into textures, alter navigation or visibility rules, add rail/aircraft systems, create new commands, change approved camera behavior, or make developer debug rendering depend on final art.

## Prerequisites and dependencies

Require verified tactical/cursor source artwork, semantic identity mappings and the shared atlas/skin renderer. Complete HUD and operations/alert migration first; this scope may touch the same `RtsInformationHudSurface` and shared layout helpers after their changes are integrated. Begin from current `master` with those capabilities or deliberately reuse their synchronized existing PR. No competing map/surface rewrite.

## Mandatory repository pre-check

Inspect in order: current remote `master`; relevant branches; relevant open/closed-unintegrated PRs and interrupted work; integrated functionality; affected architecture; tests; CI; documentation and binding rules. Reuse and synchronize viable work before creating a new branch exclusively from current `master`. Do not start from `main`, cached state or an unrelated feature branch. Focus discovery on current information, camera, selection, targeting, input and platform owners.

## Relevant architecture and project rules

Read `docs/ProjectContext.md`, `docs/CameraAndInput.md`, `docs/BattlefieldIntelligence.md`, `docs/ActionableAlertsAndStrategicMap.md`, `docs/CommandInterfaceVisualLanguage.md`, `docs/UiVisualReview.md` and `docs/ReleaseVersioning.md`. Visibility, permitted identity and command results come from simulation/read models. Graphics owns GPU resources; Windows cursor APIs stay in `ForgeLine.Platform.Windows`; Presentation owns visuals. Keep technical/product naming without internal execution identifiers or production-tool markers.

## Concrete implementation tasks

1. Audit the actual map, selection, targeting, order and cursor owners and update the shared coverage inventory. Map only currently supported semantics to production sprites. Preserve existing faction colors and accessibility policy when reference colors differ.
2. Skin the fitted minimap frame and existing controls with shared assets. Keep camera-footprint geometry, terrain, routes, visibility masks, contact positions and interaction transforms live. Decorative frame/shadow pixels must not change the world-to-map transform or steal clicks outside existing interaction bounds.
3. Bind markers to permitted read models. Hidden units stay hidden; detected-but-unidentified contacts use generic art, not type-specific silhouettes or portraits. Preserve explored/visible distinctions and current stale-contact policy. A reference rail symbol remains inactive until a real runtime feature exists.
4. Replace player-facing prototype selection/hover/target/order decoration with clean production components. Scale/rotate/stretch rings or corners from actual footprints and camera projection; do not force a fixed photographed footprint onto different buildings. Keep legibility over bright terrain, darkness, fog and effects without excessive glow or world occlusion.
5. Add graphical frames/fills for existing world health and supply indicators. Keep values, visibility conditions and unit eligibility authoritative. Avoid an independent state cache that can expose stale or enemy-private facts.
6. Integrate supported cursor images with validated hotspots and DPI/scale variants through current platform/input contracts. Preserve pointer accuracy, drag selection, pan, targeting, focus loss and window transitions. Do not display both hardware and software cursors. Restore OS cursor ownership when leaving the applicable client state.
7. Retain dynamic route lines, range/coverage geometry, fog masks and placement footprints; skin their caps, dash patterns, marker glyphs and materials as appropriate. These data-dependent shapes are intentional procedural components, not omissions. Do not derive gameplay range or pathfinding from painted artwork.
8. Preserve pending/accepted/rejected order distinctions and current marker lifetimes. Decorative animation respects reduced motion, cannot move a hit target independently and never changes simulation timing. Use shared bounded batches/resource ownership rather than one texture or draw allocation per unit.

## Intended commit boundaries

Separate minimap frame/controls, intelligence-filtered markers/legends, cursor/platform integration, selection/order/world indicators, overlay styling, regression tests and documentation/evidence. Commit valid logical changes continuously. Apply one PATCH increment per PR relative to current `master`; preserve individual commits, with no per-commit increments, automatic tags/releases or squash.

## Tests and validation

Extend current `RtsMinimapInteractionTests`, information-layer, camera/input, HUD layout, visibility and targeting tests after locating current classes. Test map transforms and click targets after resize/DPI/scale changes, camera viewport bounds, non-square/rotated selection footprints, missing cursor art and hotspot bounds. Prove hidden and unidentified entities cannot leak class/health/position through new visuals beyond the current knowledge policy. Stress repeated marker updates and large visible groups with bounded memory.

## Build and CI requirements

Use the current pinned SDK/Microsoft.Testing.Platform. Restore/build Release, validate architecture references, compile runtime assets, run targeted tests and the complete solution suite plus required CI. Use existing named `Contacts`, `Targeting`, `MinimapStrategic`, `MixedSelection` and supply states for native evidence. Validate close/normal/strategic zoom, compact/1080p/1440p/ultrawide/4K, bright/dark terrain and DPI variants. Physical cursor/monitor behavior requires recorded native testing; WARP geometry alone cannot approve it. Do not suppress baseline GPU shutdown failures.

## Branch and pull request strategy

Reuse a synchronized relevant PR or start `ui/graphical-tactical-indicators` from current `master`. Inspect current-head CI, repair introduced regressions and leave draft while required tests, privacy checks or physical review remain pending. No merge with failed/pending required checks.

## Documentation and recovery

Update map/cursor/indicator documentation and classify deliberate procedural/debug exceptions in the coverage inventory. At each logical commit record base/head SHA, migrated layers/cursors/states, source/runtime hashes, exact test/capture identities, blockers and next action in the PR. Resume from verified current repository state rather than regenerating or remapping completed art.

## Definition of done

Every existing in-scope tactical surface uses production art where appropriate, retains correct live geometry and privacy, preserves input precision, and remains readable at supported sizes without unbounded rendering cost. Debug/failure paths stay usable.

## Concrete completion criteria

Return marker/cursor/indicator coverage, hotspot and privacy evidence, intentional dynamic/procedural exceptions, native captures, resource/draw measurements, passed/failed/not-run checks, review status, commits and PR. Zero static map substitution, zero new gameplay from reference symbols and zero unauthorized intelligence.
