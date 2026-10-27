# Navigation integration contract

## Revision and current state

Contract revision **3** is `NavigationIntegrationContract.Revision`. The namespace roots remain `ForgeLine.Navigation` and `ForgeLine.Game`. Folders express ownership; no project or dependency is added.

Historical foundation base: `cdb60b1a926e770f25eb1ec350548bb00349ff49`, 0.1.472-dev. Navigation recovery PR #354 and adjacent technology/rescue PR #350 are already integrated. This foundation retains them unchanged and prepares exactly one PATCH increment to 0.1.473-dev.

The normal MatchRuntime connects Traffic, continuous Motion, Routing and copied
Diagnostics for the defined Cargo/Supply logistics scope. Scheduling/cache and
RegionalUpdates remain NotConnected. Standalone legacy navigation instances do
not acquire routing availability merely by constructing a kernel.

VehicleRoutingKernel supplies bounded resumable hierarchy/refinement/fallback,
real checked endpoint connectors and copied results. NavigationRequests assigns
and retains root generations, advances the existing pending requests in bounded
slices and checks full binding/profile/world freshness before movement admission.
Owned congestion costs are finite, scoped, delayed and expiring. Existing Fuel,
GroundMovement and precise Dock/Queue authorities remain in place. No regional
storage or prepared fleet scheduler is activated. See
[Connected vehicle routing](CongestionAwareRouting.md) for exact scope, bounds,
current implementation calls, conservative clearance and pending manual approval.
Authoritative schema 12 records the new routing state; envelope 1/payload 9 remain
unchanged. [The routing ADR](../adr/BoundedVehicleRoutingIntegration.md) records
the compatibility decision.

The signature directory and original foundation evidence below retain their
historical ownership context. Current source declarations remain authoritative
where later additive contract revisions extended that directory. Availability
requires actual composition and must never be inferred from compilation alone.

## Exclusive implementation slots

| Owner | Exclusive source paths | Input/output contract |
| --- | --- | --- |
| Traffic and blockade recovery | `src/ForgeLine.Game/Navigation/Traffic/` | `INavigationTrafficPlanner<TCheckpoint>`: immutable physical frame to generation-bound directive |
| Clearance and continuous motion | `src/ForgeLine.Navigation/Geometry/`; `src/ForgeLine.Game/Navigation/Motion/` | `INavigationGeometryQuery`, `INavigationMotionPlanner`: swept-prefix results and proposals, no transform writes |
| Routing and endpoint selection | `src/ForgeLine.Navigation/Routing/`; `src/ForgeLine.Game/Navigation/RouteSelection/` | `INavigationSearchKernel<TCheckpoint>` and sessions: specification to bounded resumable work and copied route |
| Scheduling, workspaces and cache | `src/ForgeLine.Game/Navigation/Scheduling/`; `src/ForgeLine.Navigation/Workspaces/`; `src/ForgeLine.Navigation/Caching/` | `INavigationSearchScheduler<TCheckpoint>`: admission, cancellation, snapshots and ordered results |
| Regional updates | `src/ForgeLine.Navigation/Regional/`; `src/ForgeLine.Game/Navigation/WorldUpdates/` | `INavigationWorldUpdateSession<TCheckpoint>`: authoritative old/new bounds to atomic publication |
| Diagnostics | `src/ForgeLine.Game/Navigation/Diagnostics/`; `src/ForgeLine.Presentation/NavigationDiagnostics/`; `src/ForgeLine.Client/NavigationDiagnostics/` | Viewer-filtered `NavigationDiagnosticFrame` and optional `INavigationDiagnosticsSink` |

Domain documentation paths, respectively: `docs/navigation/PredictiveTraffic.md`, `VehicleClearanceAndMotion.md`, `CongestionAwareRouting.md`, `NavigationSearchScheduling.md`, `RegionalNavigationUpdates.md`, `LogisticsNavigationDiagnostics.md` (all under `docs/navigation/`). Each owner records concrete classes, configuration, checkpoint type, integration calls and open manual acceptance there.

The central integration owner alone edits existing `NavigationRouting.cs`, `GroundMovementSystem*`, `HierarchicalPathfinder.cs`, `LocalPathSearch.cs`, `LocalPathFrontier.cs`, `NavigationWorld.cs`, dock/queue/cargo callers, construction notifications, `MatchRuntime*`, command acceptance, save/replay and UI registration. Central handoff: `docs/navigation/LogisticsNavigationIntegration.md` and `ManualLogisticsNavigationAcceptance.md`.

Shared declarations belong only to `src/ForgeLine.Navigation/Contracts/`, `src/ForgeLine.Game/Navigation/Contracts/` and this document. Domain owners must not redefine or independently change shared signatures. New internal implementation types remain in their own slots. A missing common requirement must be brought to the contract owner, not implemented as an incompatible shadow interface.

## Snapshot ownership and thread access

`NavigationSnapshotRevision.Geometry` reuses `NavigationVersion`. `Costs` is an independent soft-cost revision; zero means no published cost generation. Soft penalties are finite and non-negative. Both revision domains are deterministic, not timestamps.

`NavigationWorldSnapshot` pins the existing immutable grid and an already prepared sector graph. Its publisher must supply terrain from that same world and the graph made for the entire recorded capability profile, including slope/cost weighting. The constructor checks grid identity and movement class; the existing graph does not expose the full capability key. Graph preparation is charged to the regional/publication work budget, not hidden inside `CreateSession`.

Region/cost arrays are copied into read-only wrappers. The terrain adapter copies the chunk container and retains existing immutable heightfields without exposing the container. Existing grid/graph APIs retain their canonical read-only ownership: never cast legacy collection properties to arrays and mutate them. Retain the published snapshot until all readers finish. A worker never receives EntityRegistry or a live spatial index.

`HasTerrain == false` makes height/normal queries return false. Fallback XZ grid bounds do not invent terrain heights. Missing region stamps require global geometry invalidation, not an assumption that a route is unaffected. Regional stamps must be valid and no newer than the global geometry revision. Routing cost snapshots are shared on demand within an owning scope and tick; copying and retained capacity count toward publication/memory budgets.

`KnowledgeScope` is an opaque Game-owned faction-knowledge token. Scope, full capabilities, vehicle profile, goal constraints and revisions participate in cache keys. Physical collision knowledge is not a visibility-filtered cost field. A cost-only change must not discard physically safe paths en masse. Unavailable cost data supplies no invented penalties and remains explicitly NotConnected/Unavailable.

## Existing-data adapters and order identity

`TryCaptureVehicle` runs on the simulation thread and only reads components. It copies actual transform dimensions, rotation, velocity, owner, direct/original/pending orders, dock visit, building slot and FIFO entry. It never creates components, claims a bay, repairs an entity or executes a search. False means capture unavailable/invalid; it does not remove a physical obstacle or permit driving through a wreck. Clearance is supplied from the applicable existing configuration, not a second truck-size table.

`AdaptPendingRequest` retains the existing request ID, start, capability profile, original target and world version. The caller supplies the rotation captured with the start, explicit goal semantics/constraints and existing Game generations. Original gameplay destination and resolved navigational endpoint are distinct. A projected DockAccess endpoint never authorizes service at the exact dock pose.

`NavigationOrderBinding` retains the complete EntityId (index and generation), complete MovementOrder (issuer, both ticks, kind and heading), request ID, command generation, dock generation and queue generation. Legacy MovementOrder retains its existing fields. The connected NavigationRequests owner assigns a checked monotonic root generation when admitting a new vehicle root, carries it in NavigationPendingPath/NavigationBoundSearch and retains it across replans. The authoritative routing checkpoint records the counter. A tick or constant 1 is not a substitute: same-tick identical-position replacement orders invalidate old pending work and receive a new root generation.

Dock/queue generation zero means absent or unconfirmed, not authority. A copied building slot must still match full truck identity and visit generation before service. Entity obstacle keys map exactly to the existing index/generation; AuthoredGeometry and TrafficReservation keys retain their producer's stable identity in separate source domains. Do not replace source identities with coordinate hashes.

## Work accounting and search failure contract

`CreateSession` captures bounded inputs/leases only. `Advance(workAllowance)` charges every search stage: high-level search, refinements, fallback, endpoint candidates, clearance and output reconstruction. Costly inner loops must be divisible into deterministic primitive work units. No wall-clock/millisecond budget changes simulation decisions.

For every Advance/Plan/Propose/Sweep, `0 <= WorkConsumed <= workAllowance`. Zero allowance performs no search or movement approval; negative allowance is invalid. Child work is charged exactly once; parallel tasks receive disjoint reservations within the fleet cap. A request-wide limit is not reset by calling Advance again. Workspace byte accounting includes actual leased capacity/backing storage, not collection Count alone.

| Search status | Meaning and output |
| --- | --- |
| InvalidInput | Invalid pose/profile/identity/semantics/data; no route |
| Pending | Work can continue in a later bounded step; original order remains |
| Found | Complete legal route and resolved endpoint; non-null proposal and FailureReason.None |
| Unreachable | All admissible alternatives exhausted in valid current geometry; no route |
| BudgetExhausted | Work, workspace or candidate cap interrupted exploration; not disconnection |
| Canceled | Explicit owner invalidation; no resurrection by late completion |
| Stale | Captured geometry/ownership is no longer applicable; no result takeover |

Default search status is InvalidInput, not success. FailureReason reuses the existing enum. Scheduler retries may create a new bounded request ID after a deterministic interval while retaining the same command generation and original target. They cannot loop until success in one tick or discard cargo. The scheduler owns admission/age; the kernel must not introduce another global queue.

Route proposals copy waypoints and region dependencies; the last point equals the resolved endpoint. The routing implementation additionally enforces maximum projection distance, position/heading semantics and every diagonal/swept clearance. A found path is not proof of affordable propulsion, arrival or completed transfer. The existing cargo/supply fuel gates and exact dock checks remain authoritative.

## Collision, traffic and motion contract

`NavigationCollisionFrame` is a bounded internal physical frame with world revision, capture tick, finite coverage box, complete/partial flag and copied bodies. Truncation, failed capture and insufficient coverage mean incomplete data, not an empty obstacle set. Capture uses existing physical classification, not model names or visible pixels.

`NavigationSweep` covers the full translation and rotation from Start to End and other-body motion during DurationSeconds. AllowedFraction is a proven safe prefix of that same interpolation, never merely a safe endpoint. It is finite in [0,1]. Clear requires 1; Blocked, Unavailable, InvalidInput and Stale require 0. Limited/BudgetExhausted can authorize only the fully checked prefix. Missing terrain or incomplete/out-of-coverage geometry cannot authorize movement. Consumers reject mismatched ticks/revisions, invalid enums and excess work counts.

Traffic defaults to Hold with NotConnected availability. MaximumSpeed is finite, non-negative and bounded by authored/effective limits. SuggestedTarget remains a proposal requiring a sweep. Original order, queue age and recovery phase are not replaced by an avoidance suggestion. ReconsiderAt is a review tick, not permission to release a physically occupied corridor. Dock and queue reservations mirror existing owners; there is no second service bay or FIFO authority.

All participants read the same immutable traffic frame. Plan must not let earlier proposed moves change later vehicles' input observations. Fairness and decisions use stable entity/command/lease keys and simulation ticks, not task completion order, hash iteration or unseeded randomness. Partial work conservatively holds affected vehicles without indefinite priority starvation.

Game supplies real fixed delta and combines existing fuel, tactical, suppression, formation and convoy limits into CanMove/EffectiveMaximumSpeed. Motion proposals carry finite curvature, braking and safe-distance bounds; only the existing GroundMovementSystem may approve and apply the final transform. Game verifies binding, tick, world revision and complete sweep first. Fuel follows actual resulting motion. No teleport, free fuel, speed increase or weakened LocalApproach/settled-dock condition is introduced.

## Mapping to existing simulation phases

No enum values, phase ordering or system registrations change in this foundation.

| Existing boundary | Future integration |
| --- | --- |
| InputCommands / OrderProcessing / AiDecisions | Existing accepted commands replace ownership and assign command generation |
| Start of NavigationRequests | Adopt completed world publications atomically; reconcile commands, queue/dock ownership and reservations |
| NavigationRequests | Admit/advance bounded sessions; collect results at the normal job boundary and apply in stable request order |
| Before Movement | Capture physical neighborhood, plan traffic, propose motion and authorize sweeps without cross-vehicle input mutation |
| Movement | Existing GroundMovementSystem commits approved transforms; no second loop |
| Supply / Logistics / Infrastructure / Production / Economy / EntityLifecycle | Retain existing stock/service/build/destruction rules; accumulate obstacle changes for later publication |
| SnapshotEvents | Publish copied viewer-filtered diagnostics after authoritative updates |

Workers never mutate ECS or accept results directly. All capability graphs publish one coherent geometry generation, not half-updated variants. Newly added or destroyed physical obstacles remain covered by the existing collision source while a replacement grid is pending. Until central wiring is complete, the existing pipeline alone remains active.

## State capture and restore

Generic checkpoint parameters require concrete immutable, versioned, data-only records owned by each domain. Integration chooses concrete generic types at composition time; this is not a plugin registry, service locator, reflection discovery or arbitrary object serialization.

| State | Required ownership and persistence |
| --- | --- |
| Existing command, FIFO age/order, dock occupancy/generations, cargo/fuel | Existing Game/ECS authority; observe, never duplicate or independently release |
| New command sequence, queued search age/retry tick and ordering | Command owner and scheduler checkpoint with complete bindings and monotonic counters |
| Frontier/costs/predecessors, candidate cursor, work consumed and input revision/scope | Kernel TCheckpoint via CaptureState/RestoreSession; detach all captured collections |
| Traffic reservation, yielding side, waiting age, recovery phase and fairness history | Traffic TCheckpoint; retain actual physical occupancy and owner generations |
| Dirty-region backlog, old/new source revisions and partially prepared world | Regional TCheckpoint; preserve cursor and publication revision until atomic replacement |
| Motion proposals | Stateless per tick; recompute at a coherent boundary from identical authoritative inputs |
| Route/graph caches | Derived only, rebuilt from pinned inputs without different readiness/order or extra simulation ticks |
| Diagnostic buffers and UI state | Non-authoritative; may be discarded and never influence routing/resources |

Restore validates contract/algorithm/configuration versions, content identity, capacities and entity/order generations before ticking resumes. Mismatches reject explicitly; never reset a lease to free or silently lose waiting age.

Exact frontier reconstruction is allowed only with original inputs, every relevant geometry/cost snapshot, algorithm/configuration revision and deterministic work cursor. Replay to exactly that cursor before resuming simulation; load-time reconstruction is not extra ticks or newly granted budget. If cache readiness affects admission timing, its relevant state must be captured too. Central integration assesses save/replay schema changes when semantics become active; historical checkpoints are not rewritten. No new authoritative state or schema change is installed here.

## Visibility, configuration and completion boundary

Diagnostic frames contain copied owned-vehicle rows and bounded events only. Constructors check owner/event membership. Producers additionally apply current visibility and clear VisibleBlocker when disclosure is not authorized. Refilter or clear on ownership/visibility changes; a historical copy is not continuing authorization. Never pass a physical collision frame, live ECS or reservation collection into presentation. An optional full/unavailable sink returns false; its errors/buffering must not affect simulation decisions.

Limits have no preset values. All positive/finite requirements are validated. Any new operational values must be documented as unmeasured, including planning horizon, retries, candidate counts, work/memory/cache caps and diagnostic buffers. Existing settings are untouched. Record timings only as observations, not simulation budgets or performance promises.

No tests were added, changed or manually run. No benchmark, smoke, soak, headless or native/visual execution was requested. Existing ordinary PR CI is unchanged; no extra workflow, dispatch, rerun, skip flag or relaxed gate was added.

The local environment has no dotnet executable, so these targeted Release commands were **not executed locally**:

```powershell
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release
```

The unchanged automatically triggered navigation workflow [40177242188](https://github.com/DaveBeusing/Forgeline/actions/runs/40177242188) completed its portable-contracts job successfully for source head `847fce14133bdbe7eab5994cf70c7e0160b4a20b`. Its normal Release commands compile Navigation and Game; those source files are unchanged by this documentation commit. This is separate automatic CI evidence, not a manually requested test or a completed full-CI/Windows/manual acceptance. Current checks and remaining failures belong in the PR handoff.

Implemented: contracts, read-only adapters, failure/budget separation, typed state boundaries and six disjoint slots. Pending: current required checks/review, integration into master, six concrete implementations, central wiring/persistence assessment and Dave's final manual acceptance. No delivered traffic functionality or measured performance gain is claimed.

## Public C# signature directory

The following declarations are the authored integration surface; compiler-generated record members are omitted. This is a signature reference, not a compilation unit. Full property implementations/validation are in the linked sources. Domain implementations must not change these revision-1 signatures independently.

### Navigation configuration and state transfer

[Source](../../src/ForgeLine.Navigation/Contracts/NavigationIntegrationLimits.cs)

```csharp
public static class NavigationIntegrationContract // public const int Revision = 3
public enum NavigationModuleAvailability : byte { NotConnected = 0, Available = 1, Unavailable = 2 }
public sealed record NavigationIntegrationLimits(
    int WorkPerTick, int WorkPerSearch, int MaximumActiveSearches,
    long MaximumSearchBytes, long MaximumCacheBytes, int MaximumEndpointCandidates,
    int RetryIntervalTicks, float PlanningHorizonSeconds, int MaximumNeighbors,
    int MaximumReservations, int MaximumDiagnosticEvents);
// NavigationIntegrationLimits member:
public void Validate();
public interface INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    TCheckpoint CaptureState();
    void RestoreState(TCheckpoint checkpoint);
}
```

### World snapshot

[Source](../../src/ForgeLine.Navigation/Contracts/NavigationWorldSnapshot.cs)

```csharp
public readonly record struct NavigationSnapshotRevision(NavigationVersion Geometry, ulong Costs);
// NavigationSnapshotRevision member: public bool IsValid { get; }
public readonly record struct NavigationRegionRevision(NavigationSectorCoordinate Region, NavigationVersion Revision);
public readonly record struct NavigationCellPenalty(NavigationCellCoordinate Cell, float AdditionalCost);
// NavigationWorldSnapshot : ITerrainQuery constructor and members:
public NavigationWorldSnapshot(NavigationWorld world, NavigationSectorGraph preparedGraph,
    NavigationCapabilities capabilities, TerrainWorld? terrain, ulong costRevision,
    ulong knowledgeScope, NavigationModuleAvailability costAvailability,
    ReadOnlySpan<NavigationRegionRevision> regions, ReadOnlySpan<NavigationCellPenalty> penalties);
public NavigationSnapshotRevision Revision { get; }
public NavigationCapabilities Capabilities { get; }
public NavigationGrid Grid { get; }
public NavigationSectorGraph SectorGraph { get; }
public ulong KnowledgeScope { get; }
public NavigationModuleAvailability CostAvailability { get; }
public IReadOnlyList<NavigationRegionRevision> Regions { get; }
public IReadOnlyList<NavigationCellPenalty> Penalties { get; }
public bool HasTerrain { get; }
public AxisAlignedBounds WorldBounds { get; }
public bool TrySampleHeight(float worldX, float worldZ, out float height);
public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal);
```

### Search kernel and route output

[Source](../../src/ForgeLine.Navigation/Contracts/NavigationSearchContracts.cs)

```csharp
public enum NavigationTargetSemantics : byte { Unspecified = 0, PlayerGoal = 1, DockAccess = 2, Transit = 3 }
public enum NavigationSearchStatus : byte
{ InvalidInput = 0, Pending = 1, Found = 2, Unreachable = 3, BudgetExhausted = 4, Canceled = 5, Stale = 6 }
public readonly record struct NavigationGoalConstraints(
    float MaximumProjectionDistance, float PositionTolerance, float? RequiredHeadingRadians, float HeadingToleranceRadians);
// NavigationGoalConstraints member: public bool IsValid { get; }
public sealed record NavigationSearchSpecification(ulong RequestId, NavigationPose Start,
    Vector3 OriginalDestination, NavigationTargetSemantics TargetSemantics, NavigationGoalConstraints Goal,
    NavigationCapabilities Capabilities, NavigationVehicleProfile Vehicle, NavigationSnapshotRevision Revision,
    ulong KnowledgeScope, int MaximumWork, long MaximumWorkspaceBytes, int MaximumEndpointCandidates);
// NavigationSearchSpecification member: public void Validate();
// NavigationRouteProposal constructor and members:
public NavigationRouteProposal(NavigationSearchSpecification request, Vector3 resolvedDestination,
    ReadOnlySpan<Vector3> waypoints, ReadOnlySpan<NavigationRegionRevision> regions,
    NavigationPathDiagnostics diagnostics, int maximumWaypoints);
public NavigationSearchSpecification Request { get; }
public Vector3 ResolvedDestination { get; }
public IReadOnlyList<Vector3> Waypoints { get; }
public IReadOnlyList<NavigationRegionRevision> Regions { get; }
public NavigationPathDiagnostics Diagnostics { get; }
public readonly record struct NavigationSearchAdvance(NavigationSearchStatus Status, int WorkConsumed,
    long WorkspaceBytes, NavigationRouteProposal? Route, NavigationFailureReason FailureReason);
public interface INavigationSearchSession<TCheckpoint> : IDisposable where TCheckpoint : class
{
    NavigationSearchSpecification Request { get; }
    NavigationSearchAdvance Advance(int workAllowance);
    TCheckpoint CaptureState();
    void Cancel();
}
public interface INavigationSearchKernel<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    INavigationSearchSession<TCheckpoint> CreateSession(NavigationWorldSnapshot snapshot, NavigationSearchSpecification request);
    INavigationSearchSession<TCheckpoint> RestoreSession(NavigationWorldSnapshot snapshot, TCheckpoint checkpoint);
}
```

### Physical geometry

[Source](../../src/ForgeLine.Navigation/Contracts/NavigationGeometryContracts.cs)

```csharp
public readonly record struct NavigationPose(Vector3 Position, Quaternion Rotation);
// NavigationPose member: public bool IsValid { get; }
public readonly record struct NavigationVehicleProfile(Vector3 HalfExtents, float ClearanceMeters,
    float MaximumSpeed, float Acceleration, float Deceleration, float MaximumYawRate);
// NavigationVehicleProfile member: public bool IsValid { get; }
public enum NavigationObstacleSource : byte { Unspecified = 0, Entity = 1, AuthoredGeometry = 2, TrafficReservation = 3 }
public readonly record struct NavigationObstacleIdentity(NavigationObstacleSource Source, ulong Value, ulong Generation);
public enum NavigationObstacleKind : byte { Unspecified = 0, Structure = 1, Vehicle = 2, Wreck = 3, Reservation = 4 }
public readonly record struct NavigationCollisionBody(NavigationObstacleIdentity Identity,
    NavigationObstacleKind Kind, NavigationPose Pose, Vector3 HalfExtents, Vector3 Velocity);
// NavigationCollisionFrame constructor and members:
public NavigationCollisionFrame(NavigationWorldSnapshot world, ulong tick, AxisAlignedBounds coverage,
    bool isComplete, ReadOnlySpan<NavigationCollisionBody> bodies, int maximumBodies);
public NavigationWorldSnapshot World { get; }
public ulong Tick { get; }
public AxisAlignedBounds Coverage { get; }
public bool IsComplete { get; }
public IReadOnlyList<NavigationCollisionBody> Bodies { get; }
public enum NavigationGeometryStatus : byte
{ Unavailable = 0, Clear = 1, Limited = 2, Blocked = 3, BudgetExhausted = 4, InvalidInput = 5, Stale = 6 }
public enum NavigationBlockReason : byte
{ Unknown = 0, None = 1, StaticObstacle = 2, MovingVehicle = 3, ReservedCorridor = 4,
  Terrain = 5, OutsideWorld = 6, MissingGeometry = 7, SearchWork = 8, StaleSnapshot = 9 }
public readonly record struct NavigationSweep(NavigationObstacleIdentity Mover,
    NavigationPose Start, NavigationPose End, NavigationVehicleProfile Vehicle, float DurationSeconds);
public readonly record struct NavigationSweepResult(NavigationGeometryStatus Status,
    NavigationSnapshotRevision Revision, ulong FrameTick, float AllowedFraction,
    NavigationBlockReason Reason, NavigationObstacleIdentity? Blocker, int WorkConsumed);
public interface INavigationGeometryQuery
{
    NavigationModuleAvailability Availability { get; }
    NavigationSweepResult Sweep(NavigationCollisionFrame frame, in NavigationSweep movement, int workAllowance);
}
```

### Regional publications

[Source](../../src/ForgeLine.Navigation/Contracts/NavigationWorldUpdateContracts.cs)

```csharp
public readonly record struct NavigationObstacleChange(NavigationObstacleIdentity Identity,
    NavigationVersion SourceRevision, AxisAlignedBounds? PreviousBounds, AxisAlignedBounds? CurrentBounds);
public enum NavigationWorldUpdateStatus : byte
{ NotConnected = 0, Pending = 1, Published = 2, BudgetExhausted = 3, InvalidInput = 4, Stale = 5 }
public readonly record struct NavigationWorldUpdateAdvance(
    NavigationWorldUpdateStatus Status, int WorkConsumed, NavigationWorldSnapshot? Snapshot);
public interface INavigationWorldUpdateSession<TCheckpoint> : INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationWorldUpdateStatus QueueChanges(ReadOnlySpan<NavigationObstacleChange> changes);
    NavigationWorldUpdateAdvance Advance(int workAllowance);
}
```

### Game bindings and adapters

[Source](../../src/ForgeLine.Game/Navigation/Contracts/NavigationReadAdapter.cs)

```csharp
public readonly record struct NavigationOrderBinding(EntityId Requester, MovementOrder OriginalOrder,
    ulong RequestId, ulong CommandGeneration, ulong DockGeneration, ulong QueueGeneration);
public sealed record NavigationBoundSearch(NavigationOrderBinding Binding, NavigationSearchSpecification Search);
// NavigationBoundSearch member: public void Validate();
public readonly record struct NavigationVehicleObservation(EntityId Entity, PlayerId Owner,
    NavigationPose Pose, NavigationVehicleProfile Profile, GroundMovementState Movement,
    MovementOrder? DirectOrder, MovementOrder? RouteOrder, NavigationPendingPath? Pending,
    VehicleDockVisit? DockVisit, VehicleDockSlot? BuildingSlot, VehicleQueueEntry? QueueEntry);
// NavigationReadAdapter static members:
public static bool TryCaptureVehicle(EntityRegistry entities, EntityId entity,
    float clearanceMeters, out NavigationVehicleObservation observation);
public static NavigationBoundSearch AdaptPendingRequest(in NavigationPendingPath pending,
    Quaternion capturedStartRotation, ulong commandGeneration, ulong dockGeneration, ulong queueGeneration,
    NavigationTargetSemantics semantics, in NavigationGoalConstraints goal, in NavigationVehicleProfile profile,
    NavigationWorldSnapshot snapshot, NavigationIntegrationLimits limits);
public static NavigationObstacleIdentity GetObstacleIdentity(EntityId entity);
```

### Traffic and reservations

[Source](../../src/ForgeLine.Game/Navigation/Contracts/NavigationTrafficContracts.cs)

```csharp
public enum NavigationReservationKind : byte { Unspecified = 0, Passage = 1, Dock = 2, Queue = 3 }
public readonly record struct NavigationTrafficReservation(ulong ReservationId, EntityId Owner,
    ulong Generation, ulong CommandGeneration, NavigationReservationKind Kind, NavigationPose Pose,
    Vector3 HalfExtents, SimulationTick CreatedAt, SimulationTick ReconsiderAt, bool PhysicallyOccupied);
// NavigationTrafficFrame constructor and members:
public NavigationTrafficFrame(SimulationTick tick, NavigationCollisionFrame collision,
    bool isComplete, ReadOnlySpan<NavigationVehicleObservation> vehicles,
    ReadOnlySpan<NavigationTrafficReservation> reservations, NavigationIntegrationLimits limits);
public SimulationTick Tick { get; }
public NavigationCollisionFrame Collision { get; }
public bool IsComplete { get; }
public IReadOnlyList<NavigationVehicleObservation> Vehicles { get; }
public IReadOnlyList<NavigationTrafficReservation> Reservations { get; }
public enum NavigationTrafficAction : byte { Hold = 0, Proceed = 1, Yield = 2, Recovery = 3 }
public readonly record struct NavigationTrafficDirective(NavigationModuleAvailability Availability,
    NavigationOrderBinding Binding, SimulationTick Tick, NavigationTrafficAction Action,
    float MaximumSpeed, Vector3? SuggestedTarget, NavigationBlockReason Reason);
public interface INavigationTrafficPlanner<TCheckpoint> : INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationTrafficDirective Plan(NavigationTrafficFrame frame, in NavigationOrderBinding binding,
        INavigationGeometryQuery geometry, int workAllowance, out int workConsumed);
}
```

### Fixed-tick motion proposals

[Source](../../src/ForgeLine.Game/Navigation/Contracts/NavigationMotionContracts.cs)

```csharp
public readonly record struct NavigationMotionInput(NavigationOrderBinding Binding,
    SimulationTick Tick, float DeltaSeconds, NavigationVehicleObservation Vehicle,
    NavigationTrafficDirective Traffic, NavigationRouteProposal? Route,
    float EffectiveMaximumSpeed, bool CanMove);
public readonly record struct NavigationMotionProposal(NavigationModuleAvailability Availability,
    NavigationOrderBinding Binding, SimulationTick Tick, NavigationSnapshotRevision Revision,
    NavigationPose Start, NavigationPose End, float DesiredSpeed, float Curvature,
    float BrakingDistance, float SafeRemainingDistance, NavigationBlockReason Reason);
public interface INavigationMotionPlanner
{
    NavigationModuleAvailability Availability { get; }
    NavigationMotionProposal Propose(in NavigationMotionInput input, NavigationCollisionFrame collision,
        INavigationGeometryQuery geometry, int workAllowance, out int workConsumed);
}
```

### Fleet scheduling

[Source](../../src/ForgeLine.Game/Navigation/Contracts/NavigationSchedulingContracts.cs)

```csharp
public enum NavigationSubmissionStatus : byte
{ NotConnected = 0, Accepted = 1, AlreadyPending = 2, CapacityExhausted = 3, InvalidInput = 4, Stale = 5 }
public readonly record struct NavigationScheduledResult(NavigationOrderBinding Binding, NavigationSearchAdvance Search);
// NavigationScheduleAdvance constructor and members:
public NavigationScheduleAdvance(SimulationTick tick, int workConsumed,
    ReadOnlySpan<NavigationScheduledResult> results, int maximumResults);
public SimulationTick Tick { get; }
public int WorkConsumed { get; }
public IReadOnlyList<NavigationScheduledResult> Results { get; }
public interface INavigationSearchScheduler<TCheckpoint> : INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationSubmissionStatus Submit(NavigationBoundSearch request, NavigationWorldSnapshot snapshot);
    bool Cancel(in NavigationOrderBinding binding);
    void UpdateSnapshot(NavigationWorldSnapshot snapshot);
    NavigationScheduleAdvance Advance(SimulationTick tick, int workAllowance);
}
```

### Copied diagnostics

[Source](../../src/ForgeLine.Game/Navigation/Contracts/NavigationDiagnosticsContracts.cs)

```csharp
public readonly record struct NavigationModuleConnections(NavigationModuleAvailability Traffic,
    NavigationModuleAvailability Motion, NavigationModuleAvailability Routing,
    NavigationModuleAvailability Scheduling, NavigationModuleAvailability RegionalUpdates,
    NavigationModuleAvailability Diagnostics);
public readonly record struct NavigationDiagnosticEvent(
    SimulationTick Tick, EntityId Entity, ulong RequestId, NavigationBlockReason Reason);
public readonly record struct NavigationVehicleDiagnostic(PlayerId Owner, EntityId Entity,
    ulong CommandGeneration, Vector3 OriginalTarget, GroundMovementStatus MovementStatus,
    NavigationModuleAvailability SearchAvailability, NavigationSearchStatus SearchStatus,
    NavigationBlockReason BlockReason, SimulationTick LastProgressTick,
    int QueuePosition, bool OwnsDock, EntityId? VisibleBlocker);
// NavigationDiagnosticFrame constructor and members:
public NavigationDiagnosticFrame(PlayerId viewer, SimulationTick tick, NavigationModuleConnections modules,
    ReadOnlySpan<NavigationVehicleDiagnostic> vehicles, ReadOnlySpan<NavigationDiagnosticEvent> events,
    NavigationIntegrationLimits limits);
public PlayerId Viewer { get; }
public SimulationTick Tick { get; }
public NavigationModuleConnections Modules { get; }
public IReadOnlyList<NavigationVehicleDiagnostic> Vehicles { get; }
public IReadOnlyList<NavigationDiagnosticEvent> Events { get; }
public interface INavigationDiagnosticsSink
{
    bool TryPublish(NavigationDiagnosticFrame frame);
}
```
