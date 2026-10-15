# Graphical battlefield HUD

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

No changes to hit regions, command submission, readiness/work authority or input
confirmation are introduced. Production/operations card contents and alerts stay
with their existing owners for the production interface migration. Minimap,
cursor, world markers and dynamic tactical geometry are outside this migration.
FPS/TPS and emergency bitmap/text diagnostics retain their independent paths.

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
