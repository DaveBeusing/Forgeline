# Paid construction templates

Status: implemented; manual user review outstanding.

## Decision

Offer exactly two authored, client-session designs with two relative, cardinally
rotated buildings and one explicit OUT-to-IN relation. Fixed menu identities
3001/3002 are intercepted by placement and never enter the building definition
catalog or SubmitBuild. Storage and Smelter are the only building families.
No generic editor, copying of arbitrary entities or external-attachment inference.

Choose sequential partial acceptance before implementation. Preview reserves
nothing. The initial whole design must be valid and quote affordable combined
unreserved stock. Application issues one ordinary command at a time; each step
revalidates and pays normally. Building acceptance reserves that building's
costs through the existing processing system; completion consumes them through
its existing lifecycle. The route waits for both exact sites to complete and
pays its ordinary actual Steel cost. No transaction, rollback, free object,
shared reservation or independent construction scheduler exists. Failure stops
unsent steps without retry. Accepted work remains under normal authorities.

## Authority and bounded work

Simulation capture produces a detached two-building preview, two cost lists,
one route and three-state result presentation. Proposed endpoints enter a
read-only overload of the same bounded automatic route planner and completion
validator; they never enter an execution path or synthetic ECS registry.
Cross-check future footprints/loading lanes against each other and all proposed
route pieces. Existing live terrain, roads, docks, ports and stock remain inputs.
The ordinary execute API still resolves only completed owned real endpoints.

At most two site identities, three local results, one outstanding host command
and one preview request exist per placement controller. Existing automatic route
candidate, route length, support and global conveyor limits are retained.
Presentation sends ordinary commands through the session-bound host/gateway.
Construction feedback now copies CreatedSite from the accepted Build result;
it is an observation with the complete generation, not another site authority.
Correlation, session and completed-tick request identity prevent unrelated
results or stale locations from continuing a plan. No hidden enemy data is used.

## Interruption and persistence

Focus/modal/pause/display/session interruption and explicit cancellation stop
unsent work. An already submitted command is not retracted; normal feedback
still reports it. No late result can reactivate a cancelled controller. Loading
or restarting never reconstructs local continuation.

No new authoritative state or replay command is introduced. Accepted ordinary
Build and Conveyor commands use existing serialization and normal reservation,
site and infrastructure state. Envelope 1, payload 11, authoritative schema 14,
registered map/catalog identities and historical hashes remain unchanged.
A partially applied save retains accepted work while discarding unsent intent.

## Tradeoffs and delivery

The two presets do not contain recipes, stock policies, power generation,
extractors, generic edits, external ports or automatic terrain changes.
The UI distinguishes unsubmitted draft, queued request, accepted site,
completed endpoint, paid link and rejected/skipped/unsent outcomes.
Actual costs/routes may change while construction runs; no preview promise
overrides the later authoritative command.

No tests, smoke, benchmark, native qualification, workflow dispatch or rerun
are created or executed for this change. Existing gates stay unchanged.
Product compiler evidence and manual acceptance limitations belong in the PR.
See [construction controls](../BuildingPlacementAndConstruction.md#paid-construction-templates).
