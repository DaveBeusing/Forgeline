# Strategic logistics capacity

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

## Controls and network meaning

Choose LOG in the existing minimap selector. Repeated LOG clicks filter All networks, Local conveyors, Regional Cargo, Field service orders, then All networks. F10 cycles these views before the existing Supply/Sensors/Navigation/Power/All views; OFF removes the overlay. Existing mode numeric identities are preserved. F11 remains independent minimap visibility. No additional binding, routing rule or action hierarchy is introduced.

- BELT: solid lines and triangular markers describe actual owned conveyor output connections. The copied current-tick transfer rate is OUT/s; BUF is physical buffer quantity/capacity, including reserved material. It is not available stock or a delivery promise.
- CARGO: dashed lines and square markers describe known owned GroundRoad topology and its configured rated capacity. RESV is scheduled capacity utilization from a matching current-tick capacity observation, not physical delivered throughput. A road is not a belt and a configured road is not a predicted truck path.
- FIELD: dotted lines and diamond markers describe actual accepted ResupplyOrder provider relations, with owned supply-provider/truck anchors. They are service relations, not a computed driving path. This layer does not replace the separate tactical Supply/readiness layer.

Markers use X for broken/disconnected/offline, ! for congestion/damage and ? UNKNOWN when current evidence is unavailable. Labels provide semantic state as well as color. Conveyor port, support, power, destination-capacity and reserved-stock causes reuse existing transfer observations; unobservable causes remain unknown. A connected low-output line is not called a broken port merely because its buffer is empty. The projection carries no historical cause strings or foreign cause identities.

Click a marker to select its full-generation owned subject and open the existing Operations panel, including in the combined All layer. Operations retains its chain explanation and Open Existing Controls entry. Prioritization still uses existing authenticated stock/queue/supply commands and their result feedback. The overlay has no separate priority editor and issues no simulation command. A held gesture is cancelled on mode, session, layout, focus, permission or target changes. HUD/placement/targeting input keeps precedence.

## Unavailable measurements

Downtime duration and service queue length are not measured by this network-wide projection; DOWN and Q show N/A. Cargo physical per-edge delivered throughput and field service rate are also unavailable. Scheduled reservations, in-transit load, local buffers and free inventory are distinct quantities and are never added into a purported available-stock total. Existing selected Operations observation windows remain the place for their supported measured rates/blocked windows; selecting an overview does not invent a global history.

## Completed-tick and visibility contract

StrategicLogisticsCapacitySnapshot belongs to Presentation. It copies scalars, stable entity generations and authorized world positions after a completed tick. Rendering and picking require matching session, player, tick and requested mode, and reject terminal snapshots. Extraction requires owned controllable World-visible instances and rejects conflicting building/factory/conveyor/depot/provider/truck ownership. Links require both current authorized endpoints. A removed endpoint or unknown enemy network produces no link, position, inventory or cause identity. Re-reading a retained snapshot cannot mutate inventories, costs, routing, navigation or commands.

## Bounded sampling and composition

Each network retains at most 1024 samples (3072 total). Failure evidence has retention priority over congestion, normal work and unknown samples; ties preserve source order. Up to 16384 authorized world subjects and 16384 authorized road edges are admitted. Limit accounting concerns authorized subjects rather than foreign network counts. Existing immutable source APIs still enumerate/copy the complete topology and visible-instance input; these limits bound admitted indexing/retention and rendering, not the cost of the underlying source collections.

The renderer projects with one camera matrix, retains at most 80 representatives per network (32 in All) and uses 24-pixel spatial bins, expanding to 64 pixels at strategic camera distance >=350, scaled with the UI. Bins retain an actual representative and its original facts; no average utilization or inferred network total is fabricated. At most 12 collision-checked labels fit the safe area and avoid the minimap. Sampling is not a completeness claim; the footer says SAMPLED and +LIMIT when extraction omitted authorized data. Render-decimated segments remain available through ordinary world selection and Operations. Render/picking scratch is bounded and reused.

See [executed qualification](StrategicLogisticsQualification.md) and [Operations diagnosis](OperationsDiagnosis.md).
