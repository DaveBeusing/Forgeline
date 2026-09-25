# Resource Deposits and Extraction

## Purpose

FORGELINE models raw resources as finite physical deposits in the simulation world. Extraction is authoritative fixed-tick simulation state and remains independent of rendering, UI, storage, transport, processing, and market systems.

The initial vertical-slice resource IDs are:

- `1` — `resource.ferrous_ore` — Ferrous Ore
- `2` — `resource.volatiles` — Volatiles
- `3` — `resource.silicates` — Silicates

Rare Elements remain deferred.

## Resource definitions

`ResourceId` is a stable numeric gameplay identifier. Human-readable keys and metadata live in `ResourceDefinition` and `ResourceCatalog`.

`InitialResourceDefinitions.CreateCatalog()` provides the initial vertical-slice definitions. Simulation code compares stable IDs and does not branch on display strings or CLR type names.

Each definition exposes:

- stable resource ID;
- canonical data key;
- display name;
- default extraction rate;
- default richness.

The catalog is the content boundary for later external game-data loading.

## Deposit state

A resource deposit is an ECS entity carrying `ResourceDeposit`.

Authoritative deposit state contains:

- resource ID;
- world-space axis-aligned bounds;
- total quantity;
- remaining quantity;
- base extraction rate;
- richness multiplier;
- optional faction ownership;
- available/depleted state.

Deposits are finite. Remaining quantity is always clamped to the inclusive range from zero through total quantity. A zero remaining quantity is represented explicitly as `ResourceDepositState.Depleted`.

`ResourceDeposit.Restore` exists for persistence-friendly reconstruction of partially depleted or exhausted deposits.

## Extractor association

An extractor is an ECS entity carrying `ResourceExtractor`.

The component stores:

- target deposit `EntityId`;
- accepted resource ID;
- maximum extraction rate;
- optional faction ownership;
- enabled state;
- current extraction state.

The deposit reference uses the ECS generation-aware `EntityId`. Destroyed and reused entity slots therefore do not silently retarget an extractor.

Extractor state reports:

- ready;
- extracting;
- disabled;
- depleted deposit;
- invalid/stale deposit reference;
- resource mismatch;
- ownership mismatch.

## Fixed-tick extraction

`ResourceExtractionSystem` runs in `SimulationPhase.Economy`.

Extractor iteration uses stable entity-index order. For every eligible extractor the effective rate is:

```text
min(extractor maximum rate, deposit base rate) * deposit richness
```

Requested quantity for the current tick is:

```text
effective rate * fixed tick duration
```

Actual extraction is clamped to the deposit's remaining quantity. This prevents over-extraction even when multiple extractors consume the same deposit during one tick.

No wall-clock time participates in extraction.

## Ownership and eligibility

A deposit with no owner is extractable by any otherwise compatible extractor.

When a deposit has an owner, the extractor owner must match it. Resource type must always match.

Invalid entity references, resource mismatches, ownership mismatches, disabled extractors, and depleted deposits produce explicit extractor states and no extracted output.

## Output handoff

Storage and logistics are intentionally outside this subsystem.

Successful extraction emits a `ResourceExtractionResult` through `IResourceExtractionSink`. The result includes:

- simulation tick;
- extractor entity;
- deposit entity;
- resource ID;
- extracted quantity.

The default sink discards output. Inventory/storage can later implement the sink contract or adapt it to an event/transfer queue without changing deposit or extraction semantics.

## Diagnostics and read models

`ResourceExtractionMetrics` exposes:

- deposit count;
- depleted deposit count;
- extractor count;
- active extractor count;
- quantity extracted on the last tick;
- last-tick extraction rate;
- cumulative extracted quantity.

`ResourceExtractionDebugSnapshot.Capture` creates debug/read models in stable entity order.

Presentation may render these read models but must never mutate deposit or extractor simulation state.

`ResourceDepositDebugVisualization` draws deposit bounds and labels resource key, remaining/total quantity, and richness.

## Headless validation

Resource extraction requires only the simulation/ECS/world layers. Tests run depletion scenarios directly through `SimulationCoordinator` without creating graphics, audio, UI, or a window.

The resource test coverage includes:

- initial definition IDs and keys;
- normal fixed-tick extraction;
- richness scaling;
- exact depletion;
- already depleted deposits;
- stale deposit references;
- resource mismatch;
- ownership mismatch;
- multiple extractors sharing a deposit;
- deterministic repeated fixtures;
- long headless depletion execution.

## Benchmarks

`ResourceExtractionBenchmarks` is part of `ForgeLine.Simulation.Benchmarks`.

It measures complete Economy-phase extraction ticks with:

- 100 deposit/extractor pairs;
- 1,000 deposit/extractor pairs;
- 10,000 deposit/extractor pairs.

The benchmark is measurement evidence rather than a CI pass/fail gate.

## Architectural boundaries

Resource extraction is simulation-authoritative.

It must not depend on:

- Direct3D;
- windowing;
- UI;
- audio;
- presentation state.

Presentation consumes read models only. Storage, logistics, processing recipes, power consumption, construction costs, Rare Elements, currency, and markets remain separate systems.
