# Regional navigation preparation

[Documentation](../README.md) · [Complete catalog](../DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Status and source boundary](#status-and-source-boundary)
- [Source ownership and baseline requirements](#source-ownership-and-baseline-requirements)
- [Change aggregation and source validation](#change-aggregation-and-source-validation)
- [Immutable raster and connectivity preparation](#immutable-raster-and-connectivity-preparation)
- [Work budgets and complete rebuild behavior](#work-budgets-and-complete-rebuild-behavior)
- [Publication ordering and pending safety](#publication-ordering-and-pending-safety)
- [Route dependency proofs](#route-dependency-proofs)
- [Snapshot lifetime and world boundaries](#snapshot-lifetime-and-world-boundaries)
- [State capture and restore](#state-capture-and-restore)
- [Configuration and diagnostics](#configuration-and-diagnostics)
- [Internal API directory](#internal-api-directory)
  - [Coordinator and state](#coordinator-and-state)
  - [Journal, pending checks and route proofs](#journal-pending-checks-and-route-proofs)
  - [Layout, raster and connectivity](#layout-raster-and-connectivity)
  - [Preparation, canonical input mapping and Game source helpers](#preparation-canonical-input-mapping-and-game-source-helpers)
- [Required central contract and integration changes](#required-central-contract-and-integration-changes)
- [Validation and later acceptance](#validation-and-later-acceptance)

</details>

## Status and source boundary

The independent internal preparation layer is implemented under `src/ForgeLine.Navigation/Regional/`, with read-only source helpers under `src/ForgeLine.Game/Navigation/WorldUpdates/`. It prepares immutable regional raster pages, capability-specific connectivity, pending hard blockers, route dependency proofs and an explicit publication boundary. Internal adapters consume the integrated canonical revision-1 input contracts. **There is no public world-update output adapter, runtime registration or delivered gameplay integration.** Existing navigation, movement, construction, demolition, destruction, conveyors, infrastructure and composition callers remain unchanged.

Initial analysis used [master `cdb60b1a926e770f25eb1ec350548bb00349ff49`](https://github.com/DaveBeusing/Forgeline/tree/cdb60b1a926e770f25eb1ec350548bb00349ff49), version `0.1.472-dev`. Publication preparation uses the subsequently current [master `19e237d2e53c9a95abd2f8bb192ca19a4129c683`](https://github.com/DaveBeusing/Forgeline/tree/19e237d2e53c9a95abd2f8bb192ca19a4129c683), version `0.1.473-dev`, with one PR PATCH increment to `0.1.474-dev`. The navigation repairs from [PR #354](https://github.com/DaveBeusing/Forgeline/pull/354) and adjacent technology/supply changes from [PR #350](https://github.com/DaveBeusing/Forgeline/pull/350) are integrated. The intervening conveyor changes in [PR #357](https://github.com/DaveBeusing/Forgeline/pull/357) preserve support-only ground blockers and do not change the legacy navigation grid/graph interfaces.

During implementation on 25 October 2026, [PR #356](https://github.com/DaveBeusing/Forgeline/pull/356) was integrated into master. Its canonical integration-contract revision is 1. This feature references the integrated declarations directly; it does not copy or redefine them or import a separate feature branch. All new implementation types remain internal. The `INavigationWorldUpdateSession<TCheckpoint>` output adapter remains absent until the shared contracts and central storage changes can represent the prepared data safely.

The current `NavigationGrid` owns one contiguous array; `NavigationWorld` and `NavigationSectorGraph` bind that concrete grid. The integrated `NavigationWorldSnapshot` also binds the concrete grid and prepared graph. Materializing a complete legacy grid after every regional edit would defeat the storage requirement. This change therefore retains private page-backed preparation and records the necessary central extensions below. It does not modify those existing classes, shared contracts, save/replay schemas, project references, tests, CI, map references or golden hashes.

The [handoff](RegionalNavigationUpdates/Handoff.md) records the remaining integration sequence and evidence boundary.

## Source ownership and baseline requirements

`RegionalUpdateCoordinator` requires a complete immutable **permanent** grid plus a separate authoritative registry for every changeable obstacle. The permanent grid contains terrain and geometry that will never change during that world. It must exclude all mutable starting buildings, constructed buildings, infrastructure blockers and conveyor supports. A combined grid cannot recover which source originally set a blocked cell; it is not a valid permanent baseline for this module. Creating this correctly separated baseline remains a central integration task.

The registry retains `RegionalObstacleKey(Source, Value, Generation)`, source revision, physical layer and nullable bounds. Source values 1 and 2 map directly from the canonical Entity and AuthoredGeometry domains respectively. The internal key is a storage detail, not an alternative public identity contract. Entity adaptation preserves both index and generation. Authored source values must be stable producer identities, not hashes of positions. `RegionalContractAdapter.TryAdapt` accepts canonical `NavigationObstacleChange` input together with its explicit physical kind; unsupported vehicle, reservation or source/kind combinations are rejected.

| Authoritative source | Required treatment |
| --- | --- |
| Permanent terrain/map geometry | Present only in the immutable baseline; unchanged by regional edits. |
| Mutable authored geometry | Separate `Authored` registry entry with stable source identity, generation and source revision. |
| Live building or construction footprint | Entity identity with `Structure` layer; submit exact previous and current physical bounds. |
| Persistent physical wreck | Explicit `PhysicalWreck` entry for its actual physical footprint. A destruction notification alone does not establish or remove wreck collision. |
| Decorative wreck mesh | No physical obstacle entry. Current building-destruction comments describe its wrecks as visual-only. |
| Conveyor | Preserve the existing static-presence rule and support-only classification; elevated non-support pieces are not added as ground blockers. |
| Strategic crossing | Capture its existing `NavigationBlocker` while non-operational; operational state produces an explicit non-blocking observation. |
| Moving vehicle or reservation | Excluded from the permanent obstacle registry. Existing movement and traffic authorities remain separate. |

The internal `ForgeLine.Game.RegionalObstacleCapture.CaptureStaticEntity` uses `SpatialPresence.CreateEntry` and the existing static-mobility/conveyor-support checks. It does not reinterpret rendering rotation or scale as a changed collision model. `CaptureInfrastructure` reads the existing strategic-infrastructure operational state and marks `IsInfrastructure`. Both retain the complete `EntityId` and return `Unavailable`, `NonBlocking` or `Blocking`. Missing components return `Unavailable`; they do not prove authoritative removal.

`TryCreateChange` maps a valid observation, supplied identity/source revision and previous bounds to the canonical `NavigationObstacleChange`. Entity-shape identities must match `NavigationReadAdapter.GetObstacleIdentity` exactly. Infrastructure blockers require a supplied stable AuthoredGeometry identity, separate from the same entity's own physical shape. The helper does not invent that authored key. These methods neither enumerate the complete world nor assign source revisions, submit events or mutate entities. The central event feed must provide those responsibilities and reconcile authoritative destruction/removal records.

## Change aggregation and source validation

`RegionalChangeJournal.Queue(tick, changes)` constructs a private candidate ledger before committing it. Changes are sorted by complete source key and source revision. A batch with inconsistent predecessors, contradictory equal revisions, invalid identities or exceeded capacities is rejected atomically and enters an explicit source hold. The pending overlay then blocks all movement requests presented to that overlay. Rejection cannot silently remove an earlier blocker or authorize an already-created obstacle.

Lower source revisions and retired generations are ignored as stale. A newer generation cannot replace a still-live older generation in the same source slot. Exact duplicate edits must retain the same predecessor and current bounds; latest accepted predecessor records survive capture/restore. Removed entries remain revisioned tombstones, which prevent delayed events from resurrecting retired state. Tombstones count toward the configured registry capacity; no automatic pruning policy is installed.

Geometry changes are determined from the difference between the published and current physical bounds for each identity. Source-revision-only changes do not create geometry changes. Congestion costs have no state or revision in this module. Multiple same-tick changes coalesce to the current registry while retaining intermediate added bounds conservatively in the pending overlay until the next coherent tick boundary. An obstacle that moves away and back to its published bounds can therefore avoid a geometry rebuild while its same-tick intermediate footprint remains conservatively protected.

`BeginPreparation` freezes one source ledger, dirty set, rebuild mode/reason and original pending ages. Later accepted edits update the current ledger without restarting that preparation. A separate after-batch pending-age map tracks differences between the frozen target and current sources. The pending overlay covers current blockers that differ from either the published ledger or the frozen target, as well as same-tick intermediate bounds. This permits a complete older batch to publish safely while preserving later changes for the next batch, avoiding endless restarts during continuous construction. Metadata-only changes also leave the active preparation intact. Empty submissions in the same tick take a no-change fast path rather than recomputing the dirty ledger.

Dirty coverage is based on the union of published and current bounds. `RegionalGridLayout.GetOverlap` uses binary searches over the same computed floating-point cell edges as the existing rasterizer, including touching cells, negative origins and fractional cell sizes. It does not round AABBs inward through integer division. Its optional `includeRoundingHalo` expands candidate limits outward for dirty-region and route-proof discovery; rasterization leaves that option false to retain the exact original raster predicate. `AddOutward` rounds summed clearances outward, and the final segment clipping uses double arithmetic. The dirty halo includes:

- Existing static-obstacle clearance.
- The configured maximum swept horizontal footprint radius.
- Adjacent cells around the overlapping cell range.
- Neighboring sectors, including both sides of portal boundaries.

`MaximumSweptRadiusMeters` includes the complete horizontal vehicle envelope and additional clearance. Supplying only `NavigationVehicleProfile.ClearanceMeters` would understate the dependency and collision envelope. `RegionalContractAdapter.TryGetSweptRadius` derives an outward-rounded circumscribed radius from all three half-extents plus the canonical profile's clearance margin. That radius conservatively covers translation and intervening rotation, including terrain pitch. The integrating physical-geometry owner must reject profiles exceeding the configured maximum and preserve existing exact collision authority.

## Immutable raster and connectivity preparation

`RegionalRasterSnapshot` retains an immutable permanent baseline and a persistent sorted map of replacement sector pages. An update copies only affected sector cells into private builder storage. Untouched pages and immutable map branches are shared. Page arrays never escape as mutable reader storage, and there is no pool reuse while readers retain them.

Each dirty page is rebuilt from the permanent baseline, then rasterized against **every currently valid obstacle whose bounds overlap that page**. It does not clear cells directly on removal and does not begin from the old combined page. Consequently, overlapping surviving sources retain their blockage, while removed sources cannot become permanent baseline blockage. The raster retains the existing terrain, slope and static-clearance semantics; the larger swept-radius halo controls invalidation and pending checks rather than changing the underlying collision model.

The current implementation scans the immutable obstacle registry once per dirty region, charging candidate scans to the preparation budget. It does not yet maintain a region-to-obstacle spatial index. This is an explicit bounded-cost limitation: localized page updates avoid a complete raster copy, but a large source registry still contributes paid work for each affected region. No speedup or memory improvement has been measured.

`RegionalPortalGraphBuilder` prepares a separate immutable graph for each complete `NavigationCapabilities` value, including slope limit and slope cost weight. Initial construction visits every region incrementally. Subsequent construction schedules the dirty sectors and immediate neighbors, then replaces only their connectivity pages in a persistent map.

Every connectivity page contains eight-direction connected-component labels with diagonal corner-cut prevention. `GetComponent` and `AreConnectedWithinRegion` distinguish disconnected areas within one coarse sector. Directed boundary portals retain the existing contiguous-run midpoint and unit-cost semantics. Both directions of an affected crossing are reconstructed from the same completed raster. Consumers must use component membership when proving local portal connectivity; a list of coarse portals alone is not such a proof.

All capability graphs must be complete and reference the same completed raster before a `RegionalPublication` becomes ready. A publication contains its raster, graphs, the exact immutable obstacle ledger used to build them, and the eventual publication tick. No partially prepared graph is exposed as current geometry.

## Work budgets and complete rebuild behavior

Preparation uses deterministic integer work units. `MaximumWorkPerTick` limits coordinator preparation and successful publication across repeated `Advance` calls in the same tick. A successful publication consumes one unit. Work allowance is not a millisecond deadline.

| Work area | Accounting |
| --- | --- |
| Complete-region enumeration | One region ID per preparation step; no synchronous full-world scheduling array. |
| Raster preparation | Bounded phase transitions, one baseline-cell copy, one obstacle candidate scan, one raster-cell write, or one page commit per builder step. |
| Connectivity preparation | Bounded scheduling/phase steps, cell-label initialization, component seeds, neighbor examinations, boundary samples and page commits. |
| Capability graphs | Prepared sequentially in canonical full-profile order; at most 16 distinct profiles. |
| Page allocation/finalization | Bounded by sector size, which cannot exceed 64 cells per side or 4096 cells per page. |
| Queue admission | Separate bounded metadata work using `MaximumChangesPerTick`, capped obstacle/overlay collections and at most 4096 dirty-region visits per dirty aggregation. It is not charged to `MaximumWorkPerTick`. |
| Route proofs and pending checks | Separate explicit work allowances; exhaustion never returns a successful proof. |

Queue admission may sort a batch and rebuild bounded ledger/overlay metadata. It is not a claim that every operation performed by the module is charged to one global work counter. Likewise, a step may perform a bounded tree lookup/update or page-sized allocation. These are documented work units, not measured CPU-equivalent units.

The dirty coverage threshold is `ceil(regionCount * FullRebuildRegionPercent / 100)`. Reaching that threshold selects `LargeDirtyCoverage`; exhausting the 4096-visit dirty-aggregation limit selects `DirtyAggregationLimit` when coverage has not yet reached the threshold. Initial mutable obstacle population selects `InitialPopulation`.

For these cases the internal preparation enumerates all regions under budget and uses the same resumable raster/graph stages. It does not invoke the existing synchronous `NavigationWorld.Build`. A conflict or capacity error instead enters `SourceHold`: preparation and acquisition stop, and the pending overlay blocks all. A full rebuild of an untrusted ledger would not repair missing provenance, so the hold requires a complete authoritative resynchronization. No method silently clears it. Central integration must establish the safe recovery boundary and either reconstruct a correctly populated coordinator or invoke the approved legacy full-build path while navigation waits. The existing legacy fallback remains unconnected here.

## Publication ordering and pending safety

Only the simulation owner may call coordinator mutation methods. No worker receives a live journal or ECS registry. The intended central call order is:

1. Create a coordinator for one nonzero world generation and exact content identity, using the correctly separated permanent grid, complete initial mutable registry, capability profiles and explicit limits.
2. Submit authoritative old/new footprint events through `QueueChanges` as they occur, before a later movement decision can use obsolete geometry. An active frozen preparation remains stable while later changes accumulate separately. A source-integrity hold cancels preparation and preserves conservative blocking.
3. At the start of a later `NavigationRequests` boundary, call `TryPublishAtBoundary(tick, expectedPreviousVersion)`. It accepts only a complete candidate prepared on an earlier tick, the exact retained frozen target, the expected preceding geometry version, available publication budget and sufficient retention capacity. It advances the published ledger to that batch while preserving later differences and their pending blockers for the next preparation.
4. Acquire the current publication with `AcquireSnapshot` for searches/jobs. `Advance` prepares subsequent publications within that tick's remaining configured allowance.
5. Before accepting a search result, reusing a route or approving movement, validate existing request/command/entity ownership and consult the coordinator's **latest** pending overlay. A worker's earlier overlay cannot authorize current movement.
6. Release leases at deterministic joined job boundaries. Capture state at a coherent simulation boundary; disposal and world replacement must preserve the same ownership rules.

These are required future integration calls. They are not wired into the current game phases by this change. Only the existing authoritative `GroundMovementSystem` may apply final transforms. FIFO docking, exclusive dock occupancy, physical presence, cargo, reservations, fuel, exact stop/orientation/service rules and command supersession remain owned by their current systems.

`RegionalPendingBlocking.CheckSegment` checks the actual XZ segment against pending bounds expanded by existing static clearance plus the supplied swept radius. It validates finite inputs, configured radius capacity, whole-sweep world coverage, the expected overlay generation and work allowance. `Blocked`, `BudgetExhausted`, `InvalidInput` and `Stale` all prohibit authorization through this query. `Clear` means only that this overlay found no additional blocker; it does not replace terrain, current collision, vehicle separation or service validation.

The coordinator exposes the older complete snapshot while a valid update is pending, together with the latest pending blocker view. Removed geometry may remain conservatively blocked until publication. Added geometry is protected only when the integrating result/movement paths enforce that view. There is no assertion that the unchanged runtime currently consumes it.

## Route dependency proofs

`RegionalRouteDependencies.Capture` receives actual path segments, not merely a bounding rectangle around a route. It first enumerates candidate regions for each segment, then performs segment-versus-expanded-region clipping and records each touched region's geometry revision. The stored points include the actual initial segment start; the integrating path producer must not omit the portion from the physical start to the first waypoint.

The proof records the swept radius and immutable segment points. Invalid input, insufficient work, capacity exhaustion or an incomplete region proof produces `IsComplete == false`. `Matches` then returns false conservatively. Each recorded region has both its scalar revision and opaque immutable page identity, tied to the exact layout. This prevents a proof from a discarded future timeline matching a reused revision number after restore. Replacement-page tokens hold no page-buffer reference. Untouched page identities remain stable, so unrelated published regions can change without automatically invalidating the proof. These runtime identities are not durable serialized tokens; a durable restore must rebuild the appropriate proofs.

`RegionalUpdateCoordinator.CanReuseRoute` combines that revision proof with a check of every stored segment against the **current** pending overlay, sharing the supplied query allowance. It returns false on any incomplete or blocked check. This proves only regional geometry/pending compatibility. Capability, order generation, requester identity, goal semantics, congestion costs and current physical collision remain separate validations owned by their existing or shared-contract consumers.

## Snapshot lifetime and world boundaries

`RegionalSnapshotLease` pins one immutable `RegionalPublication`; disposing a lease decrements its reader count exactly once. Current publications with readers are retained when replaced. The coordinator will hold a completed candidate when publishing it would exceed `MaximumRetainedSnapshots`. The configured capacity includes the new current publication and still-read older publications.

The simulation owner must release leases at deterministic joined boundaries. Releasing them in variable worker-completion order could otherwise change retention backpressure and publication ticks. Atomic counters make lease retention safe; they do not grant multi-writer coordinator access or define simulation ordering.

`Dispose` abandons incomplete builders and drops coordinator ownership of current, retired and queued state. Already-issued leases retain their immutable publication until released; they cannot be reused for a different world generation. Replacing a world requires a fresh coordinator, correct world/content identity and ordinary stale-result rejection. There is no mutable shared page pool or forced reclamation beneath a reader.

## State capture and restore

The current capture contract is **in-memory and same-world only**. It preserves immutable object references and validates the same layout, baseline, content identity, world generation, settings and capability profiles. It is not a durable save-file format and cannot be restored into a newly constructed layout merely because its dimensions match. No save/replay schema or serialization codec is changed.

| Captured state | Required preservation |
| --- | --- |
| `RegionalJournalState`, format revision 3 | Current and published ledgers, latest accepted predecessor edits, first pending ticks, the frozen batch and after-batch pending ages, same-tick transient blockers, tick/admission count, overlay generation, source hold and initial-population state. |
| `RegionalPublication` | Exact completed raster, all matching capability graphs, exact source ledger and publication tick. Geometry and journal publication ledgers must have the same identity. |
| `RegionalFrozenBatch` | Exact frozen source ledger, dirty set, full-rebuild decision/reason, start tick and pending ages at freeze. Later source edits must not replace these inputs. |
| `RegionalPreparationState` | Previous publication, permanent baseline, exact frozen source ledger, input dirty set, profiles, rebuild mode/reason, start tick, consumed-work cursor, readiness and ready tick. |
| `RegionalUpdateCheckpoint`, algorithm revision 1 | World/content/layout/configuration identity, profile order, journal, publication, preparation, current tick, work already spent that tick and last rebuild reason. |

Restore first requires all existing reader/job leases to be joined. It validates and reconstructs a private replacement before changing coordinator state. Journal restore checks capacities, canonical ordering, generation/source provenance, matching current/published/frozen bounds, predecessor history, pending ages and transient blocker provenance. Preparation must match the journal's exact frozen target and its dirty/rebuild inputs; its target need not equal the newer current ledger. Dirty regions and overlays are reconstructed from the recorded ledgers; they are not reset to empty.

Preparation reconstruction reruns exactly the recorded deterministic work cursor from its original immutable inputs at the safe load boundary. This does not create simulation ticks, grant extra tick allowance or publish earlier. Readiness and its original tick are retained. Reconstructing a large cursor can take substantial load-time work; there is currently no externally resumable durable loader or independent reconstruction-time budget. A future durable codec must preserve the same publication state and validate content/algorithm/configuration revisions before ticking resumes.

## Configuration and diagnostics

`RegionalUpdateLimits` has no implicit measured defaults. Integration supplies every value:

| Setting | Meaning |
| --- | --- |
| `MaximumWorkPerTick` | Total preparation/publication units permitted in one simulation tick. |
| `MaximumChangesPerTick` | Total submitted edit count permitted for one tick, including duplicate or stale submissions. |
| `MaximumTrackedObstacles` | Registry capacity including tombstones and generation history. |
| `MaximumPendingBounds` | Maximum distinct current and same-tick intermediate unpublished hard blockers. |
| `MaximumRetainedSnapshots` | Retention bound covering current and still-read retired publications. |
| `MaximumDiagnosticRegions` | Maximum copied dirty-region coordinates in a diagnostic sample. |
| `MaximumRouteDependencyRegions` | Integration's route-proof region capacity, passed to `Capture`; the coordinator does not invent a route producer. |
| `FullRebuildRegionPercent` | Inclusive whole-region rebuild threshold, from 1 to 100 percent. |
| `MaximumSweptRadiusMeters` | Maximum complete horizontal footprint radius plus its extra margin. |

The layout additionally enforces existing chunk/sector alignment and a maximum sector side of 64 cells. The coordinator accepts 1 to 16 unique complete capability profiles and sorts them canonically.

`GetDiagnostics` returns copied scalar state and a bounded immutable coordinate sample. Status is `Idle`, `Pending`, `Ready`, `SourceHold` or `RetentionHold`. The record includes published version, overlay generation, live dirty-region count, frozen `PreparationRegions`, updated/pending preparation-region counts, pending bounds, tracked identities, rebuild reason, wait ticks, tick work, retained snapshots, active readers and `RetainedCellSlotsUpperBound`.

Updated/pending counts describe **raster preparation of the frozen batch**, not completion of every capability graph or later incoming edits. Status can remain pending when the raster count reaches zero pending regions; live dirty counts may differ from frozen preparation counts. `RetainedCellSlotsUpperBound` is a conservative slot-capacity estimate that includes the permanent baseline and partial-preparation allowance and may double-count shared pages. It is not allocated bytes, retained heap size or a memory benchmark; trees, edge arrays and other metadata are not represented as byte accounting.

Diagnostics and pending geometry are internal physical data. They are not viewer-filtered and must not be exposed to ordinary players or used to infer unseen opponent information. The central diagnostics owner must enforce the established development-access and faction-knowledge rules before publishing any view.

## Internal API directory

There are no new externally public types. The following methods are public members of internal implementation classes and document the future adapter surface, not a replacement shared contract.

### Coordinator and state

```csharp
RegionalUpdateCoordinator(ulong worldGeneration, string contentIdentity,
    NavigationGrid permanentGrid, NavigationSectorSettings sectorSettings,
    NavigationVersion initialRevision, ReadOnlySpan<NavigationCapabilities> profiles,
    RegionalUpdateLimits limits,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> initialObstacles,
    ulong initialTick = 0);
RegionalQueueStatus QueueChanges(ulong tick, ReadOnlySpan<RegionalObstacleEdit> changes);
int Advance(ulong tick, int workAllowance);
bool TryPublishAtBoundary(ulong tick, NavigationVersion expectedPreviousVersion);
RegionalSnapshotLease? AcquireSnapshot();
bool CanReuseRoute(RegionalRouteDependencies dependencies, int workAllowance, out int consumed);
RegionalUpdateCheckpoint CaptureState();
void RestoreState(RegionalUpdateCheckpoint state);
RegionalUpdateDiagnostics GetDiagnostics();
void Dispose();
```

`PendingBlocking` exposes the latest immutable pending view. `PublishedVersion` reports the current published raster revision, or the baseline revision before the first publication; it does not imply that `AcquireSnapshot` can yet return a publication. `RegionalSnapshotLease.Publication` is valid until that lease is disposed.

### Journal, pending checks and route proofs

```csharp
RegionalChangeJournal(RegionalGridLayout layout, RegionalUpdateLimits limits,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> initialObstacles,
    ulong initialTick = 0);
RegionalQueueStatus Queue(ulong tick, ReadOnlySpan<RegionalObstacleEdit> changes);
void AdvanceTick(ulong tick);
RegionalFrozenBatch BeginPreparation(ulong tick);
void CancelPreparation();
void MarkPublished(ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> targetRegistry,
    ulong tick);
RegionalJournalState CaptureState();
void RestoreState(RegionalJournalState state);

RegionalBlockingResult RegionalPendingBlocking.CheckSegment(Vector3 start, Vector3 end,
    float sweptRadiusMeters, ulong expectedGeneration, int workAllowance);
static RegionalRouteDependencies RegionalRouteDependencies.Capture(RegionalRasterSnapshot snapshot,
    ReadOnlySpan<Vector3> waypoints, float sweptRadiusMeters, int maximumRegions,
    int workAllowance, out int consumed);
bool RegionalRouteDependencies.Matches(RegionalRasterSnapshot current,
    int workAllowance, out int consumed);
```

Journal properties expose immutable current/published ledgers, `FrozenBatch`, `AfterBatchPendingSince`, dirty regions and the geometry, rebuild, hold, oldest-pending-tick and overlay state. Canceling a preparation removes its ownership while retaining differences between published and current sources. Route proofs expose `IsComplete`, `RegionCount`, read-only `Waypoints` and `SweptRadiusMeters`. Pending views expose generation, capture tick, block-all state and immutable bounded entries. `RegionalBlockingResult.IsClear` is true only for `Clear`.

### Layout, raster and connectivity

```csharp
RegionalGridLayout(NavigationGrid baseline, NavigationSectorSettings settings,
    float maximumSweptRadiusMeters);
int GetRegionIndex(NavigationSectorCoordinate region);
NavigationSectorCoordinate GetRegion(int index);
int GetCellRegion(NavigationCellCoordinate cell);
RegionalCellRange GetRegionCellRange(int index);
AxisAlignedBounds GetRegionBounds(int index);
RegionalCellRange GetOverlap(AxisAlignedBounds bounds, float inflation,
    bool includeRoundingHalo = false);
IEnumerable<int> GetDirtyRegions(AxisAlignedBounds bounds);
static bool SameBounds(AxisAlignedBounds? left, AxisAlignedBounds? right);
static bool SegmentTouches(Vector3 start, Vector3 end, AxisAlignedBounds bounds, float inflation);
static bool IsFinite(Vector3 value);
static float AddOutward(float first, float second);

static RegionalRasterSnapshot RegionalRasterSnapshot.CreateBaseline(
    RegionalGridLayout layout, NavigationVersion revision);
NavigationCellSample RegionalRasterSnapshot.GetCell(NavigationCellCoordinate cell);
NavigationVersion RegionalRasterSnapshot.GetRegionRevision(int region);
object RegionalRasterSnapshot.GetRegionIdentity(int region);
bool RegionalRasterSnapshot.IsTraversable(NavigationCellCoordinate cell,
    in NavigationCapabilities capabilities);
Vector3 RegionalRasterSnapshot.GetCellCenter(NavigationCellCoordinate cell);

RegionalRasterBuild(RegionalRasterSnapshot previous,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> obstacles,
    ImmutableSortedSet<int> dirtyRegions, NavigationVersion targetRevision);
RegionalPortalGraphBuilder(RegionalPortalGraph? previous, RegionalRasterSnapshot raster,
    ImmutableSortedSet<int> dirtyRegions, NavigationCapabilities capabilities);
// Both builders:
int Advance(int workAllowance);
void Dispose();

bool RegionalPortalGraph.Contains(int region);
ReadOnlySpan<NavigationSectorEdge> RegionalPortalGraph.GetEdges(int region);
NavigationVersion RegionalPortalGraph.GetRegionRevision(int region);
int RegionalPortalGraph.GetComponent(NavigationCellCoordinate cell);
bool RegionalPortalGraph.AreConnectedWithinRegion(NavigationCellCoordinate first,
    NavigationCellCoordinate second);
```

Both builders expose `IsComplete`, `TotalWork`, `CompletedRegionCount` and completed-only `Result`. Raster snapshots expose layout/global/baseline revisions and page/slot counts; graphs expose their exact raster, full capability profile and page/slot counts. Mutable page buffers are private builder state. Internal page constructors transfer exclusive ownership; they are not general-purpose borrowed-buffer APIs.

### Preparation, canonical input mapping and Game source helpers

```csharp
RegionalPreparation(RegionalPublication? previous, RegionalRasterSnapshot baseline,
    ImmutableSortedDictionary<RegionalObstacleKey, RegionalObstacleRecord> obstacles,
    ImmutableSortedSet<int> dirtyRegions, ImmutableArray<NavigationCapabilities> profiles,
    bool rebuildAll, RegionalRebuildReason rebuildReason, ulong startedTick);
int RegionalPreparation.Advance(ulong tick, int workAllowance);
RegionalPreparationState RegionalPreparation.CaptureState();
static RegionalPreparation RegionalPreparation.Restore(RegionalPreparationState state);
void RegionalPreparation.Dispose();

static bool RegionalContractAdapter.TryAdapt(in NavigationObstacleChange change,
    NavigationObstacleKind kind, out RegionalObstacleEdit edit);
static bool RegionalContractAdapter.TryGetSweptRadius(in NavigationVehicleProfile profile,
    out float radius);

static RegionalObstacleObservation RegionalObstacleCapture.CaptureStaticEntity(
    EntityRegistry entities, EntityId entity);
static RegionalObstacleObservation RegionalObstacleCapture.CaptureInfrastructure(
    EntityRegistry entities, EntityId entity);
static bool RegionalObstacleCapture.TryCreateChange(in RegionalObstacleObservation observation,
    NavigationObstacleIdentity identity, NavigationVersion sourceRevision,
    AxisAlignedBounds? previousBounds, out NavigationObstacleChange change);
```

Preparation exposes immutable input obstacles, readiness, start/ready ticks, total work, rebuild reason, dirty count, completed raster count and completed-only publication result. `RegionalUpdateLimits.Validate` checks all capacity/range requirements. `RegionalObstacleKey.CompareTo` and its comparison operators supply canonical source ordering.

## Required central contract and integration changes

The following extensions remain outside this feature's owned files:

1. **Page-backed grid storage.** Extend the existing canonical grid read path to accept immutable sector pages and retained untouched pages, preserving current cell/terrain/traversability behavior without complete per-publication materialization.
2. **Prepared regional graph installation.** Allow the existing world/snapshot to adopt the matching immutable regional connectivity and prepared capability graphs without triggering a complete synchronous legacy graph rebuild. Expose complete capability identity and actual component compatibility.
3. **Explicit expected-base adoption.** Shared update results need a ready-versus-published distinction and an integration call carrying the expected previous geometry revision and coherent `NavigationRequests` tick. Publication must match the retained frozen target, reject mismatched batches and mixed geometry generations, and retain later authoritative changes and their blockers for the following batch.
4. **Latest pending-blocker enforcement.** Supply a shared pending-generation query/hold contract and require result adoption, route reuse and authoritative movement to consult its latest view with complete swept-footprint coverage. An older snapshot or stale overlay cannot independently authorize movement.
5. **Complete baseline and change provenance.** Construct the permanent-only baseline and canonical mutable initial registry; feed authoritative old/new bounds with complete source identity, generation, revision and physical kind from construction, removal, destruction, conveyors and infrastructure. Missing observations must remain distinguishable from confirmed removal.
6. **Changed-region and segment proof.** Connect matching regional revisions and actual swept route segments to the shared result/cache lifecycle. Missing dependencies invalidate conservatively; existing command, requester, goal, capability and cost-generation checks remain separate.
7. **Regional capacities and deterministic lifetime.** Map explicit regional work/admission/retention/diagnostic capacities into shared configuration and release snapshot leases at deterministic joined job boundaries. Safe waiting and recovery must include both source hold and retention hold.
8. **Durable save/replay and world reset.** Define a versioned content-bound codec for ledgers, unpublished blockers, publication identity and preparation cursors; reconstruct equivalent readiness before simulation resumes. Integrate restart/match-end cleanup and complete authoritative resynchronization without importing old-world requests.

A public world-update output adapter must be added only after these boundaries can be expressed coherently. The already integrated revision-1 foundation supplies canonical input types but does not supply the storage and publication extensions listed here.

## Validation and later acceptance

Both exact targeted Release build commands were attempted in the available environment. Each exited with code 127 and `dotnet: command not found`; no source compilation occurred:

```powershell
dotnet build src/ForgeLine.Navigation/ForgeLine.Navigation.csproj --configuration Release
dotnet build src/ForgeLine.Game/ForgeLine.Game.csproj --configuration Release
```

No automated tests were created, changed or manually executed. No benchmark, soak, smoke, headless, gameplay or visual-reference run was requested. Existing automatically triggered push/PR CI remains unchanged; its results, if available, must be attributed to their exact source head. No additional CI dispatch or retry is part of this work. A successful compilation would establish compilation only, not behavioral acceptance, full CI or a performance result.

Manual acceptance remains outstanding after central integration. It must cover construction on an active route while preparation is delayed; demolition and destruction; overlapping structures; physical versus decorative wrecks; changes at sector boundaries; closing/reopening crossings; same-tick move/remove/recreate chains and recycled entity slots; continuous edits during a small-budget frozen batch; many simultaneous edits and capacity holds; conveyor supports; unaffected-route reuse with complete swept footprints; delayed readers and retention backpressure; world replacement; and save/load while later edits, blockers or a ready-but-unpublished generation remain pending.
