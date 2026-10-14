# Combat, Destruction, and Logistics VFX

## Purpose

ForgeLine VFX are a presentation consequence of authoritative simulation state. They communicate fire, impacts, damage, destruction, cargo handling, and battlefield resupply without determining hits, damage, transfers, movement, or destruction.

The compiled mesh/material effects remain the compatibility baseline. A bounded GPU analytic particle runtime now replaces selected smoke, dust, spark and explosion visuals behind those same stable effect IDs, event bindings and viewer-authorized snapshots. It does not create a new event source or move gameplay authority into presentation.

## Source layout

Editable sources live below:

```text
assets/source/vfx/
  materials/
  sources/
  *.asset.json
```

Generated runtime output remains compiler-owned below `assets/runtime/`.

The initial set contributes 36 effect meshes and 10 VFX materials. Together with the existing source tree, the current clean compile contains 492 runtime assets. Particles add no source assets or runtime asset schema.

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

The asset runtime still has no dedicated ribbon or effect-graph type. Version-1 presentation emitter descriptors reuse existing mesh/material IDs and palette/scale, while the GPU runtime evaluates procedural sprites. Mesh fallback retains the authored atlas. Shared ground/receiver impact decals and short owned-vehicle tracks use a bounded presentation pool. Compute/indirect simulation and independent enemy trails remain outside this implementation.

Final audio is separate. Repair effects remain deferred until repair gameplay exposes an authoritative trigger.

## Authored effect textures

All 36 existing effect mesh sources now have normalized face UVs and sample the shared RGBA effect atlas through the ten stable material families. Emissive bindings are present for fire, flashes, sparks, projectiles and logistics transfer. Cell UV transforms, retained atlas mip depth and ordered alpha coverage avoid opaque rectangular backgrounds while preserving the existing pool, lifetime and distance budgets. See [effect source layout](../assets/source/vfx/textures/README.md).


## Bounded GPU particle runtime

`ParticleEmitterDescriptor` version 1 maps the existing smoke, dust/impact-dirt, spark/impact-metal, explosion, fire and heavy-weapon muzzle families. Infantry muzzle, projectile and logistics visuals retain their production mesh path. Descriptor validation preserves stable asset pairs and caps count (1–32), lifetime (0.05–4 seconds), radius (up to 8m), speed (up to 40m/s) and acceleration magnitude (up to 20m/s²). Runtime mesh/material payloads are checked before enabling each mapping; unavailable or corrupt mappings retain mesh fallback.

`ParticleVfxRenderer` consumes only copied World-visible instances. Its state key is the copied pool slot/generation for bursts, or full entity identity/effect kind for persistent state. The old synthetic display ID remains compatible, but does not determine emitter continuity. Source spawn ticks suppress already-aged bursts; persistent effects continuously stagger reproducible visual ages. Removing an authorized instance clears its particles immediately. A changed session clears the emitter cache, clock, seeds and counters.

The render clock advances by at most 0.1 seconds per frame. Pause/help, unfocused/minimized metric suspension and terminal freeze stop visual age; long stalls do not create catch-up spawn storms. No particle clock, descriptor, seed or buffer enters save/replay, combat, collision, selection, navigation, intelligence or simulation state.

Quality is persisted as `particles.quality` in client settings; `FORGELINE_PARTICLE_QUALITY` provides a validated qualification override. Changing quality requires renderer/session recreation. Disabled uses the original mesh rendering path with a 128-instance fallback cap.

| Quality | Particle limit | Maximum conservative screen coverage | Upload payload, 3 frame streams plus indices |
| --- | ---: | ---: | ---: |
| Disabled | 0 | 0 | 0 |
| Low | 512 | 0.5 frame areas | 98,316 bytes |
| Medium (default) | 2,048 | 1 frame area | 393,228 bytes |
| High | 4,096 | 2 frame areas | 786,444 bytes |

At most 2,048 emitter identities are retained. Candidate admission prioritizes large explosions, then smoke/dust, then sparks; nearer effects win within a priority, with stable-ID tie breaking. The inherited maximum-distance rules remain. Sub-two-pixel sparks disappear; explosions retain a three-pixel projected radius where the eight-metre radius cap permits. A conservative nearest-depth estimate includes all allowed motion/growth. Every particle has at most a 96-pixel projected diameter and the summed quad area stays inside the tier's fill budget. Supported particle effects rejected by capacity, distance or fill budget are suppressed for that frame. Only disabled, unsupported or unavailable particle mappings retain mesh fallback, capped at 32/64/128 instances for Low/Medium/High and 128 when Disabled. Snapshot publication is capped at 4,096 effects.

Initial conditions occupy 64 bytes per particle. Graphics owns one indexed quad, fixed synchronized frame upload streams and one alpha-blended draw. Velocity, gravity, bounded smoke curl, lifetime fade and billboard expansion are evaluated in the vertex shader. CPU emission/sorting reuses fixed scratch arrays and emits no per-frame allocation after warmup. There is no UAV, indirect dispatch, particle texture, additional render target or GPU particle readback.

The pixel shader rejects sprites behind opaque scene depth and fades intersections over 0.8m. It never writes depth. Particles compose after opaque world and before output/overlay; linear output receives exposure/ACES once. Atmosphere can share the same depth descriptor. Atmospheric extinction still uses opaque depth, and far-to-near sorting uses emitter anchors, so translucent layering/fog is approximate. See [the architecture decision](adr/BoundedAnalyticGpuParticles.md).

Diagnostics include active/peak particles, active emitters, capacity, admitted/cycled emission totals, cumulative rejected particle frame requests, mesh-fallback count, conservative screen coverage, preparation CPU time, fixed/peak buffer payload and particle draw count. Graphics reports the combined analytic update/render GPU time and its completed fence; separate compute-update time is unavailable because no compute pass exists. Native adapter memory budgets remain graphics-owned. Logical upload payload excludes heap alignment and driver allocation and is not asserted as dedicated VRAM.

Window-mode smoke validates production startup/shutdown/composition. Focused tests additionally cover finite/negative descriptors, asset failure fallback, partial GPU initialization cleanup, stable/reordered effect identity, deterministic seeds, copied burst age, pause, session reset, hidden copies, allocation-free steady preparation, saturation, terrain/atmosphere integration, direct/linear depth rejection, soft intersections, minimize/resize and fence retirement. Hardware comparisons and reproduction are in [GPU particle qualification](GpuParticleQualification.md).

## Industrial effects and shared decals

Steam and welding extend the typed taxonomy to 38 concrete kinds while reusing the 36 existing mesh sources and ten VFX material families. Running, operational factories resolve their compiled sockets; idle or unpowered facilities stop emitting. Impact marks and owned-vehicle tracks use the shared bounded DecalPool and existing atlas materials. Missing decal resources are skipped rather than replaced with opaque fallback geometry. See [combat and industrial rendering](CombatIndustrialRendering.md) for lifetimes, visibility, budgets, animation integration and measured evidence.