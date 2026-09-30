# Directorate Unit Assets

## Purpose

The Directorate Vertical Slice unit set is the first gameplay-unit presentation family compiled through the production ForgeLine asset pipeline. It preserves the existing authoritative unit definitions and gameplay IDs while replacing generic renderer placeholders with stable runtime mesh, material, LOD, collision, socket, damage-state, and strategic-symbol bindings.

Simulation remains authoritative for movement, combat, health, supply, ownership, targeting, and destruction. Presentation consumes immutable extracted metadata and compiled runtime assets only.

## Included families

| Gameplay unit | Stable LOD0 asset | Presentation role |
| --- | --- | --- |
| Rifle Squad | `unit.directorate.rifle_squad` | four-person infantry silhouette |
| Scout Vehicle | `unit.directorate.scout_vehicle` | light 6×6-style reconnaissance silhouette with dominant sensor mast |
| Main Battle Tank | `unit.directorate.main_battle_tank` | wide tracked chassis, angular turret, long cannon |
| Mobile Artillery | `unit.directorate.self_propelled_artillery` | tracked chassis with rear weapon mass and dominant long artillery barrel |
| Cargo Truck | `unit.directorate.cargo_truck` | heavy logistics chassis with general cargo module |
| Supply Truck | `unit.directorate.supply_truck` | shared heavy logistics chassis with visible supply containers |

The existing Combat Engineer intentionally reuses the Rifle Squad presentation family until a dedicated infantry-art package is justified. No gameplay definition or stable `UnitId` is duplicated.

## Source layout

Editable unit assets live below:

```text
assets/source/units/directorate/
  infantry/
  vehicles/
  artillery/
  logistics/
  collision/
  materials/
  symbols/
  sources/
```

Runtime output remains generated below `assets/runtime/` and is never edited or committed as source.

## LOD contract

Each six-family LOD0 mesh references explicit LOD1 and LOD2 assets in compiler metadata.

- LOD0 preserves the strongest silhouette and family-identifying details.
- LOD1 removes small detail while retaining weapon, sensor, turret, cab, or cargo-module identity.
- LOD2 preserves only strategic-scale silhouette features.
- Wreck presentation uses the LOD2 geometry with the wreck damage-state treatment rather than introducing six unique wreck meshes without demonstrated visual value.

The presentation catalog defines the current camera-distance thresholds. Renderer selection is presentation-only and never changes simulation state.

## Collision and gameplay footprint

Every primary unit mesh has `collisionRequired: true` and a stable collision reference:

```text
unit.directorate.<family>.collision
```

Collision assets are separate compiler assets and are not LOD children. Render LOD changes therefore cannot alter the gameplay collision contract.

The current gameplay simulation continues to use its established `GroundMovement`, `CombatHitbox`, `SpatialPresence`, and unit-definition scale values. This package deliberately does not rebalance those values. Tests verify the existing gameplay footprints remain internally sane while the authored collision assets remain independent presentation/tooling contracts.

## Gameplay-facing sockets

The LOD0 assets expose the anchors currently required by gameplay or near-term VFX integration.

| Family | Required sockets |
| --- | --- |
| Rifle Squad | `weapon_muzzle` |
| Scout Vehicle | `weapon_muzzle`, `sensor_origin` |
| Main Battle Tank | `turret_pivot`, `gun_pivot`, `weapon_muzzle`, `recoil_anchor` |
| Mobile Artillery | `gun_pivot`, `weapon_muzzle`, `recoil_anchor` |
| Cargo Truck | `cargo_load` |
| Supply Truck | `cargo_load`, `supply_transfer` |

The Asset Compiler stores these sockets in the runtime manifest. The weapon/VFX layer can resolve them later without introducing a second unit-asset naming scheme.

## Animation boundary

The current runtime asset contract does not yet contain an animation asset type and the compiler intentionally rejects animation references. This package therefore does not invent unsupported animation metadata.

The Main Battle Tank already uses the articulation contract for its LOD0 turret. LOD0 is authored as a hull mesh plus `unit.directorate.main_battle_tank.turret`. `PresentationExtractor` derives relative horizontal aim yaw from the authoritative `WeaponState` target and unit transforms, and the renderer rotates the turret mesh around `turret_pivot`. LOD1 and LOD2 remain combined silhouette meshes to avoid strategic-distance attachment overhead.

The remaining anchors establish supported integration points without pretending that the engine owns systems it does not yet have:

- gun elevation may later rotate around `gun_pivot`;
- weapon recoil may later use `recoil_anchor`;
- projectile/VFX origin resolves at `weapon_muzzle`;
- sensor motion may later use `sensor_origin`.

Gun elevation and recoil are not simulated presentation states in this package because the current gameplay/runtime contracts do not publish those values. Infantry remains a static authored silhouette until the engine owns a real animation/skeleton runtime contract. Gameplay simulation remains unaffected.

## Materials and damage states

Each family has one stable runtime material:

```text
material.directorate.unit.<family>
```

`PresentationExtractor` derives unit presentation damage state from authoritative `HealthState`:

- Intact: above 67% health;
- Damaged: 33–67%;
- Critical: 0–33%;
- Wreck: destruction presentation proxy.

Damage treatment is applied as presentation tinting over the compiled base material. This avoids unnecessary unique damaged meshes while preserving readable escalation.

When a combat unit is destroyed, the normal gameplay entity is still removed at the authoritative lifecycle synchronization point. A non-commandable, non-combat wreck presentation entity preserves only transform, visual identity, unit presentation identity, and intelligence signature. It cannot move, fire, receive commands, or affect simulation combat.

## Strategic symbols and selection

Every family exposes one stable strategic/minimap symbol material:

```text
material.directorate.symbol.<family>
```

These references are owned by `UnitPresentationCatalog` and form the shared binding point for future minimap and strategic-view consumers. No second icon-name table is introduced.

Normal unit selection continues to use the existing `ControllableEntity` metadata. Wreck proxies never receive that component and therefore cannot enter command selection.

## Renderer integration

`PresentationExtractor` publishes `UnitFeaturePresentationMetadata` alongside existing selectable and world-feature metadata.

`SimpleInstanceRenderer`:

1. resolves the unit presentation definition;
2. chooses LOD0/LOD1/LOD2 from camera distance;
3. resolves the stable runtime mesh from `RuntimeAssetCatalog`;
4. resolves the compiled material tint;
5. applies authoritative damage-state presentation;
6. adds the articulated MBT LOD0 turret batch when applicable;
7. batches identical runtime meshes through the existing indexed-instancing path.

The MBT turret uses the same per-frame instance stream as other repeated geometry, so a field of tanks batches hulls together and turrets together rather than introducing one draw per tank. Combat Engineer instances reuse the Rifle Squad asset IDs and therefore naturally share the same runtime mesh buffers and draw batches.

## Validation

Automated coverage verifies:

- all six source families compile through the production Asset Compiler;
- LOD1 and LOD2 references resolve;
- collision references resolve and stay independent of render LOD;
- required sockets are present;
- the dedicated MBT turret runtime asset resolves;
- material and strategic-symbol IDs resolve;
- unit damage and wreck states are extracted into render snapshots;
- destroyed units leave presentation-only wreck identities;
- gameplay hitbox/movement footprint contracts remain sane;
- 256 repeated Main Battle Tank presentation instances remain one instanced draw batch in the renderer test fixture;
- CI compiles runtime assets before the real Windows D3D12 smoke path.

## Current boundary

This is the first production-oriented Directorate unit visual baseline, not final high-detail art. Final texture sets, skeletal animation, track/wheel animation, articulated turret/gun transforms, VFX, portraits, audio, and higher-fidelity destruction can replace or extend the stable contracts established here without changing gameplay IDs or creating a parallel runtime asset path.


## VFX integration

Directorate weapon and damage visuals use the shared presentation VFX contracts documented in [Combat, Destruction, and Logistics VFX](CombatAndLogisticsVfx.md).

The compiled unit sockets are now active presentation anchors: `weapon_muzzle` supplies muzzle origin, while logistics-family sockets can anchor cargo/supply effects. Missing socket/runtime data falls back to the unit transform without changing weapon or supply simulation.

Damage-state smoke/fire/sparks and destruction bursts are derived from authoritative health/wreck state. They do not replace the existing unit material/geometry damage treatment or create additional combat authority.
