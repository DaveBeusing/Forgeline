# Building Placement and Construction

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Purpose](#purpose)
- [Building definitions and footprints](#building-definitions-and-footprints)
- [Placement grid and visible access](#placement-grid-and-visible-access)
- [Placement preview](#placement-preview)
- [Authoritative placement validation](#authoritative-placement-validation)
- [Build command](#build-command)
- [Resource reservation policy](#resource-reservation-policy)
- [Construction state and progress](#construction-state-and-progress)
- [Cancellation](#cancellation)
- [Completion and capability activation](#completion-and-capability-activation)
  - [Shared completed-building combat initialization](#shared-completed-building-combat-initialization)
- [Construction diagnostics](#construction-diagnostics)
- [Extractor placement](#extractor-placement)
- [Tests and invariants](#tests-and-invariants)
- [Current boundaries](#current-boundaries)
- [Player action boundary](#player-action-boundary)
- [Production presentation boundary](#production-presentation-boundary)
- [Rotation during active placement](#rotation-during-active-placement)
- [Paid construction templates](#paid-construction-templates)

</details>

## Purpose

Hold Shift when clicking a current valid placement to continue placing that building after its matching authoritative acceptance. Each click submits at most one request; held buttons cannot repeat. Submission acceptance only acknowledges the queue, and a pending request blocks further placement with explicit pending feedback. A rejected command keeps placement intent but invalidates the preview. A successful unmodified click ends placement. Releasing Shift after the click does not change the submitted continuation intent; the next unmodified successful click ends the sequence.

Continuation requires a new preview request identity, matching building/orientation/location and completed tick at or after the result. The previous valid preview is discarded immediately on submission. Construction resource, footprint and same-tick conflicts remain revalidated by the existing command pipeline. Host submission sequence and command correlation identity connect results to the correct intent; unrelated/late results cannot revive canceled placement.

Placement rotation is remembered for the last stable building type during the session, including after successful single placement and reselection. Switching building type starts at North. Escape, focus loss, help/pause, incompatible panel mode, display change and session replacement clear repeat/pending state and rotation memory. No orientation or placement authority is persisted to saves or settings.

FORGELINE uses an authoritative, command-driven construction lifecycle. The interactive client may preview a building footprint and explain whether the current location appears valid, but only simulation command execution decides whether construction starts.

The initial construction slice supports:

- Command Core;
- Power Plant;
- Mine / Extractor;
- Storage Depot;
- Smelter;
- Refinery;
- Electronics Plant;
- Logistics Hub;
- Ammunition Plant;
- Supply Depot;
- Barracks;
- Vehicle Factory;
- Radar.

Building definitions are data-driven and use stable `BuildingId` values rather than CLR type names.

## Building definitions and footprints

`BuildingDefinitionCatalog` contains stable identifiers, content keys, footprints, construction duration, resource costs, capability metadata, slope limits, initial presentation identity, and completed-building combat values. `MaximumHealth` must be finite and positive; `TargetPriority` and an optional `ArmorProfileId` complete the declared combat contract. A specified armor profile must resolve in the content's armor catalog.

`BuildingFootprint` describes width, depth, and height. The four cardinal `BuildingOrientation` values rotate width and depth deterministically.

The initial definitions are exposed by `InitialBuildingDefinitions.CreateCatalog()`. They provide construction contracts for the first economy structures, including processing capability and production-inventory metadata, while recipe definitions remain owned by the economy production system. The nine core Vertical Slice industrial families now have stable Directorate presentation assets; gameplay footprints remain independent from render geometry and LOD.

The current durability values are shared by normal construction and starting buildings:

| Building | Maximum Health | Target priority |
| --- | ---: | ---: |
| Command Core | 1,200 | 100 |
| Power Plant | 750 | 0 |
| Mine / Extractor | 600 | 0 |
| Storage Depot | 900 | 0 |
| Smelter | 900 | 0 |
| Refinery | 800 | 0 |
| Electronics Plant | 700 | 0 |
| Logistics Hub | 800 | 0 |
| Ammunition Plant | 800 | 0 |
| Supply Depot | 700 | 0 |
| Barracks | 800 | 0 |
| Vehicle Factory | 1,100 | 0 |
| Radar | 500 | 0 |

All thirteen current definitions leave `ArmorProfileId` unspecified and retain the existing unarmored damage resolution. A content definition can declare an armor profile without changing the construction or damage pipeline.

## Placement grid and visible access

Interactive building placement snaps to the same 4m grid as conveyors. A bounded
terrain-following grid is visible around the preview. Foundation and clearance
validation use that snapped site. Rotation keeps the same grid origin.

The building ghost shows cyan **IN** and amber **OUT** arrows at prospective
fixed-purpose belt sockets. Command Core and Storage Depot instead show four
neutral **IN / OUT** positions at their actual prospective footprint, orientation,
foundation height and raised conveyor tier. An invalid placement marks these
positions as blocked rather than implying a valid connection. Mines expose only an
eligible solid-resource output; Barracks and Vehicle Factory expose only input.
Buildings without belt ports do not show belt sockets. The truck loading corridor
also identifies **CARGO IN / CARGO OUT** access, including for liquid-processing
buildings such as the Refinery. These arrows distinguish the loading access from
belt connections and do not add new transport ports.

Existing saves, starting layouts and scripted build commands keep their original
positions. Conveyor terminal snapping retains exact legacy socket coordinates.

## Placement preview

`BuildingPlacementService.CreatePreview` remains simulation/game-side validation and creates a non-authoritative detached read model. The presentation controller no longer receives `EntityRegistry` or `BuildingPlacementService`.

Instead, `RtsBuildingPlacementController` publishes a bounded latest placement-preview request through `PresentationInteractionState`. The request contains a monotonically increasing request ID, issuer, building, requested position, and orientation. At the simulation-owned completed-tick boundary, `PresentationExtractor` evaluates the latest request against current authoritative terrain/occupancy facts and publishes `BuildingPlacementPreviewReadModel` with the matching request ID and completed tick.

The preview reports:

- selected building;
- grounded world position;
- orientation;
- authoritative-shaped footprint bounds;
- valid or invalid state;
- explicit invalid reason;
- resource-deposit binding where required.

Preview evaluation queries terrain and current world occupancy but never changes simulation state.

The Windows development client exposes the complete current building catalog through the shared player action palette:

- `B` — open/close the construction palette;
- `Tab` — move through catalog entries;
- `Enter` or left click on a palette row — begin placement for the selected building;
- the palette displays copied construction costs and current Command Core inventory availability.

F3-F9 direct development shortcuts are retired. Use the construction palette
for Command Core, Power Plant, Extractor, Storage, Smelter and the rest of the
catalog. Q/E rotates an active preview; left click requests the current valid
location and Escape cancels. Optional gameplay actions can be assigned other
permitted keys in Settings. Developer diagnostics are configured through F1;
unmodified F2 remains the activated Developer world's master switch.

The placement ghost is green when the matching preview is currently valid and red when it is currently invalid. Preview freshness is explicit:

- `Current` — the published request ID matches the controller's latest request;
- `Stale` — a previous or out-of-order preview arrived after a newer request was issued;
- `Unavailable` — no result exists yet for the current placement intent.

A click can produce a build request only from a `Current` valid preview. Stale/unavailable previews remain advisory display state and cannot authorize a command. Invalid previews expose the rejection reason but not the identity of an obstructing entity, so placement feedback does not become an enemy-information side channel.

## Authoritative placement validation

The compact context card distinguishes **placement check pending**, **stale preview**, **current invalid reason**, **location valid at captured tick**, and **missing core materials**. Invalid text uses the existing allowed failure labels (bounds, slope, terrain, buildable area, obstruction or required deposit); it never names an obstructing entity or exposes a deposit reference. Freshness takes precedence over old validity and costs. For a current valid location, material text uses only the coherent Action Dock construction model's captured Command Core quantities; missing/stale action models omit amounts. Only as many bounded cost rows as fit are shown.

This text cannot authorize placement. Left click requests construction from the existing current-valid preview path; execution still revalidates location, ownership and inventory. A shortage hint does not grant resources or fabricate a deficit from absent data. Q/E rotates and Escape cancels. The context card captures world clicks to prevent requests through its text, and old-session/tick views are discarded at render time.

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

The client preview is therefore guidance only. The eventual `BuildCommand` carries the chosen position/orientation and is re-evaluated from current simulation state during `OrderProcessing`. A location that becomes blocked, loses a required deposit, or otherwise becomes invalid after preview is rejected authoritatively even if the preview was previously green.

Accepted construction sites are inserted into the spatial index immediately during command processing. Multiple build commands targeting the same footprint in the same tick therefore resolve deterministically: the first accepted site occupies the area and later conflicting requests are rejected.

## Build command

`BuildCommand` carries:

- issuer;
- stable building type;
- world position;
- cardinal orientation;
- construction source inventory entity;
- submission tick metadata;
- an optional player-command correlation ID used only to route the copied result back across the presentation boundary.

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

Construction progress is simulation-owned and can run fully headless. Presentation reads that completed-tick state and maps it to shared foundation, structural-frame, and partial-shell runtime meshes; it never advances construction itself.

Construction sites retain their existing combat boundary. Accepting a site does not attach completed-building Health, Structure targeting, or radar capability. The shared combat initializer rejects an entity that still has `ConstructionSite`; unfinished sites do not gain combat vulnerability from the completed-building contract.

## Cancellation

`CancelConstructionCommand` is owner-authorized.

When accepted, the site receives a simulation-owned cancellation request. During the construction phase the site:

1. releases every reserved construction resource;
2. removes its authoritative occupancy;
3. is destroyed;
4. increments construction diagnostics.

Cancellation never consumes the reserved material.

## Completion and capability activation

Completion first verifies that all required reservations still exist, then consumes them and activates the definition's capabilities. Activation happens once, immediately before `ConstructionSite` is replaced with `CompletedBuilding`. `BuildingCombatInitialization.Initialize` then attaches the completed structure's combat contract using the same catalog definition.

Initial capability activation includes:

- Command Core: command marker and critical power consumer;
- Power Plant: power-network membership and generator;
- Mine / Extractor: dedicated output inventory, power consumer, and `ResourceExtractor` bound to the resource deposit;
- Storage Depot: inventory storage, storage-depot contract, and power consumer;
- Smelter: processing marker, Steel Processing production facility, dedicated input/output inventories, and power consumer;
- Refinery: processing marker, Fuel Processing production facility, dedicated input/output inventories, and power consumer;
- Electronics Plant: processing marker, Electronics Processing production facility, dedicated input/output inventories, and power consumer.

Power-capable buildings join the owner's logical development network, whose ID is the owner's player ID. Physical transmission topology remains a later power-system extension. A completed radar waits for a valid power allocation before scanning; see [Battlefield Intelligence](BattlefieldIntelligence.md#powered-radar-operation).

Construction activates processing capability and inventory bindings only. Recipe execution remains owned by `ProductionSystem`, preserving the construction/production responsibility boundary.

### Shared completed-building combat initialization

The initializer derives controllable ownership and combat faction from `CompletedBuilding.Owner`, requires `TargetClass.Structure`, and attaches catalog Health and target priority when absent. Its `CombatHitbox` uses the placed `SpatialPresence` half-extents, including the selected orientation; a completed entity without spatial presence uses the definition's North-facing footprint. Gameplay collision bounds remain independent of render assets.

Initialization is idempotent. Existing Health, including damaged or depleted Health, is preserved; repeated initialization never repairs a building. Existing target priority, hitbox, and armor are retained. Declared armor is attached only when the entity has no armor component. Inconsistent controllable ownership, combat faction, target class, building definition, or a player ID that cannot represent a combat faction is rejected.

The starting-base factory initializes its Command Core from the composition's building catalog. Match initialization applies the same contract to completed starting structures before objective attachment. Reattaching the same Command Core objective preserves its entity identity and Health; conflicting objective identities are rejected.

Completed structures participate in ordinary target acquisition, direct fire, damage resolution, and lifecycle destruction. Destruction closes building-owned economic work and inventories at the lifecycle boundary; its stock-loss and wreck rules are documented in [Combat Execution](CombatExecution.md#completed-buildings-and-economic-ownership). Building again uses the normal placement and construction path and creates a fresh entity.

## Construction diagnostics

`BuildingCommandProcessingSystem.Metrics` continues to expose simulation diagnostics. Presentation does not poll those mutable metrics for command feedback. Correlated player submissions instead receive a copied `BuildCommandResult` through the bounded player-command result boundary.

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
- preview-valid placement becoming invalid before command execution, including correlated authoritative rejection delivery;
- stale placement-preview responses never authorizing a build request;
- current matching placement-preview responses producing the expected request/tick metadata;
- insufficient-resource rejection with reservation rollback;
- same-tick concurrent footprint conflicts;
- cancellation refund and occupancy cleanup;
- exact-once resource consumption and capability activation;
- completed-building combat activation through normal construction, with no Health or Structure targeting on the unfinished site;
- all thirteen definitions' shared combat contract, starting-catalog values, and repeat-initialization preservation;
- invalid durability, ownership, combat identity, and armor-reference rejection;
- Command Core objective identity preservation;
- extractor deposit/output/power activation;
- multi-tick headless construction completion.

`BuildingConstructionTests` exercises the normal command/completion path. `BuildingCombatInitializationTests` covers the shared initializer, starting Core, and custom starting industry, while `DirectorateContentTests` covers optional armor references. `RadarPowerTests` constructs its radar through the same authoritative path and checks independent power outages and recovery. These checks use simulation state and do not require a rendered client.

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
- building repair or automatic rebuilding;
- blueprint copy/paste;
- terrain deformation;
- final high-detail building art replacement;
- mechanical building animation and richer VFX;
- complete production recipes.

Those systems can build on `BuildingDefinition`, authoritative footprint occupancy, `ConstructionSite`, and `CompletedBuilding` without replacing the construction lifecycle.

## Player action boundary

Catalog selection does not authorize construction. The palette reads immutable `PlayerActionSnapshot` data published at the completed-tick boundary and only selects a `BuildingId` for the existing placement controller. Placement still requires a current valid preview, and the eventual `BuildCommand` is revalidated authoritatively.

Pointer input inside the action palette is captured before placement and world-selection processing. Switching to a production action mode cancels an active placement intent so panel input cannot become an unintended world click. Session changes invalidate the panel's transient mode and selection state.

## Production presentation boundary

The Directorate building baseline is documented in [Directorate Building and Infrastructure Assets](DirectorateBuildingAssets.md). It maps the existing construction, power, production, damage, and destruction state into immutable render metadata after each completed tick.

Presentation changes do not modify `BuildingFootprint`, `SpatialPresence`, placement validity, power allocation, construction progress, production state, or combat damage. LOD and shared collision asset references are renderer/tooling contracts only.

## Rotation during active placement

With a valid active building placement, Q rotates 90 degrees counterclockwise and E 90 degrees clockwise. The optional `RotatePlacement` action starts unbound; F3-F9 are retired. Each accepted edge changes the existing four-state `BuildingOrientation`, invalidates the prior preview request and requires a fresh matching snapshot before an authoritative build request can be submitted. Awaiting an already issued build result does not accept a late rotation. Simultaneous Q/E edges cancel one another; key repeats do not produce extra turns. The placement HUD prompt now displays the actual contextual controls.

## Paid construction templates

Open Build and select **Template / STORAGE LINK** or **Template / SMELTER OUTPUT**.
If Storage or Smelter placement is deliberately assigned another permitted key,
Ctrl plus that key selects the corresponding template. Both actions start unbound. Move the complete draft on the shared
4m grid; Q/E rotates it by 90 degrees. One left click starts the explicit
three-step application. Escape, right click, focus/modal/pause/display changes or
session replacement cancel unsent steps. Submitted commands remain authoritative.

The initial bounded catalog stores two layouts: two Storage Depots, or a Smelter
followed by a Storage Depot. Both keep their building orientation and a 48m
relative displacement. Their explicit connection is building 1 OUT to building 2
IN. These are fixed authored designs, not a capture/editor/import format. No
production recipe, power generation, stock policy, external port or resource
deposit is inferred. Further industrial configuration remains deliberate.

The whole draft shows both footprints, loading corridors, directed ports and
the prospective conveyor parts. Each building label quotes its individual
material requirements; the connection quotes Steel including required supports.
The context panel lists combined requirements and available unreserved Command
Core stock. Invalid building locations and the failed connection location are
marked separately. If no priced route can be proposed, its cost is explicitly\nunavailable and no complete total quote is displayed. A design must have valid geometry and affordable combined
requirements before the initial click. This observation is not a reservation,
guaranteed route, paid order or completed building.

**Acceptance is sequential and partial, not atomic.** At most one ordinary command
is outstanding. Buildings use SubmitBuild/BuildCommand and their usual per-building
reservation, site progression, cancellation and consumption behavior. The next
building is checked again against a fresh completed-tick observation. The full
created-site identity is returned in correlated construction feedback. A rejected
or unsent step stops the remaining work; nothing retries or grants resources.
Already accepted sites stay accepted and use normal cancellation/demolition rules.

The connection waits for both exact owned site identities to become completed,
living buildings. It then uses the normal automatic directed ConveyorConstruction
command with those explicit endpoints and pays the actual route's Steel. Changed
terrain, stock, occupation, ownership, identity, road/support, dock or port state
can change the plan or reject it. Missing endpoints stop the link. Each complete
conveyor route retains its own existing all-or-nothing payment semantics; the
larger template has no rollback or up-front shared reservation.

Blue footprints identify accepted sites/completed buildings; green/red draft
geometry identifies observed validity. Per-step UNSENT, REQUEST PENDING, SITE
ACCEPTED, WAITING FOR COMPLETED BUILDINGS, LINK ACCEPTED, REJECTED, SKIPPED and
CANCELLED UNSENT remain distinct. Acceptance does not claim completed production
or transported materials. Closing placement never retracts submitted commands.

The template controller and pending continuation exist only in the current client
session. New Game, Restart and Load discard them. Accepted ordinary Build and
Conveyor inputs already use existing replay codecs and authoritative site/inventory
state. There is no new authoritative component, command kind, saved template,
registered content identity or schema: envelope 1, payload 11 and authoritative
schema 16 remain current. A save between steps retains paid work but does not
resume the unsent template continuation on load. Historical hashes stay intact.

Status: implemented; manual user review outstanding. Product compilation does
not qualify real pointer input, native label legibility, physical construction,
stock consumption or Save/Replay. Review both templates and all rotations,
terrain/build-area/road/loading-lane/occupied-port rejection, changed stock and
ownership, destroyed/cancelled sites, partial acceptance, cancellation before
and after submission, session replacement and save/replay during each paid step.
