# Startup Timing and Readiness

Startup diagnostics are automatic for interactive launches and owned by the Windows client. They do not change simulation ticks, command ordering, snapshot hashes, splash duration or readiness decisions.

## Collection and correlations

Use `--startup-diagnostics-output <report.json>` to choose a report path. Without it, reports go to `%LOCALAPPDATA%/FORGELINE/diagnostics/startup-<PID>-<launch>.json`. A bounded 128-event buffer collects milestones under a short lock. One serialized writer checkpoints changed events about once a second using atomic replacements; initialization stalls retain the last completed phase. Failure and exit flush terminal state. A write failure is logged and does not change the application's exit code. Disabled collection remains available to isolated callers/tests but is not the normal interactive launch policy.

Every event contains a sequence, raw monotonic Stopwatch timestamp, milliseconds since managed `Program.Main` entry, optional phase duration, phase, outcome and session correlation. This origin excludes OS process creation and runtime initialization before Main. `StopwatchFrequency` permits conversion of raw timestamps. Wall-clock adjustments cannot change durations.

`ProcessId` is a diagnostic GUID shared across in-process restarts. `LaunchId` advances for each settings/window/application launch; later launches write `<name>-launch2.json`, etc., preserving the first report. `SessionId=0` denotes launch/frontend work. Positive SessionIds advance for each match request/retry within that window. A new attempt resets current session phase/readiness slots while preserving earlier events in the bounded report. A restart creates another launch and a fresh match correlation.

## Phase and milestone definitions

| Identifier | Measurement boundary |
| --- | --- |
| ProcessEntered | Managed Main entry, recorded at the original monotonic timestamp |
| NativeBootstrapFirstFrame | Completed native GDI painting before settings and graphics; not display scan-out |
| GraphicsReady | Device and frontend overlay resources initialized on their render owner |
| GameplayAssetsReady | Required gameplay payload checks complete on the session worker; not menu readiness |
| StartupFailed | Terminal interactive startup failure, also shown on the native window |
| Run | Settings load through exit/restart, including failures after readiness |
| Launch | Launch start through ApplicationReady; user time in the menu is excluded |
| Settings | Existing settings load, validation, default creation and recovery |
| Window / WindowVisible | Platform window creation returns after native ShowWindow/UpdateWindow and configured mode application; this is an upper-bound observation of native visibility, not a display scan-out measurement |
| BootRenderer | Asynchronous boot graphics device and frontend renderer construction on the render owner |
| FirstPresentedFrame | First non-occluded successful swap-chain presentation across observed boot/menu/gameplay render hosts |
| RuntimeAssets / RuntimeAssetsReady | Cancellable compiled manifest load and structural validation complete; gameplay payload validation is deferred until session selection |
| SaveCatalog | Independent background save discovery and immutable frontend entries |
| FrontendDependenciesReady | Both startup products completed successfully; intro completion and menu presentation may still be pending |
| StudioSplash | Bootstrap start through controller completion, user skip, startup failure or window-close cancellation |
| SplashBootstrapFirstFrame | Successful presentation of the asset-independent pre-video loading frame (historical name; native branding is measured separately) |
| SplashArtwork | CPU preflight enables artwork or selects TextFallback; render resource failure is recorded separately |
| StudioSplashFirstFrame | Successful presentation of decoded studio video; fallback loading frames do not qualify |
| Frontend / FrontendReady | Reused save-catalog results, menu/settings models and menu render host initialized |
| MainMenuFirstFrame | Successful presentation of a main-menu view |
| MainMenuInteractive | Platform input loop observes the presented menu; internal frontend preparation alone is insufficient |
| ApplicationReady | Presented interactive menu on normal launch; first presented gameplay frame in smoke mode, which bypasses menu selection |
| SessionReconstruction / SessionRuntimeReady | New-game creation or validated save reconstruction returns a complete MatchRuntime; this does not claim GPU or gameplay readiness |
| SessionLoadRequested | New positive session correlation for construction or retry |
| SessionConfiguration / SessionAssembly | Worker-side validation and CPU scenario composition |
| SessionReplay / SessionVerification | Complete replay tick/control boundaries and mandatory RNG/checkpoint/authoritative hash verification |
| SessionPresentationBinding | Platform-owned gateway/observer binding through the first completed simulation snapshot |
| SessionRendererReady | Platform loop observes actual matching gameplay presentation after renderer initialization |
| SessionLoadFailed / SessionLoadCancelled | Recoverable pre-activation failure/category or cancelled request; previous menu application readiness is retained |
| GameplayRenderer | Gameplay terrain/instance/HUD resources initialized on the original graphics owner, retaining the transition device/swap chain |
| FirstGameplayFrame / SessionReady | Actual successful gameplay presentation after simulation snapshot and control preparation; a frontend overlay does not qualify |

Session construction uses a dedicated CPU owner and immutable latest progress. Replay updates occur at most every 64 complete tick/control boundaries plus initial/final observations; diagnostic phase timing remains bounded and does not append an event per tick. Gameplay presentation requires the requested simulation identity and a successful non-occluded Present. Readiness is also checked with diagnostic collection disabled. See [Asynchronous session loading](AsynchronousSessionLoading.md).

The graphics device's presentation counter advances only after successful non-occluded Present. Publishing a view, invoking a render callback, submitting commands, suspended surfaces and occlusion cannot establish a presented milestone. This measures Present return, not GPU completion or physical monitor scan-out. The client observes the counter on its existing graphics owner thread without allocating a complete graphics diagnostics snapshot.

Readiness can remain Pending, become Ready, or terminate Failed/Cancelled. Closing before readiness cancels unfinished phases. OperationCanceledException is cancellation. A failed phase records its duration and exception type, and cannot itself produce ready. Errors after readiness remain failures in the Run outcome and terminal report state, while the earlier readiness milestone remains historical evidence. Optional splash failure permits existing frontend fallback and can coexist with eventual application readiness.

Splash bypasses preserve `--skip-splash`, the saved splash setting and existing smoke-mode bypass. Bypasses record Skipped with a reason; interactive skip is Skipped and closing mid-splash is Cancelled. Unavailable branding assets select TextFallback, while failed artwork resources are recorded as Failed in SplashArtwork and do not prevent a valid frontend. The existing visible intro interval starts after actual presentation; bypassed launches require a presented loading surface and valid dependencies without an intro delay.

## Observation procedure

1. Record revision, Release configuration, SDK/runtime, CPU, GPU/driver, RAM, storage, resolution/window mode, VSync, asset manifest revision, splash setting and launch arguments. Build and compile assets before measuring; source compilation is an explicit prerequisite and is never included in player startup.
2. For cold observations, reboot, wait for background activity to settle, and run the packaged client once. Describe exactly which caches are cold; rebooting alone does not prove all driver caches are empty. Do not compare compilation/bootstrap runs with packaged runs.
3. For warm observations, close the process and repeat the same executable/settings/assets on the same machine. Use distinct report paths. Keep cold and warm groups separate and collect multiple samples; report sample count, median and range without claiming an improvement from a single run.
4. Normal splash launch: `ForgeLine.Client.exe --startup-diagnostics-output artifacts/startup-splash.json`. Allow the menu to appear, start a new match, wait for gameplay, then close normally. Repeat with `--skip-splash` and a separate output. Include interactive skip, disabled splash setting, save load, settings-driven restart, close-before-ready and unavailable splash resources in manual qualification.
5. Bounded gameplay smoke: `ForgeLine.Client.exe --smoke-test --startup-diagnostics-output artifacts/startup-smoke.json`. Smoke mode always skips the splash even without `--skip-splash`; it cannot qualify actual splash presentation or menu interactivity. Existing visual qualification arguments can be combined with diagnostics.
6. Inspect Started/terminal phase pairs, nonnegative durations, monotonic sequence/timestamps, zero dropped events, terminal state and session IDs. FirstPresentedFrame and RuntimeAssetsReady are independent: fast cached work may finish first. Under deliberately blocked I/O, verify SplashBootstrapFirstFrame occurs while the worker is pending. On normal launch FrontendReady and MainMenuFirstFrame must precede MainMenuInteractive/ApplicationReady, and SessionRuntimeReady must precede FirstGameplayFrame/SessionReady. StudioSplashFirstFrame qualifies compiled artwork only; SplashBootstrapFirstFrame qualifies geometry only. Do not invent absent milestones for skipped/cancelled runs.

These are baseline measurement procedures, not performance targets. Startup timing remains hardware/cache dependent. Unit tests validate collection, correlation, failure, cancellation, disabled behavior and readiness distinctions; physical splash/menu presentation, display visibility and cold/warm samples require Windows interactive qualification.

## Packaged menu qualification

```powershell
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/prealpha/win-x64/publish/ForgeLine.Client.exe -Samples 5
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/prealpha/win-x64/publish/ForgeLine.Client.exe -Samples 5 -IncludeStudioIntro -OutputRoot artifacts/startup-studio
```

The first launch is an excluded warmup. Later samples are warm observations, with no reboot or cache eviction. The script bounds each process to 60 seconds, retains console/checkpoint evidence, and requires actual MainMenuInteractive with no session construction. `--startup-menu-smoke-test` exits after the owner observes a presented menu; ordinary launch remains interactive. Native failure is painted and logged in both modes; smoke mode exits with failure rather than waiting for the user to close the error window. Existing studio playback has no additional bootstrap delay and can be skipped or disabled using the existing preference.

An explicit runtime override and a published installation with `build-provenance.json` cannot borrow a different catalog when their manifest is missing. Recompile development assets explicitly or repair the packaged installation. Gameplay payload corruption is surfaced during cancellable session preparation before simulation activation.
