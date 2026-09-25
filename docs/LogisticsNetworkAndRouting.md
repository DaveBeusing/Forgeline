# Logistics Network and Routing

## Purpose

FORGELINE logistics routing is an authoritative graph model for strategic cargo connectivity between economic locations. It is deliberately separate from low-level unit navigation: the logistics graph decides which economic nodes and transport links form a valid transport route, while physical transport execution may later use the navigation subsystem to move vehicles between the route's node positions.

The initial implementation provides the shared network foundation for extraction outputs, storage, processing, logistics hubs, supply depots, future cargo transport, congestion, infrastructure disruption, and battlefield supply.

## Network Model

LogisticsNetwork owns stable network-local identifiers for nodes and edges:

- LogisticsNodeId identifies a logistics location for the lifetime of its registration.
- LogisticsEdgeId identifies one transport connection.
- LogisticsNetworkVersion identifies the topology and routing-validity generation.

Nodes reference the authoritative simulation EntityId and a world position. The initial node kinds are:

- Extractor Output;
- Storage Depot;
- Processing Facility;
- Logistics Hub;
- Supply Depot.

Node capabilities are independent flags rather than being inferred only from the node kind. They currently describe cargo source/destination behavior, storage, processing, distribution, and supply responsibilities.

The graph stores direct entity-to-node mappings so simulation systems and future transport execution can resolve economic entities without scanning the world.

## Transport Edges

A LogisticsEdge contains:

- stable source and destination node IDs;
- transport mode;
- physical distance;
- base route cost;
- throughput capacity per second;
- enabled/disabled state;
- directed or bidirectional traversal;
- congestion-penalty metadata;
- threat-penalty metadata.

The initial operational transport mode is GroundRoad.

Rail, Pipeline, and Drone are represented as extensible mode values and policy masks so later systems can add those links without replacing the graph or route contracts. Their physical gameplay systems are not implemented by this foundation.

Disabled edges remain in topology for diagnostics and repair/re-enable workflows but are never traversed by reachability or routing.

## Route Cost Policy

Routing uses an explicit LogisticsRouteCostPolicy.

A policy controls:

- allowed transport modes;
- base-cost weight;
- distance weight;
- congestion weight;
- threat weight;
- minimum required edge capacity.

The default policy allows Ground/Road links and minimizes BaseCost. Distance, congestion, and threat can be incorporated explicitly by callers without hard-coding one future balance model into the graph.

An edge is ineligible when it is disabled, its transport mode is excluded, or its throughput capacity is below the policy minimum.

## Reachability and Route Search

Reachability uses graph adjacency directly and does not scan world entities.

Route discovery uses a deterministic Dijkstra search over eligible edges. The returned LogisticsRoute contains ordered LogisticsRouteSegment values suitable for later physical transport execution. Each segment exposes:

- edge ID;
- from/to node IDs;
- transport mode;
- distance;
- evaluated route cost;
- throughput capacity.

The route also reports total cost, total distance, the cost policy, and the network version under which it was calculated.

When multiple candidates have equal cost, the stable node identifier participates in priority ordering and adjacency is processed in stable edge-ID order. Existing equal-cost predecessors are retained. This provides repeatable tie behavior without tying routing to dictionary iteration order.

## Availability and Infrastructure Disruption

Both nodes and edges can be enabled or disabled.

A disabled source or destination produces an explicit route failure. Disabled intermediate nodes and disabled edges are never silently bypassed. If an alternate valid route exists, normal route search may select it.

Removing a node also removes all incident edges. This guarantees that destroyed economic locations cannot leave traversable stale links behind.

## Versioning, Invalidation, and Caching

Every effective node or edge mutation invalidates route state by:

1. incrementing LogisticsNetworkVersion;
2. clearing the route cache.

The network also exposes InvalidateRoutes() for future external routing-cost changes whose inputs are owned outside the graph.

Cached routes are keyed by source node, destination node, and the complete cost policy. A cache entry is therefore reused only while the network version remains unchanged and the route request has equivalent routing semantics.

Physical transport systems must treat the route's Version as part of its validity contract. When the network version changes, a previously obtained route must not be assumed to remain valid.

## Economic Building Registration

BuildingLogisticsRegistrationSystem bridges completed economic structures into the logistics graph while leaving graph ownership in ForgeLine.Logistics.

The system runs during EntityLifecycle, after construction completion and economic capability activation. It currently maps:

| Economic capability | Logistics node kind | Primary logistics capabilities |
| --- | --- | --- |
| Resource extractor | Extractor Output | Cargo Source, Storage |
| Storage depot | Storage Depot | Cargo Source, Cargo Destination, Storage, Distribution |
| Production facility | Processing Facility | Cargo Source, Cargo Destination, Processing |

World positions come from the authoritative WorldTransform.

Destroyed or no-longer-eligible buildings are removed from the network. Storage-depot operational state and extractor enabled state propagate to node availability.

The registration system does not create roads or infer arbitrary links between nearby buildings. Transport infrastructure remains explicit graph data.

## Diagnostics

LogisticsNetworkMetrics exposes:

- total and enabled node counts;
- total and enabled edge counts;
- connected-component count;
- route-request count;
- failed-route count;
- route-cache hits;
- last successful route cost;
- last successful route length.

LogisticsNetworkDebugSnapshot produces presentation-safe read models for nodes, edges, and an optional selected route. Presentation can inspect this state but does not own or mutate it.

The F2 development world-debug path can render:

- logistics nodes;
- enabled links;
- disabled links;
- chosen route segments.

## Performance

Routing operates on graph adjacency rather than broad simulation/world scans.

LogisticsRoutingBenchmarks measures cold-route search and reachability on representative 1,000-node and 10,000-node road networks. Correctness remains covered separately by unit tests; benchmark timings are measurement evidence rather than hardware-dependent CI pass/fail thresholds.

Route caching is deliberately versioned and conservative. It is intended to remove repeated identical graph searches without allowing stale infrastructure state to leak into transport execution.

## Validation Coverage

The logistics test suite covers:

- simple multi-segment routing;
- disconnected networks;
- disabled edges;
- alternate routes;
- deterministic equal-cost ties;
- node removal;
- route-cache invalidation;
- transport-mode filtering;
- minimum-capacity filtering;
- disabled nodes.

Game integration tests cover economic-building registration, lifecycle removal, capability mapping, and operational-state propagation.

Presentation tests cover generation of logistics debug geometry without moving simulation ownership into the presentation layer.

## Current Boundary

This foundation does not implement:

- physical cargo trucks;
- automated dispatch;
- battlefield resupply;
- rail gameplay;
- pipelines;
- cargo drones;
- traffic simulation;
- final logistics UI.

Those systems consume and extend the graph rather than replacing it.
