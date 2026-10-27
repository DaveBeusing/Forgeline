# Committed inbound storage admission

Status: implemented for #397.

Admission derives one phase-local view of live cargo orders, keyed by the actual
destination inventory and resource. It subtracts accepted, not-yet-unloaded
quantities from both shared and resource headroom before choosing another truck.
New assignments update the same phase view immediately. Desired but unassigned
requests reserve no destination stock. Local conveyor input uses the same derived
view while continuing to check live inventory and port throughput on every move.

Pre-load orders commit their accepted load bounded by truck capacity. Loaded
orders count physical cargo still on the truck; delivered stock already belongs
to the destination inventory. Rebuilding from full live entity identities releases
failed, idle, destroyed and reassigned orders without a persisted cache or cargo
discard. Existing source, route, Fuel, priority and retry owners remain active.

This changes authoritative admission decisions: schema 17 supersedes schema 16.
Envelope 1 and payload 11 are unchanged. Old saves/replays fail compatibility
validation; historical hashes and map artifacts are unchanged. The capacity view
is derived and is never serialized or treated as inventory.

Focused coverage includes two resources and two real trucks sharing capacity,
destination aliases, resource limits, partial unloading and consumption,
unassigned demand and lifecycle cleanup. Existing distribution and active
Save/Replay cases retain conservation and deterministic continuation checks.
PR CI remains the integration gate; physical client acceptance is separate.

Source retention follows the inventory that a policy fills. A processor input
policy cannot retain materials from its distinct finished-output inventory.
Policies sharing an actual source inventory keep the maximum applicable target
and existing priority ordering. The same schema-17 admission compatibility
boundary covers this correction (#398).
