# Navigation search scheduling and bounded storage

## Implementation status and prerequisite

This change implements independent, internal scheduling policy and bounded storage.
It does not install a fleet scheduler, change current routing, or claim a whole-game
CPU or memory limit. All existing runtime callers remain authoritative.

Initial inspection used master `cdb60b1a926e770f25eb1ec350548bb00349ff49`.
The source baseline was synchronized to master
`574040b1cde354bc1414a692e2ec075968e0959a` (0.1.473-dev), including conveyor routing
#357. Navigation recovery #354 and technology/rescue work #350 are already
integrated. The branch prepares one PATCH increment to 0.1.474-dev.

The shared revision-1 contracts in [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356)
are not present in that master. They were inspected at
`df9290ead9bd09b608a1720d992fac0163d8598a` and were not copied into this branch.
No public substitute, composition hook, save schema, or new project was introduced.
Implementation types remain internal to their existing assemblies.

The missing contracts are `NavigationOrderBinding`, `NavigationBoundSearch`,
`NavigationWorldSnapshot`, `NavigationSnapshotRevision`,
`NavigationSearchSpecification`, `NavigationSearchAdvance`,
`INavigationSearchSession<TCheckpoint>`, `INavigationSearchKernel<TCheckpoint>`,
`INavigationSearchScheduler<TCheckpoint>`, and `INavigationStateTransfer<TCheckpoint>`.
The typed scheduler adapter, cooperative search dispatch, revision-aware cache
adapter, and exact combined restore must follow their integration into master.

The [handoff](NavigationSearchScheduling.Handoff.md) records the concrete remaining
work, validation evidence, and manual acceptance boundary.

## Ownership and integration boundaries

| Location | Responsibility in this change |
| --- | --- |
| `src/ForgeLine.Game/Navigation/Scheduling/` | Bounded coordinator-owned admission, deterministic service policy, work reservations and policy state |
| `src/ForgeLine.Navigation/Workspaces/` | Exclusive fixed-capacity byte arenas, combined active/retained accounting and lifecycle |
| `src/ForgeLine.Navigation/Caching/` | Bounded copied-result storage, explicit negative-result admission and bounded maintenance |

No changes are made to `NavigationRouting.cs`, `HierarchicalPathfinder.cs`,
`LocalPathSearch.cs`, `LocalPathFrontier.cs`, `NavigationWorld.cs`, existing jobs,
movement, docks, cargo, reservations, command acceptance, or save/replay schemas.
The existing high-level route/graph caches and local-search pool therefore retain
their existing behavior until the central integration replaces their use.

The new policy operates only on copied metadata. It never deletes a gameplay order
or cargo, releases a physical corridor, changes a transform, or authorizes docking.
The central owner must retain an old safe route remainder when appropriate, or
hold movement safely while the scheduler is deferred. That behavior is not wired
by these internal components.

## Explicit configuration and work units

No measured defaults or runtime presets are added. The inspected shared
`NavigationIntegrationLimits` also has no defaults. Its work, active-search,
search-memory and cache-memory fields must be mapped explicitly when connected.
Queue capacity, admissions per tick, fixed slice size, aging interval, cache entry
and maintenance caps, and retained workspace count additionally need explicit
composition configuration; they are not silently inferred from unrelated limits.

All authoritative allowances use integer work units and simulation ticks. A future
kernel must charge high-level expansion, refinement, fallback, candidate enumeration,
clearance, workspace initialization, and output construction to the same request
and fleet allowance. Merely calling a monolithic legacy search once does not satisfy
this requirement. The policy can validate reported work; it cannot meter an algorithm
that does not cooperate or undo work already performed outside a grant.

Fixed disjoint slices are reserved in deterministic order before jobs execute.
Actual consumption is settled in that same order after the existing barrier.
Unused reservations are not redistributed according to worker completion timing.
Per-request budget exhaustion is distinct from a proved disconnected destination.
Wall-clock timings, if later observed, must not change admission or publication.

## Admission, fairness, retry and ownership

The policy bounds metadata entries and admissions. Full capacity is explicit
backpressure. The outer Game adapter must keep the actual order and cargo and retry
with the same command identity. A new command has a new checked command generation,
including an identical command accepted during the same tick.

Service classes are finite internal policy ranks, without research bonuses. Aging
reduces the effective rank until an old request reaches the highest class. Equal
ranks use ready-since age and a stable request sequence, never dictionary iteration.
Ready-since age resets after each settled service attempt, including zero progress;
original queued time remains separately
available. This prevents an old long-running request from permanently taking the
first slice solely because it was originally accepted first.

Blocked or zero-progress work waits for a configured tick interval. A deferred
replacement keeps the existing bounded slot and its same-command waiting/retry age,
but is excluded from planning until an explicit later submission passes admission.
A completely new entity refused admission has no additional policy entry; its
authoritative caller retains that waiting intent. Cancellation
and replacement invalidate metadata ownership; work already dispatched still counts
against the reserved tick allowance, and a late result cannot settle a newer entry.
No cancellation returns a live worker's workspace.

The current `MovementOrder` has no command-generation field. The central command
owner must assign and persist generations at actual acceptance and validate every
submission against current authoritative state. Request ID or accepted tick alone
is insufficient. Metadata removal is not a permanent tombstone: an obsolete caller
must not resubmit a canceled command. Owner change, destruction, dock/queue generation,
and snapshot validation belong in the shared adapter before publication.

The policy does not create or retain kernel sessions. Admission of actual sessions
must separately enforce `MaximumActiveSearches` and the shared workspace limit.
Blocked sessions must not occupy every active slot indefinitely. Suspension requires
an exact frontier checkpoint; discarding a frontier and restarting invisibly is not
a fairness or replay solution.

### Internal policy API and ordering

`NavigationSchedulingPolicyLimits` explicitly requires `MaximumEntries`,
`MaximumAdmissionsPerTick`, `MaximumSlicesPerTick`, `WorkPerTick`, `WorkPerRequest`,
`WorkPerSlice`, `AgingIntervalTicks` and `RetryIntervalTicks`.
`NavigationServiceClass` ranks `PlayerOrder`, `GeometryChange`, then `Recovery`.
Effective rank is `max(0, rank - waitedTicks / AgingIntervalTicks)`, with promotions
clamped to the finite class range. No priority comes from technology state.

The internal `NavigationPolicyRequest` retains the existing `NavigationPendingPath`
and supplied command/dock/queue generations. It is metadata inside Game, not a public
replacement for `NavigationOrderBinding`. Same-command replacement requires an
unchanged complete original order and a newer search request ID. New commands require
a newer command generation. Rejected replacements invalidate old grants without
admitting a new search. `AdmissionDeferred` and `BudgetLimited` are explicitly
excluded from planning; the former requires successful resubmission, the latter a
new bounded request after its retry interval.

```csharp
new NavigationSchedulingPolicy(NavigationSchedulingPolicyLimits limits);
NavigationPolicyDiagnostics Diagnostics { get; }
void BeginTick(SimulationTick tick, int workAllowance);
NavigationPolicySubmission Submit(in NavigationPolicyRequest request, NavigationServiceClass serviceClass);
bool Cancel(in NavigationPolicyRequest request);
void Clear();
bool TryGetEntry(EntityId entity, out NavigationPolicyEntryState entry);
int Plan(Span<NavigationPolicyGrant> destination);
bool Settle(in NavigationPolicyGrant grant, int workConsumed, NavigationPolicyContinuation continuation);
NavigationSchedulingPolicyCheckpoint CaptureState();
void RestoreState(NavigationSchedulingPolicyCheckpoint checkpoint);
```

The future adapter follows this order at the existing simulation boundary:

1. Settle the preceding worker batch before `BeginTick`. A repeated or backward
   tick cannot refill allowances.
2. Validate current authoritative ownership, cancel obsolete metadata, and submit
   retained orders in stable order. Preserve gameplay intent on refusal.
3. Call `Plan` once into a buffer of at least `MaximumSlicesPerTick` elements.
   This reserves disjoint slices and assigns stable grants before execution.
4. Admit/initialize memory in grant order during a budgeted coordinator step of
   `Advance`; charge that work to the same grant before dispatch. Workers must not
   race to rent arenas. Advance the corresponding exclusive sessions through the
   existing jobs using pinned snapshots and the remaining grant. Cache access,
   child geometry work and output construction share that remaining allowance.
5. Complete the existing job boundary, then `Settle` every grant exactly once and
   in its original order. The actual work must lie between zero and its grant.
6. Revalidate the full shared binding and revisions before publishing a result.
   A true `Settle` return proves policy ownership only. `Finished` does not mean
   reachable or unreachable; only the typed search outcome supplies that fact.
7. Capture state after settlement. Dispose sessions/leases only after their readers
   have completed. `Clear` invalidates metadata but does not forgive outstanding work.

Policy diagnostics include entry/deferred/budget-limited counts, unresolved grants,
maximum original waiting age, tick allowance/reserved/actual/remaining work and
remaining admissions. Lifetime counters cover accepted/canceled/superseded requests,
stale settlements, completed requests, slices and actual work. Real session
started/resumed/canceled counters require the later session adapter.

## Exclusive workspaces

`NavigationWorkspacePool`, `NavigationWorkspaceLease`, and
`NavigationWorkspaceStatistics` are internal Navigation implementation types.

```csharp
new NavigationWorkspacePool(
    int capacityBytes, int maximumActiveWorkspaces,
    int maximumRetainedWorkspaces, long maximumAccountedBytes);
bool TryRent(out NavigationWorkspaceLease? lease);
int TrimRetained(int maximumToRelease);
NavigationWorkspaceStatistics GetStatistics();
void Dispose();
```

A lease exposes a fixed `Span<byte> Buffer`, `CapacityBytes`, `AccountedBytes`, and
idempotent `Dispose()`. No growable dictionary or backing array is exposed. The
kernel may partition the arena into bounded unmanaged spans; any storage outside
the arena needs separate admission/accounting before allocation.

Every arena has the same capacity. Accounted bytes are capacity rounded up to eight
bytes plus a conservative 192-byte allowance for the array, storage node and lease.
The pool has a 512-byte allowance. The total cap covers the pool plus **both active
and retained** arenas. These allowances estimate reachable owned managed storage;
they are not process working-set measurements or a limit on garbage collection lag.

`TryRent` returns false for active-count or memory pressure. It returns zero-initialized
storage, including retained arenas. Initialization touches `CapacityBytes` and must
be charged to a budgeted kernel initialization step. Do not hide arena acquisition
and clearing in an uncharged `CreateSession`. If initialization cannot fit a slice,
the integration must provide a resumable initialization strategy before enabling it.

Admissions occur in coordinator order after the preceding job barrier. Internal
locking preserves lifecycle consistency, but racing worker admissions would still
be an invalid nondeterministic policy. A worker exclusively owns its span until it
finishes. Spans must not escape lease lifetime or overlap disposal.

`TrimRetained` releases at most its explicit count. Pool disposal stops admissions
and drops retained arenas while active leases remain accounted and valid. Late
returns are discarded. Do not recycle storage on cancellation before the worker
has stopped. New match/world composition must dispose the old pool and complete
all old readers; a lease is not a snapshot-lifetime mechanism.

Statistics expose active/retained/peak counts and bytes, rent/reuse/return/release
counts, active-limit and memory-limit refusals, initialized bytes and disposal state.
Diagnostic counters saturate instead of overflowing into simulation decisions.

## Cache storage and negative results

`NavigationResultCache<TKey, TElement>` and `NavigationCacheMetrics` are internal.
Keys must be unmanaged and equatable; payload elements must be unmanaged.

```csharp
new NavigationResultCache<TKey, TElement>(
    int maximumEntries, long maximumEstimatedBytes,
    int maximumMaintenanceEntriesPerTick, int maximumEvictionsPerWrite);
bool TryStoreSuccess(TKey key, ReadOnlySpan<TElement> value, ulong tick, ulong expiresAtTick);
bool TryStoreUnreachable(TKey key, NavigationFailureReason failureReason,
    ulong tick, ulong expiresAtTick);
bool TryCopy(TKey key, ulong tick, Span<TElement> destination,
    out int valueLength, out NavigationFailureReason failureReason);
bool Invalidate(TKey key, ulong tick);
int Maintain(ulong tick, int maximumSlots);
NavigationCacheMetrics GetMetrics();
void Clear();
void Dispose();
```

The container owns copied payload arrays and preallocated lookup/slot metadata.
Both metadata and payload capacity count toward its byte cap. It exposes no retained
array. Storage and access belong to the coordinator; workers receive bounded copies.
The caller must supply immutable value semantics for equality/hash and a stable
operation order. Hash-bucket order never chooses LRU victims.

`TryCopy` returns true for a completed positive or a confirmed negative. A negative
has zero elements and its explicit failure reason. A miss or expired key returns
false with zero length; an undersized destination returns false with the required
positive length. `TryStoreUnreachable` permits only `NoHighLevelRoute` or `NoLocalRoute`
from a completed exhaustive search. Canceled, stale, invalid and
`SearchBudgetExhausted` results cannot be inserted as negatives. Proving exhaustive
search is the kernel adapter's responsibility, not an inference by the storage class.

Eviction follows LRU links. The number of victims per insertion is bounded, and all
eviction preflight, expiration cleanup and maintenance share one allowance per tick.
Rejected writes still consume inspection allowance. A write refused under pressure
does not evict entries merely to make partial progress. Exact-key invalidation makes
the value ineligible immediately; delayed cleanup remains counted against memory.
`Maintain` scans from a retained cursor. Repeated calls in a tick cannot reset the
allowance. `Clear` is a world/match lifecycle operation, not congestion handling.

Lookup probes are bounded by entry count and copying by payload capacity; these
costs and the number of calls also need integration-level work admission. Maintenance
quota alone does not meter every lookup or payload copy. No cache hit counter or byte
estimate is evidence of a measured performance gain.

### Required route-key adapter

The generic storage does not yet construct route keys or consume regional updates.
The shared adapter must distinguish at least the following inputs:

| Input | Required distinction |
| --- | --- |
| Geometry | Matching world/content identity and global geometry revision, or validated complete regional dependencies |
| Costs and knowledge | Independent cost revision for cost-dependent results; exact faction knowledge scope |
| Capabilities | Complete capability value, including movement/slope/cost semantics |
| Actual vehicle | Dimensions, clearance, motion limits and orientation-relevant data from the actual profile |
| Start and destination | Exact original positions/pose and concrete connection segments; shared sectors alone do not establish identity |
| Goal | Player/dock/transit semantics, projection/tolerance and heading requirements |
| Algorithm/configuration | Revisions affecting output and bounded-search semantics |

Do not key an actual endpoint/connection result solely by source and target sector.
Do not reuse a confirmed negative with incomplete dependencies after a geometry
change. Missing regional stamps require conservative geometry invalidation.
Regional publications must make affected keys ineligible before lookup while
reclaiming memory within the maintenance bound. A soft cost change affects only
cost-sensitive eligibility; it does not demand a synchronous full cache clear.

## State capture and restoration

The policy checkpoint restores scheduling metadata only. Capture must occur with
all dispatched grants settled. It must retain stable sequence, queued/ready/retry
times, consumed request work, remaining current-tick allowances and admission state.
Configuration, capacities, identities, duplicates and sequence/tick relations are
validated before installing restored metadata.

Combined restore remains an integration prerequisite. The kernel must restore its
exact frontier, candidate/clearance cursor, work position and pinned immutable inputs.
Reconstruction must reach the identical logical work position before simulation
continues, without extra ticks or new work grants. Workspace leases are reacquired
only after old workers have stopped; the raw pool does not serialize frontier data.

The cache has no persistence adapter in this change. If cache readiness affects
admission/publication, preserve contents, payloads, expiry, LRU order, slot/free-list
order, clock, maintenance cursor and remaining allowance, or reconstruct identical
logical readiness before resuming. Blindly clearing caches or restarting searches
does not establish replay equivalence. The central owner assesses schema changes;
no historical checkpoint or state hash is rewritten here.

## Diagnostics and later manual acceptance

Policy, pool and cache counters are value snapshots. No per-vehicle tick logging or
presentation access is added. The future diagnostics adapter must apply current
ownership/visibility and must never expose physical collision knowledge as a faction
cost or UI fact. Diagnostic errors must not change routing decisions.

Later manual acceptance must cover simultaneous fleet orders, same-tick replacement,
cancellation and destruction while work is pending, owner/world changes, sustained
aging under high-priority arrivals, zero-progress retries, workspace pressure, cache
expiry/regional invalidation and Save/Load with queued and active searches. Observe
actual primitive work, retained bytes and publication order, not request count alone.

Automated tests, benchmarks, smoke/headless/visual runs were not created, changed or
manually executed for this work. Existing automatically triggered CI remains intact.
Release compilation is recorded separately in the handoff. Combined gameplay
integration, reference performance and manual acceptance remain outstanding.
