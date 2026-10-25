# Navigation search scheduling and bounded storage

## Implementation and integration status

This change implements an internal fleet search scheduler against the shared
revision-1 navigation contracts, bounded workspace ownership, revision-aware route
result caching, and detached checkpoint state. The shared foundation from
[PR #356](https://github.com/DaveBeusing/Forgeline/pull/356) is part of the synchronized
master `78e889d471435477c6b03ad866a572c64a3f262c`. Recovery #354, technology/rescue #350,
conveyor routing #357 and regional update preparation #361 are already integrated
and are not reimplemented here. The branch carries one PATCH increment from
0.1.474-dev to 0.1.475-dev.

These services are not registered in current gameplay composition. Concrete routing
sessions, authoritative command generations, movement consumption, and central
save/replay wiring remain integration work. The existing routing, graph caches and
local-search pool retain their current behavior until their callers are migrated.
Compilation does not establish gameplay acceptance, replay equivalence or a measured
performance improvement. See the [handoff](NavigationSearchScheduling.Handoff.md)
for validation and the next integration steps.

| Owned location | Responsibility |
| --- | --- |
| `src/ForgeLine.Game/Navigation/Scheduling/` | Full request ownership, bounded staging/admission, fair fixed grants, existing-job dispatch, ordered publication and scheduler checkpoints |
| `src/ForgeLine.Navigation/Workspaces/` | Exclusive fixed-capacity arenas, cooperative initialization, active/retained accounting and lifecycle |
| `src/ForgeLine.Navigation/Caching/` | Copied bounded storage, typed exact route keys, regional validation, negative-result distinction and cache checkpoints |

All implementation types remain internal to their existing assemblies. The public
integration interfaces come from the shared foundation. No existing movement,
routing, job, docking, cargo, fuel, reservation, command or save-schema file changes.
No additional project, package, scheduler infrastructure or route authority is added.

## Explicit limits and work accounting

There are no invented performance presets. Composition must supply every limit.
Shared `NavigationIntegrationLimits` provides fleet work, per-search work, active
searches, search/cache bytes, endpoint candidates and retry ticks. Other shared
limits continue to belong to their respective navigation services.

| Internal configuration | Required interpretation |
| --- | --- |
| `NavigationSchedulingPolicyLimits.MaximumEntries` | Total staged/admitted/budget-limited request metadata capacity |
| `MaximumAdmissionsPerTick` | New policy admissions, including replacements |
| `MaximumSlicesPerTick` | Maximum distinct serviced entries in one batch; no greater than entry capacity |
| `WorkPerTick`, `WorkPerRequest` | Must equal shared `WorkPerTick`, `WorkPerSearch` |
| `WorkPerSlice` | Upper bound for one fixed grant |
| `AgingIntervalTicks` | Ticks needed for one service-class promotion |
| `RetryIntervalTicks` | Must equal the shared retry interval |
| `NavigationSchedulerStorageLimits.MaximumSnapshotProfiles` | Number of distinct retained capability/knowledge publications |
| `MaximumCheckpointBytes`, `MaximumKernelCheckpointBytes` | Separate complete checkpoint allowance and encoded payload cap per active kernel |
| `ReservedPoolBytes` | Pool fixed overhead and worst-case retained arena capacity, subtracted from shared search bytes |
| Workspace pool constructor | Fixed arena size, active count, retained count and combined arena byte cap |
| Route cache constructor | Entry/byte limits, per-tick maintenance, per-write eviction limit, maximum waypoints and dependencies |

All authoritative limits use integer work units and simulation ticks. A concrete
kernel must charge graph expansion, local refinement, fallback, endpoint candidates,
clearance, initialization, cache probes/copies, and output construction against the
same request and fleet work allowance. Calling an existing monolithic search outside
the granted `Advance` does not satisfy this contract. The scheduler validates reported
consumption; it cannot retroactively meter work concealed by a producer.

`Advance(tick, workAllowance)` clamps available work to the shared per-tick limit.
The policy reserves fixed, disjoint slices before workers start; each entry receives
at most one slice per tick. The session receives the smaller of its grant and its
own remaining `MaximumWork`. Actual consumption settles in grant order. Unused
reservations are not redistributed according to worker completion timing. Wall-clock
durations and diagnostic counters never affect an authoritative decision.

Metadata scans and sorting have explicit entry/slice/profile capacities. They are
coordinator overhead, separate from kernel search work. Snapshot storage, published
gameplay routes, checkpoint staging and process working set are not silently claimed
as part of the active-session byte figure.

## Scheduler composition and call order

`NavigationSearchScheduler<TKernelCheckpoint>` implements
`INavigationSearchScheduler<NavigationSchedulerCheckpoint<TKernelCheckpoint>>`.
Its constructor requires the typed kernel, shared and internal limits, stable
`NavigationSchedulerIdentity`, a versioned checkpoint codec, an exact snapshot
resolver, a deterministic service-class selector, and the host's existing
`JobScheduler`. A null job scheduler selects synchronous execution; it does not
create another worker pool.

The identity contains stable content identity, world generation and algorithm
revision. The snapshot resolver must resolve publications for that identity;
matching numeric revisions from another world are insufficient.

1. Construct one scheduler and the explicitly budgeted workspace/cache services for
   the world. The concrete kernel must honor its full peak workspace declaration.
2. At the existing navigation boundary, publish new immutable inputs through
   `UpdateSnapshot(snapshot)` before submitting requests against that publication.
3. Validate current authoritative ownership and call `Submit(boundSearch, snapshot)`
   or `Cancel(in binding)`. Do not mutate scheduler state from worker jobs.
4. Call `Advance(tick, remainingFleetWork)` once for a strictly increasing tick.
   It admits, prepares sessions, dispatches, joins, validates and publishes one batch.
5. Revalidate every returned full binding and world ownership before applying a
   proposal through the existing route/movement authority.
6. Capture or restore only outside the worker boundary. On world replacement or
   match end, finish the boundary and dispose old sessions before admitting a new
   world with a fresh memory budget.

### API behavior

| Method/property | Behavior |
| --- | --- |
| `Availability` | Reflects kernel availability; disposed or faulted service is unavailable |
| `Submit` | Returns accepted, already pending, capacity exhausted, invalid, stale or not connected; accepted means retained in the bounded staging queue |
| `Cancel` | Cancels only an exactly matching full binding; a late old cancellation cannot remove a replacement |
| `UpdateSnapshot` | Installs a validated publication, releases stale sessions at a completed boundary, and retains intent for an ordered stale result |
| `Advance` | Uses real simulation tick for admission; returns actual work and copied results in stable entity/command/request order |
| `CaptureState` / `RestoreState` | Transfers exact bounded coordinator and active-kernel state; never silently restarts incomplete searches |
| `Diagnostics` | Reports queue/active/byte/wait/work/lifecycle and policy accounting without per-vehicle text logs |
| `Dispose` | Releases sessions after workers stop; failed disposal retains the failed reservation and faults the service |

The shared `Submit` interface has no tick parameter. Staged requests therefore get
their queue timestamp at the next actual `Advance`, and retain it across admission
backpressure. Original order acceptance time must not lie in the future. Staged
submission order is an explicit persisted monotonic sequence.

## Request ownership, fairness and backpressure

There is at most one retained request per complete entity identity, including entity
generation. The retained object contains the complete shared `NavigationOrderBinding`
and `NavigationSearchSpecification`. Newer command generations replace older ones
even for identical targets accepted in the same tick. Within one command generation,
the original order must match and a replacement must have a newer request ID.

`MovementOrder` does not itself supply command generations. Central command
acceptance must assign checked monotonic generations and persist them. Request ID
and acceptance tick are not substitutes. Cancellation is not a permanent tombstone:
the caller must reject obsolete commands, destroyed entities, owner changes and old
dock/queue generations before submission and again before consuming a result.

The finite service classes are `PlayerOrder`, `GeometryChange` and `Recovery`, in
that initial order. Effective class is reduced by one per `AgingIntervalTicks` of
ready waiting, down to the highest class. Ties use `ReadySince`, stable request
sequence, then full entity identity. No research bonus, hash enumeration, wall time
or worker completion order participates.

Every settled service attempt resets ready age, including a zero-progress attempt;
original queue age remains available for diagnostics. Same-command replacement keeps
waiting/retry age. If an older large reservation cannot fit, lower-ranked new
sessions stop filling released capacity while existing sessions continue. This
prevents a continuous stream of smaller allocations from starving that request.
Unavailable active slots consume no search grant and do not reset waiting age.

Admission exhaustion retains a replacement in the existing bounded slot and excludes
it until a later admission succeeds. Full staging capacity returns explicit
backpressure without allocating an overflow list. The authoritative caller retains
the gameplay order and cargo. It may keep a still-safe route remainder or hold
movement safely; those gameplay decisions remain with the existing movement path.

A session that returns `Pending` with no work, or consumes its remaining request
budget, becomes explicitly `BudgetExhausted` and releases its active reservation.
Its budget-limited metadata remains. A retry requires a new bounded request ID under
the same command and observes the retained retry interval. This is an explicit
new search attempt, not a hidden replay restart. Exhaustion is never a proof of
unreachability and never supplies a negative cache entry.

## Jobs, publication and failure ownership

All active reservations and `CreateSession` calls occur in fair coordinator order
before any worker starts. `CreateSession` may reserve an arena; logical initialization
and search work belong to its charged `Advance`. Workers receive only their session
and pinned immutable inputs, with no ECS or scheduler mutation access.

The host's normal fence is joined, and every dispatched handle is also joined
individually before session release, including partial-dispatch or fence failure.
The complete batch is validated before settlement. Status/failure consistency,
request identity, actual work, reported workspace and route output bounds are checked.
Found routes are proposals; only the existing authoritative movement path changes
transforms or completes docking/service operations.

A producer exception or invalid work report faults the scheduler. Unknown consumed
work is exposed in diagnostics and is not fabricated as a zero-cost retry. Failed
session disposal retains its session reference and reservation; no new work can
reuse that credit. `Dispose` may be retried after the underlying failure is resolved.

### Coordinator-owned caching and parallel jobs

The cache mutates LRU, maintenance and metrics and is coordinator-owned. It must not
be shared as mutable state between concurrent search jobs. Synchronous session
execution can perform cache operations as charged stages. Parallel integration must
provide a deterministic coordinator cache phase with owned per-session inputs and
the same work/memory ledger; no cache work may be hidden in `CreateSession`, which
has no work allowance. The shared revision-1 session interface has no separate
coordinator cache-stage callback. That phase and the concrete kernel's use of it
remain a central composition decision, not an installed parallel cache path here.

## Workspace and aggregate memory ownership

`NavigationWorkspacePool` owns fixed-size byte arenas. It bounds active count,
retained count and their combined estimated bytes, including conservative array,
storage-node, lease and pool overhead. Byte values describe reachable owned storage;
they are not process working-set measurements or claims about garbage-collection
timing. Every session owns its lease exclusively until its worker has stopped.

`TryReserve(request, out lease)` rejects the request before allocation if the
accounted arena exceeds its workspace allowance. It reserves capacity without
exposing uncleared storage. `AdvanceInitialization(maximumBytes)` clears a bounded
prefix and returns the actual byte count; the kernel converts that count to its
documented work unit and reports it in `Advance`. `Buffer` remains inaccessible until
the entire arena is initialized. Fresh and reused arenas start at the same logical
initialization cursor, so pool warmth cannot change publication timing.

The synchronous `TryRent` overloads clear the entire arena immediately. A caller
using them must reserve and charge that complete initialization stage in advance;
they are not a way to bypass small cooperative grants. The request-aware overload
also checks the request memory cap. `TrimRetained(maximumToRelease)` bounds idle
release work. `GetStatistics` exposes active/retained bytes, peaks, rent/reuse/release,
rejections and initialized bytes.

Use the pool's `RetainedReservationBytes` as the minimum reserved pool allowance:

```text
retained reserve = pool overhead + maximum retained arenas * bytes per arena
active allowance = shared MaximumSearchBytes - reserved pool bytes
sum of active declared peak bytes <= active allowance
```

Each active peak includes its arena, session/frontier/candidate storage, temporary
copies and output construction. Reserving idle overhead separately prevents retained
arenas and active non-arena allocations from each spending the same fleet credit.
The scheduler must reject a request too large for the active allowance. Do not give
every capability or vehicle profile an independent full fleet pool. Share one pool,
or explicitly partition and sum all pool reserves and active budgets.

The shared pending-request adapter currently uses the whole fleet search-memory cap
as the request cap. Central integration must supply meaningful per-session peaks
compatible with the reserved pool allowance; silently dividing that value would
change the request contract.

Geometry/cost publication retains the world's pool. Actual world replacement must
join old jobs, release old sessions and dispose the old pool before another world
uses the full budget. Pool disposal drops idle storage but continues accounting for
active leases until their owners return them. It never recycles live worker memory.

## Route keys, reuse and bounded maintenance

`NavigationRouteCache` owns a bounded `NavigationResultCache<NavigationRouteCacheKey, byte>`.
Its wrapper and fixed slot/index arrays count toward its byte limit, as do copied
payload arrays. Entry count and estimated bytes are both enforced. Inputs and output
values are copied; the cache retains no world snapshot or mutable caller buffer.

Keys include stable world/content generation, algorithm and shared-contract revision,
full start pose, exact original destination, target semantics, complete goal constraints,
capabilities, actual vehicle profile, knowledge scope, request work/memory/candidate
limits, and relevant grid/sector settings. Cost-sensitive keys also include cost
revision and availability. Same sectors do not make endpoint connectors equivalent.
The caller must declare whether the algorithm actually depends on costs.

Negative entries are allowed only for confirmed `NoHighLevelRoute` or `NoLocalRoute`
and remain pinned to the complete global geometry revision. Invalid, stale, canceled,
pending and budget-exhausted results are never negative entries.

Positive entries use global geometry unless the kernel explicitly proves a complete
regional dependency set covering every relevant geometry decision and endpoint
connector. A populated list alone is not proof. Every dependency must be unique,
valid and present with the same stamp in the current snapshot. Missing current
stamps select conservative global pinning at insertion; changed or missing stamps
reject regional hits. Thus an unrelated geometry change can preserve a proved
regional result without a whole-cache flush. Traffic changes do not clear geometry
caches; cost-sensitive identity makes obsolete costs miss.

The regional preparation now on master has its own page-backed snapshot and
dependency proof. Its public snapshot output remains unconnected. Central integration
must publish the shared snapshot's region vector from the authoritative regional
source and validate the complete connector proof before selecting regional reuse.
The cache does not assume those internal regional pages already implement the shared
snapshot lifetime contract.

`TryGet` and `TryStore` accept caller-owned bounded scratch and a work allowance and
return actual consumed work. Lookup, dependency validation, payload copying and
reconstruction are charged; 16 bytes form one payload-copy unit. Lookup deferral,
buffer shortage and memory pressure are distinct from an ordinary miss and from
confirmed unreachable. Cache work exhaustion may cause the kernel to defer the
stage or bypass caching, subject to its remaining search allowance.

The typed route key uses deterministic value hashing, including canonical float zero.
Actual hash/probe work is charged, and collision chains are retained exactly in
checkpoints so process hash randomization or reconstructed chain order cannot change
future work consumption. Other users of the generic storage must likewise provide
stable value-key hashing and equality.

Reconstruction checks a separate caller-supplied temporary/output allowance before
allocating both decoding and proposal-owned arrays. Successful full-route reuse has
its own counters; it does not invent a high-level-cache-hit diagnostic.

Expiration, eviction preflight and exact invalidation share the configured per-tick
maintenance allowance. A write can inspect at most its eviction limit; repeated
rejected writes still spend that inspection allowance. Capacity preflight is
non-destructive until it proves the write can fit. Reads do not require a complete
cache scan or an eager global clear. `Maintain` advances a saved cursor under the
remaining quota. `Clear` and `Dispose` are explicit lifecycle operations, not the
response to every temporary congestion change.

| Cache API | Integration responsibility |
| --- | --- |
| `TryGet(worldIdentity, snapshot, request, costSensitive, tick, scratch, workAllowance, additionalWorkspaceBytes, ...)` | Supply the exact current input and remaining grant; consume the returned status, work and reconstruction-byte estimate |
| `TryStore(worldIdentity, snapshot, request, result, costSensitive, completeRegionDependencies, tick, expiresAtTick, scratch, workAllowance, ...)` | Supply a confirmed completed result, explicit dependency proof and future expiry; charge returned work even when insertion defers |
| `Maintain(tick, workAllowance)` | Spend its returned actual work from the same coordinator fleet allowance |
| `GetMetrics()` | Read bounded counters and storage estimates |
| `CaptureState(maximumCheckpointBytes)` | Reserve additional checkpoint memory before copying |
| `RestoreState(state, expectedWorldIdentity, expectedAlgorithmRevision, maximumRestoreBytes)` | Create a detached validated cache within the checkpoint-plus-new-cache allowance |
| `Clear()` / `Dispose()` | Explicit lifecycle release; neither is an uncharged runtime invalidation strategy |

The generic storage exposes budgeted `TryPeekLength`, `TryCopy`, `TryStoreSuccess`,
`TryStoreUnreachable`, `Invalidate` and `Maintain`, together with metrics, lifecycle
and checkpoint methods. `NavigationCacheOperationStatus` distinguishes hit/miss,
stored, capacity exhaustion, work exhaustion and short output buffers. It does not
infer route identity, complete regional coverage or proof of unreachability for a
caller that supplies an incomplete key or false result classification.

## Exact capture and restore

Scheduler state is captured only at a completed worker/grant boundary. It contains
full requests and bindings, pending-admission flags, input order, queue/ready/retry
ages, stable sequences, cumulative work, tick allowances/reservations/admissions,
snapshot keys, and bounded encoded state for every active session. It contains no
live sessions, leases or snapshots and no arbitrary retained kernel object graph.

`NavigationSchedulerCheckpointCodec<T>` has an explicit revision and writes into a
bounded destination. Decoding must consume the entire payload and return non-null
typed state. The scheduler preflights metadata, outer-array copies, serialization
scratch and accumulated encoded payloads against the separate checkpoint allowance.

Restore validates identity, common/format/algorithm/codec versions, configuration,
policy correlations, request-specific consumed work, sequence/age uniqueness, snapshot
keys and active reservations before destroying current state. Original publications
must be resolvable, including capability, knowledge scope, geometry/cost revision and
cost availability. Current nonzero unfinished work cannot restore without an active
frontier. Old sessions are released before new reservations are acquired. A deep
decode or kernel-restore failure faults the service; there is no fresh-search fallback.

The concrete kernel must restore its exact frontier, tie-break state, candidate and
clearance cursors, workspace initialization position and cumulative logical work.
Reconstruction before simulation resumes must establish that same position. Replaying
an unspecified amount of search after load and publishing on another tick is not
equivalent restoration.

Shared revision 1 cannot inspect allocations inside generic `CaptureState`, a codec
delegate or `RestoreSession`. Live frontier plus detached capture, decoding graphs
and restore temporary storage must fit the concrete kernel's declared peak or an
explicit separately bounded capture arrangement. This limitation is not hidden by
the encoded-byte cap. The scheduler does not suspend arbitrary frontiers into an
unbounded fairness queue.

Cache checkpoints preserve payloads, exact keys, slot/index and LRU order, free list,
expiry, tick, maintenance cursor/quota and counters. Restore validates structural
chains, identity, payload format and byte totals before installing state. The restore
allowance covers the immutable cache checkpoint and the new cache together; an old
live cache retained by the caller needs its own allowance. Dropping cache state can
change readiness/publication timing and is not an exact-restore shortcut.

Scheduler checkpoint format is revision 1; generic cache storage format is revision 2
because exact hash chains are authoritative work state. The typed cache wrapper and
encoded route payload are revision 1. All retain shared contract revision 1 and
validate their relevant configuration/algorithm identities.

Pool contents have no independent authoritative save format. The kernel reconstructs
active lease contents and initialization position under the same admission rules.
Idle pool warmth is irrelevant because initialization work is equal for new and
retained arenas. Central integration owns serialization and any save/replay schema
change; these internal checkpoint objects are not a new central save format.

## Diagnostics and acceptance boundary

Scheduler diagnostics include retained requests, budget-limited/deferred entries,
active sessions, reserved bytes, profiles, maximum waiting ticks, starts,
continuations, cancellations, stale results, actual work, remaining grants/admissions
and fault/unknown-work indicators. Cache diagnostics include entries/bytes,
positive/negative hits, misses, budget deferrals, writes, evictions, expiration,
invalidation and inspected slots. Pool diagnostics include active/retained counts
and bytes, peaks, lifecycle counts, rejections and initialized bytes.

Lifetime counters may reset where documented on restore; policy state determining
future work does not. Diagnostics must pass through the existing viewer/knowledge
filter before UI exposure. No hidden world knowledge or per-tick vehicle text log
is introduced.

Automated tests are not created, changed or manually executed for this change. No
benchmark, soak, smoke, headless, client or visual-reference run is performed.
Existing automatically triggered CI is unchanged. Targeted Release compilation is
recorded in the handoff. Manual fleet load, cancellation, geometry/cache invalidation,
fairness, memory pressure and active/queued save-load scenarios remain acceptance
work after the full integration is runnable.
