# Production and Logistics Operations

The optional OPERATIONS tab sits above the selection inspector. Click it to open or close a nonmodal overview. Clicking the resource/power strip opens All Facilities; clicking an alert opens Blocked; clicking the single selected facility's inventory, supply or work inspector opens its relevant category. The world, minimap, inspector and existing Action Dock remain available. No keyboard binding is added.

## Completed-tick authority

Operations data is extracted only while requested, after the authoritative fixed tick. `OperationsSnapshot` carries the simulation session, completed tick and player identity; rendering and drill-down reject mismatched session, tick, player and terminal data. UI code does not query ECS or advance production/logistics state. Full entity generations identify facilities and navigation endpoints.

Extraction considers live controllable entities owned by the local player. Depleted entities and foreign facilities are excluded. Resource totals cover deduplicated referenced inventories of those facilities, storage, cargo and supply providers; a shared inventory contributes once. These totals include stored cargo, not only construction stock, and therefore have a different scope from the ordinary resource strip. Missing inventories and component-specific telemetry are unavailable, not fabricated zeroes.

Net flow is explicitly N/A: current facility/inventory contracts contain stock and cumulative production facts, not authoritative per-resource flow counters. Inventory transfers must not be represented as newly produced resources. No universal currency, rate inference or per-unit battlefield inventory table is introduced.

Queues count retained production and unit-production requests, including the active request. Facility statuses and block reasons are copied from the authoritative components. Disabled supply/hub states remain explicit. Power demand/allocation and configured generator capacity are separate facts; GENMAX is rated capacity, not measured output. Consumer Offline/Brownout is a reported state. Facility NoPower is the existing production block reason.

## Bottlenecks and navigation

Filter cycles through All, Production, Logistics, Supply, Power and Blocked. Power includes production facilities carrying power telemetry. Next Page cycles through six rows per page. Reported bottlenecks receive retention priority; total facilities and listed facilities are distinguished.

Selecting a row shows reported explanations for NoInput, NoPower, OutputFull, Paused and supported transport failures. A combined input/transport reason describes two observations; it does not prove an undocumented upstream cause. Current-tick distribution request failures may report NoRoute, CapacitySaturated, NoSourceSurplus, DestinationFull or NoTruckAvailable. No links alone does not imply a failed route or saturation.

Only links whose two endpoints are live locally owned nodes are retained. LISTED LINKS and DISABLED are counts within the retained route subset; NODE CAP/S is configured node throughput, while LOAD is current capacity utilization when its snapshot matches the completed tick. Missing load shows N/A. Focus Next cycles through retained adjacent endpoints. Facility/endpoint camera targets are revalidated against current authorized world render instances; missing, destroyed, hidden or stale-generation targets cannot be focused. Camera focus changes no orders.

Open Existing Controls selects the authorized facility and enters the existing Process, Units, Logistics or Supply Action Dock. Its existing pause/cancel, queue mode/priority, unit priority, stock thresholds/priorities and supply policy controls remain authoritative through the normal command gateway. The dock waits for an exact matching selection capture before any manipulation. Opening controls submits no simulation command, and submission intent is not accepted execution. Saved combat groups are unchanged.

## Input and bounded costs

Rendering and hit testing share the same safe-area/DPI bounds. The panel occupies the left column above the inspector, preserving the right Action Dock and minimap. The outer HUD geometry stays unchanged; guidance reserves a tab band only where two compact guidance lines still fit. When that band collapses, the tab is omitted and contextual entry remains available. Retained mouse press sequences recognize short clicks once. Resize, session, modal, focus, pause, placement and targeting transitions consume edges; skipped help/pause frames explicitly cancel retained dashboard clicks. Targeting suppresses the panel and its extraction request, then restores the open preference on return. The panel takes no keyboard focus; entering existing controls preserves their ordinary keyboard ownership.

While open, extraction runs at the existing fixed-tick cadence (20 Hz in the canonical match); there is no independent dashboard timer. It scans owned candidates, requests, referenced inventories and existing topology/capacity facts. Work and temporary storage scale with source topology and requests, including the existing ordered edge accessor. Output retains at most 256 facilities, 256 local routes and 32 resource types. Totals can exceed these limits. Renderer/input work uses those bounded copied lists, six visible rows, retained upload buffers and fixed glyph storage; it never rescans live simulation data. Closing or suppressing the view avoids the extra operations capture.

## Qualification

`OperationsTests` covers opt-in extraction, inventory deduplication/retention, enemy/dead exclusion, real NoInput/NoPower/OutputFull/brownout, the existing pause command, disconnected/disabled local links, 1/10/100/1000-node topology, tick/session/player rejection, layout/clip space at 96/144/192 DPI and increased scale, retained short clicks, modal/resize/session/targeting cancellation and zero warm rendering/input allocations.

`--operations-hotpaths artifacts/operations-hotpaths.json` measures 128 warmups and 256 samples for 1/10/100/1000 facilities. Full HUD/idle input must allocate zero warm bytes; copied snapshot storage has a fixed bound. The frame-hotpaths workflow runs these gates. Timings use null graphics and exclude ECS extraction, GPU uploads, waits and Present; they are not a frame-rate guarantee.

Native startup smoke qualifies HUD composition and window modes. It does not establish active operations interaction, terrain contrast, text readability on every monitor, live DPI transitions, mixed-route focus journeys or steady-state GPU acceptance. Those remain manual acceptance items until exercised.
