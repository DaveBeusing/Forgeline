# Building Placement and Construction

## Purpose

FORGELINE uses an authoritative, command-driven construction lifecycle. The interactive client may preview a building footprint and explain whether the current location appears valid, but only simulation command execution decides whether construction starts.

The initial construction slice supports:

- Command Core;
- Power Plant;
- Mine / Extractor;
- Storage Depot;
- Smelter shell.

Building definitions are data-driven and use stable `BuildingId` values rather than CLR type names.

## Building definitions and footprints

`BuildingDefinitionCatalog` contains stable identifiers, content keys, footprints, construction duration, resource costs, capability metadata, slope limits, and initial presentation identity.

`BuildingFootprint` describes width, depth, and height. The four cardinal `BuildingOrientation` values rotate width and depth deterministically.

The initial definitions are exposed by `InitialBuildingDefinitions.CreateCatalog()`. They provide construction contracts for the first economy structures while leaving production recipes and final art to their owning systems.

## Placement preview

`BuildingPlacementService.CreatePreview` creates a non-authoritative read model for presentation.

The preview reports:

- selected building;
- grounded world position;
- orientation;
- authoritative-shaped footprint bounds;
- valid or invalid state;
- explicit invalid reason;
- resource-deposit binding where required.

Preview evaluation queries terrain and current world occupancy but never changes simulation state.

The Windows development client exposes:

- `F4` — Command Core;
- `F5` — Power Plant;
- `F6` — Mine / Extractor;
- `F7` — Storage Depot;
- `F8` — Smelter;
- `F9` — rotate clockwise;
- left click — submit the valid preview as a build command;
- `Escape` — leave building-placement mode;
- `F2` — toggle world diagnostics, including resource-deposit bounds.

The placement ghost is green when currently valid and red when invalid. Invalid previews include the rejection reason.

## Authoritative placement validation

`BuildingCommandProcessingSystem` runs during `SimulationPhase.OrderProcessing`. It re-evaluates placement from simulation-owned state when the command executes.

Validation includes:

1. stable building definition lookup;
2. terrain availability at footprint samples;
3. world bounds;
4. maximum slope;
5. buildable-area policy;
6. authoritative spatial overlap;
7. resource-deposit presence for extractors;
8. source inventory validity and ownership;
9. construction-resource availability.

The client preview is therefore guidance only. A location that becomes blocked after preview is rejected when the command reaches simulation.

Accepted construction sites are inserted into the spatial index immediately during command processing. Multiple build commands targeting the same footprint in the same tick therefore resolve deterministically: the first accepted site occupies the area and later conflicting requests are rejected.

## Build command

`BuildCommand` carries:

- issuer;
- stable building type;
- world position;
- cardinal orientation;
- construction source inventory entity;
- submission tick metadata.

The command itself does not directly construct a building. During `InputCommands` it creates a simulation-owned build request. `BuildingCommandProcessingSystem` validates and resolves that request during `OrderProcessing`.

This preserves the same external action boundary used by other simulation commands and keeps the workflow compatible with future replay and multiplayer command streams.

## Resource reservation policy

Construction uses the existing `InventoryStore` reservation contract.

When a build request is accepted:

1. every required cost is reserved in the source inventory;
2. partial reservation failure is rolled back before the request is rejected;
3. accepted sites keep those reservations for their full construction lifetime.

Construction resources remain physically present but unavailable to other consumers while reserved.

When construction completes, every reserved cost is consumed exactly once.

When construction is cancelled, every reservation is released in full. The initial policy is therefore a 100% refund before completion.

No resource is consumed during intermediate progress ticks.

## Construction state and progress

An accepted site is a normal simulation entity containing:

- `WorldTransform`;
- `VisualIdentity`;
- `ControllableEntity`;
- static `SpatialPresence`;
- `ConstructionSite`.

`BuildingConstructionSystem` runs in the Economy phase and advances every active site by one fixed simulation tick.

`ConstructionSite` owns:

- building ID;
- owner;
- source inventory;
- start tick;
- required construction ticks;
- progress ticks;
- optional extractor resource ID;
- optional resource-deposit binding.

Construction progress is simulation-owned and can run fully headless.

## Cancellation

`CancelConstructionCommand` is owner-authorized.

When accepted, the site receives a simulation-owned cancellation request. During the construction phase the site:

1. releases every reserved construction resource;
2. removes its authoritative occupancy;
3. is destroyed;
4. increments construction diagnostics.

Cancellation never consumes the reserved material.

## Completion and capability activation

Completion first verifies that all required reservations still exist, then consumes them and activates the definition's capabilities. Activation happens once, immediately before `ConstructionSite` is replaced with `CompletedBuilding`.

Initial capability activation includes:

- Command Core: command marker and critical power consumer;
- Power Plant: power-network membership and generator;
- Mine / Extractor: dedicated output inventory, power consumer, and `ResourceExtractor` bound to the resource deposit;
- Storage Depot: inventory storage, storage-depot contract, and power consumer;
- Smelter shell: processing marker and power consumer.

Power-capable buildings initially join the logical development network with ID `1`. Physical transmission topology remains a later power-system extension.

The Smelter deliberately activates only the processing capability shell. Production recipes remain outside this construction slice.

## Construction diagnostics

`BuildingCommandProcessingSystem.Metrics` exposes accepted and rejected command counts, the last command rejection reason, the last placement failure, and the last created site.

`BuildingConstructionSystem.Metrics` exposes active sites, cancelled sites, and completed buildings.

`BuildingConstructionDebugSnapshot` extracts stable read models for active and completed structures. Presentation uses these read models to render:

- placement footprint and validity;
- active construction footprint;
- fixed-tick progress and tick counts;
- optional completed-building debug bounds.

Presentation never mutates construction state.

## Extractor placement

Mine / Extractor placement requires the footprint center to fall inside a live, non-depleted `ResourceDeposit`.

The accepted build request records both the deposit entity and its stable resource ID. Completion creates an output inventory accepting that resource and binds `ResourceExtractor` to the same deposit.

This makes extraction operational without redefining deposit or inventory semantics.

## Tests and invariants

Regression coverage validates:

- stable initial building definitions and rotated footprints;
- buildable-area rejection without preview side effects;
- preview-valid placement becoming invalid before command execution;
- insufficient-resource rejection with reservation rollback;
- same-tick concurrent footprint conflicts;
- cancellation refund and occupancy cleanup;
- exact-once resource consumption and capability activation;
- extractor deposit/output/power activation;
- multi-tick headless construction completion.

The critical invariants are:

- preview never authorizes simulation state;
- every accepted footprint becomes authoritative occupancy;
- failed reservation sequences roll back;
- cancellation cannot duplicate or lose material;
- completion consumes costs once;
- capability activation occurs once;
- construction remains runnable without graphics, audio, UI, or a window.

## Current boundaries

This slice intentionally does not implement:

- builder-unit animation or pathing;
- walls;
- roads or rail construction;
- defensive structures;
- repair or rebuild;
- blueprint copy/paste;
- terrain deformation;
- final building art;
- complete production recipes.

Those systems can build on `BuildingDefinition`, authoritative footprint occupancy, `ConstructionSite`, and `CompletedBuilding` without replacing the construction lifecycle.
