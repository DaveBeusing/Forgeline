# Predictive traffic — implementation in progress

The local finalization adds a canonical all-participant batch, bounded multi-second acceleration/braking/yaw prediction, swept relative crossing checks, physical geometry and reservation vetoes, wait-age priority, a bounded wait-for graph, and one completely swept reverse escape for a stopped cycle. Checkpoint revision 2 retains wait-for and recovery state. Seven targeted regression cases pass, including input-order independence, exhausted work, stale/reused entities, geometry veto, detached checkpoint parity, and clear/blocked reverse escape.

This is not yet wired to the live movement or save/replay owner. Directed passage occupancy/fair release, retained right of way, replan requests, runtime integration and their acceptance tests remain required before this PR is ready. No gameplay or performance acceptance is claimed.

## Status

This module prepares a read-only traffic advisory planner against navigation contract revision 1. It is **not connected** to the simulation and is not a complete predictive-traffic or blockade-recovery implementation. It does not establish right-of-way, multi-tick conflict prediction, lateral escape paths, cycle-breaking fairness, or scheduler replan requests. Those remaining capabilities must not be inferred from the presence of the implementation type.

## Current behavior

`PredictiveTrafficPlanner` accepts one complete copied `NavigationTrafficFrame`, a full `NavigationOrderBinding`, the sole authoritative `INavigationGeometryQuery` and a non-negative work allowance. It returns an advisory `NavigationTrafficDirective` and accounted work. It never modifies an ECS component, transform, cargo, fuel, dock slot or queue.

Incomplete frames, unavailable geometry, missing vehicle ownership and insufficient work are conservative Hold/Unavailable. The planner asks geometry to sweep a short fixed-time predicted step and proceeds only on a complete matching-revision Clear result. It also checks all copied reservations (not just currently occupied slots) and defers to active docking and queue participation; this is not a replacement for authoritative FIFO, dock or collision enforcement. This short first-step check is deliberately not a long-horizon prediction or an alternative-route proof.

The matching binding must be revalidated by central integration against the accepted command generation, current direct/route intent, queue and dock generations, and entity lifecycle. The current type does not authorize movement itself. In particular, `Proceed` is not a substitute for the final authoritative swept movement check.

## State and persistence

`PredictiveTrafficCheckpoint` contains immutable-shaped, detached version-1 vehicle records; `CaptureState` sorts by complete identity. `RestoreState` validates all records before replacement. This prototype retains wait-start tick and reason but does not own physical reservations. Game's dock/queue reservation owner remains authoritative. Integration must reassess save/replay schema and state lifecycle before enabling this planner; new command generations and reservation history must be persisted coherently, not invented from a tick.

## Integration handoff

The central integration owner should connect after a complete frame capture at the start of the movement phase, following the documented navigation contract. Resolve current entity generation and command identity, pass the same immutable snapshot for all decisions, enforce one shared per-tick work allowance, and reject stale directives. A future full planner will require an ordered all-participant tick preparation step for fair bidirectional passage, tracked physical occupancy until rear clearance, cycle analysis, safe escape candidates and rate-limited replan requests. The current per-vehicle `Plan` contract alone cannot provide an order-independent bidirectional lease arbitrator.

The shared contract itself is unchanged. No existing movement/docking/queue/composition/save/CI files are modified except the normal global PATCH increment. There are no new tests and no manually executed automated runs.

## Build and acceptance

Required command: `dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release`. If the SDK is unavailable, record this as **not executed**. No gameplay, timing or performance acceptance can be claimed. Manual testing after combined integration must cover opposing trucks, stationary blockers, crossroads, convoys, dock exits, cancellation while occupied, wrecks, starvation, entity reuse and save/load during an occupied passage.
