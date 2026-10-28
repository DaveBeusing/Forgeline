# Offline strategic map audit

Status: implemented; final acceptance follows required CI.

## Decision

Keep map balance diagnostics in the headless Game/tooling boundary. Reuse
BattlefieldDefinition, ScenarioRegistry, NavigationWorld, HierarchicalPathfinder
and BattlefieldBuildableAreaQuery. Schema-1 observations are deterministic and
separate hard invalidity from review warnings. Route, capacity and permission
metrics have explicit units and documented limits; no verdict certifies fairness.

Exceptions retain exact warning evidence and a bounded rationale, cannot waive
errors and fail on stale identities. Full reports bind to compiled map/content/
terrain identities. The tool consumes no match RNG, mutates no world and exposes
no new player read model, command or persistence schema.

## Consequences

Qualification can cover every registered map and disrupted crossing state
without graphics. Full vehicle geometry, building placement, live logistics and
played-match balance retain their existing owners and acceptance requirements.
Rules/schema changes require deliberate documentation and regression updates.
See [audit contract](../StrategicMapBalanceAudit.md).
