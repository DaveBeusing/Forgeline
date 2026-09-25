# Artillery and Indirect Fire

## Purpose

FORGELINE indirect fire connects battlefield intelligence, coordinate-based fire missions, authoritative Ammunition, ballistic shell travel, terrain impact, radial damage, and battlefield resupply.

Artillery never receives permission to inspect a hidden enemy entity merely because a player or presentation layer knows an entity exists.

## Core Authority Rule

A fire mission resolves to a fixed world coordinate before the first shell is launched.

Two target-information paths are supported:

- a directly requested coordinate that is currently visually `Visible` for the firing faction;
- an intelligence `ContactKey` that resolves to the faction's stored `LastKnownPosition`.

After resolution, `FireMissionState` stores the coordinate and optional opaque contact key. It does not store the enemy `EntityId`.

This allows artillery and later tactical systems to use reconnaissance information without retaining live access to hidden enemy transforms.

## Artillery Weapon Definition

`ArtilleryWeaponDefinition` is data driven and contains:

- stable `WeaponId`;
- minimum range;
- maximum range;
- fixed-tick fire cadence;
- acquisition delay;
- Ammunition cost per shot;
- base damage;
- area radius;
- minimum edge damage fraction;
- projectile travel speed;
- trajectory apex height;
- dispersion radius;
- weapon effectiveness/penetration.

`ArtilleryWeaponCatalog` owns the definitions.

An artillery entity participates by carrying `ArtilleryCapability` with the selected artillery `WeaponId`.

## Fire Mission Commands

`FireMissionCommand` enters through the normal fixed-tick command schedule.

The command supports:

- explicit coordinate missions;
- contact-key missions;
- one or more owned artillery entities;
- requested round count;
- submission tick.

The command itself performs ownership and capability filtering and creates `FireMissionRequest` components. Intelligence validation is intentionally deferred to the authoritative `ArtilleryFireMissionSystem`, which owns access to the faction intelligence store.

`CancelFireMissionCommand` cancels active missions owned by the issuing player.

## Target Information Rules

### Coordinate Mission

A direct coordinate is accepted only when its intelligence-grid cell is currently `Visible` for the firing faction.

`Explored` alone is not sufficient.

This prevents arbitrary coordinate guessing from bypassing Fog of War.

### Contact Mission

A contact mission uses `FactionIntelligenceStore.TryGetContact`.

The system copies only:

- opaque contact key;
- last-known position;
- last-seen tick.

A `Detected` radar contact is sufficient for a coordinate fire mission because the artillery fires at the stored contact position, not at a live entity.

An `Identified` contact is also valid.

A stale contact remains usable as a last-known coordinate. The projectile does not home, update, or reacquire from the hidden target after mission acceptance.

## Range Validation

A mission is accepted only when the resolved coordinate is between the weapon's configured minimum and maximum horizontal range.

Range is revalidated before later shots so artillery that moves out of a valid firing envelope cannot continue firing from an invalid position.

Invalid-range missions transition to `Cancelled`.

## Mission Lifecycle

The implemented mission states are:

- `Ordered`
- `Acquiring`
- `Firing`
- `WaitingReload`
- `NoAmmo`
- `Complete`
- `Cancelled`

### Ordered

The request has been accepted and converted into an authoritative coordinate mission.

### Acquiring

The configured acquisition delay has not yet elapsed.

### Firing

A shell was successfully launched and additional requested rounds remain.

### WaitingReload

The next fire tick has not yet arrived.

### NoAmmo

The mission remains valid but the unit's existing `AmmunitionState` inventory cannot provide the required quantity.

The mission is not discarded.

### Complete

All requested rounds have been launched.

Shells already in flight remain authoritative entities until impact.

### Cancelled

The mission failed intelligence, terrain, ownership/range validation, was explicitly cancelled, or otherwise cannot continue.

## Ammunition

Indirect fire uses the same `AmmunitionState` and `AmmunitionConsumption.TryConsume` path as direct combat.

No parallel artillery-ammunition counter exists.

A shot is recorded only after authoritative inventory removal succeeds.

When Ammunition is exhausted:

1. the mission enters `NoAmmo`;
2. no shell is spawned;
3. no shot event is emitted;
4. the mission remains active;
5. the system checks again on following ticks.

## Battlefield Resupply

`BattlefieldSupplySystem` already treats every controllable entity with `AmmunitionState` as a supply recipient.

Artillery therefore resumes without a special resupply implementation.

Because Combat executes before Supply in the canonical phase order, ammunition transferred during Supply becomes available to artillery on the next Combat phase.

This yields the intended lifecycle:

```text
Firing
  ↓
Ammunition depleted
  ↓
NoAmmo
  ↓
BattlefieldSupply transfers Ammunition
  ↓
next Combat tick
  ↓
Firing / WaitingReload
  ↓
Complete
```

## Projectile Travel Model

Each shell is an authoritative ECS entity with:

- source entity;
- faction;
- weapon ID;
- launch position;
- fixed target position;
- launch tick;
- impact tick;
- apex height;
- area radius;
- falloff data;
- damage payload;
- impact state.

Travel time is calculated from horizontal distance and configured projectile speed:

`flight time = horizontal distance / projectile speed`

The value is converted to at least one fixed simulation tick.

The visual/simulation trajectory uses linear horizontal interpolation plus a deterministic parabolic height offset:

`arc = 4 * apexHeight * t * (1 - t)`

This is the V1 indirect-fire travel model. It is deterministic-friendly and gives meaningful travel time and arc behavior without claiming full external-ballistics simulation.

## Dispersion

Dispersion is applied once when a shell is spawned.

The offset uses the simulation-owned deterministic random source and a uniform-disc distribution inside the configured dispersion radius.

The resulting coordinate becomes the shell's immutable impact target.

No renderer randomness participates.

## Terrain Impact

The dispersed target X/Z position is sampled against `ITerrainQuery`.

The sampled terrain height becomes the projectile's final Y coordinate.

A shell impacts when the authoritative simulation reaches its fixed `ImpactTick`.

Each shell marks itself impacted and is queued for lifecycle removal, which prevents repeated damage on subsequent ticks.

Terrain cratering and obstacle interception during the arc are intentionally deferred.

## Area Damage

At impact, the system queries the existing `SpatialGridIndex.QueryRadius` when available.

A headless ECS fallback exists when no spatial index is composed.

Damageable entities inside the radius receive falloff based on horizontal distance:

`damageFraction = 1 - normalizedDistance * (1 - minimumDamageFraction)`

The result is clamped between the configured minimum damage fraction and 1.0.

This means:

- impact center receives full damage;
- damage decreases linearly with distance;
- the radius edge receives the configured minimum fraction.

Area damage is intentionally capable of affecting friendly units. The impact coordinate, not target allegiance, defines the blast.

## Armor Interaction

Indirect-fire area damage is queued through the existing `CombatRuntime` and resolved by `CombatDamageResolutionSystem`.

`CombatDamageResolutionSystem` can resolve effectiveness from both direct-fire and artillery catalogs.

Indirect impacts use a downward incoming direction, so armored targets classify the hit as `Top` armor under the existing directional-armor model.

Health, destruction, retaliation bookkeeping, and entity-lifecycle synchronization remain shared with direct combat.

## Combat Events

Artillery reuses the existing combat output stream:

- `ShotFired`
- `ProjectileSpawned`
- `Impact`
- `DamageApplied`
- `EntityDestroyed`

Coordinate missions use an invalid target entity in shot/projectile/impact events because the mission intentionally does not retain an enemy entity reference.

Presentation treats these events as output only.

## Diagnostics

`ArtilleryFireMissionSystem.Metrics` exposes:

- active missions;
- missions waiting for ammunition;
- shells in flight;
- shots this tick;
- impacts this tick;
- area-damage targets this tick;
- Ammunition consumed this tick;
- area damage queued this tick;
- cumulative shots;
- cumulative impacts;
- cumulative damaged targets;
- cumulative Ammunition consumption;
- cumulative queued area damage.

The subsystem has its own shell count so headless compositions do not depend on the direct-fire execution system for artillery diagnostics.

## Debug Visualization

The F2 debug path can display:

- minimum-range circle;
- maximum-range circle;
- mission target line;
- current mission status and round count;
- shell position;
- sampled parabolic trajectory;
- impact-area circle;
- mission/shell/impact/Ammunition diagnostics.

These are copied read models and never affect mission validation or shell travel.

## Phase Ordering

The intended runtime composition is:

```text
InputCommands
  FireMissionCommand creates request
        ↓
Movement
        ↓
Sensors
  BattlefieldIntelligenceSystem refreshes contacts/visibility
        ↓
Combat
  ArtilleryFireMissionSystem resolves requests and shells
  CombatExecutionSystem resolves direct fire
        ↓
DamageResolution
  shared Health / armor resolution
        ↓
Supply
  BattlefieldSupplySystem may replenish Ammunition
        ↓
EntityLifecycle
  impacted shells / destroyed entities removed
        ↓
SnapshotEvents / presentation
```

Registration order inside `Combat` is deterministic. The client composition registers artillery before direct combat execution.

## Correctness Coverage

Automated tests cover:

- hidden coordinate rejection;
- radar-contact fire mission;
- contact-to-last-known coordinate resolution;
- minimum range;
- maximum range;
- fixed projectile travel time;
- exactly-once impact;
- radial falloff damage;
- no damage outside the blast radius;
- Ammunition consumption;
- `NoAmmo` state;
- real `BattlefieldSupplySystem` ammunition transfer;
- automatic mission continuation after resupply.

The end-to-end supply scenario does not inject ammunition directly into the artillery inventory after depletion. It enables a real `SupplyProvider`, lets the existing supply phase perform the transfer, and verifies that the mission resumes on a later Combat tick.

## Performance Coverage

The simulation BenchmarkDotNet host includes simultaneous 10- and 100-artillery fire-mission workloads.

The benchmark exercises:

- fixed-tick command processing;
- current visual-coordinate validation;
- mission creation;
- Ammunition removal;
- deterministic dispersion;
- projectile creation;
- combat event recording.

Projectile-impact and large-radius stress can be expanded separately when artillery density becomes a production tuning concern.

## Deferred Refinements

The first implementation deliberately leaves these outside the subsystem:

- counter-battery radar;
- multiple shell/ammunition types;
- guided artillery;
- weather and wind;
- full external ballistics;
- terrain cratering;
- arc obstacle interception;
- strategic missiles;
- final VFX/audio;
- final artillery UI.

Future systems must keep the same intelligence-safe coordinate boundary and existing Ammunition/supply authority.
