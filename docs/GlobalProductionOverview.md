# Global production overview

Open Operations by clicking the resource/power strip, then choose QUEUES. The view shares the existing unit-factory queue and action ring. FOLD keeps only the header and counts; OPEN restores the rows. Hidden panel space becomes available to world input immediately. CLOSE and OPS leave the view through the normal transient-panel lifecycle.

## Reading and acting

The filter cycles All, Selected factories, Infantry, Vehicles, Logistics and Blocked. PAGE cycles six factories; select a row and use NEXT to inspect its retained orders. Rows show factory role/identity, authoritative progress, unit and queue length. INFO cycles summary, one page per required material, and rally detail. Selected-factory progress, status, block reason and rally use the same completed-tick components as the existing action ring. Unsupported unit, invalid facility, missing input, no power, paused and output blocked remain explicit. ETA is N/A: no authoritative remaining-time estimate exists.

Materials distinguish the selected order's requirement, whether its inputs are held, local free stock and all local reservations. KNOWN FREE/HELD is the current filter's retained, deduplicated input inventories. Shared inventories contribute once; reservations reduce free stock. These are factory input stocks, not construction funds or a transport/delivery promise. Inputs are held at production start and consumed through the existing production completion path. Finished orders leave the queue; no historical spending ledger is inferred. Missing inventories are unavailable.

FOCUS moves the camera. SELECT enters the existing factory action ring. CANCEL and PRIO submit the existing request commands; priority cycles High/Normal/Low without interrupting the already active request. Submission is provisional; only a matching session/correlation execution result reports accepted or rejected. The copied queue remains the source of displayed facts. Changes require the original full factory/request identities; held clicks cannot retarget replacement generations or orders.

RALLY selects the owned factory and opens ground targeting. Left click chooses finite ground inside the viewport; right click or Escape cancels. Focus loss, session change, resize, unavailable factory or match completion cancels. UI, minimap and captured pointers do not place a world marker. The existing owner-validated rally command stores the point. This workflow selects a destination, not a guaranteed route: produced units use normal navigation from their real spawn, including its ordinary failure handling. The existing command result HUD reports rejection. Save/replay already serialize the rally and priority commands; no new save schema is introduced.

Tab and Enter use their existing rebound Next Item/Activate bindings in the expanded view. Collapsing releases keyboard ownership. Selection/jump/rally revalidate current owned visible instances; absent or hidden factories can retain owned queue facts but cannot be camera or world-target endpoints. No opposing queues, free transfers, new production rules or serialized command kinds are added.

## Snapshot and cost contract

The optional projection is captured after completed fixed ticks only while Operations requests data (normally 20 Hz). It scans owned factories and requests once each. Stable ordering is active order first, then priority, submitted tick and full entity ID. Output retains at most 256 factories, 16 orders per factory and eight materials; selected factories have retention priority. LIMITED indicates omitted factories/orders. Filter counts/totals cover retained factories. Request/factory total counts are also captured for diagnostics. Source scan time grows with live source counts; the fixed output bound is not a constant-time extraction claim.

Stocks are copied once per unique input inventory. Published lists and nested material arrays are independent immutable copies. The renderer reads bounded arrays and draws six rows using retained buffers; it never reads ECS. Closing/suppressing Operations skips the additional capture. No persistent poller, independent timer or network payload is introduced.

## Validation

GlobalProductionTests covers shared inventory deduplication, immutable copies, one/16/300/1000 owned factories, foreign exclusion, selected retention, current action-ring equivalence, zero power, destruction, closed capture, changed order/generation/session/display, skipped pause-frame gestures, collapsed pointer bounds and execution-result correlation. GlobalProductionRallyTests covers finite ground intent, one-shot emission, invalid terrain/outside viewport, blocking, focus/session/resize/dead-generation loss and right/Escape cancellation. GlobalProductionRenderingTests checks queue/material/collapsed pages, fixed vertex budget and zero warm render allocations. Existing unit-production tests cover actual input reservations, shortage/power recovery, cancellation and normal rally movement. MatchPersistenceTests adds exact save/replay equality for rally and priority.

Run the normal Release solution build, relevant Game/Presentation/Client suites, and the dedicated native inspection:

~~~powershell
ForgeLine.Client.exe --global-production-capture artifacts/global-production-capture assets/runtime
~~~

The capture reuses the native battlefield and compiled HUD renderer with authored owned read-model inputs. Its JSON records assembly identity, device, text/glyph diagnostics, repeatability and HUD-only timing. It covers summary, material and collapsed states at 1280x720, 1920x1080 and 3440x1440, 96/192 DPI, scale 1/2, plus strategic zoom at the reference display. Layout DPI is simulated; it is not physical-monitor or natural-match acceptance. Timings include clear/overlay/text and submit waits/Present, with 64 warmups and 128 samples. Do not interpret them as whole-match frame rates.

See [qualification results and native evidence](GlobalProductionQualification.md) for the measured local scope.
