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
MovementOrder component
```

Navigation, formation movement, local avoidance, attack orders, control groups, minimap commands, and final interaction styling remain deferred.

## Ownership Boundaries

Selection is player interaction state owned by presentation. It is not an authoritative simulation component and it does not change entity ownership.

Simulation owns:

- stable `EntityId` lifetime and generation validation;
- `ControllableEntity` ownership/category metadata;
- accepted `MovementOrder` state;
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
- submission tick.

It submits the command through `SimulationCoordinator.SubmitCommand` for the next simulation tick and uses the matching `SimulationCommandSource`.

The command does not change `WorldTransform`.

At execution time it revalidates every target against the live ECS and owner. Accepted entities receive or replace a `MovementOrder` containing the issuer, target, submission tick, and accepted tick.

The later navigation system can consume this component during the existing navigation/order-processing phases without changing the interaction boundary.

## Stale Entity Handling

`EntityId` includes an index and generation. A destroyed entity ID therefore cannot accidentally address a later entity that reuses the same slot.

`MoveEntitiesCommand` uses safe component lookup at execution time. Missing, destroyed, generation-stale, non-controllable, or foreign-owned targets are rejected individually while valid targets in the same command continue to receive their order.

The command exposes accepted/rejected target counts and execution tick for development diagnostics.

## Feedback and Diagnostics

Hover and selection feedback use presentation-only debug geometry around interpolated entity bounds. This feedback does not mutate simulation state and remains independent of simulation outcomes.

The Windows client periodically reports:

- selected entity count;
- hovered entity;
- last movement-command sequence;
- accepted target count;
- rejected target count;
- command execution tick.

F1 continues to toggle the development metrics overlay. F2 continues to toggle broader world-debug visualization; interaction feedback remains available independently when entities are hovered or selected.

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
- the invariant that a movement command does not directly change `WorldTransform`.

The full solution build, project-reference validation, Windows client smoke test, headless smoke tests, and complete test suite remain the CI gate.
