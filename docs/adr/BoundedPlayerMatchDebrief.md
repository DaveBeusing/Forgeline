# Bounded player match debrief

Status: implemented.

## Context

The match result already has simulation authority. Existing GameplayProgressJournal publishes bounded first/latest owned completed actions, whereas offline gameplay telemetry includes opponent balance data and aggregate metrics. Neither source establishes a complete strategic causal history. A durable player report must remain safe to load independently of save/replay internals.

## Decision and ownership

Game owns immutable schema-1 MatchDebriefSummary and its strict bounded codec. The simulation-owner terminal presentation extractor and explicit single-match headless export consume the existing journal and public MatchState through the same projection. They introduce no measurement producer and no simulation commands. Projection filters player, session and completed tick; strips global identities; deduplicates first/latest evidence; and emits at most 128 rows with report-local owned subject tokens. Public result is distinct from incomplete observation.

Presentation renders copied terminal data through the existing Operations HUD surface and buffers. Client owns explicit save/load of one fixed local slot and transient controls. Saved reports are labeled historical and cannot drive ECS selection, camera targets or simulation actions. Missing optional evidence cannot gate result acknowledgement or restart.

## Compatibility and privacy

The JSON schema and byte/row/text/depth bounds are independent from authoritative save envelopes, payloads, hashes and replay versions. There is no migration of those formats. Future report versions, foreign players and unknown fields fail closed. Local files are not authenticated result authority. Global event IDs and entity generations are excluded because gaps and stable IDs can reveal unrelated activity. Safe historical coordinates and battle/contact history are absent, so map jumps and invented causal analysis are unavailable.

## Consequences

The view explains exactly which retained actions are known and which categories are unavailable. It cannot claim full-history throughput, casualty totals, power-loss causation or territory changes. A future producer may supply those facts only through an explicitly versioned visibility-safe contract. This decision does not add replay controls, coaching, opponent intelligence, background storage or new gameplay authority.
