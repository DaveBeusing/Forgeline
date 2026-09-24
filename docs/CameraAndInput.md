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

`BeginFrame()` clears only transient values. Held state and the last valid pointer position remain available across frames.

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
