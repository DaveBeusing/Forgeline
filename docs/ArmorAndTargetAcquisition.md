# Armor and Target Acquisition

## Purpose

FORGELINE directional armor and target acquisition extend the authoritative combat execution layer without creating a parallel combat authority.

The system provides:

- data-driven directional armor profiles;
- weapon penetration and target-class effectiveness;
- deterministic impact-facing classification;
- automatic hostile target acquisition;
- target validity and stable priority selection;
- Hold Fire, Return Fire, and Fire At Will policy hooks;
- intelligence-availability and line-of-fire extension points;
- diagnostics, debug visualization, tests, and dense-field benchmarks.

Presentation consumes read models only. Target choice, facing, armor mitigation, and fire eligibility remain fixed-tick simulation decisions.

## Ownership

`ForgeLine.Combat` owns the reusable data contracts:

- `ArmorProfileId`
- `ArmorProfileDefinition`
- `ArmorCatalog`
- `ArmorState`
- `ArmorZone`
- `WeaponEffectiveness`
- `TargetClass` and `TargetClassMask`
- `Targetable`
- `TargetPriority`
- `AutoTargetState`
- `FirePolicyState`
- `ITargetAvailabilityPolicy`
- `ILineOfFirePolicy`

`ForgeLine.Game` owns the simulation systems that combine these contracts with ECS state, transforms, factions, Health, the spatial index, and the existing weapon/damage lifecycle.

## Armor Profiles

Armor profiles use stable `ArmorProfileId` values and explicit logical armor values for:

- Front
- Side
- Rear
- Top

An entity participates in armor resolution by carrying `ArmorState`, which references one profile from the authoritative `ArmorCatalog`.

Armor profiles are logical gameplay data. They are not mesh materials, collision geometry, render metadata, or rigid-body physics.

## Facing Classification

Facing is derived from the target's authoritative `WorldTransform.Rotation`.

The engine convention uses local +Z as forward. Incoming attack direction points in the direction the shot or projectile is travelling toward the target.

Facing classification is:

- Top when the attack source is sufficiently above the target;
- Front when the source direction is within the front-facing sector;
- Rear when the source direction is within the rear-facing sector;
- Side otherwise.

Hitscan fire records its source-to-target direction in the pending damage request. Physical projectiles record their normalized simulation velocity at impact.

Presentation orientation never participates in classification.

## Weapon Effectiveness

Each `WeaponDefinition` exposes a `WeaponEffectiveness` value containing:

- valid target classes;
- logical penetration.

The V1 target classes are:

- Infantry
- Light Vehicle
- Armored Vehicle
- Structure

Existing weapons that do not supply an explicit effectiveness definition use the general-purpose compatibility default and remain valid against all target classes.

## Armor Damage Resolution

Directional mitigation occurs in `CombatDamageResolutionSystem`.

For an armored target:

1. resolve the weapon definition from the shot's stable `WeaponId`;
2. resolve the target's armor profile;
3. classify the incoming direction against the target transform;
4. read the armor value for the resulting zone;
5. resolve the damage fraction from penetration versus armor;
6. apply the resolved damage through the existing `HealthState` lifecycle.

The V1 logical rule is:

`damage fraction = clamp(penetration / armor, 0, 1)`

Zero armor receives full damage.

This deliberately avoids detailed ballistic penetration physics. It provides an explicit data-driven relationship where stronger armor and stronger penetration directly change outcomes without universal hard-counter multipliers.

## Target Acquisition Phase

`TargetAcquisitionSystem` runs in the canonical `Sensors` phase after movement and spatial-index synchronization and before `Combat`.

This ordering means acquisition sees authoritative current transforms and publishes only `WeaponState.Target` for later combat execution.

The system uses the existing `SpatialGridIndex.QueryRadius` when a spatial index is available. A stable ECS fallback exists for headless compositions that do not provide the spatial index.

## Target Validity

Automatic acquisition requires a candidate to satisfy all applicable rules:

1. the entity is alive;
2. source and target are valid combatants;
3. source and target are hostile under the current V1 faction rule;
4. the candidate is explicitly `Targetable`;
5. the weapon supports the candidate's target class;
6. the candidate has live Health;
7. the candidate has an authoritative world transform;
8. the candidate is within weapon range;
9. the target-availability policy exposes the target to the source;
10. the line-of-fire policy permits the shot;
11. the current fire policy permits engagement.

Existing explicitly assigned targets remain compatible when they predate `Targetable`; weapon-class restrictions apply whenever a `Targetable` component is present.

`CombatExecutionSystem` revalidates fire policy, target class, target availability, range, and line of fire before Ammunition is consumed. Acquisition therefore cannot make a stale target authoritative merely by selecting it on an earlier tick.

## Deterministic Target Priority

Valid candidates are ordered by:

1. higher `TargetPriority.Value`;
2. shorter squared distance;
3. lower stable `EntityId`.

This produces repeatable target selection without relying on dictionary order, frame timing, renderer visibility, or nondeterministic spatial bucket order.

## Automatic Targeting

Weapons automatically acquire a new target when their existing target is invalid unless `AutoTargetState.Enabled` is false.

A disabled auto-target state preserves manual or externally assigned targeting behavior while still allowing combat execution to perform final target validation.

When a previous target becomes invalid and a replacement is selected, the acquisition metrics record a reacquisition.

## Fire Policies

`FirePolicyState` provides three V1 policies:

### Hold Fire

The weapon does not automatically acquire or authoritatively fire.

### Return Fire

The weapon may engage only the recorded retaliation target.

When `CombatDamageResolutionSystem` applies damage to a unit configured for Return Fire, the actual live attacker becomes the authoritative retaliation target. Acquisition can select that attacker on the following Sensors phase.

### Fire At Will

Normal automatic target acquisition and engagement are permitted.

UI commands for changing these policies can be added later without replacing the policy contract.

## Intelligence Availability Hook

`ITargetAvailabilityPolicy` separates combat targeting fundamentals from the future intelligence layer.

The default policy exposes otherwise valid targets. A later fog-of-war/intelligence implementation can reject unknown, stale, detected-only, or otherwise unavailable targets while keeping the same acquisition algorithm and priority rules.

## Line-of-Fire Hook

`ILineOfFirePolicy` separates target selection from future terrain, cover, obstruction, and weapon-specific line-of-fire rules.

The default policy is unobstructed. Future terrain-aware implementations can reject blocked fire during both acquisition and final combat execution.

The V1 hook does not attempt realistic projectile penetration through terrain or structures.

## Projectile Target Validity

Physical projectiles continue to use swept simulation collision.

When an intersected entity has `Targetable`, projectile hit eligibility also respects the weapon's valid target classes. Friendly entities and dead targets remain invalid.

## Diagnostics

`TargetAcquisitionSystem.Metrics` exposes:

- scans;
- candidates considered;
- acquisitions;
- reacquisitions;
- rejections;
- friendly rejects;
- target-class rejects;
- range rejects;
- intelligence-availability rejects;
- line-of-fire rejects;
- fire-policy rejects.

`CombatDamageResolutionSystem.Metrics` exposes:

- armored hits;
- Front hits;
- Side hits;
- Rear hits;
- Top hits;
- cumulative mitigated damage.

These metrics are simulation observations only.

## Debug Visualization

When F2 world debugging is enabled, combat debug snapshots can expose:

- weapon/acquisition radius;
- selected target line;
- projectile positions and directions;
- armor facing axes;
- rejected target positions and rejection reasons;
- impacts;
- damaged Health values.

Debug visualization reads copied `SnapshotEvents` state and never controls target selection or damage.

## Correctness Coverage

Game tests cover:

- Front/Side/Rear/Top classification;
- directional armor mitigation;
- weapon target-class filtering;
- friendly rejection;
- deterministic priority selection;
- stable entity-ID tie breaking;
- reacquisition after target destruction;
- Hold Fire execution suppression;
- Return Fire attacker acquisition;
- intelligence-availability rejection;
- line-of-fire rejection.

The existing combat tests continue to cover cadence, Ammunition consumption, hitscan/projectile execution, exactly-once impact, stale entities, destruction, and repeatable headless outcomes.

## Performance Coverage

The simulation BenchmarkDotNet host includes dense-field acquisition workloads with 100 and 1,000 target candidates.

The benchmark rebuilds the scenario outside the measured acquisition tick so timing measures spatial synchronization plus authoritative target acquisition rather than setup allocation.

Benchmark results are measurement evidence and are not hardware-specific CI gates.

## Extension Boundary

Later systems may add:

- richer faction/diplomacy relationships;
- fog-of-war and identification-level target availability;
- terrain-aware line-of-fire;
- weapon-specific target-priority tables;
- explicit attack commands;
- aim time and accuracy;
- realistic ballistic penetration;
- module and crew damage;
- suppression;
- advanced cover;
- artillery workflow;
- veterancy.

Those systems must extend these armor, targeting, weapon, Health, and lifecycle contracts rather than replace them with parallel combat state.
