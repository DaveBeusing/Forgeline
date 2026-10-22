# Operations Diagnosis and Bottleneck Read Models

## Scope

The player-visible diagnosis is a copied, read-only explanation of one **owned, singly selected** production facility, logistics facility or Cargo Truck. It is published as `PresentationSnapshot.Diagnosis` at the completed simulation tick and is independent of the Operations HUD being open. `OperationsDiagnosisSnapshot.Resolve(snapshot)` verifies session, tick, player and match lifetime before exposing it.

The projection consumes the existing `PlayerActionSnapshot` after its ownership, generation and endpoint checks. It does not query the world, schedule deliveries, change resources or issue gameplay commands. Unselected facilities are not silently diagnosed, and foreign, hidden or stale transport topology is never reconstructed. The existing optional `OperationsSnapshot` remains the capped owned-facility overview; this projection is the focused drill-down contract, not a replacement for it.

## Quantity meanings

Each row has a distinct `OperationsDiagnosisSubject`. Rows represent alternative observations of the same economic work and **must not be summed together**.

- `Demand`: positive difference between the configured, enabled target and current **local** stock; it is not a new request or an available resource elsewhere.
- `LocalQuantity`: physically stored amount at the selected destination.
- `ReservedQuantity`: existing reservation reported by the inventory system; `FreeQuantity` is local stock minus reservations, floored at zero.
- `OutstandingRequestQuantity`: the existing transport demand figure. On a stock-policy row this includes pending and assigned outstanding work, already adjusted by the player extractor for observed delivery progress. On a transport-request row it is the request's reported quantity and should not be interpreted as an additional demand.
- `ConfirmedIncomingQuantity`: on stock-policy rows only, the already-reserved or loaded assigned incoming portion calculated by the established distribution readmodel. **It is a subset of outstanding work**, not an additional deficit.
- `LoadedCargoQuantity`: separately observed load in a request's assigned truck or in the selected Cargo Truck's inventory. Loaded does **not** mean delivered.
- `SourceFreeQuantity`: absent (`null`) because the current player action contract cannot establish source-local unreserved stock for the entire chain. Do not synthesize a zero or a remote stock estimate.
- `DockQueuePosition` and `DockWaitingCount`: present only when an actual copied vehicle queue is available. No distance-based or derived queue estimates.

Unknown quantities are `null` and debug output prints `NOT OBSERVED`. A disabled or unconfigured stock policy is not treated as an active material request. Production rows report actual production status and block reason independently from stock-policy or transport rows.

## Reasons and links

`OperationsDiagnosisReason` preserves a deterministic primary cause and at most two further supported causes in the priority order: **NO POWER, NO INPUT, NO SOURCE STOCK, NO ROUTE, NO TRUCK, CAPACITY, WAITING FOR BAY, PRODUCTION PAUSED, OUTPUT FULL**. More specific `NO DRIVE FUEL` and `DESTINATION FULL` reasons retain information that would otherwise be misreported. `UNKNOWN/NOT OBSERVED` is not proof of a root cause.

Production reasons come from `ProductionBlockReason` and `UnitProductionBlockReason`. Transport reasons come from simulation-owned `LogisticsTransportRequestFailureReason` and `LogisticsBottleneckReason`; queue evidence is taken only from copied, owned `PlayerWorkSummary` service observations. The presentation layer does not infer unavailable sources, traversable routes or shipment confirmation.

An `OperationsDiagnosisLink` requires a nonzero real request ID and two already authorized, generation-stable endpoint IDs. `TraceUpstream` follows at most eight explicit links, stops on missing edges and detects cycles. Upstream traversal is not evidence of a physically delivered shipment. Lists are copied, immutable and capped at 128 rows / 64 links, with `TotalRows` recording truncation; consumer code must not persist links into a newer session or tick.

`ToDiagnosticLines()` formats the copied snapshot into a bounded text-only diagnostic without a UI, graphics dependency or world scan.

## Workflow and limitations

Select an owned building or Cargo Truck. Inspect its existing production/logistics commands for stock thresholds, requests, vehicle state and docking service. The diagnostic is suitable for a future Operations drill-down or headless readmodel consumer; no new panel, chart, notification, control or automatic retry is introduced here. When the existing Operations surface is opened, it continues to use its established player-owned aggregate overview.

The current player readmodel exposes only the selected entity and its authorized request endpoints. It does not provide a whole-base dependency graph, power-network accounting for the selected row, or guaranteed availability of source-side inventory. These remain explicitly unobserved rather than being inferred. Source, truck or destination entity IDs must be revalidated at command dispatch; diagnosis is not a dispatch permission.

## Validation

`OperationsDiagnosisTests` covers copied policy/request quantities, precedence, current tick and session filtering, closed match behavior, unknown policy drafts, truck load versus delivery, documented queue positions, cap/immutability, and bounded cyclic chain traversal. Existing `OperationsTests`, `PlayerActionReadModelTests` and `AutomatedDistributionSystemTests` remain regression gates.

Run the targeted Microsoft.Testing.Platform tests and the full canonical Release solution/CI validation described in [Development](Development.md). The required CI job is `build-test`, including WARP/Windows smoke, headless diagnostics, packaging and the seed-2026 terminal match. Any unavailable native/manual qualification must be reported rather than claimed.
