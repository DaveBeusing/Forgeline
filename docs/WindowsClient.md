# Windows Client

## Purpose

`ForgeLine.Client` is the interactive Windows x64 host. It owns the native application lifecycle, Direct3D 12 graphics foundation, RTS input/camera stack, presentation extraction, rendering, and real-time frame pacing without introducing Win32 or D3D12 details into world, simulation, or game rules.

Authoritative vertical-slice gameplay construction is owned by `VerticalSliceScenario` in `ForgeLine.Game`. The client creates that shared runtime with the `Gameplay` profile, Player 1 explicitly human-controlled, Player 2 computer-controlled, and a host-owned `JobScheduler`. It then adds only presentation/platform concerns around the shared simulation.

The client consumes the platform input stream through `ForgeLine.Input` and updates the presentation-only RTS camera and interaction controllers on the platform owner. A dedicated simulation owner advances the fixed-tick runtime and publishes immutable snapshots; a dedicated render owner consumes the latest complete snapshot and renders the authoritative skirmish together with depth-tested chunked terrain. RTS selection, movement commands, hierarchical navigation, shared-route formation movement, and the integrated tactical systems are active development capabilities. Synthetic render instances are opt-in through `--render-stress`, are created after gameplay runtime construction, and therefore do not participate in the authoritative starting navigation obstacle set. Production unit art, the final RTS combat-command UI, and audio playback remain deferred.

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
WindowsPlatform / primary window
    ↓
Direct3D 12 device + frontend renderer
    ↓
Boot/loading → Main Menu
    ↓
New Game / Continue / Load Game
    ↓
ClientSessionRequest
    ↓
Fresh VerticalSliceScenario or validated save restore
    ↓
Presentation extraction + RTS input/camera
    ↓
Gameplay message/render loop
```

The client creates one primary `FORGELINE` window with a 1600×900 requested client area. Direct3D 12 initialization consumes the existing opaque native-handle boundary immediately after window creation. The boot/loading surface and main menu run before any gameplay scenario is constructed. New Game creates a fresh scenario only after the player confirms the selected seed. Continue resolves the newest valid save, while Load Game restores the explicitly selected valid save through the existing persistence service. Corrupt and incompatible saves remain visible but cannot become session requests. The resulting gameplay runtime constructs or restores the canonical battlefield, participants, simulation systems, navigation, cargo transport, logistics, intelligence, combat, and match objectives without referencing Windows or presentation types.

The client-provided scheduler remains client-owned. Disposing the shared scenario does not dispose it; the client host disposes it once after the scenario. A restart leaves the current loop through the existing restart result and creates a fresh runtime on the next application session, so old authoritative routes, inventories, controllers, snapshots, or orders are not reused.

## Message Loop

`WindowsPlatform.PumpEvents()` drains pending Win32 messages without tying platform processing to simulation or rendering.

The graphics client pumps messages once per rendered frame. Raw input events are drained after platform messages and mapped into frame-scoped RTS camera actions. Present pacing controls the active render loop. Minimize state is propagated separately from the last valid non-zero client dimensions so suspended rendering does not depend on a transient `0x0` resize becoming authoritative.

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

Each window event carries the last valid non-zero client size, DPI, focus state, minimize state, and window mode. A minimized or otherwise transient zero-sized `WM_SIZE` does not replace the valid client dimensions. During a window-mode transition, intermediate native resize messages are folded into the transition and the platform emits the final valid resize state after the native placement has completed. Keyboard, mouse-button, pointer, wheel, and focus-loss input are exposed separately through `IWindow.TryDequeueInputEvent` so higher layers never inspect Win32 messages directly.

## DPI Behavior

The Windows platform requests per-monitor-v2 DPI awareness before the first native window is created.

Initial client dimensions are converted to the corresponding outer window size with DPI-aware Win32 sizing. On `WM_DPICHANGED`, the platform applies the Windows-recommended bounds, refreshes client dimensions, and emits a DPI-change event.

If process DPI awareness was already established before ForgeLine initializes, Windows may reject a second process-level change with access denied. That condition is treated as an already-configured process rather than an initialization failure.

## Window Modes

The initial supported modes are:

- `Windowed`
- `BorderlessFullscreen`

Borderless fullscreen uses a normal visible `WS_POPUP` desktop window. The platform captures the previous windowed bounds, resolves the monitor associated with the current window, applies the monitor bounds with a frame refresh, and keeps DXGI exclusive fullscreen disabled.

Returning to `Windowed` restores the normal overlapped/resizable style and the saved windowed bounds. Restored bounds are validated against the currently available monitor work area; an off-screen or invalid placement is constrained or safely centered on an available display.

Persisted Borderless startup and runtime mode changes use the same platform transition path. When the Settings screen requests a client restart for a display-mode change, the restart reloads the persisted settings before creating the next native window.

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

## Pre-Alpha Frontend

A normal interactive launch presents a visible boot/loading sequence followed by the FORGELINE main menu before gameplay exists. The frontend exposes Continue when a valid save is available, New Game, Load Game, Settings, Credits, and Exit. New Game currently exposes Central Divide, Directorate, and a configurable deterministic seed. Load Game discovers the existing save format, keeps corrupt or incompatible entries visible but disabled, and maintains a bounded visible save window around the current focus.

The frontend supports keyboard and pointer navigation. Main-menu and detail focus, primary actions, Back, seed/settings adjustment, hover/pressed feedback, and short presentation-only screen-entry transitions all use the custom Direct3D 12 frontend path. The 1920×1080 reference surface is centered and safely scaled for 16:9, 16:10, ultrawide, and smaller windows; rendering and hit testing consume the same resolved safe-area layout.

After a session begins, Space toggles player pause during gameplay and F12 opens the controls/onboarding view.

Setup, explicit player pause, help, and minimized/zero-size window state all use the existing simulation-owner pause control transition. They are combined as pause reasons so restoring the window cannot resume a match that remains explicitly paused or has help open.

Client settings are loaded from the current user's Local Application Data FORGELINE/settings.json path before every native-window application session. Missing settings create validated defaults; malformed or invalid settings are quarantined and recovered. A display-setting restart reloads the just-persisted window size and mode before recreating the window. The current settings contract includes window size/mode, UI scale, onboarding visibility, edge scroll, camera pan speed, camera keys, and drag-pan binding.

See [Pre-Alpha UX and Operations](PreAlphaUxAndOperations.md) and [Pre-Alpha Verification Checklist](PreAlphaVerificationChecklist.md).

## Launch

From the repository root:

```powershell
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release
```

The executable is a Windows x64 host with the D3D12 graphics foundation, RTS camera/input stack, and representative chunked terrain active. The initial strategic camera view spans multiple chunks; pan, rotation, pitch, zoom, edge scrolling, negative/positive chunk traversal, resize behavior, depth testing, and frustum culling can be validated directly against the visible world.

Default controls are W/A/S/D or Arrow Keys to pan, Q/E to rotate, R/F to change pitch, Middle Mouse drag to pan, and Mouse Wheel to zoom. Left click/drag selects owned units, logistics entities, and buildings; right click issues movement orders for movable selections. F1 toggles development metrics while the player HUD remains visible, F2 toggles world debug visualization, and F3 cycles the development formation selection through Compact, Line, Column, and Wedge. F4–F8 enter placement mode for Command Core, Power Plant, Mine / Extractor, Storage Depot, and Smelter respectively. F9 rotates the selected building clockwise, left click submits a valid placement, and Escape exits placement mode. The placement controller only emits a build request; simulation revalidates placement and construction resources when the command executes. After an authoritative victory, defeat, or draw, normal match ticks stop; R restarts with a fresh simulation session and Escape ends the completed session. Edge scrolling is enabled by default. See [RTS Camera and Input](CameraAndInput.md) for camera interaction and coordinate conventions, [Selection and Command Interaction](SelectionAndCommandInteraction.md) for selection/order flow, [Formation Movement and Group Orders](FormationMovementAndGroupOrders.md) for shared group movement, [Building Placement and Construction](BuildingPlacementAndConstruction.md) for the construction lifecycle and controls, and [World and Terrain](WorldAndTerrain.md) for world/chunk semantics, culling, and terrain diagnostics.

## Match HUD

The player-facing HUD is rendered through the existing lightweight overlay path but is independent from the F1 development metrics toggle.

It displays the current match state, elapsed time, core resource stock, local power generation/demand, local intelligence counts, owned selection details, Health/Fuel/Ammunition/readiness, building power/inventory information, construction or production progress, and current block reasons.

Alerts are derived from authoritative state for constrained power, blocked production, critical supply, and Command Core damage/destruction. Enemy information is not obtained through raw UI-side entity inspection; intelligence summaries remain scoped to the local faction's intelligence store.

See [Match Flow and Player Experience](MatchFlowAndPlayerExperience.md) for lifecycle, restart, HUD/read-model, and intelligence-filtering boundaries.

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
6. advances the configured skirmish, removes the opposing Command Core through authoritative entity state, advances simulation until the objective system resolves a local victory, and renders the terminal player HUD;
7. reports platform, graphics, world, camera, terrain submission, and completed match state through the normal validation path;
8. requests normal window destruction;
9. waits for graphics work to retire and exits only after orderly graphics/platform cleanup.

CI executes this validation only on Windows runners.

For manual validation, also resize the window, minimize and restore it, move it between displays with different DPI scaling where available, change focus, close it using the system close button, and repeat several debug launches while watching process and USER/GDI handle counts.


See [Presentation Extraction and Debugging](PresentationExtractionAndDebugging.md) for the simulation-to-render ownership boundary, interpolation, debug controls, metrics, and render stress workflow.


## Clean Playable Match Composition

A normal client launch contains the configured Central Divide skirmish only. The earlier standalone Blue-vs-Red tactical fixture and its extra weapons, providers, and units are no longer injected into the playable match.

Combat, sensing, artillery, Fuel/Ammunition, resupply, readiness, opponent behavior, and Command Core victory are exercised by the actual Directorate/skirmish systems. F2 continues to expose their diagnostics without adding presentation-owned gameplay state.

Synthetic generic entities remain available exclusively through `--render-stress <count>` for bounded rendering validation. CI uses that option for graphics stress independently of the headless full-match correctness gate.


## Execution owners

The active client session uses three explicit owners:

- **Platform:** the creating thread of `WindowsPlatform` and `IWindow`; Win32 pumping, input/window-event draining, mutable window state, camera/input controllers.
- **Simulation:** the dedicated `ForgeLine Simulation` thread; all fixed-tick advancement, command-gateway invocation, authoritative ECS/game mutation after startup, pause/terminal control.
- **Rendering:** the dedicated `ForgeLine Render` thread; graphics device/swap chain, renderer resources, resize, draw submission, present, GPU idle and disposal.

The platform copies HWND/initial dimensions into `GraphicsWindowTarget` before starting the graphics owner. The render owner never calls `IWindow` or Win32 APIs. Window size/minimize changes cross as copied frame state.

Gameplay submissions use a bounded client→simulation queue. When full, admission fails instead of blocking the Win32 pump or allocating unbounded history. The simulation owner assigns the normal gateway correlation/target tick/command sequence. Completed command results continue through the separate bounded result queue.

Minimize explicitly requests simulation pause and independently suspends zero-sized rendering. Restore requests resume. This replaces the earlier accidental behavior where a minimized render-loop branch simply skipped simulation advancement.

A terminal completed-tick snapshot freezes normal simulation ticks while event pumping and terminal rendering continue. Escape applies a non-ticking simulation-owned `EndMatchCommand` control transition. Restart shuts down the old render/simulation owners and constructs a fresh session.

See [Client Execution Ownership](adr/ClientExecutionOwnership.md).
