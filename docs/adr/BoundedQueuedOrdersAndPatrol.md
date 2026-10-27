# Bounded queued unit orders and patrol

Status: implemented; manual acceptance outstanding.

## Decision

Store deferred military intent in one immutable QueuedUnitOrders component per complete unit identity. A queue holds at most 16 entries and a fixed cursor; global admission is 1,024 units. A command supplies at most 1,024 identities and validates current ownership, controllability, health, movement components and eligible combat-unit role. Physical Cargo/Supply trucks remain excluded.

QueuedOrderSystem runs on the simulation owner in OrderProcessing. It waits for previous accepted work, observes existing completion and activates at most one entry per unit/tick by executing existing single-unit movement/tactical commands. It owns no transforms, path searches, weapon targets, inventories, service queues or opponent policy. Formation and GroundMovement remain the movement authorities.

A replacement sequence takes effect at that phase. A finite append waits for the current order and compacts consumed entries. Full/invalid edits leave prior sequence state intact. Existing normal Move and tactical replacement commands clear old queue state. Stop/Hold and Retreat/recovery take precedence; accepted explicit artillery fire also clears deferred intentions. Resupply suspends progression and reissues the same active entry after actual service completion. Blocked navigation is retained rather than called complete.

A patrol captures the selected unit's real current point and one selected destination, rejecting a degenerate two-meter leg. Later patrol appends extend only a real patrol's bounded cycle. It loops the same array/cursor without new recorded commands or repeated group creation. Queued legs are per-unit; an existing formation finishes first and a later manual formation order replaces queues. Persistent group cohesion through a patrol is not supplied by this change.

Attack entries require current identified faction knowledge on edit and normal tactical validation again on activation. Unknown enemies never supply speculative future positions. Rejected activation stops the sequence visibly; it cannot cause an automatic hidden attack or skip to a new intent. Ownership changes, destruction or invalid movement roles remove the queue.

## Observation and input

Viewport Shift + right click appends Move. Shift while resolving Attack/Attack Move targeting appends that tactical entry. Ctrl + right click starts Patrol; Ctrl + Shift + right click extends it. Existing modal, focus, terminal and session gates remain. Immutable requests cross the normal host/gateway boundary.

The completed selected-order snapshot adds next kind/coordinate, pending count, loop and queue state. Next attack coordinates use current identified contacts only. Render code consumes copied facts. Current/next connections share the previous 1,024-segment cap and 8,192-line budget; pale amber distinguishes next segments.

## Persistence and compatibility

Authoritative schema advances 13 to 14 and payload 10 to 11; envelope 1 is retained. Queue state participates in the normal ECS snapshot/hash. Explicit edits are typed recorded commands; automatic execution is reproduced by fixed ticks. Old authoritative states are rejected rather than silently assigning new queues. New command kinds in older payloads are rejected. No historical hashes, registered catalogs or compiled maps are rewritten.

This decision adds no second movement, combat, logistics, persistence or strategic authority. Fixed storage and traversal bounds do not establish performance improvement. Compilation is not functional, save/replay, native interaction or manual acceptance evidence. Tests/workflows/gates remain unchanged; manual acceptance belongs to the product owner.
