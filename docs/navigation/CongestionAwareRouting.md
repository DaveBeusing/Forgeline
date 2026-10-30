# Connected vehicle routing and endpoint selection

[Documentation](../README.md) · [Complete catalog](../DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Product scope](#product-scope)
- [Search and result lifecycle](#search-and-result-lifecycle)
- [Observed congestion and selection](#observed-congestion-and-selection)
- [Work, ownership and persistence](#work-ownership-and-persistence)
- [Compilation and manual acceptance](#compilation-and-manual-acceptance)

</details>

## Product scope

MatchRuntime connects VehicleRoutingKernel through
HierarchicalNavigationSystem.ConnectVehicleRouting after initial navigation world
construction. Normal strategic Cargo Truck and Supply Truck orders with Wheeled
capabilities use this kernel. The existing NavigationRequests owner advances it;
GroundMovementSystem remains the only transform writer. Military/formation paths
retain the existing HierarchicalPathfinder path. No second movement system or regional storage is enabled here. Fleet admission,
arena ownership and exact bounded caching now use the connected prepared services;
see [Fleet scheduling](NavigationSearchScheduling.md).

Precise queue/dock ownership is unchanged. NavigationPrecisionOwnership excludes
heading-constrained and already precise service motion from coarse admission.
An owned first queue entry may admit DockAccess coarse travel; its projected
endpoint cannot grant a slot, change FIFO order or establish service arrival.
The existing GroundMovement handoff alone begins the exact owned local approach.

## Search and result lifecycle

A request pins the immutable grid, prepared sector graph, copied scope costs,
full movement capabilities and actual vehicle profile. Its binding retains the
complete entity generation, original MovementOrder and target, request ID,
checked monotonic root generation, dock generation and queue generation.
A new root admitted by NavigationRequests receives a new generation, including
identical same-tick replacements. Traffic/world replans retain that root generation
while allocating a new request ID. The system is bound to its simulation's entity
registry and session. Changed commands, profile, pose, grid or service generations
cancel an old continuation before it can publish movement.

Each Advance slice performs explicit charged stages:

1. Enumerate bounded start connectors without relocating the vehicle.
2. Enumerate original/alternative endpoint probes with EndpointCandidateCursor.
3. Prove real start/end connections using the conservative vehicle envelope.
4. Search the prepared sector graph with stable numeric frontier ties.
5. Reconstruct a sector corridor, refine locally, expand it by one sector if
   necessary, then attempt bounded unrestricted grid search.
6. Reconstruct the actual predecessor chain, waypoints and traversed sector
   dependencies, accounting for publication copies before returning Found.

Endpoint and start candidates, graph edges, local neighbors, clearance cell and
obstacle inspections, predecessor steps and publication work share one request
cap. Calling Advance again does not reset consumed work or memory accounting.
Candidate/work/workspace exhaustion returns BudgetExhausted with
SearchBudgetExhausted, never global disconnection. Unreachable requires exhausted
admissible searches and enumeration in the pinned navigation model. Invalid
inputs, cancellation, stale geometry and pending work remain distinct.

Start projection is only a proposal: the complete connector from the real start
is checked before any waypoint can be exposed. Alternative target cells likewise
need a checked connector to the unchanged original destination, or an endpoint
within the explicit allowed goal domain. No unchecked straight tail is appended.
A projected service endpoint can only hand off through the existing precise
queue owner. Source original/resolved goals, profile, scope and geometry/cost
revisions remain in NavigationPath.Proposal.

Clearance uses a rotation-independent circumscribed vehicle envelope, including
the actual GroundMovement clearance. It checks every terrain cell crossed by
the swept horizontal envelope and the original static obstacle bounds retained
by NavigationGridBuilder. This is conservative and can reject a physically valid
narrow passage; it is not a minimum-turn-radius planner. Grid topology, terrain
availability, slope limits and diagonal corner clearance remain required.
The existing continuous physical sweeps and traffic arbitration still veto
every actual move, including moving vehicles and exact service geometry.

## Observed congestion and selection

CongestionPenaltyMemory is connected per owning player. Only that player's own
Cargo/Supply observations are aggregated, sorted and recorded; the global
physical collision frame and hidden enemy state never become route costs.
OutOfFuel and tactically paused states do not create traffic evidence. Missing
observations break continuous-delay evidence and permit existing costs to decay.
Truncated scans, aggregation/history omissions and unknown scopes publish costs
as Unavailable with no penalty array; they do not report complete cost coverage.

The publisher uses these bounded, unmeasured operational values:

| Setting | Value |
| --- | ---: |
| Knowledge scopes / observations and retained cells per scope | 16 / 128 |
| Own logistics observation scan per tick | 4,096 entities |
| Ordinary sustained delay activation / dock FIFO grace | 20 / 80 ticks |
| Penalty ramp / expiry | 40 / 200 ticks |
| Maximum soft penalty / linear decay | 12 / 0.1 per tick |

Publication is separately bounded by those scan/aggregation/history capacities;
it is not hidden in a search work unit. Snapshot costs are copied once per scope
and tick on demand and pinned until each reader completes. Cost revision is that
deterministic publication tick. Local steps consume cell costs; sector search
uses the maximum observed penalty in each destination sector. Costs remain
finite, nonnegative and soft, never terrain blockers. A cost-only change neither
invalidates an active geometric route nor starts speculative replans.

The first complete geometrically valid endpoint route is submitted to the
existing SupplyRescueTravel and CargoDeliveryFuelPolicy final authorization,
exactly once, before exposing a waypoint. No alternative repeatedly consumes the
prepared Cargo fuel budget. RouteChoicePolicy remains an independent preparation
for future coherent multi-alternative affordability comparison; it is not
advertised as connected. Existing traffic progress/replan intervals continue to
bound reactive replans. No measured improvement or global optimality is claimed.

The continuous-motion adapter creates an explicitly cost-free physical-frame
view of the accepted waypoints; this is not a cost reassessment or another route
choice. The source NavigationPath.Proposal preserves the searched cost revision,
scope and actual corridor.

## Work, ownership and persistence

| Vehicle search bound | Value |
| --- | ---: |
| Search work per tick / slice / request | 16,384 / 2,048 / 1,048,576 |
| Active continuations | 8 |
| Reserved peak workspace per request / active aggregate | 8 MiB / 64 MiB |
| Start candidates / endpoint candidates | 16 / 16 |

NavigationRequests now delegates admission and active work to the prepared
NavigationSearchScheduler and policy ledger. The existing pending ECS component
retains unadmitted requests; the bounded arena pool and exact cache are connected
through coordinator phases around existing JobScheduler work. Routing and
Scheduling availability are advertised only after actual MatchRuntime composition.
Regional availability remains NotConnected. See
[Connected fleet scheduling](NavigationSearchScheduling.md) for the current
submission, work, memory and checkpoint bounds.

Authoritative schema **15** records root/request counters, rotation fairness,
cost publication history and complete detached continuations with their pinned
penalties in navigation.routing.v1. Pending/accepted bindings also travel in ECS
snapshots. The existing save/replay reconstruction replays the same commands and
fixed work slices, then compares the complete authoritative checkpoint. Envelope
1 and payload 11 remain unchanged; older authoritative checkpoints are explicitly
incompatible. Historical hashes are not rewritten and no old frontier is guessed.
The existing total document-size limit still applies; an oversized save fails
without replacing the previous file. See
[the routing decision](../adr/BoundedVehicleRoutingIntegration.md).

## Compilation and manual acceptance

Only Navigation/Game and their product dependencies are compiled, using the
pinned SDK. No tests are created, edited, built or manually executed, and no
smoke, benchmark, headless/native qualification, workflow dispatch or rerun is
started. Source inspection and compiler success are not functional acceptance.

Manual approval remains outstanding. Exercise ordinary physical Cargo/Supply
travel; short FIFO waits; sustained observed blockage and expiry after reopening;
a disconnected nearest endpoint with a farther legal connector; enclosed goals;
blocked starts; real Fuel rejection; same-tick replacement and changed dock/queue
generations during a suspended search; world rebuilds; and save/load/replay
continuation. Check that narrow-corridor conservatism does not strand the normal
map's service routes. Natural match completion remains unverified.

Status: implemented in the defined logistics scope; manual user approval pending.
Current fleet/cache and schema-15 acceptance is tracked in NavigationSearchScheduling.Handoff.md.

Historical routing-package source compilation: Release Game and its Navigation/product dependencies
compiled in both Public and Developer variants with SDK 10.0.402, zero warnings
and zero errors. Product source: d509c793931e32e4db2e6b0e665de634328152c6. Commands:

```powershell
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj -c Release --no-restore -p:ForgeLineEnableDeveloperTools=false --nologo -m:1 -nr:false
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj -c Release --no-restore -p:ForgeLineEnableDeveloperTools=true --nologo -m:1 -nr:false
```

The compiler workspace contains the pinned product sources retrieved from the
repository plus this change; it contains no test projects. Normal product restore
completed first. Earlier compiler attempts failed before single-node restore,
and a later compile found a local-name collision in the precise-owner guard;
that error was corrected before the final passing builds. No functional,
Save/Replay or natural-match execution is implied by compilation.
