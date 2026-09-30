# Combat, Destruction, and Logistics VFX

## Purpose

ForgeLine VFX are a presentation consequence of authoritative simulation state. They communicate fire, impacts, damage, destruction, cargo handling, and battlefield resupply without determining hits, damage, transfers, movement, or destruction.

The initial baseline intentionally uses compact compiled mesh/material effects rather than a separate particle runtime. This establishes stable effect IDs, event bindings, pooling, distance policy, socket anchors, renderer batching, and diagnostics on the existing production asset pipeline. A future particle implementation can replace individual authored visuals without moving gameplay authority into presentation.

## Source layout

Editable sources live below:

```text
assets/source/vfx/
  materials/
  sources/
  *.asset.json
```

Generated runtime output remains compiler-owned below `assets/runtime/`.

The initial set contributes 36 effect meshes and 10 VFX materials. Together with the existing source tree, the current clean compile contains 210 runtime assets.

## Combat event bindings

`CombatRuntime.Events` is the authoritative event source consumed after a completed simulation tick.

| Simulation event/state | Presentation result |
| --- | --- |
| `ShotFired` | weapon-family muzzle effect |
| hitscan `ShotFired` + matching `Impact` | short-lived tracer/beam presentation |
| `ProjectileState` | direct-fire projectile visual |
| `IndirectFireProjectileState` | artillery-shell visual |
| `Impact` | target/weapon-appropriate impact family |
| tank/artillery impact | small/medium explosion layer where configured |
| new unit wreck | vehicle or infantry destruction treatment |
| new building wreck | building-scale destruction treatment |

The Directorate weapon mapping is:

- Rifle / Engineer Carbine -> infantry muzzle + bullet tracer;
- Scout Autocannon -> machine-gun muzzle + bullet tracer;
- Main Battle Cannon -> tank-cannon muzzle + cannon shell;
- Mobile Artillery -> artillery muzzle + artillery shell.

Unknown weapon IDs have stable presentation fallbacks. Missing runtime mesh/material content also retains the renderer's shared geometry/material fallback rather than affecting simulation.

## Surface and impact language

The initial impact families are:

```text
vfx.combat.impact.dirt
vfx.combat.impact.metal
vfx.combat.impact.concrete
vfx.combat.impact.armor
vfx.combat.impact.explosive
```

Current target-class information differentiates structure, armored-vehicle, light-vehicle, and infantry impacts. This baseline does not introduce a new physical material-query system merely for VFX. More precise terrain/material classification can extend the presentation mapping when an authoritative surface contract exists.

## Explosion and destruction language

Explosion families:

```text
vfx.combat.explosion.small
vfx.combat.explosion.medium
vfx.combat.explosion.vehicle
vfx.combat.explosion.building
vfx.combat.explosion.ammunition_secondary
```

Destruction families:

```text
vfx.destruction.vehicle_burst
vfx.destruction.building_burst
vfx.destruction.debris
vfx.destruction.smoke_plume
vfx.destruction.persistent_fire
vfx.destruction.spark_emission
vfx.destruction.dust_cloud
```

A newly observed vehicle/building wreck receives its one-shot burst layers once. Persistent wreck smoke/fire is derived from the wreck presentation state instead of replaying the destruction event every tick.

## Damage-state effects

Authoritative `HealthState` is already converted into unit/building presentation damage state. The VFX layer reads that copied state:

- Damaged -> light smoke;
- Critical -> heavy smoke, fire, and sparks;
- Wreck/Destroyed -> heavy/destruction smoke and persistent fire.

These effects supplement the existing geometry/material damage treatment. They never modify Health, collision, targetability, selection, or simulation occupancy.

## Socket anchors

When the compiled runtime catalog is available, unit effects resolve stable sockets from the unit mesh record:

- `weapon_muzzle` for muzzle origin;
- `cargo_load` for cargo loading/unloading;
- `supply_transfer` for battlefield supply transfer.

If the runtime catalog or socket is unavailable, presentation falls back to the entity world transform. Socket resolution is presentation-only; simulation remains independent from runtime assets.

## Logistics bindings

Logistics VFX are added only for gameplay states already exposed by the Vertical Slice:

| Authoritative state/change | Effect |
| --- | --- |
| `CargoTransportLifecycleState.Loading` | loading + resource-transfer |
| `CargoTransportLifecycleState.Unloading` | unloading + resource-transfer |
| increase in `UnitSupplyState.FuelFraction` | supply-transfer + refuel |
| increase in `UnitSupplyState.AmmunitionFraction` | supply-transfer + rearm |

The fraction comparison is presentation-local history used only to recognize an already-completed authoritative transfer. It never initiates or changes inventory movement.

Repair VFX are deliberately deferred. The current Vertical Slice exposes no authoritative repair operation/event/state, so authoring a repair effect now would create an unused contract with no legitimate trigger.

## Pooling and reuse

One-shot transient effects use `VfxEffectPool`.

Current policy:

- fixed capacity: 2,048 transient effects;
- expired slots are reused in place;
- a full pool drops additional presentation effects instead of allocating unbounded objects or affecting gameplay;
- total spawned, reused, dropped, active count, and pool capacity are tracked;
- persistent damage/logistics effects are derived from current presentation state and do not allocate simulation entities.

Projectile visuals are also presentation instances derived from existing projectile entities; no duplicate projectile simulation objects are created.

## Distance and strategic zoom

Every effect definition declares a maximum render distance. Fine-detail muzzle, spark, tracer, and logistics effects use shorter limits than artillery, destruction, and building-scale effects.

`SimpleInstanceRenderer` evaluates this policy before submitting a VFX instance. Effects outside their maximum distance are discarded for that frame. This keeps strategic zoom focused on tactically meaningful large events instead of fine-detail noise.

Representative limits range from roughly 320–500 m for fine muzzle/impact/logistics details up to 1,100 m for building-scale destruction.

## Renderer path

VFX use the existing runtime asset and indexed-instancing path:

1. presentation publishes immutable `VfxFeaturePresentationMetadata`;
2. the renderer resolves the stable mesh and material through `RuntimeWorldAssetResources`;
3. identical effect meshes batch by stable runtime mesh asset ID;
4. missing runtime content uses the existing renderer fallback;
5. transient synthetic presentation IDs are never interpolated across snapshots.

No VFX code is referenced by Combat, Economy, Logistics, Navigation, or Simulation projects.

## Visibility and readability

Combat effects respect the same faction-intelligence boundary used by world presentation. Effects are emitted when a relevant source/target is currently visible/identified or when the event position lies in currently visible terrain.

This prevents VFX from becoming an unintended intelligence side channel.

The first-pass geometry is intentionally compact. Effects are supplementary and are not allowed to replace selection, command, health, supply, or objective feedback.

## Diagnostics

The client render diagnostic line now reports:

- active transient VFX / pool capacity;
- cumulative dropped transient effects.

Existing frame, CPU render, draw-call, and renderer diagnostics remain the performance authority. VFX do not add a second timing system.

## Validation

Automated validation covers:

- compilation/loading of all required VFX mesh/material families;
- Directorate combat-event mapping;
- stable unknown-weapon fallbacks;
- pool saturation, expiration, and slot reuse;
- distance suppression;
- damage-state and cargo-state extraction;
- refuel/rearm detection from authoritative supply-fraction changes;
- unit/building wreck destruction effects;
- presentation extraction without simulation entity mutation;
- repeated representative VFX using the existing instanced fallback batch.

The normal Windows D3D12 smoke remains the runtime integration check for the complete compiled asset manifest.

## Current boundaries

The current asset runtime has no dedicated particle, ribbon, GPU-emitter, or effect-graph asset type. The baseline therefore uses mesh/material effects and fixed-tick presentation lifetimes. Alpha-rich smoke, particle animation, richer debris motion, decals created by impacts, GPU emitters, and final production VFX can evolve behind the stable presentation/event contracts.

Final audio is separate. Repair effects remain deferred until repair gameplay exposes an authoritative trigger.
