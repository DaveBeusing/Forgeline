# Graphical production and operations interface

Smelters, Refineries and Electronics Plants now process local raw materials automatically while powered and able to store output. Their action rings and contextual commands omit PROCESS, and its shortcut does not open a redundant production panel. Inspect their state and configure actual material deliveries through LOGISTICS and Operations. Other production facilities retain their existing order controls. See [industrial production](IndustrialProduction.md#automatic-raw-material-processing-in-matches).

The current context and alert presentation uses separate bounded rows: a left placement-status hint (current/stale/pending/invalid/materials) and the right notification stack. Both use independent 0.30-opacity backgrounds, centered single-line content and 180 ms presentation-time easing; existing notification actions continue to access Operations and complete authorized details. The previous tall placement guidance panel is not a background hit target. Onboarding controls, authoritative alert severity, lifecycle, facility actions and request boundaries remain unchanged. Qualification evidence for previous layouts remains historical; current-source DPI and physical-monitor visual review is required.

Current multi-selection controls use the shared [group action ring](GroupActionMenus.md). Rectangular group controls below describe historical parity; the inspector now retains read-only facts and exact command results.

Own single buildings and units now use [world anchored action rings](WorldAnchoredActionMenus.md). Selection release opens categories; the global mode bar and bound keys open the same ring. Click an item to act immediately. Parameters exposes existing priority, mode, thresholds and queue cancellation before activation; Next item selects the edited item. Back / previous / next page remain fixed controls. The inner hole and gaps allow world input; outside clicks dismiss centrally. The inspector remains informational. Group selection temporarily retains the existing rectangular controls. Closed/fading rings own no input; alpha transitions last 160 ms and reduced/Low modes settle immediately.

Rectangular action-card/footer/pagination descriptions below apply to the temporary group/global fallback. Earlier single-object qualification captures are historical; current single-owned controls and evidence are defined by the linked ring-menu specification and qualification report.

Compact Operations uses independent horizontal control fitting and vertical
row/type scaling through `OperationsLayout`. Its panel avoids primary commands,
inspector, action dock and minimap; normal/larger UI scales retain a 14-pixel text
floor. Six-row paging and existing actions remain authoritative. See the
[qualification and original comparisons](GraphicalInterfaceQualification.md).

The command dock now renders its existing domain cards from prepared production atlases. Operations rows/details and actionable alerts use the same skin. The implementation consumes copied read models; simulation rules, input geometry and command validation remain authoritative.

## Surface coverage

| Surface | Production layers | Live content and retained controls |
| --- | --- | --- |
| Construction | compact card control, building portrait, availability overlays | actual building identity, available/required materials, deposit requirement, existing placement request |
| Processing | processing role, queue cards, warning/pending states, clipped track/fill | recipe inputs/outputs, request priority/mode/pause, active progress, input/power/output/inventory reasons, existing pause/cancel/cycle controls |
| Unit production | actual offered/queued unit portrait, queue states and clipped progress | material/technology requirements, production ticks, active versus waiting request, priority, existing queue/cancel/rally controls |
| Logistics | logistics role, card and warning layers | current stock, policy limits, enabled state, distribution status and reported route/capacity failures; existing policy controls |
| Supply | fuel/ammunition/provider roles and warnings | copied quantities/thresholds/provider state, existing automatic policy and resupply request; a request is not an inventory transfer |
| Technology | stable TechnologyId icon, completed/researching/blocked layers | existing four technologies, facilities/power/materials/prerequisites, progress and start/cancel controls |
| Tactical cards | existing command role artwork on the same cards | existing supported combat actions only; world, cursor and minimap geometry retain their owners |
| Operations | primary/raised/inset panels, category icons, selected/disabled/warning controls | six-row pages, filters, selected facility, causes, stock/power/load/rated capacity, current authorized focus/open-controls navigation |
| Alerts | raised panels, severity overlays, semantic icons and structural rails | existing Core/Supply priority, bounded records, counts, command results and FOCUS/OPS/MORE |
| Domain tooltips | tooltip frame with live domain-state overlay | existing bounded descriptions, real costs, prerequisites, reasons and hotkeys; existing delay, clipping and privacy guards |

`PlayerActionDockHudRenderer`, `OperationsHudSurface`, `ResourcePowerHudRenderer` and `WorldHoverTooltipSurface` retain their existing ownership. In current code the alert cards belong to the resource/power owner, rather than the tactical information renderer. No second alert implementation is introduced.

## Artwork and render order

`GameplayHudArtwork` resolves copied building/unit identities to `ui.artwork.directorate.portrait.building_<id>.p{48,64,96,128,256}` and `portrait.unit_<id>.p{48,64,96,128,256}`. No display-name lookup or simulation query occurs. Construction cards use the offered BuildingId; queued units use each request's UnitId. Unknown identities do not invent portraits.

`EconomicCardVisual` binds TechnologyId 1/2/3/4 to `building_role.processing`, `unit_role.logistics`, `unit_role.armor`, and `unit_role.reconnaissance`, respectively. These match the existing artwork catalog; unknown future IDs get neutral status art. Operations category roles are `building_role.factory`, `unit_role.logistics`, `building_role.supply`, `building_role.power` and neutral status art. Existing RtsUiIcon bindings supply the other roles and their 16/24/32/48/64 variants. Production queue entries use processing art rather than an unconditional alert triangle.

Frames use `ui.skin.directorate.control.button_compact`, `panel.primary`, `panel.raised`, `panel.inset`, `panel.portrait` and `panel.tooltip`, with existing `overlay.*` and `progress.progress_track` / `progress.progress_fill` sprites. No source, definition, atlas or compiled asset is changed. Reference sheets remain reference-only.

The owners record skin, remaining dynamic geometry, then live text. Operations renders only its on-demand main panel with a 0.30-opacity alpha-blended skin background; the former fully opaque underlay and permanent OPERATIONS tab are no longer present. Its header/inset/control backplates use bounded alpha, while text, category icons, status and selection rails are independently rendered for readability. The fixed 180 ms reversible panel-skin transition never changes layout or input hit geometry. Alert drawing continues in the right stack while Operations owns its separate left panel. No decorative hit region is added. Shared resources and optional catalog-free bootstrap paths retain their original lifetime rules.

Below the track's authored twelve-pixel height, the complete progress sprite is resampled so compressed nine-slice borders do not remove its centre. Filling clips actual width/UVs and never stretches a fraction across the entire track. Normal cards place progress above selection decoration; compact cards retain numeric progress and avoid overlaying cost text.

## Work state and authority

The Supply editor tolerates absent action snapshots, missing supply data and
snapshots rejected by the existing tick/selection checks. These states emit no
supply priority, policy or resupply request. Once valid copied data returns,
thresholds, automatic policy and priority are refreshed even for the same entity.

Command availability is independent from work state. Pending means awaiting a result. Accepted/partial/rejected feedback remains copied command feedback, never production completion. A retained request can be queued, paused, started or blocked; its own active flag and the facility's actual block reason determine treatment. Production-skin cards never borrow active work progress for inactive requests. Blocked work remains visible while supported cancel/priority controls remain usable.

Technology completion is shown only for copied `PlayerTechnologyState.Completed`. Processing/unit read models do not retain completed-job history; removed requests disappear rather than creating invented completed cards. There is no new reorder/drag-and-drop capability or fabricated queue capacity. Empty/current-context messages and the actual item count remain explicit; empty Operations rows do not gain actions or frames.

Action data must match the snapshot session/tick. Operations additionally validates player, experience tick and terminal state through `OperationsSnapshot.Resolve`. Selection changes and missing/destroyed facilities resolve from the current copied list each frame. Renderers retain no actionable entity targets. Existing controllers continue to validate full entity generations and permitted navigation.

Stock, demand/allocation, configured generation capacity and measured utilization retain their separate meanings. Absent/non-finite telemetry displays N/A; flow remains N/A because no flow counters exist. Network geometry, remote transfers, new recipes, future research nodes and railway mechanics are not introduced.

## Bounded resources and qualification

The composed HUD shares seven atlas pages (28 MiB): one Core, two Icons and four Portraits. Twelve bounded batches prepare 48 upload buffers, reserving 5,603,328 bytes: six Core batches at 1,024 quads, four icon batches at 256, and two portrait batches at 64. Pages are shared across surfaces; per-frame recording performs no texture preparation. Operations retains its legacy geometry upload buffer only for fallback primitives and foreground rails; no opaque underlay is uploaded.

`EconomicInterfaceSkinTests` covers work/availability independence, shortages, output/power/inventory blocking, queued versus active units, cancel-only controls, technology completion/identity, missing routes/providers, stale tooltips, current Operations selection, zero warm allocation, native thin/selected progress visibility and input ownership independent of decorative background opacity. Existing Operations, alert, tooltip, strict bitmap geometry and client tests remain in place. The complete CI job remains required.

See [source-bound qualification, package and remaining gates](GraphicalProductionAndOperationsQualification.md). Automated repeatability does not grant human art, readability, accessibility or physical-monitor acceptance.

## Notification details

The version-3 notification stack consumes the visible right dock bounds. Operations
and notifications remain simultaneously visible without overlapping. The header's
Notifications control opens current authorized alert and command text, including
content hidden by stack overflow. At most six cached lines are retained per detail page at a minimum of 14 physical pixels following dock content scale;
compact panels reduce visible line count instead of font size.
Next Message Page cycles all text and Facilities returns to the existing facility
view. Current snapshots replace resolved/expired facts instead of keeping history.
See [notification qualification](NotificationStackQualification.md).

## Contextual Operations panel transition

There is no permanent Operations toggle, reserved band or residual input surface. The Resource/Power strip and actionable OPS/MORE alerts remain contextual entry points; the opened panel retains its own header and CLOSE control. The panel's full real rectangle is interactive only while logically open. The panel skin transitions from/to the current presentation alpha over 180 ms using shared SmoothStep, but close immediately discards actionable content and its capture. The open panel retains one shared `OperationsLayout` geometry for rendering and input; the discarded strip has no geometry owner; the UI does not modify fixed-tick gameplay authority, facility data, notifications, or production routes. Reduced Motion/Low quality, invalid display geometry, session changes, focus loss or interrupted clocks immediately settle the logical target. Earlier opaque-panel screenshots remain historical rather than current qualification evidence.
