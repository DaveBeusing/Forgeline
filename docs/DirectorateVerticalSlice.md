# Directorate Vertical Slice

## Purpose

The Directorate is the first complete playable FORGELINE faction content set. It exercises the existing construction, economy, industrial production, power, logistics, navigation, battlefield supply, intelligence, direct-combat, armor, and indirect-fire systems without faction-specific simulation shortcuts.

The stable faction identity is:

- faction key: `faction.directorate`
- content namespace: `directorate`
- faction ID: `1`

Unit keys use the `directorate.unit.*` namespace. Existing building keys remain stable for compatibility while faction availability explicitly binds them to the Directorate slice.

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
8. initializes the unit with its real movement, combat, armor, intelligence, and supply components.

Cargo Truck and Supply Truck continue to use the existing physical transport and battlefield-supply systems. Unit factories are registered as logistics cargo destinations rather than receiving resources through a special transfer path.

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

The normal test suite includes a bounded headless vertical-slice smoke scenario that constructs every required structure through the authoritative construction lifecycle and composes every required unit without graphics, audio, or UI.

## Presentation Status

The slice uses distinguishable placeholder presentation IDs for every required structure and unit. These IDs are stable content metadata, not final production assets.

Final models, materials, animations, effects, audio, portraits, and polished RTS build/production UI remain outside this slice. Placeholder presentation must not affect simulation outcomes.

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
