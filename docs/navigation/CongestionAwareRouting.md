# Congestion-aware routing preparation

## Delivery state and prerequisite

This change implements **three independent internal preparation components**.
It does not implement the resumable hierarchical search kernel or connect a new
route-selection service. Existing gameplay still uses the original navigation
pipeline. No route, movement transform, inventory, fuel balance, queue or dock
reservation is changed by these components.

The inspected master is `cdb60b1a926e770f25eb1ec350548bb00349ff49`, version
`0.1.472-dev`. Navigation recovery [PR #354](https://github.com/DaveBeusing/Forgeline/pull/354)
and adjacent technology/rescue [PR #350](https://github.com/DaveBeusing/Forgeline/pull/350)
are already merged. Shared-contract [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356),
head `df9290ead9bd09b608a1720d992fac0163d8598a`, is still open at preparation.
Its revision-1 search, snapshot, clearance and binding declarations are absent
from master. They have not been copied, imported or redefined here.

The independent work starts from master. Its single PATCH increment is 472 to
473, not an allocation of the later integrated version. After the prerequisite
lands, synchronize this same branch with current master and reconcile exactly
one PATCH again before integration. Do not start a competing implementation.

| Area | Current delivery |
| --- | --- |
| Sustained observed congestion and deterministic decay | Internal policy implemented; no observation publisher connected |
| Bounded deterministic endpoint enumeration | Internal probe cursor implemented; no reachability claim |
| Stable route-choice scoring and hysteresis | Internal policy implemented; no shared result or fuel adapter connected |
| Resumable hierarchical search, clearance, reconstruction | Pending prerequisite and implementation |
| Full target binding and fuel authorization | Pending shared-contract and existing-authority integration |
| Actual refined corridor and selected-alternative result metadata | Pending kernel/result integration |
| Fleet scheduling, world publication, central caller changes | Separate integration ownership |
| Save/replay activation and manual acceptance | Pending |

No new public API, project, dependency, test, workflow, save schema or composition
registration is introduced. The source files remain in the two routing ownership
directories. The only existing file changed is `Directory.Build.props`.

## Congestion history and soft costs

[`CongestionPenaltyMemory`](../../src/ForgeLine.Game/Navigation/RouteSelection/CongestionPenaltyMemory.cs)
is internal to Game. One instance belongs to one opaque knowledge scope and one
unchanged grid coordinate space. The producer must filter observations according
to that faction's knowledge, aggregate vehicles into unique cells, validate the
cells against the pinned grid, and sort by Z then X. A physical collision frame
must never be published as faction-visible congestion or diagnostics.

`Record(scope, tick, observations)` accepts one complete aggregate batch at each
strictly increasing recorded simulation tick. Invalid/duplicate/unsorted input
is rejected before changing history. Kinds are `Clear`, `TrafficDelay` and
`DockQueueWait`. Missing observations, skipped simulation ticks and changes of
kind break evidence of continuous delay. They do not invent newly observed
traffic. Clear observations and missing samples allow existing costs to decay.

Activation requires at least two consecutive observed ticks. Dock FIFO waiting
has a strictly longer configured grace period. Let n be the number of consecutive
observations and A the applicable activation threshold. The activation term is
zero for n < A; afterwards it is:

`maximumPenalty * min(1, (n - A + 1) / rampTicks)`.

The retained penalty is the maximum of that term and the previously observed
penalty after decay. A lookup at age d returns
`max(0, retainedPenalty - decayPerTick * d)`, or zero once d reaches expiry.
All terms are finite, nonnegative and bounded. This cost never makes a traversable
cell a permanent terrain blocker. FIFO grace does not grant access to the dock.

Storage uses a fixed-capacity sorted array. Existing cells receive the current
observation before eviction priorities are compared. Capacity pressure evicts
the oldest observed tick, then the larger Z/X cell; ties favor smaller cells.
`Record` returns the number of delayed-cell observations omitted by capacity.
The future publisher must retain that information as incomplete cost coverage;
absence of a penalty is not evidence of a physically clear corridor.

`GetAdditionalCost(scope, cell, tick)` is a pure lookup, including future decay.
It does not advance observation time or change activation history. Earlier reads
and mismatched knowledge scopes are rejected. No cost revision is allocated by
this helper; publication owns that shared revision.

Settings have no operational defaults or measured tuning claims:

| Setting | Constraint and purpose |
| --- | --- |
| `MaximumCells` | Positive fixed storage capacity |
| `MaximumObservationsPerTick` | Positive aggregate input bound |
| `ActivationTicks` | At least 2 consecutive observations |
| `DockQueueGraceTicks` | Strictly greater than ordinary activation |
| `RampTicks` | Positive ticks to reach the penalty cap |
| `ExpiryTicks` | Positive maximum observation age |
| `MaximumPenalty` | Positive finite soft-cost cap |
| `DecayPerTick` | Positive finite tick decay |

Publication work is bounded by those configured capacities, but is not a search
`Advance` implementation. Validation, lookups, compaction, insertion/eviction,
checkpoint copying and shared snapshot publication must be budgeted at their
own integration boundary. Do not hide a complete `Record` call in one search
work unit. No scheduler, route cache or traffic-reservation system is created.

## Endpoint probes and accounting

[`EndpointCandidateCursor`](../../src/ForgeLine.Navigation/Routing/EndpointCandidateCursor.cs)
is internal to Navigation. Construction captures a grid, matching geometry
version, capabilities, original destination, maximum horizontal projection
distance and candidate cap. Invalid/out-of-world input is rejected. The owner
must obtain the grid and version from the same approved snapshot; the legacy
grid itself carries no revision. A stale owner must discard the cursor.

`MoveNext(inspectionAllowance, out probe, out workConsumed)` inspects the original
destination first. It then enumerates cell centers in deterministic square rings,
with stable perimeter order. Each original/cell inspection or empty perimeter-side
transition consumes one work unit. Out-of-grid spans are clipped with constant
arithmetic rather than walked coordinate by coordinate. Zero allowance advances
nothing and still reports an already terminal state. Work never exceeds allowance.

The existing grid's terrain/static-blocker/slope predicate is reused. A final
squared XZ distance check enforces the projection radius; the radius only bounds
proposals, not connection lengths or physical access. The exact target remains
separate from every alternative probe. Maximum candidate count and radius are
explicit inputs; future composition must keep the shared candidate limit within
the route-choice policy's capacity.

| Cursor result | Meaning |
| --- | --- |
| `Pending` | More enumeration work remains |
| `Candidate` | One geometrically unproven probe; not a route or arrival |
| `EnumerationComplete` | The configured coordinate domain was inspected |
| `CandidateLimitReached` | Candidate budget stopped enumeration; incomplete |

None of these results means that the requested destination is unreachable. Even
complete enumeration is only evidence about proposed cells, not completed route
searches. The kernel must prove connectivity from the current reachable region,
real start/end connectors, oriented vehicle clearance, slope, diagonal clearance,
goal tolerance and any heading requirement. This cursor does not project a start
pose, jump an obstacle or permit an unproven `LocalApproach`.

`CaptureState` returns a value-only cursor state. `RestoreState` validates the
pinned grid metadata/version, capabilities, original target, limits and cursor
bounds before replacing progress. This is an internal piece of the future kernel
checkpoint, not a new shared checkpoint protocol or persisted save format.

## Route scoring and hysteresis

[`RouteChoicePolicy.Evaluate`](../../src/ForgeLine.Game/Navigation/RouteSelection/RouteChoicePolicy.cs)
is an internal stateless numeric policy. The future adapter must first prove that
all assessments belong to one complete entity/order/command/dock/queue binding,
the same knowledge scope, geometry and authorized goal. Full entity generation
and actual command generation are required; a tick or a target-position hash is
not a replacement. This policy deliberately does not duplicate the pending shared
`NavigationOrderBinding` contract.

Each candidate has a nonzero stable producer identity and finite, nonnegative
travel, congestion, checked connection and checked exit costs. Their finite sum
is the score. Candidate IDs must not depend on array order. The policy rejects
duplicates, caps a batch at a configured value of at most 16, and resolves equal
candidate scores by stable ID. That cap is a structural limit, not measured tuning.

Only `Eligible` candidates participate in selection. This status requires proof
of physical legality, complete connectors to the unchanged authorized target,
and the applicable existing Game fuel authorization. Other states explicitly
retain incomplete work, blocked geometry, pending fuel or insufficient fuel.
The policy does not calculate fuel, validate a sweep, grant a dock or issue movement.

All assessments, including the current choice, must be evaluated at the supplied
current tick and cost revision. Completed multi-tick results therefore require a
fresh, budgeted assessment against a coherent valid cost/fuel view. Merely changing
their timestamp is insufficient. Stale/incomparable batches wait without erasing
the current route. A cost-only revision never directly invalidates its geometry.

For a feasible current route, switching requires its configured minimum hold time
and a strictly positive score improvement at least as large as both the absolute
threshold and `currentScore * relativeThreshold`. Equal-cost ties do not churn an
active route. A proven hard geometry blockage bypasses hold/improvement requirements
only for an otherwise eligible replacement. Congestion alone cannot claim that state.

An active route with pending fuel authorization waits. If it has insufficient
fuel, an affordable alternative still needs the documented hold/improvement
criteria; otherwise the decision is `AwaitFuel`, even if that alternative is
physically legal. This conservative rule does not treat financial infeasibility
as a hard geometry blockage. Integration must not silently broaden this policy or
grant fuel to bypass it.

`SelectAlternative`, `KeepActive`, `AwaitMoreWork`, `AwaitFuel`, `InvalidInput` and
batch-local `NoFeasibleAlternative` remain distinct. No result claims global
`Unreachable`. Incomplete exploration/candidate caps remain incomplete, and missing
active assessments do not silently select a replacement.

The caller owns and persists active alternative identity and its accepted selection
tick under the full binding. It changes them only after a selection is actually
accepted. Pending/invalid decisions preserve waiting orders, loaded cargo and
existing reservations. No state is retained inside the policy itself.

## Existing-authority fuel handover

The current `HierarchicalNavigationSystem.ApplyCompletedResults` invokes
`SupplyRescueTravel.ValidateCompletedRoute` and
`CargoDeliveryFuelPolicy.ValidateCompletedRoute` before exposing waypoints.
These remain the authority. Both currently accept the legacy `NavigationPath`.

In particular, Cargo validation removes its prepared `CargoDeliveryFuelBudget`.
Calling it once per candidate would consume the budget on the first call; later
calls can return true when no matching budget remains. **Do not use repeated
validation calls as independent candidate approvals.** The integration owner must
provide a coherent non-mutating assessment through the existing authority and one
final accepted-route authorization, or retain a single-candidate authorization
boundary without claiming that other candidates were assessed.

The completed route must include real checked start/destination connections and
the necessary queue/dock approach/exit segments. Existing fuel methods currently
measure legacy waypoints and the original endpoint; an unchecked straight tail
cannot establish a connector's physical legality. A future adapter must preserve
Cargo ownership, reserve policy and recovery recipient identity, and refresh any
required same-tick Supply/Rescue budget. No second fuel calculator is added here.

## Required kernel implementation and delegation

After the shared prerequisite is integrated, implement the existing revision-1
`INavigationSearchKernel<TCheckpoint>` / `INavigationSearchSession<TCheckpoint>`
against `NavigationWorldSnapshot`, `NavigationSearchSpecification`, geometry and
the agreed workspace ownership. Do not invent shadow public contracts. Confirm
workspace leasing and the ownership of refined-corridor/alternative metadata with
the common interface owner before exposing new cross-module signatures.

The full state machine is still outstanding: high-level expansion, corridor
construction, local refinement, expanded corridor, bounded general fallback,
candidate reachability, swept clearance, predecessor traversal, waypoint/actual
corridor reconstruction and immutable result publication. Charge all work, including
child work exactly once, and retain the request-wide cap across every tick. No
synchronous `FindPath` may hide inside a small `Advance` step. Reuse terrain/slope
and diagonal rules, stable frontier ties and prepared graph topology.

Only exhausted admissible search in valid geometry may return `Unreachable`.
Work, candidate or workspace exhaustion must remain `BudgetExhausted`/incomplete.
Geometry changes invalidate affected sessions; cost changes preserve physically
valid routes. Return the actual refined corridor and traversed dependencies,
geometry/cost revisions, original/resolved goal, effort and selected alternative.
The initial high-level corridor is not proof of the actual fallback route.

| Existing boundary | Central integration action |
| --- | --- |
| `HierarchicalPathfinder.FindPath` | Delegate to the one completed kernel or retire the old orchestration after caller migration |
| `HighLevelRouteSearch.TryFindRoute` | Replace its synchronous expansion/reconstruction with resumable stages; no second permanent algorithm |
| `LocalPathSearch.TryFindPath` | Move refinement/fallback into resumable work while preserving traversal and diagonal rules |
| `LocalPathFrontier` | Reuse/extract storage only through agreed exclusive workspace ownership; no separate pool/cache here |
| `NavigationSectorGraph` / `NavigationGrid` | Reuse immutable graph/grid queries; graph preparation belongs to publication work |
| `NavigationRouting.ScheduleRequest` / `Compute` | Admit the shared bound request through the scheduler; no second global queue |
| `NavigationRouting.ApplyCompletedResults` | Validate full binding/revisions, approved goal and current fuel before adopting a route |
| `VehicleDockingService.Approach` / `Exit` | Preserve the same building, FIFO entry, exclusive slot and exact service pose |
| `CargoTransportSystem` / `SupplyRescueTravel` | Retain current source/recipient, carried inventory and recovery ownership |

No method in that table is changed by this preparation. The existing navigation
pipeline remains the only active routing authority.

## State, ordering and continuation handover

After prerequisite integration, the required order is: capture authorized traffic;
update bounded congestion history; publish the shared immutable costs/world;
admit a fully bound request; advance the one kernel under scheduler allowance;
prove connectors/clearance; refresh coherent cost and existing-authority fuel
assessments; evaluate preference; revalidate ownership and finally authorize the
accepted route before the normal movement phase. Only `GroundMovementSystem`
changes final transforms. Arrival and exact dock service remain separate checks.

Congestion checkpoints contain revision 1, exact settings, scope, last recorded
tick and detached sorted entries with delay-start/last-observed ticks and retained
penalty. Restore rejects inconsistent timing, order, capacity and activation/cost
history before mutation. The integration owner must additionally validate content
and world-coordinate identity and replace instances when that identity changes.

Endpoint cursor state belongs in the future kernel checkpoint alongside frontier,
predecessors, reconstruction cursors, alternative progress and consumed work.
Selection identity/tick belong to the Game binding. These records are currently
unregistered and do not change saves. Central integration must assess/persist
authoritative state before enabling them; losing history on load would alter
activation, hysteresis and simulation decisions.

Continuation: integrate/review PR #356 through normal repository policy, synchronize
this branch from master, implement the missing kernel/adapters in the owned paths,
then hand off the above caller edits to central integration. Full interface revision,
base/HEAD SHAs, version reconciliation and current evidence belong in the PR.

## Compilation and acceptance

The required final commands are:

```powershell
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release
```

Local Release compilation is **not executed**: the available environment has no
`dotnet` executable. The repository continues to require SDK 10.0.401 with its
existing latest-patch policy, C# 14, analyzers and warnings-as-errors. No settings
were relaxed. Existing automatically triggered PR CI may supply separate source-
matched evidence; it is not a manually requested test run or gameplay acceptance.

No automated tests were created, modified or manually executed. No benchmark,
soak, smoke, headless or visual-reference execution, replacement test script,
additional workflow, dispatch or rerun was requested. Existing tests and CI gates
are unchanged. No performance improvement is measured or claimed.

Manual acceptance remains pending after complete integration: short FIFO queue
without detour; sustained observed blockage with a legal alternative; reopening
without oscillation; nearest endpoint disconnected but a farther one reachable;
fully enclosed goal; invalid start connection; insufficient fuel including real
connector/exit distance; replacement command during a suspended search; and
save/restore retaining congestion age and active-route hold time.

**Status: independent components implemented / local build not executed /
full kernel and overall integration pending / manual acceptance outstanding.**
