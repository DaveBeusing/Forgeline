# Bounded Operations observation

Status: implemented; manual acceptance outstanding.

## Context

The existing selected Operations diagnosis explains stock, production, transport requests and upstream causes at a completed tick. Its milestone journal deliberately coalesces events and cannot establish actual delivered throughput. Repeated blocker labels also do not prove a common cause.

## Decision and ownership

Extend GameplayProgressJournal with an optional bounded receiving-inventory observer on the simulation owner. Successful Cargo Truck unloads and conveyor inventory transfers contribute their actual positive quantity directly, before milestone-event coalescing. The existing inventory operation remains the only transfer authority. No new inventory, reservation, route, queue, repair, dispatch or opponent decision is introduced.

Presentation extraction supplies only the current authorized observation scope and copied blocker/backlog facts. The simulation-owner journal retains those samples, while immutable OperationsObservationSnapshot and OperationsCauseSnapshot values cross the existing completed-tick publication boundary. Render and input consumers access no ECS or inventory state and submit no new commands.

A receiving subject is identified by the current session, player, full EntityId generation, InventoryId and ResourceId. Changed scope, inventory, ownership or extraction continuity requires fresh coverage. The maximum is 128 retained subjects and 600 slots per subject. Actual simulation durations form the rate denominator; unknown wait/backlog samples have independent coverage. New scopes cannot claim a zero delivery rate before a subsequent tick is observed. Unknown or overflowed quantities remain unavailable.

A shared power cause requires an actual network membership, a current network deficit and enabled underpowered owned members. Conveyor groups require the same authorized cause entity, normalized reason and resource. Stable full-generation member ordering, copied membership and release-time revalidation protect existing focus/control navigation. The collector admits at most 64 candidates and 256 member references from a bounded 4,096-instance/network slice. Missing evidence never becomes a healthy state.

## Compatibility

These histories, watch scopes and groups are observations only. They are excluded from authoritative hashes, command journals, save payloads and replay configuration. No new authoritative state or command exists; envelope 1, payload schema 9 and authoritative schema 12 remain unchanged. Historic hashes are not rewritten. Fresh sessions, save restoration and replay observation start new coverage instead of pretending earlier transfers were captured.

The existing milestone and navigation-delivery observations retain their semantics. Neither decision-making nor replay execution reads the new Operations observer. Opening/closing the panel changes observation scope only.

## Consequences and limits

The UI can compare actual received quantities, reported waiting and known remaining policy work. It cannot promise a future supply rate or collapse overlapping requested, reserved, confirmed and loaded quantities into availability. Multiple inventories/resources and intermediate chain hops are separate views.

No complete-base capture, retrospective transfer history, delivery forecast, automatic repair or second economic authority is provided. Coverage/admission limits are visible. The architecture does not establish allocation, latency, GPU or gameplay performance improvements. Compilation alone does not establish behavioral or manual acceptance.
