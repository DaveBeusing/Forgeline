# Selection and Command Interaction

Owned single objects and groups use the existing world anchored action rings.
The rectangular inspector, mode bar and command dock have been removed. Shared
published geometry drives rendering and sector hits. Item release submits once;
existing targeting still owns its world confirmation. B/P/U/L/Y/K/H and
Tab/T/M/Left/Right/Enter/C retain their bindings. Empty-selection global actions
remain reachable through those existing mode shortcuts.

Groups retain the full authorized selection and existing dispatcher/shared-route
path. Partial capabilities show eligible/selected counts; type filters, attention,
formation and focus preserve saved groups. Outside dismissal reaches the world
once. Session, generation, ownership and membership changes invalidate presses.
See [current parity and qualification](HudSurfaceConsolidation.md).

## Hover tooltips

Keeping the pointer on a world object or ring sector for 175 ms reveals a compact, noninteractive tooltip. The delay uses elapsed presentation time, independently of simulation ticks; moving more than three scaled pixels, changing target, or changing viewport/DPI/UI scale starts a new delay. Long frame gaps clear the delay. Tooltips never capture pointer input or issue commands.

Owned units, buildings and construction sites show their authored identity/semantic role and available Health, supply/Fuel/Ammunition, readiness, power and work facts. Missing facts are omitted. Visible deposits show resource identity, copied remaining quantity, locally owned extraction state where present and the existing Mine / Extractor requirement. Unexplored or no-longer-visible deposits show nothing; foreign extractor operational state is not copied.

Current detected contacts are picked near their copied opaque world position and display only `UNKNOWN CONTACT / DETECTED`. Identified hostile entities may show authored identity, with no health, inventory, supply, readiness, production or orders. Losing current intelligence immediately removes the applicable tooltip at the next published snapshot.

Ring sector hits take precedence over world hover and use exactly the published angular geometry and captured availability model. Tooltips explain disabled reasons, authored costs and copied availability, recipe outputs, production ticks, deposit/technology/facility prerequisites, panel shortcuts and supported control actions. Availability is a captured observation; command execution still revalidates through the authoritative command boundary. Closing or changing ring context invalidates old action tooltips. The open ring also exposes copied single/group status in this existing read-only tooltip, including keyboard invocation without a pointer.

Drag selection, held pointer buttons/camera drag, wheel motion, placement, tactical targeting, modal help/pause, focus loss, minimization, terminal state and session replacement suppress hover. The panel flips/clamps inside the DPI-aware safe area and fits the available vertical space to keep clear of the cursor; extreme scale or tiny viewports can shorten the visible text. Compact text is bounded to twelve lines and 48 characters per line, including up to three cost rows, two output rows and two prerequisite rows. Ring status and detailed action explanations use bounded wrapping up to 48 rows, measured before fitting; no inspector or dock remains as a secondary view. Per-user tooltip settings and top-bar hover are deferred; no settings/save migration is introduced.

## Purpose

Two left clicks within 350 ms and the latched 6-pixel DPI/UI-scaled movement threshold select only currently screen-visible owned controllable units of the same authored `UnitId`. Both click targets must have current permitted identities; the first entity's full generation must still exist. Different units of the same type can participate inside the threshold. Buildings, missing unit identity, resources, foreign/hidden objects and wrecks never expand selection. Shift clicks retain single-object toggle semantics. Drag, camera/zoom, display, focus, modal, targeting, placement, intervening inspection and session transitions cancel the sequence. One processed click never submits a movement order.

Click completion reuses the nearest current hover hit, so a foreign object in front does not expose an owned selectable behind it through a second ownership-only raycast. Same-type selection scans only the current render world; no map-wide or live ECS query is used. Selection remains stably ordered and duplicate-free; retained list capacity avoids allocating one tree node per unit on each repeated same-type selection.

Context cursors use existing compiled cursor identities. UI ownership wins over world cursors. Move requires current owned visible copied movement capability; selecting only an immobile building keeps the default cursor. Attack/AttackMove/FireMission/Retreat cursors occur only in the existing validated targeting mode. A Supply panel alone is not a supply-targeting gesture. Repair and Patrol affordances are not inferred. Build requires a matching valid preview without a known copied material shortage; pending command results use the default cursor. Preview/cursor validity never authorizes the eventual command.

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

Strategic navigation, shared-route formation movement, tactical targeting, combat groups and minimap commands use their existing command boundaries. Interaction feedback remains presentation-owned.

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

The client allows local `Unit`, `Building` and `Logistics` categories. Visible foreign-owned objects can be hovered but cannot enter the local selection. Hidden/non-world instances produce neither hover nor selection markers.

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

A drag becomes box selection after six logical pixels, scaled by the active DPI/UI layout at press time. Crossing the threshold latches box mode until release, even if the pointer returns near its starting point. Mouse press/release edges retain clicks completed between frame updates.

Focus loss, pointer leave, HUD capture, help/pause and placement capture cancel the selection gesture without changing the selected set. Presses beginning on captured HUD geometry and releases over HUD cannot become world selection or orders. The placement-confirmation frame remains captured.

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

## Read-only selection status

`WorldHoverTooltipSurface` displays copied single/group status beside the open
ring. It preserves inventory, work/block reasons, component-covered group means,
composition, formation and exact command-result counts. No persistent inspector
or inspector click target remains. The semantic `SelectionInspectorHudModel`
continues to supply authorized labels/icons without a rendering owner.

## Feedback and Diagnostics

Hover, selection, placement-preview, tactical-target, and strategic-overlay feedback use a dedicated player-facing world-overlay path around immutable presentation data. They no longer share the engineering debug buffer. Player-facing line markers render after depth-tested developer diagnostics and without depth testing so selection and command feedback remain readable over terrain and world geometry. None of these visuals mutates simulation state. Normal player selection no longer paints elliptical rings. Each selected unit, building and group member receives four oriented L-shaped corner markers from `TacticalFootprint`; permitted hover uses four diagonal corner notches and foreign hover adds outward ticks and its distinct tint. Corners sample terrain height with the existing visual ground-plane fallback. Primary selection/permitted hover retain authored corner artwork while all selected group members remain legible via procedural corners. Hidden, offscreen or invalid instances never gain unauthorized markers. Construction/collision footprints and F2 developer sensor/range/navigation circles retain their independent renderers.

Engineering diagnostics use an independent category model. F2 toggles the developer master switch; Shift+F2 toggles Rendering, while Shift+F4 through Shift+F9 toggle Navigation, World, Logistics, Sensors, Combat, and Entities. Unshifted F4–F8 building shortcuts and F9 placement rotation remain unchanged. Disabled developer categories do not request their associated simulation debug-capture paths.

The Windows client periodically reports:

- selected entity count;
- hovered entity;
- last movement-command sequence;
- accepted target count;
- rejected target count;
- command execution tick.

Shift + F1 toggles the development metrics overlay and now also reports player-overlay lines, developer-debug lines, dropped debug lines, and developer-overlay CPU submission cost.

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

## Player action rings

`PlayerActionPanelController` retains request/editor state. `PlayerActionRingHudSurface`
draws only the existing single/group/global rings. B/P/U/L/Y/K/H open their seven
modes. Parameters exposes the existing priority, repeat, stock and resupply
settings and cancellation. Tab, Enter, C, T, M and Left/Right retain their actions.
Pointer activation validates the published ring revision and stable item key on
release. Disabled sectors own only their actual sector and cannot arm requests.

All gameplay mutations remain `PlayerActionRequest` values crossing the existing
dispatcher/gateway. Terminal state and session replacement close menus and clear
pending local intent. No rectangular fallback or global mode bar remains.

## Tactical Targeting

Owned selection and enemy targeting remain separate interactions. `K` opens the Combat mode of the action ring for the current owned selection. Attack, AttackMove, Retreat, and Fire Mission enter a transient targeting mode; `Escape`, focus loss, session replacement, terminal match state, or opening another action mode cancels that targeting state.

Attack picking never scans foreign render instances or live ECS transforms. Presentation receives only current identified enemy candidates copied into `PlayerTacticalActionReadModel`; each candidate contains the authoritative entity identifier needed for the later command plus its intelligence-approved last-known position and compatibility count. The target controller projects only those copied positions for hit testing.

Fire Mission targeting is intentionally broader than direct Attack. Detected or identified contacts are selected by opaque `IntelligenceContactKey` and retain their stored last-known coordinates. A click that does not select a contact becomes a coordinate request; the simulation accepts that coordinate only when its intelligence cell is currently visible.

While a tactical targeting mode is active, its left click is captured through the end of the frame so it cannot also change owned selection or create a movement request. Normal right-click movement and normal owned selection semantics remain unchanged outside targeting mode.

## Selection and command feedback rendering

The normalized drag marquee follows current pointer state in each published presentation frame, independently of simulation ticks. It is clipped to the client gameplay viewport, drawn with a faint 10% alpha fill, a dark contrast border and a DPI-scaled colored outline. The information renderer reserves geometry capacity for the marquee after other overlays; the only mouse cursor is native Windows. Box picking still uses visible eligible projected entity centers; Shift retains the existing toggle semantics.

Player world lines use the existing bounded overlay renderer with reusable screen-expanded triangles, a three-pixel reference width scaled by DPI/UI settings and soft alpha edges. They retain the established depth-disabled player feedback policy and one draw batch. Developer lines retain their depth policy and original line path. A thousand selected objects require 24,000 lines, below the 32,768-line cap; the expanded buffer is bounded at 7.5 MiB per frame slot.

Movement clicks and existing tactical targeting clicks display presentation-only feedback for 0.8 seconds. Dynamic movement circles/crosses and attack-family inner circles retain their geometry; copied Move/Attack/AttackMove/invalid intent now additionally uses production glyphs. Invalid spatial targets retain their X and native invalid cursor. These indicate local targeting/submission intent, not authoritative acceptance or persistent simulation orders. Existing command-result HUD messages remain authoritative. Frame time expires the markers; focus/modal/session transitions clear them.

Automated coverage includes four drag directions, quick clicks, DPI threshold latching, cancellation, ownership/hidden-object filtering, footprint/terrain geometry, zoom minimum radius, marquee clipping/blending/cancellation, feedback expiry and a thousand-ring single-batch budget. Manual zoom/terrain/camera-angle, 60/144 Hz, high-DPI and HUD-overlap verification remains part of the pre-alpha checklist.

Resize and DPI/UI-scale changes cancel an active selection gesture; a still-held button cannot restart it before release. Selection already established is preserved. Minimap camera/command clicks retain down/up edges between updates and require the press to originate in the relevant map or selector region. HUD capture uses current pointer position and retained left/right press origins through the shared `HudInteractionContext.CapturesWorldPointer` policy. See [Interaction qualification](InteractionQualification.md) for integrated scenarios and reproducible checks.

## Multi-selection group controls

The existing group ring exposes type filters, damaged/unsupplied attention,
formation and group focus through `CombatGroupCardController.Activate`.
Read-only aggregates appear in the shared ring tooltip. The old card geometry
and its separate pointer controller path have been removed. Saved groups and
simulation movement groups remain independent. See [group menus](GroupActionMenus.md).

## Operations overview

Click the OPERATIONS entry in the shared left column, the resource/power strip or a populated alert control to open the nonmodal operations view. Filter and page copied local facilities, inspect reported causes, focus facilities/adjacent route endpoints and open their existing ring controls. Focus issues no orders; control entry waits for the exact new selection capture. Targeting hides the view temporarily; modal, pause, focus and resize transitions consume clicks. See [operations authority and limits](ProductionAndLogisticsOperations.md).

## Actionable alerts

Alert clicks reuse owned full-generation camera focus and selection inspection. Missing destinations open the appropriate operations filter; navigation issues no order. Retained edges are canceled by lifecycle/display transitions; targeting suppresses activation and open operations owns covered alert space. See [shared input and fallback semantics](ActionableAlertsAndStrategicMap.md).

## Gameplay shortcuts and interaction feedback

Action mode, navigation, activation and cancellation shortcuts are configurable through the shared registry. Ring sectors, tooltips and help display that mapping. Existing hover/press cues and disabled explanations accompany text; command results retain authoritative accepted/partial/rejected counts. Escape closes the ring without submitting an order. Resize, focus loss, modal transitions and session replacement consume key edges and held inputs. See [binding and feedback rules](GameplayInputAccessibilityAndQualification.md).

## HUD content density and alert capture

Copied selection details require the current completed tick and matching ring
identity/revisions. Group status preserves component coverage and owned membership.
Only populated notification cards capture pointer origins/current positions;
gaps and unused capacity remain world input. Held card presses retain existing
identity/session/layout checks. Informational result cards remain non-actionable.

## Adaptive ring navigation

Existing four-item ring pages use stable keys. Tab, wheel over an actual sector,
Previous and Next navigate without simulation requests. Hidden/absent sectors
cannot be clicked. Wheel suppresses simultaneous activation. Current source and
qualification are recorded in [HUD consolidation](HudSurfaceConsolidation.md).

## Graphical world feedback

[Projected footprint components](GraphicalTacticalInterface.md) emphasize the primary selection and current permitted hover. Group outlines use oriented X/Z extents with the existing terrain samples and bounded line batch. World status frames require current owned single-entity facts; group aggregates and foreign operational details are omitted. `RtsCommandFeedback.View` copies the existing 0.8-second local intent lifetime without adding command authority or authoritative acknowledgements. See [qualification](GraphicalTacticalInterfaceQualification.md).

## Shared green drag-selection treatment

The drag rectangle and selected entity corners share `RtsSelectionVisualStyle.Selected` (`0.25, 1.0, 0.35` RGB). The fill is 0.12 alpha and the single border is 0.92 alpha at 1.25 physical client pixels, without a secondary dark glow. The box is normalized/clipped in screen space but never changes the six-logical-pixel drag threshold, shift-toggle, picking eligibility, input capture, selection order or command authority. This is a visual-only change; tactical F2 debug circles and order/placement footprints remain intact. Geometry and allocation regressions verify 1,000 selected objects within a bounded line budget. Native screenshots across DPI/UI scales and terrain contrast still require physical review.

## Persistent selected-unit orders

Selected owned units expose their **current authoritative operation** independently of
the radial action ring and pointer hover. The copied selection snapshot contains its
session, simulation tick, full generation-aware selected entity identities, per-unit
operation/status and only authorized target coordinates. An immediate input selection
change hides the previous annotation until a matching snapshot arrives.

Supported operations include strategic movement, group end destinations (not local
formation slots or navigation waypoints), Attack, AttackMove, Retreat, Hold, cargo
collection/delivery/loading/unloading/waiting and active resupply. Invalid or completed
orders do not create a destination line. A fuel shortage is a status of the current
operation rather than an invented new order. Unknown targets remain unmarked.

The one-line caption is drawn near the selected formation or unit independently of
any hover/ring. Multiple selected units have a bounded summary of actual operation
categories (e.g. `MOVE 4 / ATTACK 2 / IDLE 1`); selecting one unit reveals its
individual phase. Text and target markers never own pointer hit regions.

A dedicated depth-disabled player presentation overlay draws a one-logical-pixel
line at approximately 22 percent alpha and one subtle grouped target pulse
(about 1.2 seconds) per unique destination. This overlay does not alter existing
three-pixel selection or 0.8-second intent feedback geometry. Reduced Motion and
low rendering quality use a static target dot; pulsing reads presentation time,
never simulation time. Endpoint and session validation prevent invalid world-origin
lines or stale target information, and target grouping avoids repeated alpha buildup
for formations.

Enemy Attack target coordinates are taken from **currently detected or identified
faction intelligence contacts**, never from an enemy's unrestricted live
WorldTransform. Visibility loss hides the coordinate while the text can continue to
report the actual accepted order. Only local owned unit state is copied. The world
annotation draws from current immutable snapshots and interpolated friendly render
transforms without reading ECS state on the render thread.

This is a presentation-only feature. It does not modify pathfinding, movement,
production, logistics, combat execution, saving or replay state. Native animation,
terrain/zoom contrast and DPI/UI-scale checks remain required for visual acceptance.
