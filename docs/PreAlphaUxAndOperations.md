# Pre-Alpha UX and Operations

## Purpose

The pre-alpha client shell turns the completed vertical-slice runtime into a launchable Windows product surface without moving gameplay authority out of simulation.

The current shell deliberately stays small. It exposes only choices that exist in the vertical slice and builds on the existing lightweight overlay renderer rather than introducing a second frontend framework.

## Match Setup

A normal client launch begins in an explicit setup state.

The current available configuration is:

- map: **Central Divide**
- local faction: **Directorate**
- opponent: **Directorate computer opponent**

These are the only currently supported playable choices, so the setup screen presents them as explicit current selections instead of inventing non-functional alternatives.

Controls:

- Enter starts the configured match;
- Escape exits the client;
- F12 opens controls and onboarding.

The setup state pauses authoritative simulation through the existing client-to-simulation control boundary. It does not emulate pause by merely suppressing rendering or player input.

CI smoke mode bypasses interactive setup so automated validation remains bounded.

## Pause and Help

Space toggles player pause during an active match.

F12 toggles the help/onboarding surface. Help also pauses gameplay through the same simulation control transition so a fresh player can read controls without the match advancing in the background.

Window minimize remains an independent pause reason. The effective simulation pause state is the combination of:

- minimized or zero-size window;
- match setup;
- explicit player pause;
- open help/onboarding.

Restore does not incorrectly resume a match that is still explicitly paused or displaying help.

## Minimal Onboarding

The F12 surface documents the minimum complete vertical-slice workflow:

1. move the camera and select units;
2. move and scout;
3. construct power and industry;
4. process materials and produce units;
5. configure logistics and battlefield supply;
6. use tactical combat and artillery;
7. destroy the enemy Command Core.

The compact gameplay hint keeps F12, Space, and the current victory objective discoverable when onboarding is enabled.

## Settings

Client settings are stored in:

    %LOCALAPPDATA%\FORGELINE\settings.json

The current schema contains:

- window width and height;
- windowed or borderless-fullscreen startup;
- UI scale;
- onboarding visibility;
- edge-scrolling enablement;
- camera pan-speed multiplier;
- camera key bindings;
- drag-pan mouse binding.

Settings are typed and validated before window or camera creation.

Current supported ranges:

- window width: 1024–7680;
- window height: 720–4320;
- UI scale: 0.75–2.0;
- camera pan-speed multiplier: 0.5–2.5.

Primary camera actions require unique non-unknown keys. Invalid drag-pan bindings are rejected.

If the settings file is missing, validated defaults are created. If it is malformed, uses an unsupported schema, or contains invalid values, the invalid file is moved to a timestamped settings.json.invalid-* file and a fresh validated default file is written.

Camera rebinding is intentionally limited to the existing RtsCameraBindings surface. Selection, command-panel, and gameplay action rebinding are not falsely exposed before those systems have a complete conflict and discoverability model.

## UI Scale and DPI

The native client remains per-monitor-v2 DPI aware.

The pre-alpha UiScale setting scales the lightweight text overlay glyph size and line spacing while the RTS information layer continues to receive actual window DPI.

Critical states use text labels in addition to color, including match state, power constraint, command feedback, supply/readiness information, pause/setup/help, and terminal results.

The manual qualification matrix is documented in [Pre-Alpha Verification Checklist](PreAlphaVerificationChecklist.md).

## Failure Diagnostics

Unhandled client failures are written to a local diagnostic report when the process can still write to disk.

Default location:

    %LOCALAPPDATA%\FORGELINE\Diagnostics

The report contains:

- UTC timestamp;
- client assembly version;
- operating-system description;
- process architecture;
- .NET runtime description;
- exception details.

Failure-report creation is best-effort. A failure to write diagnostics must not replace or hide the original client failure.

Simulation-owner and render-owner failures continue to propagate to the platform owner before the outer process-level report is generated.

## Packaging

build/Publish-PreAlpha.ps1 creates the Windows x64 pre-alpha package.

Example:

    pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible

The publish path is:

    artifacts/prealpha/win-x64/publish

The distributable archive is:

    artifacts/prealpha/FORGELINE-prealpha-win-x64.zip

The package includes:

- the published Windows x64 client;
- compiled runtime assets;
- README;
- pre-alpha verification checklist;
- SHA-256 manifest.

-VerifyReproducible publishes twice, generates sorted per-file SHA-256 manifests, and fails when the two publish trees differ.

CI launches the packaged executable from inside the publish directory. This ensures runtime asset lookup resolves against packaged content instead of silently falling back to the repository copy.

## Qualification

Pre-alpha readiness requires both automated and manual validation.

Automated CI covers:

- restore;
- project-reference validation;
- Release build;
- canonical map qualification;
- runtime asset qualification;
- Windows client smoke;
- headless diagnostics/stress;
- save/load/replay recovery;
- complete tests;
- reproducible pre-alpha publish;
- packaged fresh-install smoke;
- canonical terminal-match validation.

Manual qualification is defined in [Pre-Alpha Verification Checklist](PreAlphaVerificationChecklist.md) and includes DPI/UI scaling, keyboard flow, setup/help/pause behavior, settings recovery, clean package launch, and one complete packaged match.
