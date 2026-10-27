# Bounded vehicle routing integration

## Decision

Connect the existing revision-3 search/binding contracts to the normal Wheeled
Cargo/Supply strategic path. VehicleRoutingKernel owns only detached resumable
search state against pinned immutable geometry and copied owning-player costs.
HierarchicalNavigationSystem remains the NavigationRequests admission/result
owner. GroundMovementSystem retains all transform commits, continuous collision
vetoes and precise Dock/Queue handoffs.

Use staged sector search, corridor refinement, one-sector expansion and bounded
unrestricted fallback. Reuse EndpointCandidateCursor and CongestionPenaltyMemory.
Check real endpoint connections against terrain and original static bounds using
a conservative rotation-independent vehicle envelope. Keep original intent,
full identities, root/request/service generations and searched cost revisions.
New commands receive a root generation at navigation admission; replans keep it.
A tick or coordinate hash is not used as command identity.

Use one selected-route authorization through the existing fuel authorities.
Repeated destructive fuel queries per candidate and a parallel fuel calculator
are rejected. The first feasible endpoint policy does not activate the prepared
multi-route preference service. Dynamic physical observations do not become
faction-visible costs. Costs use owned observed delays only, with finite
activation, decay, expiry and storage bounds.

## Alternatives and consequences

Synchronous FindPath inside an apparent work slice would hide unbounded inner
work and is rejected for this vehicle path. Legacy formation/military callers
retain their existing path; this decision does not claim their migration.
No new fleet scheduler, route cache, regional publication or storage is activated.
The existing pending component and bounded rotating active work are the temporary
adapter until the separately owned scheduling integration.

The clearance envelope is intentionally conservative; it may reject valid narrow
paths and is not a curvature-optimal route proof. Actual motion remains subject
to the existing exact physical owner. Budget/candidate exhaustion cannot be
reported as Unreachable. Projected endpoints cannot grant arrival or service.
Operational bounds and manual scenarios are listed in
[Connected vehicle routing](../navigation/CongestionAwareRouting.md).

## Save/replay compatibility

Authoritative schema advances from current master 11 to **12**. The routing domain
records counters, fairness, cost ages and complete suspended search state with
pinned penalties; ECS snapshots retain pending/accepted bindings. Recovery uses
the existing command replay and checkpoint comparison rather than installing a
partly restored alternate simulation owner. Earlier authoritative semantics fail
explicitly. Envelope 1 and payload 9 remain intact; historical hashes are unchanged.
No migration fabricates congestion age, search effort or command identity.

## Validation boundary

Affected product compilation is separate from manual operation. No tests or
qualification executions are included. Manual route/service/Fuel and recovery
approval remains pending; compiler success is not a gameplay or performance claim.
