# Actionable Alerts and Strategic Map

The alert stack keeps the existing critical/warning/information text, icons and command-result cards. Current Core Destroyed, Core Damaged, Critical Supply, Blocked Production and Constrained Power conditions have at most five copied records. Their identity is session, player, condition and onset tick. Repeated completed ticks keep the identity; resolution removes the record, recurrence starts another identity. Fresh sessions or tick rollback reset lifecycle state. Pausing preserves the last completed capture. Terminal alerts remain visible but cannot activate navigation. No history, sound, dismissal command or per-tick popup is introduced.

## Current notification and context-hint presentation

The right-hand notification stack owns authoritative alert cards and local command feedback. The separate left-hand placement hint is rendered as one status row when the authorized placement preview is current; onboarding remains unchanged. Cards have one horizontal, vertically centered icon/text/action flow with measured typography and a one-line ellipsis. A truncated alert exposes MORE alongside the existing FOCUS/OPS navigation when space permits; authorized full content remains available through Operations Notifications. Local contextual messages also use the same bounded row and full-detail path.

The background material uses 0.30 alpha independently from full-opacity severity rails, icons, text and compact action buttons. A bounded 180 ms SmoothStep runs on monotone presentation time for changed/current visual identities. Resolved cards relinquish capture immediately; any outgoing decoration contains only a noninteractive panel, never stale alert text or target details. Reduced Motion, Low mode, focus/pause suppression, clock discontinuity and display/session change settle immediately. Alert prioritization, lifecycle identities and simulation requests are unchanged. Real D3D12 capture and human readability acceptance for this revision must be recorded separately from historical evidence.

 Navigation and relevance

FOCUS means a captured locally owned candidate exists. Clicking focuses and selects it for the existing inspector, after current world visibility, ownership and full entity generation are checked again. The Command Core destination is the configured local core. Supply, factory and power destinations choose the first eligible owned live candidate in stable entity order whose existing component reports the relevant condition. Aggregate counts remain visible. This chooses a representative affected target; it does not claim every contributing cause belongs to that one entity.

OPS indicates no captured spatial candidate. Clicking opens the existing Supply, Power or Blocked operations filter and explains that the target is unavailable. A target that disappeared or cannot be shown receives the same fallback. Operations capture may need the next completed tick; its waiting state remains explicit. Overflow MORE opens Blocked operations. Command-result cards are informational and do not repeat or resubmit commands. Navigation creates no simulation request and does not equate intent with accepted execution.

Operations remains in the left column while the right notification stack stays visible. Pause/focus changes consume retained press edges; targeting and placement suppress alert activation. A short click activates once only when press and release retain the same shared card, action and revision. Resize/session transitions discard activation. Existing action/minimap/world pointer priority is preserved. Compact stacks show fewer cards or a severity-marked MORE summary; resource and Operations entries remain available. General dock width and world-input sequencing retain their existing owners.

## Strategic layers

The existing seven-button minimap selector remains OFF, LOG, SUP, SEN, NAV, PWR and ALL. The selected legend gives the full layer name, local captured count and shape key. F10 still cycles layers and F11 still toggles the minimap. Existing map click/drag, right-click orders and tactical targeting remain on their original paths.

Strategic snapshots now copy read-only lists and carry player/session/tick/requested-mode identity. The legend says N/A - UPDATING and world strategic drawing is withheld while any identity differs or a match is terminal. Captured zero local items means zero captured items, not healthy global infrastructure. Unknown enemy infrastructure remains unknown: no markers, sensors or data are added to intelligence. Existing authorized local/observed extraction is reused.

Routes use lines with a cross for disabled links. Route capacity is configured throughput; utilization remains N/A because this layer does not capture current capacity-window telemetry. Supply uses range rings and crosses for critical/offline states. Sensor rings show existing visual/radar ranges, not guaranteed identification or omniscient coverage. Navigation boxes/portal points reflect available observed navigation data. Power remains logical membership without fabricated wires: offline uses a cross and brownout adds a four-line diamond, alongside explicit bounded labels. Shape and text supplement color.

## Costs and qualification

Alert capture runs at the existing completed-tick cadence (20 Hz for canonical matches), scans owned controllable candidates once and retains at most five records. Source scan cost scales with candidates; no constant-time extraction claim is made. Existing strategic capture and geometry caps are preserved. The added brownout shape adds four lines per retained brownout marker, at most 128 markers in Power or 48 in All. The retained HUD reads copied lists and performs no live ECS query.

`ActionableAlertTests` and `StrategicMapExperienceTests` cover deduplication, resolution/recurrence, session/rollback, destruction, ownership, lifecycle input guards, unavailable destinations, compact geometry, strategic freshness, immutable lists and brownout shape. Existing minimap, display, intelligence and client journeys remain regression gates. `--alert-map-hotpaths artifacts/alert-map-hotpaths.json` measures active-alert full HUD/minimap plus idle alert input at 1/10/100/1000 entities across all seven layers and both dock states, with five active conditions, one
command result and one local navigation result, with 128 warmups and 256 samples. The report includes visible cards, CPU draw/vertex/upload counts. These null
graphics counts are distinct from native atlas draws. Warm managed allocation
must remain zero. CPU/null-graphics timings exclude extraction, world overlay drawing, GPU uploads, waits and Present and do not establish FPS acceptance.

Native startup/window qualification checks composition and material bindings. Live alert-to-inspector/operations journeys, save/load interaction, terrain contrast, tiny-scale text, monitor DPI/focus transitions and steady-state GPU performance require separate manual acceptance. Startup smoke does not claim those results.

Allocation qualification sets `DOTNET_TieredCompilation=0` for the alert/map benchmark process only, then uses the same 128 warmups, 256 samples and strict zero-byte gate. This holds the compilation policy fixed while sampling; it does not change the client runtime or permit allocations. The report records that setting. Set `$env:DOTNET_TieredCompilation = '0'` before the documented benchmark command for a matching local qualification, and restore the prior environment value afterwards. See [.NET compilation configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

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
