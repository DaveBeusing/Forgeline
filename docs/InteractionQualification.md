# RTS interaction qualification

## Evidence boundary

Qualification uses the existing input state, camera, selection, minimap, help, HUD and rate samplers. `GameplayInteractionJourneyTests` supplies immutable presentation snapshots and synthetic platform events to these production controllers. The client and tests share the HUD hit/click-origin policy. This exercises controller routing and state transitions without launching the full menu, issuing native Windows input or mutating simulation entities.

`ClientSimulationHostTests` separately verifies real command receipts, completed-tick telemetry and pause/resume boundaries. `Invoke-WindowModeQualification.ps1` launches the actual D3D12 client and checks startup, ready HUD geometry, real measured running FPS/TPS and windowed/borderless lifecycle. It does not click through the controls or inspect framebuffer appearance.

There is no established screenshot/readback comparison harness for gameplay in this repository. Geometry, clipping, blend/depth/cull policy and buffer reuse are automated. Bright/dark terrain contrast, text readability, native focus/resize event delivery and complete menu-to-game interaction remain manual acceptance checks below. A passing CPU test or smoke report must not be recorded as a visual screenshot pass.

## Interaction-state matrix

| State / transition | Expected behavior | Automated evidence |
| --- | --- | --- |
| No snapshot -> player binding -> ready world | Loading/binding state then usable HUD; no invented rates | Gameplay HUD runtime tests; native smoke |
| World keyboard / middle drag / wheel | View-relative pan, drag polarity and configured zoom bounds | Camera tests; integrated journey |
| World unit/building click and marquee | Owned eligible selection; same box result in four directions | Selection tests; integrated journey |
| HUD press -> world release, same frame | Retained origin captures click; no selection/order leak | Integrated HUD boundary cases |
| World press/drag -> HUD release | Gesture canceled; no pending order survives | Integrated HUD boundary cases |
| World right click | One movement request and transient intent marker; no ECS mutation | Integrated journey; command/host tests |
| Minimap quick left/right click | Camera jump / movement request once, even between updates | Minimap regressions; integrated journey |
| Press outside minimap -> release inside | Cannot activate a map or selector action | Minimap origin regressions |
| F1 open -> Escape close -> held input | First press opens; both transition frames consume gameplay; physical release required | Help tests; integrated journey |
| Focus lost during held drag -> fresh input | Drag/feedback/pending orders canceled; selected objects remain | Input/selection tests; integrated journey |
| Resize or DPI/UI-scale change during drag | Cancel gesture; held button cannot restart it; fresh click works | Selection display regressions; integrated resize journey |
| Running -> paused -> resumed / changed load | Presented FPS independent of ticks; paused SIM state, no stale TPS; new sample window on resume | Rate sampler tests; integrated counter sequence; host pause tests |
| Session replacement | Clear old selection/orders/feedback; discard prior rate windows | Integrated journey; controller lifecycle tests |

The integrated journey uses 1024x768, 1280x720, 1600x900, 1920x1080, 2560x1440, 3440x1440, 3840x2160 and 5120x2160 with representative 96/120/144/192 DPI cases and unit/building categories. The full HUD geometry matrix independently crosses four sizes (1024x768, 1600x900, 3440x1440, 3840x2160), all four DPI values and all four marquee directions (64 cases). These are synthetic layout configurations; actual Windows monitor scaling is qualified separately.

The full HUD geometry matrix verifies each surface stays within normalized viewport bounds with culling/depth disabled, the information overlay blends its 10% marquee fill and contrast outline, cancellation removes the marquee and buffers are reused. Existing marker tests verify unit/building footprints, sloped terrain, hidden entities and shape differences for hover/foreign ownership. These properties support readability but do not prove contrast against arbitrary terrain pixels.

## Reproducible automated checks

```powershell
dotnet build ForgeLine.sln --configuration Release
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Client.Tests.GameplayInteractionJourneyTests ForgeLine.Client.Tests.GameplayHelpControllerTests ForgeLine.Client.Tests.ClientSimulationHostTests
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.GameplayDisplayQualificationTests ForgeLine.Presentation.Tests.GameplayHudRuntimeTests ForgeLine.Presentation.Tests.SelectionInteractionTests ForgeLine.Presentation.Tests.SelectionMarkerTests ForgeLine.Presentation.Tests.RtsMinimapInteractionTests
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Invoke-WindowModeQualification.ps1 -Configuration Release -RenderStressInstances 1000 -SettingsRoot artifacts/interaction-window-settings -WindowedReport artifacts/interaction-windowed.json -BorderlessReport artifacts/interaction-borderless.json
```

## Manual display and interaction checklist

Record executable version/commit, GPU/driver, resolution, Windows DPI, UI scale, refresh rate, window mode, scenario/save and selected entity count for each run. Preserve screenshots with those identifiers when manually captured. Use a normal client launch, not auto-closing smoke mode.

- [ ] At 100/125/150/200% Windows scale, repeat at 1024x768 or the smallest available client size, 1920x1080, 2560x1440, 3840x2160 and an available ultrawide size. Mark unavailable monitor modes explicitly. Repeat windowed and borderless; change UI scale through supported settings.
- [ ] Start through intro/menu, enter a new playable match, then load an existing save. HUD appears without opening diagnostics. Resources/selection/commands stay legible, metrics remain in the reserved top-right area, and loading/pause/resume states show no stale rate.
- [ ] Pan W/A/S/D and arrows before/after yaw rotation. Compare edge scroll and middle drag. Scroll to near/far zoom; verify bounds, pointer ownership over HUD and keyboard pan while the pointer is over HUD.
- [ ] Select an owned unit and building on flat/sloped, bright/dark terrain. At near/normal/far zoom, check ring footprint alignment, dark/colored contrast edges, dashed hover and foreign radial ticks. Hidden foreign objects provide no hover. Check crowded groups and visual obscuring while simulation runs.
- [ ] Drag in all four directions, cross viewport edges, release over each HUD region and repeat a quick HUD press/world release. Inspect the faint fill/outline, no stale marquee and no accidental selection/order. Right click once: observe the brief intent marker and later authoritative command result, including invalid targets.
- [ ] Click/drag minimap, issue a quick right-click order and change overlay selector. Press outside then enter the map while held: no new map action. Opening help or losing focus during map drag stops the drag; a fresh press works afterwards.
- [ ] Open F1 on the first press while keyboard pan, middle pan, world/minimap drag or right mouse is held. Close via Escape/Back and F1/F12. Held input cannot leak; release and press again to resume. Repeat via the pause controls screen.
- [ ] Alt-Tab while pan/selection/minimap input is held. Return and release, verify selection retained but drag/order feedback cleared. Minimize/restore, resize and change monitor DPI during a marquee, then release: no completion or restart of the old rectangle. New clicks and zoom still work.
- [ ] Observe independent FPS/TPS under normal and heavy entity load at available 60/144 Hz, then pause/resume. Pause shows explicit paused SIM state; resume and save/load require a new tick rate window. Never interpret the intent marker as authoritative command acceptance.

## Overlay/HUD performance qualification

```powershell
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks/ForgeLine.Rendering.Benchmarks.csproj --configuration Release --no-build -- --interaction-hotpaths artifacts/interaction-hotpaths.json
```

The bounded CPU harness reuses the existing null graphics backend, actual HUD root, terrain-sampled selected rings, command feedback, marquee/minimap geometry and metrics sampler. Thirty-six cases cross four viewport/DPI profiles, 0/1/1000 selected objects and camera distances 12/90/260. Fixtures, snapshots and arrays are created outside sampling; alternating snapshot references exercise rebuilding rather than a snapshot-reference cache. The sampler uses explicit fixture counters/time, including available numeric rates after warmup; these are not observed native FPS/TPS. Each case warms 128 frames then samples 256 frames. Reports include version/runtime/platform, p50/p95/p99/max costs, thread allocation and collection counts, vertex/line counts, dropped lines and line draw batches.

CI requires zero measured thread allocation after warmup, no dropped world lines, positive HUD geometry, exactly 24 lines per selected ring plus 22 feedback lines and at most one world-line draw batch. Time percentiles are observations, not portable pass/fail limits. Compare matching build/configuration on the same idle machine; investigate p99/max spikes and collection changes. GPU uploads, driver waits, framebuffer appearance and presentation are excluded. Native smoke counters complement this CPU measurement; extended interactive high-load runs are still required to qualify GPU hitches.
