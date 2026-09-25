# Windows Client

## Purpose

`ForgeLine.Client` is the interactive Windows x64 composition root. It owns the native application host and composes the Direct3D 12 graphics foundation, RTS input/camera stack, and the representative terrain presentation without introducing Win32 or D3D12 details into world, simulation, or game rules.

The current client initializes graphics, consumes the platform input stream through `ForgeLine.Input`, updates the presentation-only RTS camera and selection controller, advances the fixed-tick simulation, extracts immutable presentation snapshots, interpolates simple render instances, and renders them together with depth-tested chunked terrain. RTS selection, movement commands, hierarchical navigation, and shared-route formation movement are active development capabilities. Production unit art, the final RTS command UI, and audio playback remain deferred.

## Platform Boundary

Platform-facing contracts are exposed through `ForgeLine.Platform.Windows`:

- `IPlatform` owns the platform clock, window creation, message pumping, and bounded message waiting.
- `IWindow` exposes client dimensions, DPI, focus/minimize state, supported window mode changes, queued window events, and a platform-facing native-handle adapter.
- `NativeWindowHandle` carries the opaque native target required by the future graphics boundary without exposing Win32 types or constants to higher layers.
- `IClock` lives in `ForgeLine.Core` because timing is low-level infrastructure. The Windows implementation uses the runtime high-resolution monotonic stopwatch source.

Direct Win32 interop remains internal to `ForgeLine.Platform.Windows`.

Simulation, game rules, and headless execution do not depend on the Windows platform or client host.

## Startup Flow

The current startup sequence is:

```text
ForgeLine.Client
    ↓
WindowsPlatform
    ↓
Per-monitor-v2 DPI awareness
    ↓
WindowsWindow
    ↓
Win32 class registration
    ↓
Native primary window creation
    ↓
Client message loop
```

The client creates one primary `FORGELINE` window with a 1600×900 requested client area. Direct3D 12 initialization consumes the existing opaque native-handle boundary immediately after window creation.

## Message Loop

`WindowsPlatform.PumpEvents()` drains pending Win32 messages without tying platform processing to simulation or rendering.

The graphics client pumps messages once per rendered frame. Raw input events are drained after platform messages and mapped into frame-scoped RTS camera actions. Present pacing controls the active render loop, while minimized or zero-sized windows use bounded event waits so suspended rendering does not busy-spin.

## Window Events

The platform translates relevant native messages into `WindowEvent` snapshots.

Initial window-event coverage includes:

- creation
- close request and closed state
- resize
- minimize and restore
- focus gain and loss
- DPI change
- window-mode change

Each window event carries the current client size, DPI, focus state, minimize state, and window mode. Keyboard, mouse-button, pointer, wheel, and focus-loss input are exposed separately through `IWindow.TryDequeueInputEvent` so higher layers never inspect Win32 messages directly.

## DPI Behavior

The Windows platform requests per-monitor-v2 DPI awareness before the first native window is created.

Initial client dimensions are converted to the corresponding outer window size with DPI-aware Win32 sizing. On `WM_DPICHANGED`, the platform applies the Windows-recommended bounds, refreshes client dimensions, and emits a DPI-change event.

If process DPI awareness was already established before ForgeLine initializes, Windows may reject a second process-level change with access denied. That condition is treated as an already-configured process rather than an initialization failure.

## Window Modes

The initial supported modes are:

- `Windowed`
- `BorderlessFullscreen`

Borderless fullscreen captures the previous windowed bounds, removes the overlapped window frame, expands to the nearest monitor bounds, and restores the saved windowed placement when returning to `Windowed`.

Exclusive fullscreen is not implemented.

## Native Graphics Target

The Direct3D 12 layer obtains the native top-level window target through:

```text
IWindow.NativeHandle
```

Higher-level code does not need to reference `HWND`, `WNDPROC`, Win32 styles, or P/Invoke declarations.

## Shutdown Lifecycle

User close requests and programmatic smoke-test shutdown both destroy the native window through the platform layer.

The lifecycle is:

```text
Close request
    ↓
Destroy native window
    ↓
WM_DESTROY
    ↓
Closed event
    ↓
WM_QUIT
    ↓
Client loop exits
    ↓
Window disposal
    ↓
Platform disposal
```

Window and platform operations are thread-affine and must remain on the thread that created them.

## Launch

From the repository root:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

The executable is a Windows x64 host with the D3D12 graphics foundation, RTS camera/input stack, and representative chunked terrain active. The initial strategic camera view spans multiple chunks; pan, rotation, pitch, zoom, edge scrolling, negative/positive chunk traversal, resize behavior, depth testing, and frustum culling can be validated directly against the visible world.

Default controls are W/A/S/D or Arrow Keys to pan, Q/E to rotate, R/F to change pitch, Middle Mouse drag to pan, and Mouse Wheel to zoom. Left click/drag selects units and right click issues movement orders. F1 toggles the development metrics overlay, F2 toggles world debug visualization, and F3 cycles the development formation selection through Compact, Line, Column, and Wedge. Edge scrolling is enabled by default. See [RTS Camera and Input](CameraAndInput.md) for camera interaction and coordinate conventions, [Selection and Command Interaction](SelectionAndCommandInteraction.md) for selection/order flow, [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for shared group movement, and [World and Terrain](WorldAndTerrain.md) for world/chunk semantics, culling, and terrain diagnostics.

## Bounded Smoke Validation

Run:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test

# Optional bounded instance stress scene
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release -- --smoke-test --render-stress 1000
```

Smoke mode:

1. initializes the Windows platform;
2. creates exactly one primary native window;
3. initializes the D3D12 device and swap chain, using WARP only when no suitable hardware adapter is available;
4. generates the deterministic development terrain and persistent per-chunk geometry;
5. compiles terrain shaders through DXC, creates the terrain pipeline and depth target, frustum-culls chunks, and submits indexed terrain draws for a short bounded interval;
6. reports platform, graphics, world, camera, and terrain submission state to standard output;
7. requests normal window destruction;
8. waits for graphics work to retire and exits only after orderly graphics/platform cleanup.

CI executes this validation only on Windows runners.

For manual validation, also resize the window, minimize and restore it, move it between displays with different DPI scaling where available, change focus, close it using the system close button, and repeat several debug launches while watching process and USER/GDI handle counts.


See [Presentation Extraction and Debugging](PresentationExtractionAndDebugging.md) for the simulation-to-render ownership boundary, interpolation, debug controls, metrics, and render stress workflow.
