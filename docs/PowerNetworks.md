# Power Networks

## Purpose

FORGELINE power is authoritative continuous infrastructure capacity. It is not an inventory resource and it is not accumulated as stored energy in the initial implementation.

The power simulation provides the shared operational contract for industry, production, extraction, radar, defenses, logistics systems, and later infrastructure that requires continuous capacity.

## Power units

Generation capacity and consumer demand use abstract finite positive power units.

A power unit represents continuous capacity, not energy produced during one simulation tick. Therefore power values are not multiplied by tick duration and are not stored in inventories.

The current contract deliberately avoids committing gameplay data to physical MW/GW units. Content may later map the abstract unit to a physical presentation scale without changing allocation semantics.

## Logical network membership

`PowerNetworkId` is the stable logical identity of a power network.

Entities participate through `PowerNetworkMembership`. Generator and consumer components do not own topology themselves.

This separation is intentional. The initial system uses explicitly assigned logical networks, while a later physical-grid implementation may derive membership from:

- power lines;
- substations;
- switches;
- damaged transmission;
- disconnected islands.

Consumers and generators can continue to depend on the same power contract when that topology becomes physical.

For the current skirmish slice, player-owned bases and completed buildings are assigned to a logical network derived from their owning `PlayerId`. Player 1 and Player 2 therefore cannot share generation, demand, brownout allocation, or HUD power state merely because both are on the same battlefield. This ownership mapping is a prototype topology rule; it does not replace the future physical-grid model.

An enabled generator or consumer without network membership is treated as unassigned and operationally offline. Diagnostics report unassigned counts explicitly.

## Generators

`PowerGenerator` contains:

- maximum generation capacity;
- enabled state;
- current generator state.

Generator state is:

- `Generating` when enabled and assigned to a logical network;
- `Offline` when disabled or unassigned.

Disabled generators contribute zero capacity.

Removing a generator entity removes its capacity on the next Infrastructure phase without requiring separate cleanup state.

## Consumers

`PowerConsumer` contains:

- configured demand;
- priority;
- enabled state;
- allocated power;
- operational state.

Priority classes are:

1. `Critical`
2. `Industrial`
3. `Optional`

A disabled consumer contributes no active demand and is `Offline`.

`SupplyFraction` and `OperationalScale` expose the ratio:

```text
allocated power / configured demand
```

This is the common integration hook for systems that can degrade proportionally under brownout.

## Operational states

Consumers expose three authoritative states:

- `Powered` — allocated power satisfies full configured demand;
- `Brownout` — positive power is allocated but demand is not fully satisfied;
- `Offline` — no power is allocated or the consumer is disabled/unassigned.

Presentation may display these states but must not calculate or override them.

## Allocation order

`PowerNetworkSystem` runs during `SimulationPhase.Infrastructure`.

The Infrastructure phase executes after Logistics and before Production and Economy so production and extraction can consume the current tick's authoritative power result.

For each logical network the system:

1. aggregates enabled generator capacity;
2. aggregates enabled consumer demand;
3. services Critical demand first;
4. services Industrial demand second;
5. services Optional demand last;
6. publishes network and consumer results.

Network iteration and entity discovery use stable simulation ordering.

## Shortage behavior

If a priority tier can be fully supplied, every enabled consumer in that tier receives full demand.

If remaining capacity is insufficient for a tier, the remaining capacity is distributed proportionally across all enabled consumers in that tier:

```text
tier fraction = remaining generation / tier demand
consumer allocation = consumer demand * tier fraction
```

All affected consumers therefore enter `Brownout` with the same supply fraction for that tier.

After a partially supplied tier consumes all remaining capacity, lower-priority tiers receive no power and are `Offline`.

This avoids arbitrary same-priority winners while keeping shortage behavior explicit and repeatable.

## Network diagnostics

`PowerNetworkSystem` exposes per-network read models containing:

- generation;
- active demand;
- allocated power;
- spare capacity;
- deficit;
- generator counts;
- active generator counts;
- consumer counts;
- powered consumer count;
- brownout consumer count;
- offline consumer count.

Global `PowerNetworkMetrics` additionally reports:

- network count;
- total generation;
- total demand;
- total allocation;
- total spare capacity;
- total deficit;
- unassigned generator count;
- unassigned consumer count.

`PowerNetworkDebugSnapshot` captures stable generator and consumer read models for debugging, tooling, and later presentation integration.

## Data-driven power profiles

`PowerProfileDefinition` and `PowerProfileCatalog` provide a data boundary for generation and demand values.

A profile can define:

- canonical key;
- generation capacity;
- consumer demand;
- consumer priority.

Simulation systems do not hardcode building-specific power values.

As building and production definitions mature, their content definitions can reference or embed equivalent power-profile data while constructing the same `PowerGenerator` and `PowerConsumer` components.

## Extraction integration

`ResourceExtractionSystem` consumes `PowerConsumer` when an extractor declares one.

Behavior is:

- no `PowerConsumer` component — existing extraction behavior is unchanged;
- `Powered` — extraction runs at normal rate;
- `Brownout` — extraction rate is multiplied by `OperationalScale`;
- `Offline` — extraction pauses without draining the deposit.

Power-specific extractor states are:

- `PowerConstrained` — extraction is running below normal throughput because of brownout;
- `PowerUnavailable` — no power is available and extraction is paused.

Inventory-output backpressure remains independent. If both power and output capacity constrain an extractor, power first limits the requested production rate and inventory capacity then limits the storable result.

## Production and construction integration

Construction creates the same authoritative `PowerConsumer` contract for completed processing facilities. `ProductionSystem` then reads that allocated state during `SimulationPhase.Production`, which follows the Infrastructure phase.

The initial Steel, Fuel, and Electronics recipes require full allocated power. A brownout or offline facility reports `NoPower` and does not advance its active cycle until the authoritative supply fraction meets the recipe requirement.

An entity declares its requirement with `PowerConsumer` and systems read:

- `State`;
- `AllocatedPower`;
- `SupplyFraction`;
- `OperationalScale`.

Systems that require full power may gate on `Powered`. Systems that support degraded operation may scale throughput using `OperationalScale`.

## Deterministic-friendly behavior

Power allocation uses:

- explicit Infrastructure-phase ordering;
- stable ECS entity discovery;
- stable network ordering;
- explicit priority ordering;
- deterministic-friendly arithmetic reductions;
- no wall-clock decisions;
- no presentation state.

Perfect cross-machine bit-level floating-point determinism remains outside the current prototype guarantee, matching the wider simulation policy.

## Scale validation

Regression coverage includes:

- surplus capacity;
- exact capacity;
- priority shortage;
- proportional brownout;
- network isolation;
- generator removal;
- generator and consumer activation;
- unassigned membership;
- repeated deterministic fixtures;
- extraction pause and scaling;
- production power blocking and recovery through the production regression suite;
- 10,000 consumers under shortage.

`PowerNetworkBenchmarks` measures complete allocation ticks at 100, 1,000, and 10,000 consumers.

## Deferred physical-grid systems

The initial implementation intentionally excludes:

- physical power lines;
- substations;
- batteries;
- electrical loss;
- voltage or frequency simulation;
- EMP behavior;
- faction-specific power mechanics.

Those systems may refine how network membership and available generation are determined, but they must not require each consumer system to implement a separate power model.
