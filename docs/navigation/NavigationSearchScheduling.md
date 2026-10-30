# Connected fleet search scheduling and bounded storage

[Documentation](../README.md) · [Complete catalog](../DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Product path](#product-path)
- [Admission, fairness and full ownership](#admission-fairness-and-full-ownership)
- [One work ledger and coordinator cache phases](#one-work-ledger-and-coordinator-cache-phases)
- [Bounded ownership](#bounded-ownership)
- [Authoritative state, derived payloads and recovery](#authoritative-state-derived-payloads-and-recovery)
- [Copied diagnostics](#copied-diagnostics)
- [Validation and manual handoff](#validation-and-manual-handoff)

</details>

## Product path

MatchRuntime connects the prepared NavigationSearchScheduler to VehicleFleetKernel
through HierarchicalNavigationSystem. Strategic Wheeled Cargo/Supply requests
use this one bounded admission ledger. The former temporary rotating-session
adapter is removed. GroundMovement still owns transforms, physical vetoes and
precise Dock/Queue handoffs; final Fuel authorization is unchanged.

The host's existing JobScheduler is borrowed when supplied by composition.
There is no new worker pool. A host deliberately configured without Jobs uses
the same grants inline. Wall-clock durations remain diagnostic only.

The implementation reuses NavigationSchedulingPolicy, NavigationSearchScheduler,
NavigationWorkspacePool, NavigationWorkspaceLease and NavigationRouteCache.
The shared contract revision is 4; the scheduler checkpoint revision is 2 and
the concrete fleet continuation revision is 1.

## Admission, fairness and full ownership

The existing NavigationPendingPath component retains unadmitted intent. A bounded
scan inspects at most 4,096 pending components and selects the oldest 16 request
IDs for submission before fresh work can refill slots. At most 16 submissions
enter the scheduler per simulation tick. Capacity pressure leaves the request
explicitly WaitingForAdmission rather than losing its root target or cargo.

The scheduler holds at most 128 staged/admitted requests and 8 active sessions.
It owns the complete entity/root/request/dock/queue binding and one input sequence
per retained request. Policy admission is separately capped at 16 per tick.
Accepted means retained, not completed or moved. Queue/memory waits show
QueuedForBudget; a live continuation shows Searching. The policy uses its existing
stable service-class/aging comparison and a 60-tick aging interval. DockAccess
uses Recovery; other roots use PlayerOrder. These are work priorities and never
change service FIFO, bay ownership or gameplay command priority.

Before work and again before result adoption, the Game owner checks the current
entity generation, owner, session-bound registry, exact original binding, pose,
profile, geometry and service generations. Replacements/destruction invalidate
old requests and release their owners. Results publish in full entity, command
generation and request-ID order after the entire JobScheduler barrier. Completion
timing cannot redistribute another worker's grant or reorder application.

A new cost publication is used for new admissions. Existing searches keep their
exact immutable cost epoch while their geometry remains valid. The optional
preservePinnedCosts scheduler mode is captured and checked on restoration;
standalone callers retain the former strict-current-snapshot default. Geometry
changes cancel old sessions. The exact old cost publication remains resolvable
for each retained request; no missing snapshot silently becomes a fresh search.

## One work ledger and coordinator cache phases

The fleet has 16,384 logical work units per simulation tick, including at most
128 units of cache maintenance. The remainder is divided into at most eight
disjoint grants, each no larger than 2,048 units. A request retains its total
1,048,576-unit limit across all phases and ticks.

INavigationCoordinatorSearchSession adds two explicit phases around normal
Advance:

1. Prepare runs on the simulation coordinator. It advances arena initialization,
   then performs one charged exact-cache lookup.
2. Only the remaining grant reaches core Advance on an existing worker.
3. Every submitted worker is joined. Complete then runs on the coordinator in
   grant order and may store a final result using only that grant's remainder.
4. Combined work is validated and settled once. Only then are results published
   and terminal leases returned.

Arena initialization charges one unit per 64 cleared bytes, with the same cursor
for new and reused arenas. Cache hashing/probes, 16-byte copy blocks,
reconstruction and mutation use the existing cache's metered operations.
Core hierarchy/refinement/fallback/clearance retains its existing primitive work
units. A cache deferral can bypass caching and continue ordinary search; it
cannot become Unreachable. Only complete Found/Unreachable results are cacheable.
BudgetExhausted remains a terminal result to the existing request owner; no
frontier is silently discarded and restarted with the same request budget.

Cache reads mutate LRU state and therefore never run concurrently on workers.
All active reservations and leases are established before dispatch. Exception,
cancellation and shutdown paths join already submitted jobs before releasing
sessions. Invalid producer work/memory accounting faults the service and exposes
unknown work rather than reporting invented success.

## Bounded ownership

| Resource | Configured bound |
| --- | ---: |
| Retained requests / admissions / submissions per tick | 128 / 16 / 16 |
| Active sessions / slices per tick | 8 / 8 |
| Work per tick / slice / request | 16,384 / 2,048 / 1,048,576 |
| Per-session declared peak | 8 MiB |
| Active-session ledger | 64 MiB |
| Arena payload / retained idle arenas | 128 KiB / 2 |
| Fixed pool and idle reservation | 263,040 bytes |
| Combined configured pool capacity estimate | 1,313,152 bytes |
| Exact route cache | 64 entries / 2 MiB including metadata |
| Cache maintenance / eviction / expiry | 16 entries per tick / 4 per write / 200 ticks |
| Latest snapshot profiles / retained request snapshots | 16 / at most 128 |
| Complete scheduler checkpoint / encoded continuation | 40 MiB / 3 MiB per active request |

The pool/idle reserve is outside the scheduler's 64-MiB active credit; each active
arena is already inside its session's declared peak. Cache storage has its own
2-MiB ledger. These are conservative managed-storage estimates, not measured
process working-set or speed claims. Additional ECS intent is one existing
pending component per entity; it is not an unbounded auxiliary scheduler queue.
The 4,096-component admission scan is an explicit product scope limit, not an
arbitrary-population fairness guarantee.

The arena is real cache encoding/decoding scratch. The core's sparse frontier and
checkpoint/publication allocations spend the rest of the same session peak.
Cache payloads use whole-world geometry keys: the current kernel does not claim
complete regional connector dependency stamps. Exact pose, goal, capability,
profile, knowledge scope, work configuration, geometry and cost epoch participate
in reuse. World identity derives from the composition identity and seed.
Exact matching and available grant size can limit reuse; no hit-rate or
performance improvement is asserted.

Cancellation releases the full scheduler binding and its lease, not merely its
ECS marker. A failed release retains its reservation and faults the owner.
HierarchicalNavigationSystem implements IDisposable. MatchRuntime disposes it
before disposing its owned JobScheduler; borrowed Jobs remain host-owned.

## Authoritative state, derived payloads and recovery

Authoritative schema advances from 14 to **15**. Envelope 1 and payload 11 remain
unchanged. Earlier authoritative checkpoints are explicitly incompatible;
historical hashes are never rewritten.

The routing checkpoint records scheduler admissions/sequences, policy ledger,
waiting ages, original bindings, exact snapshot keys and pinned penalties,
concrete core progress, arena initialization cursor and combined consumed work.
Concrete decoded work must match the scheduler ledger. Missing or inconsistent
continuations fail instead of restarting with a fresh budget.

Route payloads are derived from immutable geometry. However, cache lookup/copy
work, LRU chains, expiry and hits can affect completion ticks. The existing exact
cache checkpoint therefore retains those execution-relevant contents and order.
Arena bytes are scratch: logical initialization progress is retained, and pool
warmth cannot change the units charged. Pool allocation statistics and per-viewer
diagnostic counters are observational.

Normal Save/Load/Replay continues to reconstruct from the original commands and
configuration. It replays the same admissions, grants and cache accesses, then
compares the complete authoritative hash. It does not inject half-restored
services from a file. For in-process state transfer, restore coordinator cache
storage at an empty, joined pool boundary before restoring the scheduler with an
exact immutable-snapshot resolver. Pool storage itself may be newly allocated;
the restored initialization prefix preserves logical progress.

Authoritative projection uses explicit data DTOs, including copied byte arrays
for encoded continuations and cache slots. It does not give reflection-based JSON
serialization the span-constructor checkpoint helper types. This also corrects
the ReadOnlySpan<CongestionMemoryEntry> metadata failure seen in baseline CI.
The existing 128-MiB document limit and atomic replacement still govern saving;
oversize or incompatible data does not replace a previous valid save.

## Copied diagnostics

The existing Developer navigation panel and bounded export now report the
selected viewer's retained/active/waiting searches, oldest scheduler wait,
work consumed this tick, current retained-request work, reserved workspace
bytes, arena rents/reuses/returns and actual cache hit/miss/write/deferral counters.
Selected requests distinguish admission waiting, budget queuing and search work.
Per-vehicle observations remain capped at 128 and expose an explicit completeness
flag. Current owned fleet totals remain available when that cap omits the selected
vehicle; observation truncation does not erase the aggregate scheduler counters.
These observations are copied at the current tick and cannot mutate scheduling.

Other players' requests/costs are not counted in the viewer's observations.
Shared cache bytes are deliberately reported unavailable as an owned-vehicle
observation; configured global capacities remain visible. Timing/allocation
instrumentation and regional publication stay unavailable. Scheduling availability
is advertised only after actual composition.

## Validation and manual handoff

The implementation uses product compiler targets only. No tests are created,
changed, built or manually executed; no smoke, benchmark, client/headless/native
execution, workflow dispatch or rerun is started. Automatically triggered
repository CI remains independent, with every existing gate unchanged.

Manual acceptance remains outstanding: mass orders above active and retained
capacity; aged requests under continued arrivals; identical same-tick replacement;
destruction and owner/service changes while queued or active; new geometry and
cost epochs; cancellation and normal shutdown; cold/reused arenas; cache misses,
hits, deferrals and expiry; zero-progress/work/memory limits; and Save/Load/Replay
with active and waiting requests. Natural match completion and narrower-road
behavior retain their existing open acceptance boundary.

Status: implemented in the connected Cargo/Supply routing scope; manual user
approval and required current-head CI assessment remain outstanding.
