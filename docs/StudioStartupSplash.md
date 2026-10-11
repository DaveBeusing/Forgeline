# Studio Startup Splash

## Status and ownership

The reusable startup splash domain lives in `ForgeLine.Presentation`. It is not part of simulation time, world state, ECS, or gameplay snapshots. The studio presentation is defined by `UndefinedBehaviorStudioSplash.Create()`.

The Windows client first paints an assembly-owned FORGELINE wordmark and phase label using native GDI. It retains the same window while settings and graphics initialize. The render owner initializes asynchronously; D3D12 draws a loading label while the packaged video decoder prepares its first frame. Runtime manifest validation and save discovery run on independent workers. Source compilation is an explicit build action. Missing or invalid manifests remain visible startup errors. The first menu reuses the bootstrap renderer and swap chain.

## Branding and timing

The official studio name is **Undefined Behavior Studios**, with the compact **U/B** mark and **UNDEF BHVIOR** wordmark. The default timeline lasts 10 seconds, with a 1.15-second minimum display interval and a 0.3-second skip fade. Timing starts when the platform loop observes the first successfully presented video frame, so graphics setup does not consume the visible interval. Timing data belongs to the splash definition, not the graphics backend.

The Windows client plays only the approved cinematic master, `studio-intro.mp4` (1672x940, 30 fps, exactly ten seconds), with the matching stereo PCM soundtrack, `studio-intro.wav`. These files are packaged into `assets/runtime/branding` by the client project for both build and publish. The master includes the camera move, logo/room parallax, three particle depth planes, drifting fog, metal highlights and final fade to black.

Media Foundation decodes on a dedicated CPU thread with a two-frame bounded queue. The render owner consumes frames by the existing presentation timestamp, uploads them before command recording and retires GPU resources through the graphics device's fence-aware lifetime. The soundtrack begins at the current presentation offset and follows skip-fade opacity. Leaving the splash, closing or restarting cancels and joins decoding, resets the audio device and disposes GPU resources. No simulation clock is involved.

Missing or invalid video skips the intro and proceeds through the normal dependency gate without displaying old branding. An unavailable audio device keeps the video playing silently. Runtime manifest validation and dependency readiness are unchanged. `SplashBootstrapFirstFrame` remains a diagnostic name for the initial black presentation.

## Contracts

`SplashDefinition` validates the identity, duration, layer uniqueness, asset identifiers, normalized geometry, and keyframe ranges. `SplashTimelineEvaluator` evaluates scalar animation channels and offsets without heap allocations on its normal path. `SplashScreenController` manages presentation elapsed time, completion, skip state, and a short fade. `ISplashRenderer` defines the engine-independent presentation boundary.

`ClientStartupCoordinator` owns two cancellable CPU/I/O jobs and their completed products. Intro completion or skip and frontend dependency readiness are independent gates. If the intro ends first, an indeterminate loading surface remains responsive until both products complete. If dependencies finish first, the existing intro/skip rules apply. Completed products are reused exactly once by frontend initialization; skipping never starts another load. Closing, failure and restart cancel and join the jobs before disposal. Cancelling development compilation terminates and waits for its child process tree.

## Integration requirements

The interactive Windows client plays the studio splash after graphics initialization and before presenting the main menu, without constructing or advancing gameplay simulation state. The video compositor preserves the master aspect ratio with black letterboxing and applies the skip fade on the frontend render thread.

The client consumes the existing platform input abstraction for Enter, Space, Escape and primary mouse button. Skip requests before the minimum visible interval do not immediately end playback. The persisted `showStudioSplash` setting enables/disables playback; `--skip-splash` bypasses it. Smoke tests and non-client tools do not display the splash.

The splash is eligible only on the first client startup in each process. Returning to the main menu, saving and returning, or applying settings that recreate the client bypasses playback without changing the persisted preference. Startup diagnostics record `ApplicationRestart` as the bypass reason. A fresh process reads the preference again.

Legacy source textures and reusable layer contracts remain available to other asset consumers; the client startup no longer uses their compositor or preflight path.

## Verification

Unit tests cover interpolation, duplicate keyframes, duration, fade, missing asset paths, and CLI bypass. Full qualification additionally requires normal launch, explicit splash bypass, missing-video bypass, four target resolutions (1920x1080, 2560x1440, 3840x2160, 3440x1440), no resource leaks, and Windows Release build/test and CI checks. These manual visual and lifetime checks are not covered by unit tests and must be performed before release.
