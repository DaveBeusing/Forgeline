# Snapshot publication qualification

Status: **implemented, not yet accepted**. Branch `performance/snapshot-publication`,
PR [258](https://github.com/DaveBeusing/Forgeline/pull/258). Base is current master
`e42f4e7537b84c56c9dbdafe56f03f1e5e74270d`, 0.1.419; the isolated version commit
sets exactly one PATCH delta to 0.1.420. No tag, release or master merge is performed.

## Integration and ownership

The integrated parallelization foundation and explicit release in the ownership manifest
were verified before implementation. Initial master was
`f2be6532d759ab0840f2a4674c28d00446fe75ea`, 0.1.418. PR 256 subsequently merged;
its current master was synchronized preserving history. PR 257 owns cargo fuel service
and does not overlap functional snapshot files. Closed historical PRs 94/98/100 were
checked for superseded work; this branch does not revive them.

All functional changes remain in the assigned snapshot/publication files and prefixes.
The ownership validator checks the complete functional path list separately from the
explicit serial `Directory.Build.props` exception. That file changes only PATCH
419→420. The graph validator reports 46 projects, no cycles and preserved architecture
boundaries. Public formats, frame/thread/fence contracts, queue count, dependency graph,
visibility authority, GPU resources and workflows remain unchanged.

## Delivered behavior

See [Decision](Decision.md) for ownership, cache invalidation and retention bounds.
Correspondence is O(n+m), including private normalization for arbitrary current order.
The public order never changes. Entity identity always includes generation; ECS, static
dressing, VFX, decal and wreck identities were audited. Decals follow VFX, so production
can contain two ascending runs rather than globally sorted identities. The nonmonotone
predecessor adapter preserves the precise hits, misses and duplicate ties of the prior
binary search, including unreachable keys; it does not silently change interpolation.
All interpolated transform, pose and wrapped yaw fields are compared against that reference.

Combat capture still inspects every eligible owned member and operational field, shares
only completely equal immutable payloads in the same session, and publishes fresh tick
envelopes. Other mutable/intelligence/selection/alert dependencies remain freshly captured.
Filtered instance extraction reuses only private producer scratch and always publishes a
fresh exact-length owned array. Old snapshots remain valid indefinitely.

Host publication compares complete input and shares only engine-owned immutable defensive
copies, bounded per producer thread. Mutating caller arrays, retained/blocked readers,
concurrent producers, shutdown, failed copying and full generation changes are exercised.
The first equality-cache profile saved allocations but regressed CPU time; commit
`5ab7b3cea21c864e1825874086cb425d0002b35e` replaces element dispatch for unmanaged
line/identity channels with conservative bitwise comparison. Distinct floating-point
representations copy again; no input bits are lost. Labels keep complete value comparison.

## Executed checks

Product source `6db272ce089b2a298e32ec14bbcd6b68ce8f1867` passes the final Release
solution build/publish with zero warnings/errors. Its complete four-cohort CPU matrix
passes all profile/frame/combat processes; six fresh native pairs are exact. Extended
512-tick warmup profiles pass too, with an independently pinned probe and pure-data
summary recovery after the retained option-splat error. No product source changes
after this measured revision. See Measurements for every timing limit and failure.

Earlier product source `29bebfdc6ca59c6b98789a9a6e110e4cf263fdb9` passed Release solution
build with zero warnings/errors, runtime asset validation, graph and version checks.
The later profile-only source `f797af6f65361949fa1017ce2387026ec41628f6` also passed
Release solution build with zero warnings/errors. The refinement source above passed an
isolated Release Client/test build and all seven dedicated publication/retained-reader
cases, including NaN payload and signed-zero caller changes. No additional references
or package changes were needed.

The complete solution invocation at f797af6 finished: **2,971 total, 2,949 passed,
5 failed, 17 skipped**, exit 2. The Presentation project repeats twice with 1,417 total / 1,402 passed / three failed /
12 skipped, exit 2 each. The Client project repeats twice with 451 total / 448 passed /
one failed / two skipped, exit 2 each. SelectedCombatGroup passes twice in isolation,
15/15 each, exit 0. Results are retained rather than
calling a failed invocation green. Snapshot capture/correspondence production files are
unchanged between 29bebfd and the refinement; the latter changes only Client publication.
Two complete refined Client direct-runner invocations finish with 452 discovered cases,
respectively two and one failures, and two explicit native-audio cases not run. The
first includes an intermittent frozen settings failure described below.

Earlier CPU cohorts overlap a subsequently reported concurrent solution run and are
unqualified for isolated CPU timing; their raw data is preserved. A coordinated
replacement and extended warmup comparison are now complete with zero detected
foreign activity in 105 and nine bounded process observations. Thread allocation windows,
retained payload counters,
source/catalog hashes and native frame/resource observations are in
[Measurements](Measurements.md). Hardware images match exactly; no global FPS or
unqualified consumer latency improvement is claimed.

## Open gates and precise serial handoff

These frozen paths require serial work. They are not modified in this PR and are not
hidden by wider budgets, disabled tests or accepted goldens:

| Gate | Observed result | Responsible frozen surface |
|---|---|---|
| Selection geometry | Presentation `WorldMarkersUseDistinctGeometryForSelectionHoverAndInvalidTargets`, line 547: expected 24 lines, actual 8 | `RtsInformationLayer` geometry and its existing test |
| Notification motion | `IdentityRevisionSessionClockAndReducedMotionDoNotRetainStaleVisuals`, line 82: expected 1, actual 0 | notification motion state/revision logic |
| Operations privacy | `NativeOperationsRejectsStaleFactsAndSelectionNeverCachesRetiredFacilityData`, line 171: Assert.True false | Operations view/renderer and existing native test |
| Controls listing | Client `ControlsPresentationListsGameplayBindings`, line 76: expected 49 entries, actual 53 | frontend controls presentation and its count contract |
| UI feedback allocation | complete solution only: `WarmCopiedFeedbackRetainsZeroAllocation`, line 138, expected 0, actual 3,344 bytes; repeated refined direct Client suites pass this case | `UiInteractionFeedback.Gameplay` / its frozen allocation measurement; no snapshot capture/index/publication is invoked in this measured method |
| Paused settings | first refined full Client run: `ApplyUpdatesReadableUiImmediatelyAndPersistsNextMatchGraphicsWithoutResettingMatch`, line 22, applied result null; next full run and baseline class pass | settings controller/store test; no render publication is invoked |

The first three reproduce directly on clean e42f4e7: 38 class cases, three failures.
Controls reproduces there too: 19 Client class cases, one failure. The intermittent
feedback/settings outcomes have no established root cause; they remain separate open
qualification findings rather than asserted snapshot regressions or invented fixes.

Frame-contracts CI at product 29bebfd fails in
`InteractionHotPathMeasurements.Run`, line 46. The unchanged benchmark expects
`selected*24+22` lines; 24/36 cases produce `selected*8+22` (30 for one, 8,022 for 1,000).
All 36 report zero allocation, no dropped lines and at most one draw. The same 24/36
failure is reproduced by the e42f4e7 benchmark, exit nonzero. A separately authorized serial UI repair owns these four preexisting UI surfaces;
this branch does not modify them. The repair is tracked in PR 260 and is not yet
part of this branch’s master base. The serial owner must
reconcile the renderer's required geometry and both frozen test/benchmark contracts,
then re-run frame-contracts. This PR does not decide that unrelated UI contract.
Current main CI at 29bebfd fails earlier in Graphical economy work states and operations
privacy contracts; later main gates are skipped. Navigation and CPU-scale CI pass;
the hosted native-reference job is explicitly skipped. Final-head CI is still required.

## Historical combat allocation report

The reported 8,104-byte SelectedCombatGroup failure is **not reproduced** in the initial
baseline's full Presentation run or current isolated class. The old zero-allocation
`RenderAndControlsShareBoundedGeometryWithZeroWarmAllocation` from the analysis anchor
was removed with the obsolete inspector in PR 242 (`09425dc`); current summary geometry
already has a 512-byte/operation bound. That existing bound is not widened. New warmed
measurements exclude fixture setup, use the current producer thread and validate the
counter with a deliberately allocating 4-KiB positive control. Tiering stays enabled by
default; process GC counts are not attributed to an operation. There is no proven
historical root cause or claim to have fixed an absent renderer. Historical error closure
remains open pending an authoritative reproduction/resolution by the responsible surface.

## Consumer allocation observations

The coordinated ratio-7 candidate-A window at tick 103 records 7,920 bytes; the extended
warmup records 3,528 and 6,712 at ticks 583 and 635. Candidate-B and ratios 1/3 are zero.
Retained index/visibility capacity stays 4,546 bytes; these are not explained by observed
scratch growth. Their origin remains unestablished and requires allocation-stack
attribution over the exact update/interpolation window. No assertion links them to the
historical 8,104-byte failure, and no tolerance or tiering switch conceals them. Raw
samples remain in Measurements. This is a further open allocation/performance finding,
not a claim of global zero-byte gameplay or render frames.

## Remaining acceptance

The current canonical refined Client project has 452 total / 449 passed / one failed /
two skipped (exit 2), matching the preexisting controls failure. Clean baseline Client
has 442 total / 439 passed / one failed / two skipped (exit 2). Feedback/settings classes
pass on both sources (18/18 each); final dedicated scopes pass 12 Presentation and
10 Client cases. The source-pinned extended host comparison is complete; it demonstrates reduced
allocation with a small observed CPU tradeoff. Record final-head CI results before acceptance. The final native recheck passes
structurally with exact images; human review remains pending. Human review of the six images remains pending. Mixed consumer
CPU timings need a controlled performance review; improved capture/publication allocation
must not be presented as universal latency improvement. Attribute the intermittent consumer counters, reconcile the frozen UI gates
and historical allocation report serially, then re-run mandatory workflows before merge.
No automatic merge, tag or release is authorized by this qualification.
