# Startup Timing and Readiness

Startup diagnostics are opt-in and owned by the Windows client. They do not change simulation ticks, command ordering, snapshot hashes, splash duration or readiness decisions.

## Collection and correlations

Launch with `--startup-diagnostics-output <report.json>`. With no option, collection is disabled: no event storage, timestamp reads, report serialization or report I/O occurs. Enabled collection uses a fixed 128-event buffer and a short lock shared by platform and render threads. Overflow is counted in `DroppedEvents`; an overflowed report is incomplete evidence. File creation and JSON serialization occur after application execution and render-thread disposal. A report-write failure is logged without changing the application's exit code.

Every event contains a sequence, raw monotonic Stopwatch timestamp, milliseconds since managed `Program.Main` entry, optional phase duration, phase, outcome and session correlation. This origin excludes OS process creation and runtime initialization before Main. `StopwatchFrequency` permits conversion of raw timestamps. Wall-clock adjustments cannot change durations.

`ProcessId` is a diagnostic GUID shared across in-process restarts. `LaunchId` advances for each settings/window/application launch; later launches write `<name>-launch2.json`, etc., preserving the first report. `SessionId=0` denotes launch/frontend work; `SessionId=1` denotes the single match requested during that launch. A restart creates another launch and a fresh match correlation.

## Phase and milestone definitions

| Identifier | Measurement boundary |
| --- | --- |
| Run | Settings load through exit/restart, including failures after readiness |
| Launch | Launch start through ApplicationReady; user time in the menu is excluded |
| Settings | Existing settings load, validation, default creation and recovery |
| Window / WindowVisible | Platform window creation returns after native ShowWindow/UpdateWindow and configured mode application; this is an upper-bound observation of native visibility, not a display scan-out measurement |
| BootRenderer | Asynchronous boot graphics device and frontend renderer construction on the render owner |
| FirstPresentedFrame | First non-occluded successful swap-chain presentation across observed boot/menu/gameplay render hosts |
| RuntimeAssets / RuntimeAssetsReady | Cancellable background development bootstrap, catalog load and production validation complete; GPU gameplay resources may still be pending |
| SaveCatalog | Independent background save discovery and immutable frontend entries |
| FrontendDependenciesReady | Both startup products completed successfully; intro completion and menu presentation may still be pending |
| StudioSplash | Bootstrap start through controller completion, user skip, startup failure or window-close cancellation |
| SplashBootstrapFirstFrame | Successful presentation of asset-independent studio glyph geometry |
| SplashArtwork | CPU preflight enables artwork or selects TextFallback; render resource failure is recorded separately |
| StudioSplashFirstFrame | Successful presentation of a studio view with initialized splash texture resources; fallback loading/overlay frames do not qualify |
| Frontend / FrontendReady | Reused save-catalog results, menu/settings models and menu render host initialized |
| MainMenuFirstFrame | Successful presentation of a main-menu view |
| MainMenuInteractive | Platform input loop observes the presented menu; internal frontend preparation alone is insufficient |
| ApplicationReady | Presented interactive menu on normal launch; first presented gameplay frame in smoke mode, which bypasses menu selection |
| SessionReconstruction / SessionRuntimeReady | New-game creation or validated save reconstruction returns a complete MatchRuntime; this does not claim GPU or gameplay readiness |
| GameplayRenderer | Gameplay device, terrain/instance/HUD resources and render owner initialized |
| FirstGameplayFrame / SessionReady | Actual successful gameplay presentation after simulation snapshot and control preparation; a frontend overlay does not qualify |

The graphics device's presentation counter advances only after successful non-occluded Present. Publishing a view, invoking a render callback, submitting commands, suspended surfaces and occlusion cannot establish a presented milestone. This measures Present return, not GPU completion or physical monitor scan-out. The client observes the counter on its existing graphics owner thread without allocating a complete graphics diagnostics snapshot.

Readiness can remain Pending, become Ready, or terminate Failed/Cancelled. Closing before readiness cancels unfinished phases. OperationCanceledException is cancellation. A failed phase records its duration and exception type, and cannot itself produce ready. Errors after readiness remain failures in the Run outcome and terminal report state, while the earlier readiness milestone remains historical evidence. Optional splash failure permits existing frontend fallback and can coexist with eventual application readiness.

Splash bypasses preserve `--skip-splash`, the saved splash setting and existing smoke-mode bypass. Bypasses record Skipped with a reason; interactive skip is Skipped and closing mid-splash is Cancelled. Unavailable branding assets select TextFallback, while failed artwork resources are recorded as Failed in SplashArtwork and do not prevent a valid frontend. The existing visible intro interval starts after actual presentation; bypassed launches require a presented loading surface and valid dependencies without an intro delay.

## Observation procedure

1. Record revision, Release configuration, SDK/runtime, CPU, GPU/driver, RAM, storage, resolution/window mode, VSync, asset manifest revision, splash setting and launch arguments. Build and compile assets before measuring; the source-checkout development bootstrap is included in RuntimeAssets and must be labelled separately from packaged launches.
2. For cold observations, reboot, wait for background activity to settle, and run the packaged client once. Describe exactly which caches are cold; rebooting alone does not prove all driver caches are empty. Do not compare compilation/bootstrap runs with packaged runs.
3. For warm observations, close the process and repeat the same executable/settings/assets on the same machine. Use distinct report paths. Keep cold and warm groups separate and collect multiple samples; report sample count, median and range without claiming an improvement from a single run.
4. Normal splash launch: `ForgeLine.Client.exe --startup-diagnostics-output artifacts/startup-splash.json`. Allow the menu to appear, start a new match, wait for gameplay, then close normally. Repeat with `--skip-splash` and a separate output. Include interactive skip, disabled splash setting, save load, settings-driven restart, close-before-ready and unavailable splash resources in manual qualification.
5. Bounded gameplay smoke: `ForgeLine.Client.exe --smoke-test --startup-diagnostics-output artifacts/startup-smoke.json`. Smoke mode always skips the splash even without `--skip-splash`; it cannot qualify actual splash presentation or menu interactivity. Existing visual qualification arguments can be combined with diagnostics.
6. Inspect Started/terminal phase pairs, nonnegative durations, monotonic sequence/timestamps, zero dropped events, terminal state and session IDs. FirstPresentedFrame and RuntimeAssetsReady are independent: fast cached work may finish first. Under deliberately blocked I/O, verify SplashBootstrapFirstFrame occurs while the worker is pending. On normal launch FrontendReady and MainMenuFirstFrame must precede MainMenuInteractive/ApplicationReady, and SessionRuntimeReady must precede FirstGameplayFrame/SessionReady. StudioSplashFirstFrame qualifies compiled artwork only; SplashBootstrapFirstFrame qualifies geometry only. Do not invent absent milestones for skipped/cancelled runs.

These are baseline measurement procedures, not performance targets. Startup timing remains hardware/cache dependent. Unit tests validate collection, correlation, failure, cancellation, disabled behavior and readiness distinctions; physical splash/menu presentation, display visibility and cold/warm samples require Windows interactive qualification.
