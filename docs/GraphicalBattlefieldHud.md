# Graphical battlefield HUD

The right-aligned status strip and action dock share a versioned physical-pixel
layout. Compact guidance uses the reserved top-left bounds with input capture and
reserves the primary command band. Text fitting accounts for wrapped objective/binding/context
and next-step rows before selecting type size. See the
[qualification and original comparisons](GraphicalInterfaceQualification.md).

The battlefield HUD reads the existing copied `PlayerExperienceSnapshot`, action,
selection and combat-group views. It adds no simulation state, selection members,
visibility decisions, resources or population limits. Layout and input ownership
remain `GameplayHudLayout`, `ResourceStripLayout`, `CombatGroupCardLayout`,
`PlayerActionDockInteractionLayout` and the existing controllers.

## Rendering and asset ownership

`GameplayHudSkin` prepares compiled atlas leases on the render thread, through
`UiSkinResources`. It performs no source-image decode, filesystem discovery,
authoring JSON parse or device creation during recording. `GameplayHudArtwork`
contains explicit bindings derived from the existing Directorate artwork catalog;
qualification compares those IDs with both the packed sprites and real roster
identities. It deliberately excludes unavailable patrol art and cursor families.
Cursor rendering belongs to the tactical interface.

Each owner records its skin before its remaining dynamic geometry and live
`UiTextBatch` output. This preserves opaque domain-card contents above the shell,
severity/selection rails above skin, and readable live values above both. Core
panels are nine-sliced; icons and square native model portraits retain their
aspect. No reference-board pixels, example values or embedded labels are used.

| Surface | Production binding | Preserved copied behavior |
| --- | --- | --- |
| Waiting/resource strip | `panel.resource`; `resource.ferrous_ore/volatiles/silicates/steel/fuel/electronics/ammunition` | All seven existing quantities; compact two-row layout; finite zero versus unavailable; no added rate or population model |
| Power | `panel.resource`, `overlay.warning` | Generation/demand and explicit constrained/stable text |
| Single selection | `panel.primary`, `panel.portrait`, identity portrait, role/status icons | Name, kind/count and permitted health, fuel, ammunition, supply, readiness, power and work |
| State bars | `progress.progress_track`, `progress.progress_fill` | Health/readiness fractions; two supply bars below the fuel/ammunition labels; finite fills clamped to [0,1], unknown fill omitted |
| Mixed/common-type selection | `panel.primary` and semantic selection icon | Existing count/common-type versus aggregate text; no invented primary portrait |
| Selected combat-group card | `panel.primary`, `control.button_compact`, `overlay.disabled` | Composition filters, damaged/unsupplied focus, formation cycling, group focus, covered aggregates and command result counts |
| Assigned group overview | `panel.compact`, button and selected/focus overlays | Existing assigned slots, active/selected flags, copied status/formation/order summaries |
| Primary commands/dock shell | `panel.primary/compact/raised`, `control.button_compact` | Current contextual availability, rebound shortcuts, page/footer controls and collapsed status |
| Dock modes/results | `control.tab`; selected/focus/disabled/hover/pressed/pending/accepted/error/warning overlays | Existing seven mode IDs, pending requests, accepted/rejected/partial results; accepted never means completed work |
| Gameplay tooltip/guidance | `panel.tooltip/compact` | Existing delay, copied permissions, viewport fitting, onboarding option and targeting/drag suppression |

Panel/control/progress/overlay names above have prefix `ui.skin.directorate.`.
Artwork names have prefix `ui.artwork.directorate.` and select existing pixel-size
variants. Portraits are `portrait.unit_<id>.p<size>` or
`portrait.building_<id>.p<size>`, using only a single identified permitted selection
with the corresponding `CommonUnitId`/`CommonBuildingId`. Construction, empty,
mixed and absent identities do not invent a roster portrait. Known enemy identity
and permission to show operational facts remain independent: rendering respects
the existing `HasHealth`, `HasSupply`, `HasReadiness` and `HasPower` flags, without
looking up entities or private gameplay data. Work continues to use the copied
`PlayerWorkSummary`, including its real block reason and percentage.

Shared hit regions follow the revised responsive geometry. Command submission,
readiness/work authority and input confirmation retain their existing behavior.
Production/operations card contents and alerts stay
with their existing owners for the production interface migration. Minimap,
cursor, world markers and dynamic tactical geometry are outside this migration.
Normal HUD surfaces do not draw or reserve FPS/SIM/TPS counters. Detailed
statistics remain available through Shift+F1, independently of resource rendering.
Telemetry collection and emergency bitmap/text diagnostics retain their owners.

## Shared responsive layout

`GameplayHudLayout.ContractVersion` is 2. `TopStatusBar.Right`,
`ActionDock.Right` and `NotificationBounds(expanded).Right` equal `SafeArea.Right`.
The resource strip is at most twice the common dock width and leaves a separate
left column. The dock begins one `Gap` below the strip. Gap is a physical-pixel
value capped at 16; DPI and user scaling are resolved once by `Create`.

`ActionDock` is the bounded expanded capacity. `VisibleActionDock(expanded)` is
the actual expanded or collapsed region; input uses the same collapsed geometry.
`NotificationBounds(expanded)` begins one gap below that visible region and is
clamped above the minimap. The notification renderer retains its current left
stack in this revision; its next migration must consume these published bounds.
`OnboardingBounds` reserves the top-left corner and is consumed by guidance;
collapsible guidance behavior remains with its existing controller.

Resource density shares 1064/640 design-width thresholds between geometry and
rendering. Full resource names retain captions of at least 11 physical pixels and
values of at least 12 pixels across the required matrix. It uses one row or two
rows while preserving all seven quantities and the existing generation/demand
state. Live quantities and labels use separate
font-sized lines; icon/text groups are centered together. Dock tabs and footer
controls center labels and current bindings in separate rows. Header title slots
are symmetric around paging controls. `UiTextBatch.AddCentered` uses logical font
line height, advances and ink overhang, with ellipsis for long lines. General text
alignment retains its existing contract. Compact cards retain progress meters.
No source assets, font atlas or world-resolution policy changes are introduced.

Clients below 320 by 240 expose empty regions instead of invisible input targets.
Resize, DPI and scale resolve the same geometry for rendering and hit tests. See
[responsive HUD qualification](ResponsiveHudQualification.md) for executed checks
and pending native/human evidence.

## Resource and failure contract

All five participating owners share a single core page, two icon pages and four
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

[Map/cursor/world consumers](GraphicalTacticalInterface.md) now share the same pages. Current composed preparation is 13 batches / 52 buffers / 8,749,056 reserved upload bytes; the increase is bounded information-symbol recording capacity. Historical HUD/economic evidence retains its original source and figures.
