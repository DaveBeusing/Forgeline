# Graphical battlefield HUD

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The player HUD contains the right-aligned resource/power strip, notifications,
minimap, guidance, assigned-group overview, Operations and the existing single/group
action rings. The rectangular ActionBar/Action Dock, global mode bar, contextual
command band and SelectionPanel/selection inspector have been removed from
render composition, pointer capture and layout. They have no hidden hit targets
or reserved rectangles. See [parity and current qualification](HudSurfaceConsolidation.md).

Selection release opens existing ring categories. B/P/U/L/Y/K/H open Build,
Process, Units, Logistics, Supply, Combat and Technology in the same ring.
Without an owned selection, existing global shortcuts open a ring for the copied
available global actions; no new key binding is introduced. Mouse item release
executes its existing action once. Parameters, Next item, Back and paging retain
the established request/controller semantics. Missing sectors, center and gaps
remain world input. Central outside dismissal preserves the original world gesture.

<details>
<summary>On this page</summary>

- [Selection and result information](#selection-and-result-information)
- [Shared responsive layout](#shared-responsive-layout)
- [Rendering and asset ownership](#rendering-and-asset-ownership)
- [Historical resource qualification](#historical-resource-qualification)
- [Subsequent economic content integration](#subsequent-economic-content-integration)
- [Subsequent tactical integration](#subsequent-tactical-integration)

</details>

## Selection and result information

The existing noninteractive tooltip beside an open ring carries authorized copied
single-selection identity, count, health, supply/Fuel/Ammunition, readiness, power,
work/block reason and inventory. Group status uses the cached selected-group
aggregate: total/live/combat counts, eight type counts, covered means, damaged and
unsupplied counts and current formation. Missing coverage says UNAVAILABLE.
Exact accepted/rejected counts and the last authoritative result remain visible.
Keyboard invocation exposes the same status without requiring a pointer position.
Hovering a sector replaces status with its existing action explanation after the
175 ms delay. Closing the logical ring immediately removes its status tooltip.

Tooltips retain session, tick, full entity generation, selection revision and menu
revision guards. They never capture input or mutate simulation. Held gestures,
dragging, placement/targeting and modal state suppress them. Notifications retain
the existing authoritative feedback lifetime; intent is distinct from execution.

## Shared responsive layout

`GameplayHudLayout.ContractVersion` is 4. Its published persistent bounds are
`SafeArea`, `TopStatusBar`, `AlertStack`, `Minimap` and `SecondaryView`.
Notifications begin one capped physical-pixel `Gap` below the resource strip and
fit above the minimap. Their bounds do not change with ring open/close state.
Guidance uses the left top column; group overview and Operations entry use the
remaining left column. Operations owns its own panel/control geometry. The
minimap retains its own map and selector geometry. Ring placement considers the
remaining published obstacles and the actual translated onboarding panel/tab.

Resource names, quantities and existing power state retain the 1064/640 design
width density rules. All seven resources remain visible. DPI and UI scale are
resolved once; clients below 320 by 240 expose no input geometry. Resize, focus,
session and layout changes invalidate retained gestures. Normal HUD has no
FPS/SIM/TPS reservation; F2 and Shift+F1 remain unchanged diagnostics.

## Rendering and asset ownership

`GameplayHudRenderer` composes `ResourcePowerHudSurface`, `PlayerActionRingHudSurface`,
`RtsInformationHudSurface`, `WorldHoverTooltipSurface` and `OperationsHudSurface`.
`PlayerActionRingHudRenderer` retains the existing ring skin and motion policy.
Its geometry buffer is bounded at 4,096 vertices (previous combined renderer:
262,144). Its skin prepares eight icon quads, with no unused panel or portrait
batch. Other owners retain the compiled assets they actually use.

`HudOverlayPrimitives` owns shared overlay shaders and fallback glyphs formerly
nested inside the inspector. `SelectionInspectorHudModel` remains a semantic
status/icon model used by copied tooltips; it is not a rendered inspector.
`GameplayHudSkin` prepares compiled atlas resources on the render owner and
records bounded batches without source-asset discovery during gameplay.
Missing required production assets fail preparation explicitly. Resource/glyph
counters remain collected independently of diagnostic visibility.

Existing 160 ms ring alpha tracks retain fixed geometry. Logical close releases
capture immediately; reduced motion and Low/fallback settle directly. Current
qualification is separate from the source-bound historical measurements below.

## Historical resource qualification

At the initial integration, five participating owners shared a single core page, two icon pages and four
portrait pages per device/catalog signature: 28 MiB total atlas residency. A
frontend holding the same core lease does not duplicate that page. Each owner
uses a bounded recording batch: five core batches at 1,024 quads, two icon batches
at 256 quads, one portrait batch at 64 quads. Thirty-two prepared upload buffers
reserve 4,374,528 bytes in total; no per-frame growth is allowed. The default
`UiSkinBatch` capacity and its existing overflow behavior remain unchanged for
other consumers.

`GameplayHudSkinDiagnostics` deduplicates atlas residency by group, sums buffer
and draw/vertex/upload counters, and records only the surfaces actually rendered
in the current frame. Missing snapshots clear current counters. Empty or invalid
selection clears its own queued skin commands. Glyph counters remain separate.
Missing required production sprites/pages fail preparation explicitly; partial
resources and previously prepared surfaces are disposed. Bootstrap and debug
devices without a production catalog remain independent. There is no silent
production-art substitution or successful readiness report after preparation
failure.

See [qualification](GraphicalBattlefieldHudQualification.md) for exact source,
checks, immutable captures, measured costs and remaining acceptance gates.

## Subsequent economic content integration

[Production cards, operations and alerts](GraphicalProductionAndOperations.md) now use the existing shared skin. The eight-batch figures above describe initial 0.1.377 integration; that economic integration prepared twelve batches / 48 buffers / 5,603,328 bytes with the same 28 MiB shared pages. The linked [economic qualification](GraphicalProductionAndOperationsQualification.md) supplies current source-bound coverage; earlier qualification remains historical evidence.

## Subsequent tactical integration

[Map/cursor/world consumers](GraphicalTacticalInterface.md) now share the same pages. The later tactical integration recorded 13 batches / 52 buffers / 8,749,056 reserved upload bytes; the increase is bounded information-symbol recording capacity. Historical HUD/economic evidence retains its original source and figures.
