# Actionable Alerts and Strategic Map

## Current interactive notification stack

FOCUS on an actionable row advances through the current authorized alert identities in
the published priority order, including focusable conditions represented by the MORE
overflow row. The first click starts at the clicked condition. Subsequent FOCUS
clicks advance cyclically, skipping missing targets. Resolution, target-generation
changes, player/session changes and tick rollback invalidate earlier identities.
An OPS button or no usable destination routes to Operations; navigation still
rechecks world visibility and full entity identity before selecting.

Clicking the **message body** (not FOCUS, OPS or MORE) toggles the single in-stack
details region underneath that row. It shows only currently authorized severity,
reason/label, affected counts, onset tick, eligible entity identifier and real
next action; unavailable facts read N/A. Its fixed available height is bounded by
the existing right safe-area notification allocation. Long content uses click-to-
advance pages, never world overflow or inferred event history. MORE continues
to open the full Operations Notifications panel independently.

New live rows advance from the right safe-area edge over 180 ms; the separate
200-ms detail expansion uses an eased vertical track. The body and outgoing
decoration are never treated as newly authoritative. Input, render and ring
obstacle geometry derive from the same copied expansion identity and current
layout. Reduced Motion, Low quality, resize, focus loss and interrupted clocks
settle instead of retaining stale gestures. Active critical conditions stay
visible until resolved, with no timer-based dismissal. Older qualification
screenshots and measurements below are historical; current-source native
D3D12/WARP and human visual approval remain distinct gates.


The alert stack keeps the existing critical/warning/information text, icons and command-result cards. Current Core Destroyed, Core Damaged, Critical Supply, Blocked Production and Constrained Power conditions have at most eight copied records including a confirmed Cargo fleet shortage, current Core Under Attack and observed Work Completed. Their identity is session, player, condition, onset tick and observed cause. Live cleared conditions disappear immediately. Attention resolution requires three seconds of completed healthy ticks; brief same-cause recurrence keeps its identity, with a five-second recurrence cooldown after confirmed resolution. Critical recurrence and a changed cause bypass that cooldown. Fresh sessions or tick rollback reset lifecycle state. Pausing preserves the last completed capture. Terminal alerts remain visible but cannot activate navigation. No history, sound, dismissal command or per-tick popup is introduced.

## Current notification and context-hint presentation

The right-hand notification stack owns authoritative alert cards and local command feedback. The separate left-hand placement hint is rendered as one status row when the authorized placement preview is current; onboarding remains unchanged. Cards have one horizontal, vertically centered icon/text/action flow with measured typography and a one-line ellipsis. A truncated alert exposes MORE alongside the existing FOCUS/OPS navigation when space permits; authorized full content remains available through Operations Notifications. Local contextual messages also use the same bounded row and full-detail path.

The background material uses 0.30 alpha independently from full-opacity severity rails, icons, text and compact action buttons. A bounded 180 ms SmoothStep runs on monotone presentation time for changed/current visual identities. Resolved cards relinquish capture immediately; any outgoing decoration contains only a noninteractive panel, never stale alert text or target details. Reduced Motion, Low mode, focus/pause suppression, clock discontinuity and display/session change settle immediately. Alert prioritization, lifecycle identities and simulation requests are unchanged. Real D3D12 capture and human readability acceptance for this revision must be recorded separately from historical evidence.

 Navigation and relevance

FOCUS means a captured locally owned candidate exists. Clicking focuses and selects it for the existing inspector, after current world visibility, ownership and full entity generation are checked again. The Command Core destination is the configured local core. Supply, factory and power destinations choose the first eligible owned live candidate in stable entity order whose existing component reports the relevant condition. Aggregate counts remain visible. This chooses a representative affected target; it does not claim every contributing cause belongs to that one entity.

OPS indicates no captured spatial candidate. Clicking opens the existing Supply, Power or Blocked operations filter and explains that the target is unavailable. A target that disappeared or cannot be shown receives the same fallback. Operations capture may need the next completed tick; its waiting state remains explicit. Overflow MORE opens Blocked operations. Command-result cards are informational and do not repeat or resubmit commands. Navigation creates no simulation request and does not equate intent with accepted execution.

Operations remains in the left column while the right notification stack stays visible. Pause/focus changes consume retained press edges; targeting and placement suppress alert activation. A short click activates once only when press and release retain the same shared card, action and revision. Resize/session transitions discard activation. Existing action/minimap/world pointer priority is preserved. Compact stacks show fewer cards or a severity-marked MORE summary; resource-strip and contextual Operations entry paths remain available. General dock width and world-input sequencing retain their existing owners.

## Strategic layers

The existing seven-button minimap selector remains OFF, LOG, SUP, SEN, NAV, PWR and ALL. The selected legend gives the full layer name, local captured count and shape key. F10 still cycles layers and F11 still toggles the minimap. Existing map click/drag, right-click orders and tactical targeting remain on their original paths.

Strategic snapshots now copy read-only lists and carry player/session/tick/requested-mode identity. The legend says N/A - UPDATING and world strategic drawing is withheld while any identity differs or a match is terminal. Captured zero local items means zero captured items, not healthy global infrastructure. Unknown enemy infrastructure remains unknown: no markers, sensors or data are added to intelligence. Existing authorized local/observed extraction is reused.

Routes use lines with a cross for disabled links. Route capacity is configured throughput; utilization remains N/A because this layer does not capture current capacity-window telemetry. Supply uses range rings and crosses for critical/offline states. Sensor rings show existing visual/radar ranges, not guaranteed identification or omniscient coverage. Navigation boxes/portal points reflect available observed navigation data. Power remains logical membership without fabricated wires: offline uses a cross and brownout adds a four-line diamond, alongside explicit bounded labels. Shape and text supplement color.

## Costs and qualification

Alert capture runs at the existing completed-tick cadence (20 Hz for canonical matches), scans owned controllable candidates once and retains at most eight records. Source scan cost scales with candidates; no constant-time extraction claim is made. Existing strategic capture and geometry caps are preserved. The added brownout shape adds four lines per retained brownout marker, at most 128 markers in Power or 48 in All. The retained HUD reads copied lists and performs no live ECS query.

`ActionableAlertTests` and `StrategicMapExperienceTests` cover deduplication, resolution/recurrence, session/rollback, destruction, ownership, lifecycle input guards, unavailable destinations, compact geometry, strategic freshness, immutable lists and brownout shape. Existing minimap, display, intelligence and client journeys remain regression gates. `--alert-map-hotpaths artifacts/alert-map-hotpaths.json` measures active-alert full HUD/minimap plus idle alert input at 1/10/100/1000 entities across all seven layers and both dock states, with five active conditions, one
command result and one local navigation result, with 128 warmups and 256 samples. The report includes visible cards, CPU draw/vertex/upload counts. These null
graphics counts are distinct from native atlas draws. Warm managed allocation
must remain zero. CPU/null-graphics timings exclude extraction, world overlay drawing, GPU uploads, waits and Present and do not establish FPS acceptance.

Native startup/window qualification checks composition and material bindings. Live alert-to-inspector/operations journeys, save/load interaction, terrain contrast, tiny-scale text, monitor DPI/focus transitions and steady-state GPU performance require separate manual acceptance. Startup smoke does not claim those results.

Allocation qualification sets `DOTNET_TieredCompilation=0` for the alert/map benchmark process only, together with DOTNET_gcConcurrent=0 as required by the benchmark host, then uses the same 128 warmups, 256 samples and strict zero-byte gate. This holds the compilation policy fixed while sampling; it does not change the client runtime or permit allocations. The report records that setting. Set `$env:DOTNET_TieredCompilation = '0'` before the documented benchmark command for a matching local qualification, and restore the prior environment value afterwards. See [.NET compilation configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

The HUD information update bounds alert cards and their actual pointer regions. Empty rows, row gaps and space right of a card no longer capture world input or open Operations. Critical supply precedes transient command feedback. Minimap controls and its two-line legend fit the actual map size; semantic intelligence sources remain unchanged. See [HUD evidence](BattlefieldHudQualification.md).

## Graphical alert integration

`ResourcePowerHudRenderer` now uses compiled raised cards, semantic icons and warning/error overlays while retaining severity labels/rails, priority, bounded records and exact FOCUS/OPS/MORE hit regions. Notifications sit one shared gap below the visible right action dock. Operations
remains on the left and does not suppress notifications. Command feedback remains separate from work completion. Tactical map/cursor/world geometry retains its existing owner. Long labels wrap using real font metrics, with two lines, ellipsis and MORE.
Operations exposes a Notifications entry even when the stack cannot fit a card;
its bounded detail pages reach the complete current authorized text. A summary-only stack preserves the highest hidden severity with explicit
CRITICAL/WARNING text and the existing marker. There is no notification history. Empty space and gaps remain world input. A held click
requires the same full stack revision, lifecycle identity and target generation
on release. Local command/navigation feedback shares this stack; placement
guidance retains its placement context. See [stack qualification](NotificationStackQualification.md).

See [production/operations contracts](GraphicalProductionAndOperations.md) and [qualification](GraphicalProductionAndOperationsQualification.md).

## Graphical tactical integration

[Production minimap/layer/marker and world feedback](GraphicalTacticalInterface.md) now uses the shared atlas over live geometry. Contacts keep faction-filtered identity and stored-position rules; map frames do not alter click transforms. Camera-footprint rays remain presentation geometry. Ranges/routes/coverage/placement retain their existing authority and shapes. [Source-bound qualification](GraphicalTacticalInterfaceQualification.md) records native pixel repeatability, copied private-fact exclusions, cursor hotspots and remaining gates.

## Minimap hover and alert coexistence

The minimap grows only within free safe-area space, preserving the right-aligned resource/notification stack and the lower-left secondary region. The currently resolved action ring also limits expansion, while action-ring placement reserves the published map bounds. The map responds to pointer hover with 180 ms reversible motion (up to 1.50 times baseline where space permits); the same published map geometry drives selector clicks and map input. Alert cards keep their existing precise hit regions and priority, and no new intelligence or gameplay command channel is introduced.

The hover controller exposes the computed maximum scale for geometry diagnostics; development diagnostics report a changed HUD occupancy cap instead of forcing the requested enlargement. Existing alert, ring and safe-area constraints always override the decorative target, and no additional map texture is created by hover alone.

## Contextual Operations access

The former persistent OPERATIONS tab, its left-column hit rectangle and the related layout reservation have been removed. Resource/power entry, actionable OPS/MORE alerts and the optional Operations panel's current filters, notifications, navigation and CLOSE affordance remain supported. Empty former-toggle pixels are world-input space unless another visible HUD surface owns them. Strategic overlay selection and F10/F11 behavior retain their minimap selector and legend, including the current layer name, authorized local count and symbol key; no independent overlay-status widget was found in this revision.

## Sustained cargo fleet capacity warning

`INSUFFICIENT CARGO TRANSPORT CAPACITY` is a typed, player-scoped logistics warning, not a restatement of total queued demand or the `NoTruckAvailable` metric. Its eligibility requires a still-enabled stock policy for an owned, live destination, a dispatch rejection that already established usable source surplus and a structural logistics route, and a currently productive owned Cargo fleet that is fully occupied. Available idle cargo capacity, fuel service/rescue, route/navigation failure and a blocking physical FIFO queue suppress the inference. A fleet with no working trucks does not trigger a misleading instruction to build more trucks in place of diagnosing why existing trucks cannot work.

The complete condition must persist for ten seconds of completed simulation ticks without a shrinking backlog. Clearing uses three seconds of hysteresis followed by a five-second recurrence cooldown. The existing alert identity is `(session, player, CargoFleetShortage, onset tick)`; reappearance gets a new identity, rollback and session changes reset evidence, and the UI receives only authorized copied observations. The warning exposes `BACKLOG`, `ACTIVE`, `IDLE` and `EFFECTIVE` counts plus a valid owned affected facility for FOCUS; it does not invent a number of additional trucks or delivery ETA. Details suggest inspecting LOGISTICS orders and assessing whether to build more Cargo Trucks once fuel, source availability, route and bay issues are ruled out. No trucks are automatically created.

The existing alert stack, MORE overflow, full Operations Notifications detail and shared hit geometry are unchanged; the new warning is ranked after production warnings but before lower-priority constrained-power information. Tests cover sustained evidence, resets, normalization and the separate causal rejection conditions. Native contrast, physical mouse/DPI/monitor qualification and save/replay/recovery journeys remain open until explicitly observed on a source-bound build.

## Operational attention controls

Actual positive DamageApplied events targeting the configured, living owned Command Core
raise CORE UNDER ATTACK immediately, even before its health crosses the existing CORE
DAMAGED threshold. The row persists for five simulation seconds after the latest hit.
It copies neither attacker identity nor attacker position. Core Destroyed, Under Attack,
Core Damaged and Critical Supply precede command results and routine industrial notices.
The existing three-row stack and severity-marked MORE summary remain bounded.

Owned production/supply/power observations carry an aggregate cause fingerprint. Multiple
facilities with the same current cause remain grouped; No Input, No Power, Output Full,
Paused, Brownout/Offline and Unsupplied changes update the incident rather than borrowing
an unrelated onset. Existing shared-power groups and safe facility cycling remain in use.
The eight-category snapshot also exposes at most eight Started, CauseChanged or Resolved
transitions for the current completed tick. These are adapter output, not a persistent log.
No UI controller mutates a simulation entity or issues a gameplay order while navigating.

Settings → General offers NOTIFICATIONS (ALL / BATCHED / QUIET) and INFORMATIONAL ALERTS.
ALL is the backward-compatible default. BATCHED accumulates observed unit/building
completion-counter deltas for five simulation seconds before publishing one completion
summary; ALL reports them immediately and coalesces within the visible five-second burst.
The first capture after session/load/rollback establishes a baseline and invents no past
completion. QUIET suppresses warning/information rows in the compact stack, while every
critical condition and command acknowledgement remains available. INFORMATIONAL ALERTS
only controls Work Completed rows. Raw current facts remain available in Operations in
all modes. These visual preferences do not alter existing audio volume or mute settings.
APPLY persists and applies to the running match at the next completed capture; BACK
discards the draft. Existing display, graphics, camera, input and audio choices survive.

Settings → Bindings exposes NEXT ALERT. Its default is UNBOUND to preserve saved global
keys; assigning a free key enables one retained-tap activation through the existing FOCUS
route. Repeated presses cycle current authorized targets in priority order, including
conditions behind MORE. Short Ctrl/Shift chords are excluded even when the modifier was
released before the frame. Modal, pause, focus, session and resize guards remain in force.
Missing/destroyed/recycled targets are skipped; no usable current destination opens the
existing Operations detail fallback. No stale or hidden enemy position is retained as a
camera fallback. F1 developer controls and radial/minimap ownership remain unchanged.

See [the bounded attention decision](adr/BoundedOperationalAlertAttention.md) and
[executed qualification](OperationalAlertQualification.md) for validation and limitations.
