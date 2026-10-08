# Studio Startup Splash

## Status and ownership

The reusable startup splash domain lives in `ForgeLine.Presentation`. It is not part of simulation time, world state, ECS, or gameplay snapshots. The studio presentation is defined by `UndefinedBehaviorStudioSplash.Create()`.

The client starts an asset-independent branded surface immediately after creating the window. Built-in glyph geometry displays the U/B mark, wordmark and subtitle without opening the runtime manifest or loading textures. The render owner initializes asynchronously while the platform owner continues pumping events. Runtime catalog discovery, development compilation and validation run concurrently with independent save-catalog discovery.

After the first successful presentation, available runtime artwork may replace the bootstrap geometry on the same render owner. Missing required branding textures or failed texture initialization retain the text/vector fallback. A missing or invalid required runtime manifest remains a startup error; branding fallback does not waive production asset validation.

## Branding and timing

The official studio name is **Undefined Behavior Studios**, with the compact **U/B** mark and **UNDEF BHVIOR** wordmark. The default timeline lasts 3 seconds, with a 1.15-second minimum display interval and a 0.3-second skip fade. Timing starts when the platform loop observes the first successful presentation, so graphics setup does not consume the visible interval. Timing data belongs to the splash definition, not the graphics backend.

The canonical asset identifiers are:

| Layer | ID |
|---|---|
| Background | `branding.undefined_behavior.splash.background` |
| Atmospheric fog | `branding.undefined_behavior.splash.fog` |
| Separator reveal | `branding.undefined_behavior.splash.separator` |
| Monogram | `branding.undefined_behavior.splash.ub_monogram` |
| Wordmark | `branding.undefined_behavior.splash.wordmark` |
| Studio subtitle | `branding.undefined_behavior.splash.subtitle` |

## Contracts

`SplashDefinition` validates the identity, duration, layer uniqueness, asset identifiers, normalized geometry, and keyframe ranges. `SplashTimelineEvaluator` evaluates scalar animation channels and offsets without heap allocations on its normal path. `SplashScreenController` manages presentation elapsed time, completion, skip state, and a short fade. `ISplashRenderer` defines the engine-independent presentation boundary.

`SplashAssetPreflight.Prepare()` checks every referenced texture before enabling the compiled-artwork path. Missing background and other non-brand layers are optional; a missing monogram, wordmark or studio subtitle selects the bootstrap fallback. Lookup exceptions are reported through the caller's diagnostic callback and handled as missing assets.

`ClientStartupCoordinator` owns two cancellable CPU/I/O jobs and their completed products. Intro completion or skip and frontend dependency readiness are independent gates. If the intro ends first, an indeterminate loading surface remains responsive until both products complete. If dependencies finish first, the existing intro/skip rules apply. Completed products are reused exactly once by frontend initialization; skipping never starts another load. Closing, failure and restart cancel and join the jobs before disposal. Cancelling development compilation terminates and waits for its child process tree.

## Integration requirements

The interactive Windows client plays the studio splash after graphics initialization and before presenting the main menu, without constructing or advancing gameplay simulation state. The dedicated compositor displays image layers with preserved aspect ratios, opacity transitions, centered composition and GPU resources owned by the frontend render thread.

The client consumes the existing platform input abstraction for Enter, Space, Escape and primary mouse button. Skip requests before the minimum visible interval do not immediately end playback. The persisted `showStudioSplash` setting enables/disables playback; `--skip-splash` bypasses it. Smoke tests and non-client tools do not display the splash.

The six studio source textures and asset definitions live under `assets/source/branding/undefined_behavior/splash/` and are compiled by the existing asset compiler. The renderer resolves stable runtime IDs through the manifest and never loads source art directly.

## Verification

Unit tests cover interpolation, duplicate keyframes, duration, fade, missing asset paths, and CLI bypass. Full qualification additionally requires normal launch, explicit splash bypass, missing-asset fallback, four target resolutions (1920x1080, 2560x1440, 3840x2160, 3440x1440), no resource leaks, and Windows Release build/test and CI checks. These manual visual and lifetime checks are not covered by unit tests and must be performed before release.
