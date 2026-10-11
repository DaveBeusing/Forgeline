# Early Native Bootstrap

## Status

Accepted for the Windows interactive client.

## Context

The previous client read settings before creating its window. Its first D3D12
surface could be black while studio-video decoding initialized. In a source
checkout, runtime loading also launched the asset compiler. Consequently an
installation launch and authoring-time compilation shared a misleading timing
boundary.

## Decision

The platform owner creates one normal taskbar window with a native GDI wordmark,
accent and phase label before settings, application composition or graphics
initialization. Artwork is assembly-owned text and geometry, with no external
files, asset catalog, image decoder or GPU requirement. Per-monitor V2 DPI
awareness is established first. WM_PAINT scales against the current window DPI.

Settings load on one transient CPU/I/O worker while the owner dispatches messages.
The bootstrap starts at the default 1600x900 size; persisted size and mode are
applied once before graphics initialization, preserving the HWND and monitor.
This can cause an initial size transition for non-default settings, but creates
no second window or additional taskbar entry. DPI and monitor changes continue
through the existing window lifecycle.

Two existing bounded workers then prepare the validated runtime manifest and
save entries. Gameplay payload checks are deferred to the cancellable session
construction worker, before constructing a simulation. Menu readiness does not
claim gameplay content readiness. The client never launches source compilation;
developers compile assets explicitly before launch. An explicit runtime root or
a packaged installation with build provenance is authoritative; missing content
cannot be concealed by borrowing another checkout's catalog.

The native surface remains active until the platform owner observes a successful
non-occluded graphics presentation. Graphics initialization, resizing and disposal
retain their render owner. While studio video is unavailable, D3D12 draws a loading
label rather than a blank frame. The first menu reuses the bootstrap render owner
and its device/swap chain. Existing user-controlled studio playback remains
skippable; the early bootstrap adds no minimum display interval.

The client collects bounded monotonic startup events automatically. A single
serialized checkpoint writer publishes atomic JSON replacements when events
change. The default path is local user diagnostics, never the installation.
Failure and normal shutdown explicitly flush the terminal report. Native errors
after window creation are drawn on that same window after graphics teardown;
the native pump remains active until the user closes it. Automation smoke modes
record the failure and exit without waiting for input.

Cancellation stops new work, requests cooperative worker cancellation and joins
owners before releasing the window. An already-running synchronous settings file
operation is not forcibly interrupted; the owner continues dispatching while it
finishes. Headless composition, deterministic ticking and asset compiler behavior
remain outside this boundary.

## Measurement

Managed-entry timestamps exclude OS process creation, runtime loading and JIT work
before Main. NativeBootstrapFirstFrame records completion of native painting, not
monitor scan-out. FirstPresentedFrame records successful D3D12 Present. Menu
interactivity still requires a presented menu observed by the input owner.
Independent asset and graphics milestones may complete in either order.

The packaged `--startup-menu-smoke-test` path exits only after menu interactivity,
without constructing gameplay. `build/Invoke-StartupQualification.ps1` captures
multiple launches with one excluded warmup, an external HWND observation and
bounded process lifetime. These warm observations do not establish cold-cache
performance or guarantee the hardware-dependent 100–300 ms aspiration.

## Consequences

Window painting now has a short native ownership phase before D3D12 ownership.
Source compilation is an explicit development/build action. Corrupt gameplay
payloads can be reported upon session selection after a usable menu exists.
Startup evidence is available while initialization remains pending, including
failure paths. Full display/focus and cold-start acceptance still require actual
Windows qualification.
