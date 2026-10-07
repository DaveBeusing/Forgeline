# Selection and Command Interaction

## Purpose

The first RTS interaction layer connects visible controllable presentation entities to the authoritative fixed-tick simulation command pipeline without allowing input or rendering code to mutate live simulation state.

The implemented flow is:

```text
Win32 mouse/keyboard input
    ↓
InputState
    ↓
RtsSelectionController
    ↓
presentation snapshot picking
    ↓
MovementOrderRequest
    ↓
ForgeLine.Client composition
    ↓
MoveEntitiesCommand
    ↓
SimulationCoordinator command schedule
    ↓
Input Commands tick boundary
    ↓
individual MovementOrder or MovementGroup
    ↓
HierarchicalNavigationSystem / FormationMovementSystem
    ↓
local MovementOrder
    ↓
GroundMovementSystem
    ↓
authoritative WorldTransform
```

Strategic hierarchical navigation and shared-route formation movement are implemented. Role-aware combat formations, attack orders, permanent control groups, minimap commands, and final interaction styling remain deferred.

## Ownership Boundaries

Selection is player interaction state owned by presentation. It is not an authoritative simulation component and it does not change entity ownership.

Simulation owns:

- stable `EntityId` lifetime and generation validation;
- `ControllableEntity` ownership/category metadata;
- accepted `MovementOrder` state;
- movement-group identity, group orders, shared routes, slot assignments, and formation speed constraints;
- command execution at fixed simulation tick boundaries.

Presentation owns:

- hover state;
- the selected-entity set;
- click/toggle/drag-box interpretation;
- screen/world picking against extracted render instances;
- hover and selection feedback.

The client composition root is responsible for translating a presentation movement request into a simulation command.

## Selectable Metadata and Identity Mapping

`PresentationExtractor` copies `ControllableEntity` metadata into `SelectablePresentationMetadata` on each `RenderInstance`.

The metadata contains:

- authoritative stable `EntityId`;
- owning `PlayerId`;
- `ControllableEntityCategory`.

Selection never invents a separate gameplay identity. The same generation-aware `EntityId` extracted from simulation is returned by picking and later carried into commands.

Entities without controllable metadata remain renderable but are not selectable.

## Visibility and Filtering

Picking requires all of the following:

- the render instance has `RenderVisibilityMask.World`;
- mesh and material handles are valid;
- the entity is inside the current camera clip volume;
- selectable metadata is present;
- ownership matches the local selection filter;
- category intersects the allowed category filter.

The development client currently allows local `Unit` and `Logistics` categories. Local `Building` placeholders and opposing-player entities remain visible but cannot be selected by that player filter.

Presentation-only picking therefore cannot select hidden/non-world instances.

## Input Conventions

Initial interaction conventions are:

| Action | Input |
| --- | --- |
| Single selection | Left click |
| Clear selection | Left click empty world |
| Add/remove one entity | Shift + left click |
| Box selection | Left-drag |
| Toggle entities in box | Shift + left-drag |
| Movement order | Right click with a non-empty selection |
| Cycle development formation | F3 (Compact → Line → Column → Wedge) |

A drag becomes box selection after a small screen-space threshold so normal clicks are not interpreted as accidental boxes.

Focus loss clears raw input state. An interrupted selection gesture is cancelled when pointer validity is lost.

## Screen and Box Picking

Single selection uses the current RTS camera screen-to-world ray and tests it against interpolated render-instance bounds. The nearest valid hit is selected.

Drag-box selection is evaluated in screen space. Each eligible entity center is projected using `RtsCamera.WorldToScreen`; only currently visible projected points inside the normalized rectangle are included.

Both paths operate on `RenderWorld`, not directly on live ECS state.

## Movement Command Flow

Right click resolves a world target from the camera and current terrain query. The presentation controller produces a `MovementOrderRequest` containing:

- selected stable entity IDs;
- world-space target.

The client creates `MoveEntitiesCommand` with:

- issuer `PlayerId`;
- copied target entity IDs;
- world target;
- submission tick;
- currently selected formation template.

The development client starts with `Compact` and cycles the formation template with F3. This is a minimal command-surface control until the dedicated RTS command UI owns formation selection.

It submits the command through `SimulationCoordinator.SubmitCommand` for the next simulation tick and uses the matching `SimulationCommandSource`.

The command does not change `WorldTransform`.

At execution time it revalidates every target against the live ECS and owner. A single formation-capable ground unit receives or replaces a strategic `MovementOrder`. Two or more formation-capable ground units create one movement-group entity and receive `MovementGroupMember` state instead of independent long-range orders. Non-formation-capable accepted targets retain the individual-order path.

`FormationMovementSystem` requests one shared hierarchical route for the group, maintains stable formation slots, and emits `MovementOrderKind.FormationLocal` targets. `HierarchicalNavigationSystem` routes individual strategic orders but deliberately bypasses formation-local slot targets so a large selection does not fall back to one global path request per member. `GroundMovementSystem` consumes the current local target during the movement phase and remains the only system that changes `WorldTransform`.

## Stale Entity Handling

`EntityId` includes an index and generation. A destroyed entity ID therefore cannot accidentally address a later entity that reuses the same slot.

`MoveEntitiesCommand` uses safe component lookup at execution time. Missing, destroyed, generation-stale, non-controllable, or foreign-owned targets are rejected individually while valid targets in the same command continue to receive their order.

The command exposes accepted/rejected target counts and execution tick for development diagnostics.

## Production selection inspector

World-space selection rings, building footprints, hover markers, and tactical target markers remain separate from screen-space inspection. The selected set continues to be presentation-owned interaction state, while `PlayerExperienceSnapshotFactory` copies only authorized owned selection facts at the completed-tick boundary.

`PlayerSelectionSummary` now distinguishes safe common identity from single-entity detail. A single authorized entity may publish its concrete unit/building identity and operational values. A multi-selection publishes an exact common UnitId or BuildingId only when all authorized members share it; heterogeneous same-kind selections use a generic kind summary and mixed categories use `Mixed Selection`.

Per-entity Health, supply, Fuel, Ammunition, readiness, power, inventory, and work data is available only when exactly one authorized entity remains after alive/ownership filtering. This prevents a primary entity from being presented as if its status represented the entire selected group. Foreign-owned, destroyed, or generation-stale entities do not contribute to the inspector summary.

`SelectionInspectorHudSurface` owns the DPI-aware `SelectionInspector` HUD region and resolves existing semantic unit/building/status icons through `RtsUiIconCatalog`. Operational meaning is also expressed through explicit labels such as `SUPPLIED`, `CRITICAL`, `POWERED`, `BROWNOUT`, `RUNNING`, and `BLOCKED`, rather than color alone.

## Feedback and Diagnostics

Hover, selection, placement-preview, tactical-target, and strategic-overlay feedback use a dedicated player-facing world-overlay path around immutable presentation data. They no longer share the engineering debug buffer. Player-facing line markers render after depth-tested developer diagnostics and without depth testing so selection and command feedback remain readable over terrain and world geometry. None of these visuals mutates simulation state.

Engineering diagnostics use an independent category model. F2 toggles the developer master switch; Shift+F2 toggles Rendering, while Shift+F4 through Shift+F9 toggle Navigation, World, Logistics, Sensors, Combat, and Entities. Unshifted F4–F8 building shortcuts and F9 placement rotation remain unchanged. Disabled developer categories do not request their associated simulation debug-capture paths.

The Windows client periodically reports:

- selected entity count;
- hovered entity;
- last movement-command sequence;
- accepted target count;
- rejected target count;
- command execution tick.

F1 continues to toggle the development metrics overlay and now also reports player-overlay lines, developer-debug lines, dropped debug lines, and developer-overlay CPU submission cost.

## Validation

Focused tests cover:

- single/toggle selection state;
- ownership/category filtering;
- hidden and off-screen exclusion;
- box inclusion;
- right-click movement request generation;
- selectable metadata extraction;
- fixed-tick command dispatch;
- stale-generation rejection;
- foreign-owner rejection;
- the invariant that a movement command does not directly change `WorldTransform`;
- authoritative movement-order consumption by fixed-tick locomotion;
- one shared strategic path request for 10, 50, and 100-unit selections;
- stable formation slot assignment, entity removal, replacement commands, choke-point fallback, and concurrent groups;
- arrival, terrain following, slope limits, local separation, obstacle steering, and chunk-boundary spatial updates.

See [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for multi-unit command semantics and shared-route formation behavior. See [Ground Movement and Local Steering](GroundMovementAndSteering.md) for locomotion semantics and limitations.

The full solution build, project-reference validation, Windows client smoke test, headless smoke tests, and complete test suite remain the CI gate.

## Production player action dock

`PlayerActionPanelController` remains the presentation-owned interaction state for the player command surface, while `PlayerActionDockHudSurface` owns the DPI-aware `ActionDock` HUD region. The dock is a visual/interaction replacement for the former text palette; it does not introduce a second command path.

The six contextual modes are Build (`B`), Process (`P`), Units (`U`), Logistics (`L`), Supply (`Y`), and Combat (`K`). `Tab` changes the selected card, `Enter` activates it, `C` cancels/removes the selected queue or policy item where supported, `T` cycles the primary setting, `M` cycles the secondary setting, and Left/Right adjust the currently editable numeric setting. Pointer input hits the same mode/card/footer model; clicking a card selects it, and the explicit `ACT` footer control performs the same activation represented by `Enter`.

Pointer capture is derived from the shared `GameplayHudLayout.ActionDock` geometry. A pointer press inside the dock is consumed before world selection, movement, building placement, or tactical targeting can interpret that same click. Disabled controls use the same availability model in rendering and controller dispatch, so a disabled footer button cannot submit an action merely because it was clicked.

The dock only exposes currently implemented actions. Semantic icons may exist for Patrol, Repair, or other future commands, but those actions are not placed in the dock until gameplay and request/command support exists. All visible actions continue to create `PlayerActionRequest` values and cross the existing dispatcher/gateway boundary; UI activation is submission intent, not proof of authoritative success.

Session replacement closes the dock and clears pending presentation-owned requests/editor state. A terminal player-experience snapshot closes and suppresses the dock entirely so no post-result Build, Process, Unit, Logistics, Supply, or Combat request can be authored from this surface.

## Tactical Targeting

Owned selection and enemy targeting remain separate interactions. `K` opens the Combat mode of the production player action dock for the current owned selection. Attack, AttackMove, Retreat, and Fire Mission enter a transient targeting mode; `Escape`, focus loss, session replacement, terminal match state, or opening another action mode cancels that targeting state.

Attack picking never scans foreign render instances or live ECS transforms. Presentation receives only current identified enemy candidates copied into `PlayerTacticalActionReadModel`; each candidate contains the authoritative entity identifier needed for the later command plus its intelligence-approved last-known position and compatibility count. The target controller projects only those copied positions for hit testing.

Fire Mission targeting is intentionally broader than direct Attack. Detected or identified contacts are selected by opaque `IntelligenceContactKey` and retain their stored last-known coordinates. A click that does not select a contact becomes a coordinate request; the simulation accepts that coordinate only when its intelligence cell is currently visible.

While a tactical targeting mode is active, its left click is captured through the end of the frame so it cannot also change owned selection or create a movement request. Normal right-click movement and normal owned selection semantics remain unchanged outside targeting mode.
