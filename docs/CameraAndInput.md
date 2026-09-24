# RTS Camera and Input

## Purpose

The RTS camera/input foundation separates native device events, semantic action mapping, presentation camera behavior, and future simulation commands.

The implemented boundary is:

```text
Win32 messages
    ↓
ForgeLine.Platform.Windows raw input events
    ↓
ForgeLine.Input state + RTS action mapping
    ↓
ForgeLine.Presentation.RtsCamera
    ↓
view/projection data and screen/world helpers
```

The camera never writes simulation state. Future selection and gameplay commands consume camera rays and mapped actions through separate command-generation paths.

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

Bindings are represented by `RtsCameraBindings`. The contract is persistence-ready and can later be fed by a settings store without introducing a remapping UI in this foundation.

## Raw Input

`ForgeLine.Platform.Windows` translates the Win32 messages needed by the first RTS interaction layer into `PlatformInputEvent` values.

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

This prevents stuck camera motion when the application loses focus.

`RtsCameraActionMapper` converts raw state into an `RtsCameraInputFrame` containing:

- normalized pan axis
- rotation axis
- pitch axis
- zoom steps
- drag-pan state
- pointer position/delta

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

`ScreenPointToWorldRay` produces a world-space ray suitable for future terrain, selection, and command picking.

`TryScreenPointToWorldOnHorizontalPlane` intersects that ray with a configurable horizontal world plane. The canonical heightfield query API can be used separately when later selection/placement workflows require the actual terrain surface.

`WorldToScreen` projects a world point into client pixels and reports normalized depth plus current clip visibility.

These helpers do not read simulation state and remain usable with the current terrain world, presentation snapshots, and future gameplay read models.

## Diagnostics

`RtsCamera.GetDiagnostics()` exposes:

- camera position
- target
- yaw
- pitch
- distance
- pointer position and validity

The Windows client emits camera diagnostics at startup, periodically while running, and at shutdown.

## Terrain Validation Scene

The current client renders the representative chunked heightfield world through the RTS camera.

Camera navigation directly exercises:

- keyboard and drag panning across chunk boundaries
- edge scrolling
- yaw rotation
- pitch
- zoom
- window aspect changes
- chunk-level frustum culling
- world-space depth-tested terrain

Terrain remains world/presentation state rather than simulation gameplay state. See [World and Terrain](WorldAndTerrain.md) for the canonical coordinate model and terrain-rendering boundary.
