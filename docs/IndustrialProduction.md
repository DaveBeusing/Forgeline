# Industrial Production

## Purpose

FORGELINE industrial production converts extracted raw resources into strategic intermediate resources through authoritative fixed-tick simulation.

The initial processing chains are:

```text
Ferrous Ore -> Steel
Volatiles   -> Fuel
Silicates   -> Electronics
```

Production uses the same inventory and power infrastructure as extraction, construction, and storage. Presentation may inspect production read models and submit simulation commands, but it does not advance production state directly.

## Stable Resources and Recipes

The initial processed resources use stable resource IDs:

| Resource | Stable ID |
| --- | ---: |
| Ferrous Ore | 1 |
| Volatiles | 2 |
| Silicates | 3 |
| Steel | 4 |
| Fuel | 5 |
| Electronics | 6 |

Processed resources are intentionally non-extractable. They enter the economy only through production.

Recipes use stable `RecipeId` values and data-driven `ProductionRecipeDefinition` entries. A recipe defines:

- stable ID and key;
- input resource quantities;
- output resource quantities;
- duration in simulation ticks;
- one required production capability;
- minimum authoritative power fraction.

The initial balance values are engineering defaults and remain data rather than production-system constants:

| Recipe | Input | Output | Duration | Capability |
| --- | --- | --- | ---: | --- |
| Steel | 10 Ferrous Ore | 10 Steel | 40 ticks | Steel Processing |
| Fuel | 10 Volatiles | 10 Fuel | 30 ticks | Fuel Processing |
| Electronics | 10 Silicates | 10 Electronics | 50 ticks | Electronics Processing |

All three initial recipes require full allocated power while progressing.

## Processing Facilities

A `ProductionFacility` owns explicit input and output inventory IDs and a set of supported production capabilities.

The initial processing buildings are:

| Building | Capability | Power Demand | Input Capacity | Output Capacity |
| --- | --- | ---: | ---: | ---: |
| Smelter | Steel Processing | 30 | 1,000 | 1,000 |
| Refinery | Fuel Processing | 25 | 1,000 | 1,000 |
| Electronics Plant | Electronics Processing | 35 | 1,000 | 1,000 |

These inventories are created only when construction completes. Construction therefore remains the authority that activates the processing capability, power consumer, and production inventories.

Transport between inventories remains a separate logistics responsibility. Production does not teleport resources from unrelated storage.

## Fixed-Tick Lifecycle

Production runs in `SimulationPhase.Production`, after authoritative power allocation in `SimulationPhase.Infrastructure` and before the economy phase.

For an eligible request, the lifecycle is:

1. validate the facility, recipe capability, inventories, request mode, and power state;
2. verify all recipe inputs are available;
3. reserve all inputs atomically, rolling back partial reservation attempts;
4. advance exactly one recipe progress tick for each production simulation tick that has sufficient power and is not paused;
5. once the duration is reached, verify that every output fits;
6. if output is blocked, retain the reserved inputs and completed progress without consuming or duplicating resources;
7. when output capacity becomes available, consume the reservations and insert every output exactly once;
8. update cycle, throughput, and completion diagnostics;
9. either remove a one-shot request or leave repeat/desired-stock requests queued for the next cycle.

Input quantities are consumed only when the completed cycle can commit its outputs. This prevents resource loss when output capacity becomes unavailable late in a cycle.

## Queue and Priority Semantics

Requests use three modes:

- `OneShot`: execute one cycle and remove the request;
- `Repeat`: remain queued and start another cycle whenever the facility becomes idle;
- `DesiredStock`: remain queued while output stock is below the configured target.

Requests use `High`, `Normal`, or `Low` priority. Selection is deterministic:

1. higher priority;
2. earlier submitted simulation tick;
3. lower stable request entity ID.

Priority does not preempt a cycle that has already started. This keeps input reservations and cycle completion deterministic.

## Desired-Stock Automation

A desired-stock request names one resource produced by its recipe and a target quantity in the facility output inventory.

The request:

- starts or resumes when stock is below the target;
- stops starting new cycles once stock reaches or exceeds the target;
- remains present so it can resume after stock is removed;
- never bypasses recipe inputs, output capacity, power, or facility capability.

The target is evaluated between cycles. Batch recipes can therefore reach or exceed the target by up to one recipe output batch.

## Pause and Cancellation

Pausing a request freezes its active cycle without releasing reserved inputs or losing progress. Resuming continues from the same progress tick.

Cancelling an active request releases every outstanding input reservation, clears the facility's active request state, and destroys the request. Already completed cycles are not rolled back.

## Blocking and Status Semantics

Facilities expose a player- and diagnostics-facing status plus a concrete block reason.

| Status | Meaning |
| --- | --- |
| `Idle` | no eligible cycle is currently advancing |
| `Running` | the active recipe advanced this tick or is ready to commit |
| `NoInput` | required unreserved input is unavailable |
| `OutputFull` | a completed cycle cannot commit all outputs |
| `NoPower` | authoritative allocated power is below the recipe requirement |
| `Paused` | the active or only eligible request is paused |

Additional block reasons distinguish desired-stock completion, unsupported recipes, and invalid inventory bindings.

Power state comes from `PowerNetworkSystem`; production never calculates independent power availability.

## Diagnostics and Read Models

`ProductionSystem` exposes aggregate metrics and per-facility read models containing:

- active recipe;
- progress;
- input availability;
- output-capacity availability;
- authoritative power state;
- production status and block reason;
- completed cycles;
- total produced quantity;
- average output throughput.

These models are observation-only. They do not provide a second state authority.

## Determinism and Resource Conservation

Production iterates facilities and requests in stable entity order and uses deterministic priority tie-breaking.

The implementation maintains these invariants:

- inputs cannot be consumed twice;
- output cannot be inserted twice;
- a blocked output cannot lose reserved input;
- cancellation cannot leave its active input reservation behind;
- one recipe completion conserves the configured input/output quantities exactly;
- identical initial state and command ordering produce identical tested production results;
- no wall-clock or presentation state affects production decisions.

## Headless Validation

The simulation tests include a bounded end-to-end industrial fixture containing:

```text
Resource Deposit
    |
Extractor
    |
Raw Resource Inventory
    |
Powered Processing Facility
    |
Processed Resource Inventory
```

The fixture runs through the real fixed-tick simulation with `PowerNetworkSystem`, `ResourceExtractionSystem`, shared `InventoryStore`, and `ProductionSystem`.

Regression coverage includes normal production, missing input, missing power, blocked output, pause/resume, cancellation, desired-stock stop/resume, priority ordering, deterministic repeated execution, construction-time production bindings, and resource conservation.

## Performance Validation

`ProductionBenchmarks` exercises 100, 1,000, and 5,000 simultaneously active processing facilities using the real fixed-tick production and power systems.

Benchmark timing remains observational and is not a hard CI threshold.
