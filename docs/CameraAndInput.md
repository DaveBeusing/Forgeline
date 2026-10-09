# RTS Camera and Input

## Purpose

The RTS camera/input foundation separates native device events, semantic action mapping, presentation camera behavior, and simulation command generation.

The implemented boundary is:

```text
Win32 messages
    ↓
ForgeLine.Platform.Windows raw input events
    ↓
ForgeLine.Input state + RTS action mapping
    ↓
ForgeLine.Presentation camera / selection interaction
    ↓
view/projection data + presentation movement requests
    ↓
ForgeLine.Client
    ↓
simulation commands
```

The camera never writes simulation state. Selection consumes camera rays and extracted render instances; gameplay changes still cross the fixed-tick command boundary.

## Coordinate Convention

The camera foundation establishes the initial FORGELINE presentation convention:

- world up is **+Y**
- the primary ground plane is **X/Z**
- yaw zero looks horizontally toward **+Z**
- positive yaw rotates the camera view toward **+X**
- camera pitch remains negative so the camera looks downward toward its target
- view/projection math uses the .NET/System.Numerics right-handed camera convention
- projected depth uses the Direct3D-compatible normalized range **0..1**
- screen coordinates use a top-left origin with +X right and +Y down
- ground-right is `Cross(GroundForward, worldUp)`, matching the active right-handed view; at yaw zero screen-right is world **-X**
- A/Left and the left edge move the view left; D/Right and the right edge move it right, including after camera rotation
- dragging moves the ground with the pointer; wheel-up zooms in and wheel-down zooms out within configured limits

The camera target/focus point lies on arbitrary world coordinates. The Windows client initializes its target at the sampled development-terrain height near the world origin; panning then remains a presentation transform and can cross chunk boundaries without mutating world state.

## Default Controls

| Action | Default |
| --- | --- |
| Pan forward | W or Up Arrow |
| Pan backward | S or Down Arrow |
| Pan left | A or Left Arrow |
| Pan right | D or Right Arrow |
| Rotate left | Q |
| Rotate right | E |
| Pitch up / shallower | R |
| Pitch down / steeper | F |
| Drag pan | Middle Mouse Button |
| Zoom | Mouse Wheel |
| Edge scroll | Enabled by default |
| Single selection | Left Mouse Button |
| Add/remove selection | Shift + Left Mouse Button |
| Box selection | Left-drag |
| Toggle box selection | Shift + Left-drag |
| Movement order | Right Mouse Button |

Camera bindings are represented by `RtsCameraBindings`. Selection conventions currently use the standard mouse buttons and Shift directly; command-panel remapping remains a later UI/settings concern.

The pre-alpha client persists the existing `RtsCameraBindings` contract in `%LOCALAPPDATA%\\FORGELINE\\settings.json`. Camera pan, rotation, pitch, and drag-pan bindings can therefore be changed without adding a second input model. Settings validation rejects unknown/no-button values and duplicate primary camera actions. Selection, command-panel, and gameplay-action rebinding remains deferred until those surfaces have equivalent conflict handling and discoverability.

`F1` (or `F12`) toggles the in-game controls reference. F1, F12, Escape, and the Back button close it. Escape closes help without opening pause. The reference uses active camera bindings and the configured edge-scroll state. `Shift + F1` toggles development metrics; `Space` toggles explicit player pause. Help and pause use the existing simulation-owner control boundary.

## Raw Input

`ForgeLine.Platform.Windows` translates the Win32 messages needed by the RTS interaction layer into `PlatformInputEvent` values.

The public platform boundary exposes:

- key down/up
- mouse-button down/up
- pointer movement in client coordinates
- mouse-wheel delta in client coordinates
- focus-loss notification

The higher-level input project does not inspect Win32 messages or virtual-key constants.

## Input State and Action Mapping

`InputState` maintains held keys/buttons and per-frame pointer/wheel deltas.

`BeginFrame()` clears only transient values. Held state and the last valid pointer position remain available across frames. Native pointer-leave invalidates pointer position and clears mouse gestures, preventing stale edge pan and drag displacement on re-entry. Modal transitions suppress held keys and buttons until their corresponding release, including native key-repeat messages. Focus loss clears both held input and suppression.

A focus-loss event clears:

- held keys
- held mouse buttons
- pointer validity
- pointer delta
- wheel delta

This prevents stuck camera motion and cancels an in-progress selection gesture when the application loses focus.

`RtsCameraActionMapper` converts raw state into an `RtsCameraInputFrame` containing:

- normalized pan axis
- rotation axis
- pitch axis
- zoom steps
- drag-pan state
- pointer position/delta

`RtsSelectionController` independently interprets left/right mouse state plus Shift for selection and movement intent. It owns no simulation state.

## Minimap camera and command input

The production minimap uses the same canonical X/Z world bounds as the minimap model. Pointer coordinates are normalized within the DPI-aware map rectangle and converted back to world X/Z; terrain sampling supplies the presentation target height.

Left click recenters the camera and left-drag pans it continuously. This changes only `RtsCamera` presentation state. Right click with a non-empty current selection produces the existing movement-order request instead of mutating movement components directly.

During Attack, Attack Move, Retreat, or Fire Mission targeting, minimap clicks use the existing tactical `PlayerActionRequest` path. Direct Attack accepts only currently `Identified` tactical targets copied into the player action snapshot. Detected contacts remain opaque intelligence contacts and cannot leak hidden entity IDs or live transforms into direct attack behavior. Fire Mission may carry an `IntelligenceContactKey` or a currently visible coordinate and is revalidated by simulation.

The minimap captures its pointer gesture through `HudInteractionContext`, so the same click cannot also become world selection, placement, or a second movement/targeting gesture.

## HUD input precedence

`HudInteractionContext` is the reusable frame-scoped boundary between player-facing HUD interaction and world interaction. The client begins a fresh HUD interaction frame for the active simulation session, accumulates pointer/keyboard capture from the action and targeting surfaces, and evaluates that result before camera input, building placement, selection, movement, or other world gestures.

The action panel uses the same `GameplayHudLayout` action-dock region for rendering origin and hit testing, so DPI/UI scaling cannot cause the visual panel and its pointer-capture bounds to drift apart. A new simulation session clears transient capture state before the new session can drive world interaction.

Modal help and pause capture input before all HUD and world handling. Both their opening and closing frames consume input; selection gestures, placement, minimap drags, targeting and pending presentation requests are cancelled without clearing the current selection. Help uses the existing frontend controls renderer, with a complete bounded text stream and ten rows above the Back button. Controls fit within the physical viewport even when UI scale is raised; rendering and Back hit testing share that scale. Growth reuses buffers after the first required increase, up to 131,072 vertices (3 MiB per frontend frame buffer).

HUD pointer capture masks camera wheel, drag and edge input at the action-mapping boundary while preserving keyboard camera navigation when keyboard focus is free. Unfocused windows cannot dispatch gameplay input.

HUD capture is presentation state only. It decides which input surface receives a gesture; it never authorizes or applies gameplay state changes. Player-authored changes continue through the existing request/command boundary.

## Camera State

`RtsCamera` owns only presentation state:

- target/focus point
- yaw
- pitch
- distance/zoom
- derived position

Movement uses elapsed frame time and therefore does not depend on render FPS. Zoom and drag-pan use event deltas directly because their input already represents discrete or per-frame displacement.

Pan speed scales with camera distance using configurable limits so strategic zoom remains usable without making close zoom excessively fast.

## Edge Scrolling

Edge scrolling is optional and configured through `RtsCameraSettings`.

It activates only when:

- edge scrolling is enabled
- a valid pointer position exists
- the pointer is inside the client viewport
- the pointer enters the configured activation zone

Losing focus invalidates the pointer for edge scrolling until a new pointer event arrives.

## Projection and Picking APIs

`RtsCamera.GetMatrices(width, height)` returns:

- view
- projection
- combined view-projection

`ScreenPointToWorldRay` produces the world-space ray used for entity picking.

`TryScreenPointToWorldOnHorizontalPlane` intersects that ray with a configurable horizontal world plane. Movement-target resolution then samples the current terrain height at the resolved X/Z position before the request is handed to the client command boundary.

`WorldToScreen` projects a world point into client pixels and reports normalized depth plus current clip visibility. Drag-box selection uses this projection and only includes visible entities whose projected centers are inside the screen-space rectangle.

These helpers do not read live simulation state. Entity picking uses immutable/interpolated `RenderWorld` instances extracted from simulation snapshots.

## Diagnostics

`RtsCamera.GetDiagnostics()` exposes:

- camera position
- target
- yaw
- pitch
- distance
- pointer position and validity

The Windows client emits camera diagnostics at startup, periodically while running, and at shutdown.

Interaction diagnostics additionally expose selected count, hovered entity, last movement-command sequence, accepted/rejected target counts, and command execution tick.

## Terrain Validation Scene

The current client renders the representative chunked heightfield world through the RTS camera and populates visible test entities with local/foreign ownership plus selectable categories.

The scene directly exercises:

- keyboard and drag panning across chunk boundaries
- edge scrolling
- yaw rotation
- pitch
- zoom
- window aspect changes
- chunk-level frustum culling
- world-space depth-tested terrain
- single/toggle/box selection
- ownership/category filtering
- right-click movement-command submission

Terrain remains world/presentation state rather than simulation gameplay state. See [Selection and Command Interaction](SelectionAndCommandInteraction.md) for the interaction/command ownership boundary and [World and Terrain](WorldAndTerrain.md) for the canonical coordinate model and terrain-rendering boundary.

## Navigation Regression Coverage

Camera tests verify view-projected left/right and forward/backward movement after rotation, keyboard/edge equivalence, drag polarity, 60/144 Hz distance, zoom limits and resized viewport edges. Input tests cover focus loss, pointer leave/re-entry, HUD pointer capture and held-input suppression. Client tests cover first-press help, repeat suppression, F1/F12 and Escape close, Back dismissal and focus transitions. Selection tests cover modal drag cancellation with selection preserved. Frontend tests verify the complete controls text and footer at 1024x720, 1600x900 and 2560x1440, including raised UI scale and reusable buffer growth.

Camera target navigation remains unbounded as before; zoom and pitch retain their configured bounds. No new map clamp or projection was introduced.

The integrated controller journey covers keyboard/drag pan, zoom bounds, unit/building selection, commands, first-press help, focus recovery and display changes. See [Interaction qualification](InteractionQualification.md) for the state matrix, automated limits and manual display checklist.
