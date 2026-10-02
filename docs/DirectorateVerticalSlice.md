# Directorate Vertical Slice

## Purpose

The Directorate is the first complete playable FORGELINE faction content set. It exercises the existing construction, economy, industrial production, power, logistics, navigation, battlefield supply, intelligence, direct-combat, armor, and indirect-fire systems without faction-specific simulation shortcuts.

The stable faction identity is:

- faction key: `faction.directorate`
- content namespace: `directorate`
- faction ID: `1`

Unit keys use the `directorate.unit.*` namespace. Existing building keys remain stable for compatibility while faction availability explicitly binds them to the Directorate slice.

## Production presentation

The core Vertical Slice presentation now covers both the six core unit families and the nine required industrial building families. Buildings resolve through stable `building.directorate.*` runtime IDs, shared construction/state modules, LOD0–LOD2, strategic-symbol bindings, and existing indexed instancing.

The presentation layer derives construction, power, production-idle, damage, critical, and destroyed state from existing simulation components. It does not create faction-specific simulation shortcuts. Central Divide also uses the Directorate road kit and a state-driven North Bridge visual family over its existing authoritative GroundRoad and strategic-infrastructure systems.

See [Directorate Building and Infrastructure Assets](DirectorateBuildingAssets.md).

## Progression

The vertical slice uses three bounded availability tiers.

| Tier | Structures | Units |
| --- | --- | --- |
| Bootstrap | Command Core, Power Plant, Mine / Extractor, Storage Depot | none produced yet |
| Industrial Foundation | Smelter, Refinery, Electronics Fabricator, Logistics Hub, Barracks, Vehicle Factory, Ammunition Plant, Supply Depot, Radar | Rifle Squad, Combat Engineer, Scout Vehicle, Cargo Truck, Supply Truck |
| Mechanized Warfare | all previous structures | Main Battle Tank, Mobile Artillery, plus all previous units |

Availability is data-driven. Build menus are derived from faction building availability, while unit-production menus combine faction availability with the production capabilities of the selected facility.

## Structures

| Structure | Construction cost | Ticks | Power | Primary role |
| --- | --- | ---: | ---: | --- |
| Command Core | 240 Ferrous Ore, 80 Silicates, 40 Volatiles | 160 | -20 | command anchor and 4,000-capacity bootstrap inventory |
| Power Plant | 160 Ferrous Ore, 60 Silicates, 20 Volatiles | 100 | +100 | logical network generation |
| Mine / Extractor | 100 Ferrous Ore, 40 Silicates | 80 | -15 | deposit-bound raw-resource extraction |
| Storage Depot | 120 Ferrous Ore, 40 Silicates | 70 | -5 | 5,000-capacity aggregate storage |
| Smelter | 180 Ferrous Ore, 80 Silicates, 30 Volatiles | 120 | -30 | Steel processing |
| Refinery | 160 Ferrous Ore, 60 Silicates, 40 Volatiles | 110 | -25 | Fuel processing |
| Electronics Fabricator | 170 Ferrous Ore, 100 Silicates, 20 Volatiles | 130 | -35 | Electronics processing |
| Logistics Hub | 180 Ferrous Ore, 80 Silicates, 20 Volatiles | 100 | -10 | regional storage and distribution |
| Barracks | 180 Steel, 60 Electronics | 120 | -20 | Infantry unit production |
| Vehicle Factory | 320 Steel, 120 Electronics | 180 | -40 | Vehicle and Logistics unit production |
| Ammunition Plant | 190 Ferrous Ore, 80 Silicates, 30 Volatiles | 130 | -35 | Ammunition processing |
| Supply Depot | 150 Ferrous Ore, 60 Silicates, 30 Volatiles | 90 | -8 | Fuel/Ammunition storage and battlefield resupply |
| Radar | 120 Steel, 80 Electronics | 110 | -18 Critical | 600 m radar detection / 180 m identification |

Unit-production facilities expose real input inventories and register as logistics destinations. Production stops without full required power and cannot advance until all physical input resources are available.

## Units

| Unit | Facility | Cost | Ticks | Battlefield role |
| --- | --- | --- | ---: | --- |
| Rifle Squad | Barracks | 24 Steel, 6 Electronics, 5 Fuel, 30 Ammunition | 90 | line infantry; short-range direct fire |
| Combat Engineer | Barracks | 28 Steel, 12 Electronics, 4 Fuel, 20 Ammunition | 100 | utility infantry with defensive carbine |
| Scout Vehicle | Vehicle Factory | 70 Steel, 24 Electronics, 60 Fuel, 40 Ammunition | 120 | fast reconnaissance, autocannon, visual/radar sensing |
| Main Battle Tank | Vehicle Factory | 210 Steel, 65 Electronics, 108 Fuel, 20 Ammunition | 220 | heavily armored direct-fire breakthrough platform |
| Mobile Artillery | Vehicle Factory | 150 Steel, 55 Electronics, 90 Fuel, 18 Ammunition | 200 | intelligence-dependent indirect fire |
| Cargo Truck | Vehicle Factory | 55 Steel, 12 Electronics, 120 Fuel | 100 | physical regional cargo transport; 220 cargo capacity |
| Supply Truck | Vehicle Factory | 65 Steel, 18 Electronics, 120 Fuel | 115 | battlefield Fuel/Ammunition delivery; 240 cargo capacity |

Initial unit Fuel and Ammunition quantities are never free. Any initial onboard quantity is covered by the corresponding production input cost before the unit entity is created.

## Combat, Movement, and Intelligence

All units are composed from the existing ECS components and simulation systems.

- Infantry uses the Infantry navigation movement class.
- Scout and logistics vehicles use Wheeled navigation.
- Main Battle Tank and Mobile Artillery use Tracked navigation.
- Rifle Squad, Combat Engineer, Scout Vehicle, and Main Battle Tank use the existing direct-fire weapon pipeline.
- Main Battle Tank uses physical projectile delivery.
- Mobile Artillery uses the existing fire-mission and ballistic indirect-fire pipeline.
- Directorate vehicle and infantry definitions use existing directional armor profiles where configured.
- Every unit owns physical Fuel/Ammunition supply state through the existing inventory-backed battlefield-supply system.
- Scout Vehicle combines visual sensing with radar.
- Radar structures use the existing faction-intelligence system.
- Structures receive an intelligence signature when construction completes.

## Production and Logistics Flow

The intended slice flow is:

`Raw deposits -> extraction -> storage/logistics -> processing -> Steel/Fuel/Electronics/Ammunition -> Barracks/Vehicle Factory -> units -> battlefield supply -> combat`

Industrial processing remains owned by `ProductionSystem`. Unit production is a separate fixed-tick game-composition system because its output is an ECS entity rather than an inventory resource.

A unit-production facility:

1. receives resources through its real input inventory;
2. selects a deterministic queued request;
3. requires sufficient allocated power;
4. reserves every unit input atomically;
5. advances fixed-tick production;
6. consumes the reserved inputs;
7. creates the configured unit through `UnitFactory`;
8. initializes the unit with its real movement, combat, armor, intelligence, and supply components;
9. if the facility has a rally point, issues the produced unit a normal simulation-owned movement order toward that point.

Cargo Truck and Supply Truck continue to use the existing physical transport and battlefield-supply systems. Unit factories are registered as logistics cargo destinations rather than receiving resources through a special transfer path.

### Unit-production rally points

Barracks and Vehicle Factory rally points are authoritative gameplay state on the production facility.

- Rally-point changes enter the simulation through an ownership-validated unit-production command.
- The configured world position is copied into unit-production read models for presentation.
- Completing a unit does not teleport it to the rally point. The unit spawns at the facility's normal spawn offset and receives a standard `MovementOrder`.
- Navigation, terrain traversal, movement speed, Fuel consumption, blocking, and later tactical orders remain owned by their existing systems.
- Facilities without a configured rally point preserve the existing spawn-and-idle behavior.

## Validation

`GameContentValidator.ValidateDirectorate` fails loading with actionable errors for:

- missing required Directorate buildings or units;
- unresolved building or unit IDs;
- resources referenced by construction, unit costs, or recipes that do not exist;
- unit references to missing direct-fire weapons, artillery weapons, or armor profiles;
- units that have no eligible production facility at their availability tier;
- processing facilities that have no compatible recipe;
- unit-production facilities with empty production menus;
- initial Fuel or Ammunition quantities that exceed the physical amounts consumed by production;
- faction or stable-namespace mismatches.

The normal test suite includes a bounded headless vertical-slice smoke scenario that constructs every required structure through the authoritative construction lifecycle and composes every required unit without graphics, audio, or UI. Focused regression coverage also verifies rally-point ownership, player read-model publication, and spawn-to-movement-order routing.

## Presentation Status

The six core Directorate Vertical Slice unit families now resolve through the production source-to-runtime asset pipeline with stable mesh/material IDs, LOD0–LOD2, collision references, gameplay-facing sockets, damage/wreck presentation, and strategic-symbol bindings. Main Battle Tank LOD0 separates hull and turret so presentation can rotate the turret toward the current authoritative weapon target without changing simulation ownership; strategic LODs remain combined meshes. The existing Combat Engineer deliberately reuses the Rifle Squad presentation family until a dedicated infantry-art package exists.

Unit presentation remains strictly downstream from authoritative gameplay state. Health determines visual damage state, destruction leaves only a non-commandable presentation wreck proxy, and render LOD never changes movement, hitbox, supply, combat, or intelligence behavior.

See [Directorate Unit Assets](DirectorateUnitAssets.md) for the binding unit-asset IDs, source layout, LOD distances, socket names, damage treatment, strategic references, and current animation boundary.

Final high-detail textures, skeletal animation, articulated vehicle motion, VFX, audio, portraits, and polished RTS production UI remain later content work.

## Current Boundaries

The slice intentionally excludes:

- Consortium and Ascendancy content;
- air and naval units;
- Tier 4/5 strategic technology and superweapons;
- campaign progression;
- veterancy;
- final production art/audio;
- a Repair Station or repair subsystem expansion.

Repair behavior should only be added when a reusable generic repair foundation exists.


## Human Construction and Production Access

The Windows client exposes the current Directorate content through one shared action surface rather than one hotkey per catalog entry.

- `B`: all current constructible Directorate buildings from the live building catalog.
- `P`: supported industrial recipes for one selected owned processing facility.
- `U`: supported units for one selected owned Barracks or Vehicle Factory.
- `Tab` selects an action or queued request; `Enter` activates the selected action; `C` cancels a selected production request.
- `T` changes production priority; `M` changes industrial request mode.

Availability and costs are copied from the same building, recipe, unit, and inventory contracts used by simulation. A human slot does not receive a hidden strategic controller: construction and production originate as explicit player commands and remain subject to authoritative ownership, material, power, placement, output-capacity, and fixed-tick rules.
