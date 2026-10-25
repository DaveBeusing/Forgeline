# Navigation scheduling handoff

## Delivery status

Typed scheduling, bounded workspace ownership, route caching and state transfer are
implemented against the merged shared contracts. Central gameplay integration and
manual acceptance remain outstanding; these services are not registered at runtime.

- Branch: `performance/navigation-search-scheduling`, targeting `master`.
- Pull request: [#360](https://github.com/DaveBeusing/Forgeline/pull/360).
- Synchronized master: `78e889d471435477c6b03ad866a572c64a3f262c`, including regional
  update preparation #361 without changes to its owned source.
- Shared foundation: [#356](https://github.com/DaveBeusing/Forgeline/pull/356), merged;
  `NavigationIntegrationContract.Revision` is **1**.
- Version: master **0.1.474-dev**; branch **0.1.475-dev**, one PATCH increment per PR.
- Final typed-source Release compilation: **BLOCKED by three existing regional
  CA1512 errors on synchronized master**; both required commands were executed.
- Overall integration: outstanding. Manual acceptance: outstanding.

The PR description records the final source HEAD and current ordinary CI results.
Complete APIs and configuration are in
[NavigationSearchScheduling.md](NavigationSearchScheduling.md).

## Owned source and implemented behavior

| Owned files | Implemented behavior |
| --- | --- |
| `Scheduling/NavigationSearchScheduler.cs`, `NavigationSearchScheduler.State.cs` | Typed scheduler; bounded staging/admission and active sessions; pre-worker memory reservations; existing job barrier; ordered work settlement/publication; cancellation and restore validation |
| `Scheduling/NavigationSchedulingPolicy.cs`, `NavigationSchedulingPolicyState.cs` | Fixed work slices, service aging, stable sequences, retry timing, request work ledger and detached policy checkpoint |
| `Scheduling/NavigationSchedulerCheckpoint.cs` | Immutable identity/configuration/request metadata, exact snapshot keys, bounded encoded kernel state, versioned codec and complete-payload decoding |
| `Workspaces/NavigationWorkspacePool.cs`, `NavigationWorkspaceLease.cs`, `NavigationWorkspaceStatistics.cs` | Fixed byte arenas; request/active/retained byte limits; coordinator reservation; cooperative initialization; bounded trimming; late-return-safe disposal and counters |
| `Caching/NavigationResultCache.cs`, `NavigationResultCacheState.cs`, `NavigationRouteCache.cs` | Copied bounded payloads, exact typed keys, confirmed negative results, proven regional reuse, deterministic LRU/maintenance and cache state capture/restore |
| `Directory.Build.props` and the two scheduling documents | One PATCH increment and integration handoff |

`Scheduling/` is under `src/ForgeLine.Game/Navigation/`; `Workspaces/` and `Caching/`
are under `src/ForgeLine.Navigation/`. New implementation types remain internal.
Shared declarations, existing routing/movement/job callers, central save layouts,
project dependencies, tests and CI configuration remain under their existing owners.

## Composition inputs and call order

Supply a concrete `INavigationSearchKernel<TKernelCheckpoint>`, existing
`JobScheduler` when parallel work is enabled, shared `NavigationIntegrationLimits`,
`NavigationSchedulingPolicyLimits`, `NavigationSchedulerStorageLimits`, and
`NavigationSchedulerIdentity`. Supply the service-class selector, exact immutable
snapshot resolver and versioned `NavigationSchedulerCheckpointCodec<TKernelCheckpoint>`.
The codec must validate its own concrete kernel schema and consume the whole payload.

Configuration values are explicit and unmeasured. Select a per-request
`MaximumWorkspaceBytes` appropriate for concurrent sessions: the shared read adapter
initially copies the fleet limit into that field. Central integration must replace
that value with a valid per-session peak within the reduced active allowance.
Reserve the full declared request peak before creating its session.

Set `NavigationSchedulerStorageLimits.ReservedPoolBytes` to at least the shared
pool's `RetainedReservationBytes`, including fixed overhead and maximum retained
arenas. The scheduler's active allowance is `MaximumSearchBytes - ReservedPoolBytes`;
submission, admission and restore all enforce it. Each active peak includes its
arena and all session/output/transient storage. If composition explicitly partitions
pools, sum their reserves and active budgets rather than assigning each the fleet cap.

1. At command acceptance, assign and persist the real checked command generation.
   Same-tick identical-target commands must still supersede earlier ownership.
2. At the navigation boundary, publish coherent snapshots with `UpdateSnapshot` and
   call `Submit` with complete entity/order/request/dock/queue generations. The host
   validates current owner and authoritative binding before submission/publication.
3. Call `Cancel` for invalidated bindings without releasing physically occupied
   dock slots, FIFO ownership or corridors through this service.
4. Call `Advance(tick, workAllowance)` once per increasing simulation tick. Admission
   uses that tick; all session reservations are prepared before any worker starts.
   Settle actual work after the existing barrier, then consume stable ordered results.
5. Preserve the original order and cargo under backpressure. Continue a still-safe
   route remainder or wait; only the existing movement authority commits transforms.

Zero-progress pending work becomes budget-limited and releases its session. A later
bounded retry uses a newer request ID, preserves command identity/age and honors retry
timing. An aged large memory request prevents smaller new admissions from repeatedly
filling the capacity it needs; existing active sessions continue receiving work.

## Workspace and cache wiring

Use one shared pool across the world's capability/profile kernels. Acquire through
`TryReserve(request, out lease)` in coordinator order before dispatch. In the kernel's
`Advance`, charge `AdvanceInitialization(maximumBytes)` and its exact returned byte
count before using `Buffer`. Fresh and retained arenas have the same logical cursor.
Keep all other session allocations within the request's remaining declared peak.

Do not recycle a lease on cancellation until its worker has stopped. At world or
match teardown, stop admission, join jobs, dispose sessions and release leases before
disposing the old pool and admitting against a replacement. Failed disposal retains
the reservation and faults the scheduler instead of granting that capacity again.

The route cache is **coordinator-owned**, including reads that update LRU or counters.
Synchronous execution with `jobs: null` supports cache work charged inside the
kernel's `Advance`. Parallel integration needs a deterministic coordinator cache
phase that divides the same granted request/tick allowance and supplies owned
read-only inputs to jobs. Do not invoke the same mutable cache from parallel kernel
`Advance` calls or hide work in `CreateSession`, which has no work allowance.
The current scheduler/shared session interface does not supply that parallel cache
phase or replace existing route/graph caches; the concrete adapter remains pending.

Cache keys include world/algorithm/contract identity, exact start/target/constraints,
capabilities, actual vehicle profile, knowledge scope, work/memory/candidate limits,
grid/sector configuration and applicable geometry/cost/availability revisions.
Regional reuse requires the kernel's complete dependency proof, including connectors;
missing stamps select global geometry identity. Negative results stay globally pinned.
Budget-exhausted, canceled and stale searches do not become unreachable cache entries.

## Capture and restore boundary

Capture only after the worker barrier and ordered settlement. Scheduler checkpoints
preserve stable sequences, complete request bindings, queued/ready/retry ages, consumed
work, remaining tick/admission state, configuration/identity and encoded active kernels.
Snapshot keys preserve capabilities, knowledge scope, geometry/cost revision and cost
availability; the resolver must supply the exact original immutable publications.

Restore validates contract/algorithm/codec/configuration/content/world identity,
capacities, policy/request correlation and cumulative request work before replacing
live state. Missing snapshots reject restoration. Current unfinished work with a
nonzero cursor requires its active frontier; it cannot silently restart from scratch.
Old sessions release before restored reservations are granted. A deep decode or kernel
restore failure faults the service; no resumed simulation or fabricated retry follows.

The checkpoint cap covers conservative metadata, temporary and owned outer arrays,
serialization scratch and retained encoded payloads. It does **not** prove the peak
allocation of arbitrary `TKernelCheckpoint` objects from `CaptureState()`, codec
delegates or `RestoreSession()`. Live frontier plus detached state can coexist. A
concrete kernel must bound those peaks within reserved memory; otherwise a shared
bounded serialization/reservation contract is required before enabling persistence.
Do not suspend generic frontiers into checkpoints merely to free admission slots.

Restore cache payloads, keys, exact hash chains, slot/free/LRU order, expiry, clock, maintenance cursor
and remaining maintenance allowance when cache readiness affects publication timing.
The cache API requires a separate checkpoint/restore allowance in addition to live
storage. Central integration owns durable schemas and cross-domain capture ordering.

## Diagnostics and pending validation

Consume scheduler `Diagnostics`, policy diagnostics, pool `GetStatistics()` and cache
`GetMetrics()` into bounded viewer-filtered diagnostics. Expose queued/active counts,
maximum wait, started/continued/canceled/stale work, actual consumed/remaining work,
reserved bytes, active/retained arenas, request/memory rejection counts and cache
hits/bytes. Do not forward physical or hidden enemy information to presentation.

| Final typed-source compilation | Result |
| --- | --- |
| `ForgeLine.Navigation` Release on synchronized master | Failed: 3 CA1512 errors in regional source; 0 warnings; 33.93 seconds |
| `ForgeLine.Game` Release on synchronized master | Failed in the Navigation dependency at the same 3 CA1512 errors; 0 warnings; 10.17 seconds |

Executed commands, using SDK **10.0.401**, C# 14 and unchanged analyzers,
warnings-as-errors and build targets:

```text
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release --disable-build-servers -m:1
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release --disable-build-servers -m:1
```

The current-base errors are `RegionalChangeJournal.cs` at lines 247 and 291, and
`RegionalUpdateCoordinator.cs` at line 318. Each explicitly throws on a decreasing
tick where CA1512 requires `ArgumentOutOfRangeException.ThrowIfLessThan`. These
files arrived unchanged from merged #361 and are outside this change's ownership.
They remain unmodified. Their owner must repair the three guards before final-base
compilation can pass; no analyzer or target was disabled to bypass them.

Before #361 advanced master, the current Navigation implementation compiled against
contract master `19e237d2e53c9a95abd2f8bb192ca19a4129c683` with 0 warnings/errors
in 14.73 seconds, after correcting two CA1512 findings in the owned cache files.
That earlier-base result is not a successful build of the final synchronized source.
To check the Game implementation without hiding the new dependency failures, a
separate compilation directory used exactly that prior master plus the current owned
C# source. Its normal Game Release build, including Navigation and all dependencies,
succeeded with **0 warnings and 0 errors in 78.81 seconds**. No project targets or
analyzers were changed. This is explicitly diagnostic evidence against the prior
basis, not a green final-base build; the submitted branch retains all #361 files.
Single-node execution with build servers disabled avoids the earlier build host's
parallel restore abort; these flags do not change targets or diagnostic severity.

No automated tests were added, changed or manually run. No benchmark, smoke, soak,
headless, client or visual-reference execution is part of this validation. Existing
ordinary CI is unchanged; no extra workflow, dispatch or rerun is requested.
Compilation alone does not establish gameplay correctness, measured performance or
replay equivalence. Manual acceptance remains outstanding.

## Next central-integration step and manual scenarios

First repair the three regional analyzer guards through their owning change and
repeat both targeted builds on the synchronized master. Then connect the concrete
routing kernel and its bounded checkpoint codec to the scheduler
at `NavigationRequests`, sharing the pool and a coordinator cache phase. Complete
command-generation persistence and final binding checks in the existing authoritative
callers, then connect coherent world publications, state transfer and filtered diagnostics.

Manually exercise large simultaneous fleets; same-tick replacements; cancellation,
destruction and owner changes; geometry/cost/availability publication while queued;
large memory requests competing with smaller ones; zero-progress and budget-limited
searches; bounded cache maintenance and regional invalidation; and save/load with
active, waiting, deferred and stale requests. Check preserved cargo/orders, exact
dock/FIFO rules, bounded work/memory, fair progress and identical restore publication
timing. No merge, tag or release is included in this change.
