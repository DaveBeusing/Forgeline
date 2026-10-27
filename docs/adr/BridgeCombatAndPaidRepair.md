# Bridge combat and paid repair

Status: accepted implementation; manual acceptance pending.

## Decision

Extend the existing StrategicInfrastructureSystem. Normal match initialization equips only RoadBridge crossings (at most the first 32 authored crossings) with neutral Structure combat contracts and 1,200 health. The neutral faction is selected deterministically from the highest unused participant faction value. Bridges have no player ControllableEntity and are excluded from automatic hostile acquisition and the opponents' copied hostile-contact lists. Explicit identified attack and ordinary projectile/artillery damage remain available.

CombatEntityLifecycleSystem retains a destroyed bridge entity and delegates to infrastructure closure. The existing owner disables the real logistics edge, invalidates navigation and retains its stable crossing identity. Bridges use combat/sensor candidate queries outside the movement spatial index: making their hitboxes static spatial entries would obstruct intact crossings. Fords and other scenery do not acquire these contracts.

RepairBridgeCommand enters through the session-bound gateway, carries full identities and an expected bridge revision, and is replayable. The infrastructure system checks a running match, current bridge identification, own live Combat Engineer within 32 m horizontally of the passage, and a live completed own material source within 64 m of that engineer. It pays 120 Steel and 20 Electronics from available, unreserved inventory exactly once. There is one BridgeRepairWork per bridge and at most one bridge job per engineer. Revision saturation rejects further repair submissions rather than wrapping.

Repair uses the existing map restoration duration and single tick loop. The route closes at acceptance and opens only at full paid completion. A destroyed bridge starts with one paid health point so it remains vulnerable during repair. Engineer loss/range pauses progress; its owner may explicitly hand over to another own engineer without repaying. Other players cannot take over the kit. Damage below the expected work health cancels repair and disables the bridge; spent materials are not refunded. Lethal damage follows the normal immediate lifecycle path. Generic unpaid Restore cannot bypass payment for combat bridges.

Presentation publishes copied scalar rows for up to eight currently identified bridges when one owned engineer is selected. It considers at most 32 own storage candidates in stable entity order, preferring an affordable nearby source, then distance and full identity. This limit is visible in the documented behavior: additional stores are not candidates for this command menu. The tooltip includes material source identity, stock, cost, progress and authoritative policy reason. Neutral bridge picking exists only for ordinary identified attack/hover; normal owned-selection filtering still applies.

Cargo order, reservation, inventory, fuel, docking, queue and locomotion owners are unchanged. Topology changes use existing route invalidation and blocked/alternate behavior. No teleport, duplicated cargo or repair-specific transport authority is introduced.

## Recovery

Envelope 1 remains. Payload 10 adds BridgeRepair command kind 17 and expected infrastructure revision. Authoritative schema 13 includes new bridge components and sensing/targeting/lifecycle semantics. Earlier authoritative checkpoints/replays are explicitly incompatible before reconstruction. Map/catalog digests and historical hashes remain unchanged. Raw host crossing commands remain unsupported external replay history.

## Validation limits

Implementation and source integration were inspected through the repository connector. The exact current source tree is absent from the available local mirror, so affected product projects could not be compiled here. No tests, smoke, benchmarks, native qualification, workflow dispatch or reruns were performed. Existing tests/gates are unchanged. Automatic PR CI, when available, is separate evidence.

Manual acceptance must cover both authored bridges, compatible/incompatible weapons, fog identification, real navigation/logistics closure, cargo already travelling, reserved/insufficient stock, simultaneous stale submissions, engineer handover/pause, re-damage, completion and Save/Replay continuation. These are outstanding user checks, not claimed results.
