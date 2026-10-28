# Explicit engagement policies and saved control groups

## Decision

Reuse FirePolicyState and the existing tactical command gateway for Hold Fire, Return Fire and Fire At Will. Apply edits to at most 1,024 requested identities, with normal ownership, controllability, live health and direct-weapon eligibility. Keep individual orders, formation routes, queue cursors, resupply and artillery missions intact. Hold Position and Attack Move retain their existing movement authority; changing fire policy never manufactures an advance, cancels an explicit stop, or rejoins detached members.

Return Fire uses only actual damage-derived retaliation plus the normal current-intelligence checks. Switching policies clears retaliation authorization; repeating the same policy preserves legitimate retaliation. Direct weapon targets forbidden by the new policy are cleared. Artillery fire missions remain explicit separate actions; direct-fire policy does not bypass setup, range or ammunition requirements and does not cancel an existing mission.

The existing ten numbered control groups remain local-player selection organization. They are not simulation armies or navigation owners. Save payload 12 adds optional bounded player/slot/label/full-generation-member metadata (ten slots, 1,024 members per slot, 24-character labels). The client transfers it once into the restored session, after an owned completed-tick member snapshot is available. It intersects every saved identity with current owned live eligibility. Foreign, destroyed and reused entity generations cannot reappear. New games still clear groups; overlapping saved slots are allowed.

## Compatibility and consequences

Policy operations are encoded in the existing Tactical replay kind. Payloads before 12 cannot contain them or control-group metadata; otherwise compatible old payloads still load. Envelope 1 and authoritative schema 18 are unchanged: the existing generic component checkpoint already captures FirePolicyState, and presentation organization does not affect the authoritative hash. Optional null metadata is omitted. Metadata errors fail persistence preflight; restoration does not grant command authority.

Completed-tick group facts copy only owned policy/order/readiness/supply data. Selection status reports common or mixed policy and objective alongside existing component-conditioned readiness and supply averages. The existing Combat ring exposes the three policy actions, using current selection-identity validation and paging. Unarmed selections show NO ARMED UNITS. No keybinding editor or navigation algorithm changes.
