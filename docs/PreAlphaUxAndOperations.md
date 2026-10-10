# Pre-Alpha UX and Operations

## Purpose

RTS shortcuts: Home focuses the current owned home building; double-tapping an assigned unmodified digit within 350 ms focuses its currently visible surviving group members. Double-left-click within 350 ms and the scaled movement threshold expands only owned visible units with the same stable type. Shift+click requests building continuation after authoritative acceptance; F9 rotates, and Escape cancels. Help lists these bindings. Brief focus/repeat/pending messages are nonblocking and never report a click as authoritative success.

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
- after the match starts, F1 (or F12) opens the controls reference.

The setup state pauses authoritative simulation through the existing client-to-simulation control boundary. It does not emulate pause by merely suppressing rendering or player input.

CI smoke mode bypasses interactive setup so automated validation remains bounded.

## Frontend and Session Transitions

The client presents explicit loading state during both application bootstrap and playable-session preparation.

Starting or restoring a match publishes real preparation stages through the frontend renderer and waits for the published loading frame to be presented before advancing to the next stage. This prevents fast session creation from visually skipping the loading surface while keeping progress tied to actual completed work.

Returning from an active match to the main menu tears down the current session and recreates the frontend window inside the existing process. Window recreation clears stale thread-level quit state from the previous Win32 window before normal event pumping resumes, so an intentional frontend restart is not mistaken for application shutdown.

## Pause and Help

Space toggles player pause during an active match.

F1 (or F12) toggles the controls reference. Help also pauses gameplay through the same simulation control transition so a fresh player can read controls without the match advancing in the background.

Window minimize remains an independent pause reason. The effective simulation pause state is the combination of:

- minimized or zero-size window;
- match setup;
- explicit player pause;
- open controls.

Restore does not incorrectly resume a match that is still explicitly paused or displaying help.

## Minimal Onboarding

The optional match guide shows the first unobserved objective, a current binding, a short explanation and the next goal when space permits. **Shift + F12** hides/shows it for the current session; unshifted F1/F12 still opens help. The existing **Onboarding** setting controls persistent default visibility, so there is no settings schema migration. Hiding the guide does not stop milestone observation or prevent RTS actions. Toggle frames consume held input and cancel in-progress pointer/placement/targeting gestures.

The canonical start already provides a Command Core, construction stock and a stocked supply provider. It therefore starts at **Establish Power**, without treating starting Steel as processed output. The advisory sequence covers Ferrous Ore extraction, Steel processing, a Vehicle Factory, fielding a Scout, available supply, permitted opponent contact, and the actual Command Core objective. Players may act in any order; already observed steps are skipped. No camera movement, resource grants, scripted build choices, tutorial timers or simulation achievements are introduced.

The compact card occupies the upper secondary information region; combat-group information uses the remainder. At high scale/small viewports the objective and binding take priority over context/next text. Alerts retain their own region. Visible guide/placement cards block world clicks, including press origins that subsequently move outside the card, but have no clickable commands. Guidance is suppressed during help/pause, focus loss, drag selection, camera drag, placement, targeting and terminal results. Placement feedback replaces the guide while building placement is active and remains available with Onboarding disabled.

Guide observations are session-local presentation state. Save/load reconstructs progress from extant authorized state and persisted production counters; presentation history for buildings/units already destroyed before restoration is not a durable achievement. Restart/session replacement clears observations and the temporary hidden state. See [milestone sources](MatchFlowAndPlayerExperience.md#early-game-guidance-observations).

The controls reference lists implemented camera, selection, movement, construction, production, logistics, supply and combat bindings, plus the Command Core objective. It uses active camera bindings and indicates whether edge pan is disabled. F1/F12, Escape or Back closes it without forwarding the closing gesture to gameplay. Shift + F1 toggles development metrics.

The minimum complete vertical-slice workflow is:

1. move the camera and select units;
2. move and scout;
3. construct power and industry;
4. process materials and produce units;
5. configure logistics and battlefield supply;
6. use tactical combat and artillery;
7. destroy the enemy Command Core.

The compact gameplay hint keeps F1, Space, and the current victory objective discoverable when onboarding is enabled.

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


## Operations onboarding and acceptance

OPERATIONS above the selection inspector opens the optional overview. Resource/power, alert and single-facility inspector clicks provide contextual entry without a new shortcut. Use Filter, Next Page, a facility row, Focus Facility/Focus Next or Open Existing Controls. Reported shortages explain observed facts; they do not invent economic causes. Net flow remains explicitly unavailable. Manual active-policy/route-focus journeys, terrain contrast, extreme-scale readability and monitor DPI/focus transitions remain acceptance items. See [operations limits](ProductionAndLogisticsOperations.md).
