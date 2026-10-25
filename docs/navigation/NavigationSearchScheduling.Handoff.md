# Navigation scheduling handoff

## Delivery state

Independent scheduling policy and bounded storage are implemented and Release-compiled.
The complete scheduling feature is **not integrated**. Keep the PR in draft until
the shared contracts and the remaining concrete adapters are completed.

- Branch: `performance/navigation-search-scheduling`, targeting `master`.
- Initial inspected master: `cdb60b1a926e770f25eb1ec350548bb00349ff49`.
- Synchronized base: `574040b1cde354bc1414a692e2ec075968e0959a`, including #357.
- Implementation source: `ed58647abbfb3a4a3887ba41165a4ea82c8826ae`.
- Version: exactly one PATCH over the synchronized base, **0.1.473-dev to 0.1.474-dev**.
- Shared contract revision: revision 1 inspected at
  `df9290ead9bd09b608a1720d992fac0163d8598a` in open [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356).
  Those contracts are absent from the synchronized master and were not imported.
- Runtime availability: no new scheduler or storage service is registered.
- Manual acceptance: outstanding.

The PR description carries the final documentation HEAD and latest CI readback.
Source details, complete internal APIs, configuration and required call order are
in [NavigationSearchScheduling.md](NavigationSearchScheduling.md).

## Own files and completed work

| Files | Completed behavior |
| --- | --- |
| `src/ForgeLine.Game/Navigation/Scheduling/NavigationSchedulingPolicy.cs` and `NavigationSchedulingPolicyState.cs` | Bounded metadata/admission, fixed preplanned work slices, service aging, retries, same-tick supplied generation handling, stale accounting, detached policy checkpoint and validation |
| `src/ForgeLine.Navigation/Workspaces/NavigationWorkspacePool.cs`, `NavigationWorkspaceLease.cs`, `NavigationWorkspaceStatistics.cs` | Exclusive fixed byte arenas, active and retained count/byte limits, bounded trimming, late-return-safe disposal and counters |
| `src/ForgeLine.Navigation/Caching/NavigationResultCache.cs` | Owned copied values, bounded entry/estimated-byte capacity, deterministic LRU, bounded maintenance, exact-key invalidation and confirmed-negative-only insertion |
| `Directory.Build.props` | One PATCH increment only |
| This handoff and `NavigationSearchScheduling.md` | Configuration, ownership, restore limitations and concrete integration steps |

All new implementation types are internal. No shared public declarations, existing
routing/movement/job callers, project references, packages, save layouts, tests,
workflows, budgets or required checks were changed. Existing production files used
for compilation match the synchronized master, apart from the prescribed version.

## Validation

Final source compilation used .NET SDK **10.0.401**, C# 14 and the repository's
unaltered analyzers and warnings-as-errors rules on Linux. Restore was allowed.

```text
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release --disable-build-servers -m:1
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release --disable-build-servers -m:1
```

| Final synchronized source | Result |
| --- | --- |
| ForgeLine.Navigation | Succeeded; 0 warnings, 0 errors; 18.18 seconds |
| ForgeLine.Game | Succeeded; 0 warnings, 0 errors; 36.95 seconds |

The initial ordinary parallel Navigation build aborted during restore without an
emitted error; a diagnostic repetition did the same. Explicit single-node builds
with build servers disabled completed. These are local build-host execution
settings, not source/target changes or suppressed diagnostics. No root cause beyond
the observed restore failure is claimed. Both projects also compiled before the
subsequent master synchronization; the table records the synchronized results.

**Automated tests were not created, changed or manually executed.** No benchmark,
soak, smoke, headless, client or visual-reference execution occurred. Existing
automatically triggered CI is unchanged; no extra run or rerun was requested.
Compilation is not behavioral, Windows, performance or manual acceptance evidence.

## Remaining implementation and central handoff

1. Integrate the shared contract foundation through ordinary repository policy.
   Synchronize this same branch with current master and refresh the single PATCH
   increment. Do not import the foreign feature branch or duplicate its interfaces.
2. Implement `INavigationSearchScheduler<TCheckpoint>` against the actual
   `NavigationBoundSearch`, pinned `NavigationWorldSnapshot`, typed kernel/session
   and state-transfer contracts. Map explicit policy limits; no defaults exist in
   the inspected shared limits.
3. Supply real checked command generations at command acceptance and persist them.
   Validate entity generation, owner, full original order, request, dock/queue
   generations and applicable revisions on every submit and publication.
4. Compose actual bounded kernel sessions with the internal Navigation pool. Account
   for all stages, memory before allocation, initialization, candidates, clearance,
   cache lookups/copies and output construction. Bound active sessions separately.
   Prevent zero-progress sessions from occupying every active slot indefinitely.
5. Use existing simulation jobs/barriers. Allocate grants before dispatch and settle
   all actual work in grant order, including canceled/stale work. Preserve safe route
   remnants or controlled waiting and retain orders/cargo during backpressure.
6. Build complete typed cache keys and consume regional hard-geometry invalidations.
   Replace the existing route/graph caches through central integration. The generic
   cache currently supports exact keys only; it does not identify affected regions.
7. Implement combined checkpoint/restore. Policy state is implemented, but kernel
   frontiers, pinned inputs, active memory reacquisition and cache readiness/state
   are not serialized here. Restore must reach identical logical work/readiness
   before simulation resumes. Central integration owns any schema change.
8. Add session started/resumed counters to the shared diagnostics handoff, retaining
   viewer filtering. Register the services and perform the later manual acceptance.

The following mutable state requires explicit integration ownership:

| Owner | State and restore requirement |
| --- | --- |
| Command acceptance | Checked command generation and authoritative current binding; not installed here |
| Policy | Entries, stable sequences, queued/ready/retry ticks, deferred/budget status, request work, current tick reservation/actual/admission state and counters; captured at a settled boundary |
| Kernel/session | Exact frontier/candidate/clearance cursor, algorithm/configuration revision, cumulative work and immutable input identity; missing shared adapter |
| Workspace pool | Active/retained arenas and leases; never recycle before worker completion; restore frontier contents under the same global memory admission |
| Cache | Payloads, keys, LRU/slot/free-list state, expiry, clock, maintenance cursor and remaining quota when readiness affects timing; persistence adapter remains outstanding |

## Later manual scenarios

Use many simultaneous fleet commands; replace identical targets twice within one
tick; cancel, destroy or change owner with work pending; change geometry and costs
while queued; sustain high-priority arrivals; exercise zero-progress retries and
admission/workspace pressure; invalidate only affected routes; save/load with active
and waiting searches. Confirm preserved cargo/orders, exact dock/FIFO rules, bounded
actual work/memory, fair progress and identical restore publication timing.

No observed gameplay improvement, throughput gain or replay equivalence is claimed
for these unregistered components. No merge, tag or release is part of this change.
