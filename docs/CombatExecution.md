# Combat Execution

## Purpose

FORGELINE combat execution is simulation-authoritative. Weapon cadence, Ammunition consumption, projectile travel, hit resolution, Health, damage, and destruction are decided entirely by fixed-tick simulation state.

Presentation may render combat events and debug snapshots, but rendering, effects, audio, frame rate, and UI never determine combat outcomes.

## Ownership

`ForgeLine.Combat` owns data contracts that do not depend on game presentation:

- `WeaponId` and `WeaponDefinition`
- `WeaponCatalog`
- `WeaponState`
- `Combatant`
- `AmmunitionState` and `AmmunitionConsumption`
- `ProjectileState`
- `DamagePayload`
- `HealthState`
- `CombatHitbox`
- `CombatEvent`

`ForgeLine.Game` composes those contracts with simulation transforms, ECS state, the world spatial index, and lifecycle synchronization.

## Weapon Definitions

Weapon definitions use stable `WeaponId` values and are data-driven runtime definitions rather than behavior subclasses.

The first execution layer supports:

- maximum range
- fixed-tick fire interval
- Ammunition consumed per shot
- damage payload
- magazine size
- reload duration in simulation ticks
- hitscan delivery
- simple physical projectile delivery
- projectile speed
- projectile collision radius
- projectile lifetime

Directional armor, logical penetration, target classes, and automatic acquisition extend this layer through the contracts documented in `ArmorAndTargetAcquisition.md`. Realistic ballistic penetration, crew/module damage, suppression, artillery workflow, repair, and veterancy remain separate refinements.

## Weapon State and Fire Eligibility

`WeaponState` contains the mutable execution state for one weapon:

- selected target entity
- fire enabled state
- next eligible fire tick
- reload-until tick
- remaining magazine shots

`CombatExecutionSystem` runs in the canonical `Combat` phase. A shot is permitted only when:

1. the weapon is enabled;
2. cadence and reload timing permit fire;
3. the target is still alive;
4. source and target have valid combat factions and are enemies;
5. the target has non-depleted `HealthState`;
6. both source and target have simulation `WorldTransform` state;
7. the target is within weapon range;
8. the source has an existing `AmmunitionState`;
9. the authoritative inventory can consume the configured Ammunition quantity.

Failed range, target, or Ammunition checks do not consume Ammunition.

Stale targets are cleared from weapon state rather than dereferenced.

## Ammunition

Combat does not own a parallel ammunition counter.

`AmmunitionState` points to the same `InventoryStore` inventory used by battlefield supply. Firing calls `AmmunitionConsumption.TryConsume`, which removes `ResourceIds.Ammunition` only when the authoritative inventory has enough unreserved quantity.

Supply therefore changes future combat endurance through the same inventory state used by logistics.

## Hitscan

Hitscan weapons resolve their impact during the firing tick after all fire-eligibility checks succeed.

A successful hitscan shot:

1. consumes real Ammunition;
2. emits a shot event;
3. emits an impact event;
4. queues a damage request;
5. leaves actual Health mutation to `DamageResolution`.

Hitscan is an execution model, not a rendering effect. Presentation may visualize tracers or impacts independently.

## Physical Projectiles

Physical projectiles are ECS entities with simulation-owned:

- source entity ID
- source faction
- weapon ID
- simulation transform
- velocity
- remaining lifetime ticks
- collision radius
- damage payload
- impacted state

Movement uses fixed simulation delta time. No rigid-body physics object is required.

Projectile collision first uses the existing `SpatialGridIndex` when supplied. A swept AABB identifies candidate entities, followed by deterministic segment-versus-expanded-bounds testing. Stable entity ordering breaks equal-time hit ties. A headless ECS fallback remains available for tests or compositions without a spatial index.

Projectiles reject:

- themselves;
- their source entity;
- friendly combatants;
- targets without live Health.

The projectile stores source faction and damage payload at creation, so a valid projectile can still resolve after its source entity is destroyed.

Once a projectile impacts, it is marked immediately and queued for lifecycle removal. Damage and removal are therefore exactly-once even though actual entity destruction occurs later in the tick.

## Damage Resolution

`CombatDamageResolutionSystem` runs in `DamageResolution`.

Damage requests are buffered by combat execution and applied in stable submission order. Damage is clamped to remaining Health. Only positive applied damage produces a damage event.

When Health reaches zero, the entity is queued for controlled destruction rather than destroyed during combat iteration.

This boundary allows future armor, penetration, resistance, module, or suppression logic to refine damage resolution without replacing weapon/projectile foundations.

## Destruction and Lifecycle

`CombatEntityLifecycleSystem` runs in `EntityLifecycle`.

It:

- deduplicates queued destruction requests;
- removes affected entities from the optional spatial index;
- destroys ECS entities only at the lifecycle synchronization point;
- emits combat destruction events for Health-depleted targets;
- silently removes spent or expired projectile entities.

Existing spatial cleanup remains a secondary safety boundary for entities removed by other systems.

## Combat Events

The first event types are:

- `ShotFired`
- `ProjectileSpawned`
- `Impact`
- `DamageApplied`
- `EntityDestroyed`

Events contain the simulation tick, relevant entity IDs, weapon ID, world position, and event-specific amount.

They are outputs only. Presentation, effects, audio, telemetry, replay tooling, and debugging may consume them; they must not alter the already-resolved combat result.

## Diagnostics and Debug Visualization

`CombatRuntime.Metrics` reports current-tick and cumulative counters for:

- shots
- Ammunition use
- active/spawned projectiles
- impacts
- hits
- applied damage
- destructions

`CombatDebugSnapshotSystem` captures presentation-safe read models during `SnapshotEvents`.

The F2 development world-debug path can render:

- weapon ranges
- weapon-to-target lines
- projectile positions and directions
- impact points
- damaged-entity Health labels

Debug rendering consumes copied state only.

## Fixed-Tick Ordering

The authoritative combat lifecycle is:

```text
Movement
  ↓
Spatial synchronization
  ↓
Combat
  - move existing projectiles
  - resolve projectile impacts
  - validate/fire weapons
  - create new projectiles
  ↓
DamageResolution
  - apply queued damage
  - queue zero-Health destruction
  ↓
Supply
  - battlefield supply can replenish remaining Ammunition
  ↓
EntityLifecycle
  - destroy zero-Health entities
  - remove spent/expired projectiles
  ↓
SnapshotEvents
  - capture combat/debug output
```

A projectile created by a shot does not receive an extra movement step on its creation tick. It starts moving on the next fixed tick.

## Deterministic-Friendly Behavior

Combat uses:

- explicit simulation phases
- simulation ticks rather than wall-clock decisions
- stable entity iteration for weapon/projectile execution
- stable candidate ordering for spatial projectile hits
- immutable weapon definitions
- no rendering-dependent state

The current implementation does not require cross-machine bit-identical floating-point results, matching the wider engine determinism policy.

## Correctness Coverage

Game tests verify:

- tick-correct fire cadence
- reload windows
- no-Ammunition behavior
- out-of-range rejection without Ammunition loss
- physical projectile travel through the spatial index
- exactly-once impact and damage
- stale projectile sources
- stale weapon targets
- zero-Health lifecycle destruction
- combat destruction events
- repeatable headless outcomes

## Performance Coverage

The simulation BenchmarkDotNet host includes:

- 100 and 1,000 simultaneously armed hitscan entities executing authoritative fire/damage ticks;
- 100 and 1,000 physical projectiles executing fixed-tick movement.

Correctness remains separate from timing. Benchmark results are measurement evidence rather than hardware-specific CI gates.

## Extension Boundary

Later combat work may add realistic ballistic penetration, aiming, accuracy, guided projectiles, area effects, indirect fire, suppression, artillery workflow, repair, and veterancy.

Those systems should extend the existing weapon, projectile, Health, damage, event, and lifecycle foundations rather than create parallel combat authorities.


See [Armor and Target Acquisition](ArmorAndTargetAcquisition.md) for the authoritative directional-armor, penetration, target-class, automatic-acquisition, priority, fire-policy, intelligence-availability, and line-of-fire contracts.
