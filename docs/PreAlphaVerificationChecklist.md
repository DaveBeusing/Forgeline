# Pre-Alpha Verification Checklist

## Purpose

Use this checklist to qualify a FORGELINE Windows x64 pre-alpha package before it is shared outside the development environment.

The packaged build is the qualification target. Do not substitute a repository-local `dotnet run` result for package validation.

## Build and Package

- [ ] Restore succeeds with the SDK pinned by `global.json`.
- [ ] Project-reference validation passes.
- [ ] Release build succeeds without repository-caused warnings or errors.
- [ ] Canonical map compilation and qualification pass.
- [ ] Runtime asset compilation and qualification pass.
- [ ] `build/Publish-PreAlpha.ps1 -VerifyReproducible` succeeds.
- [ ] Two independent publish passes produce identical SHA-256 manifests.
- [ ] `FORGELINE-prealpha-win-x64.zip` is produced.
- [ ] The archive contains the client executable, runtime assets, README, this checklist, and `manifest.sha256`.

## Fresh-Install Launch

- [ ] Extract or copy the publish output to a clean directory outside the repository.
- [ ] Start `ForgeLine.Client.exe` from that directory.
- [ ] The application creates its settings file under the current user's Local Application Data folder.
- [ ] Missing settings do not block startup.
- [ ] Malformed settings are quarantined and replaced by validated defaults.
- [ ] The match setup screen is shown before normal gameplay begins.
- [ ] No repository-relative source asset is required after runtime assets have been packaged.

## Match Setup and Lifecycle

- [ ] Setup identifies Central Divide as the current map.
- [ ] Setup identifies Directorate as the local faction.
- [ ] Setup identifies the Directorate computer opponent.
- [ ] Enter starts the match.
- [ ] Starting or restoring a match visibly presents the loading surface before gameplay appears.
- [ ] Escape exits from setup.
- [ ] Space pauses and resumes an active match.
- [ ] Return to Menu tears down the active session and restores a responsive main menu without terminating or faulting the client.
- [ ] A second match can be started after returning to the main menu.
- [ ] Minimize pauses simulation without busy-spinning.
- [ ] Restore resumes according to the explicit pause state.
- [ ] A terminal result freezes normal gameplay ticks.
- [ ] R starts a fresh session after a terminal result.
- [ ] Escape acknowledges and exits a terminal session without an additional gameplay tick.

## Input, Camera, and Help

- [ ] F1 opens readable controls on its first press; F1/F12 toggle, Escape and Back close. Holding or repeating the help key does not reopen it.
- [ ] Escape closes help without opening pause; help pauses gameplay and preserves the current selection.
- [ ] While help is visible, camera, selection, building placement, orders, formation and overlay shortcuts do not act on the world. Closing help with held movement keys or mouse buttons requires release before a new action.
- [ ] A/Left and the left edge move the view left, D/Right and the right edge move right, W/Up and S/Down stay correctly oriented, before and after camera rotation.
- [ ] Repeat pan checks at 60 and 144 Hz, resized/windowed/borderless modes and 100/150/200 percent DPI. All ten controls rows and Back remain visible at each configured UI scale.
- [ ] Leaving the client viewport stops edge pan; re-entering does not jump a drag. Wheel-up zooms in and wheel-down zooms out; neither crosses configured zoom limits or leaks through a captured HUD surface.
- [ ] Open help during left selection drag, minimap drag, middle camera drag and building placement; close after releasing outside the window or alt-tabbing. No gesture resumes and no unintended order is submitted.
- [ ] Shift + F1 toggles metrics during gameplay. F1 opens help even with onboarding disabled.
- [ ] Camera pan bindings match the persisted settings.
- [ ] Alternate camera bindings remain functional.
- [ ] Rotation and pitch bindings match the persisted settings.
- [ ] Middle-mouse drag pan and mouse-wheel zoom remain functional.
- [ ] Edge scrolling follows the persisted setting.
- [ ] Camera pan speed follows the validated multiplier.
- [ ] Invalid or conflicting primary camera bindings are rejected during settings load and recover to defaults.
- [ ] Focus loss clears held input and does not leave camera movement stuck.

## Core RTS Interaction

- [ ] Left click selects an owned visible entity.
- [ ] Shift + left click changes multi-selection.
- [ ] Left-drag performs box selection.
- [ ] Right click submits movement through the simulation command path.
- [ ] B exposes construction.
- [ ] P exposes industrial processing.
- [ ] U exposes unit production.
- [ ] L exposes logistics controls.
- [ ] Y exposes battlefield supply controls.
- [ ] K exposes tactical combat controls.
- [ ] F10 cycles strategic overlays.
- [ ] F11 toggles the minimap.
- [ ] Command acceptance and rejection remain visible to the player.

## Minimal Onboarding

- [ ] Help explains camera movement, zoom, rotation, and pitch.
- [ ] Help explains selection and movement.
- [ ] Help directs the player to construction.
- [ ] Help directs the player to processing and unit production.
- [ ] Help directs the player to logistics and battlefield supply.
- [ ] Help directs the player to tactical combat.
- [ ] Help states the current victory objective: destroy the enemy Command Core.
- [ ] A new player can reach the complete Build-Supply-Conquer loop without external developer instructions.

## Readability and Accessibility

- [ ] Player-critical state is expressed with text or symbols and does not rely on color alone.
- [ ] Setup, pause, help, alerts, supply state, and terminal result remain understandable in grayscale.
- [ ] Overlay text remains readable at UI scale 0.75, 1.0, 1.25, 1.5, and 2.0.
- [ ] The client remains usable at 100%, 125%, 150%, and 200% Windows DPI scaling.
- [ ] Window resize does not clip the essential setup, pause, or help affordances.
- [ ] Keyboard-only access is available for setup start/exit, pause/resume, and help.

## Failure and Recovery

- [ ] Invalid settings recover to defaults and preserve the invalid file for diagnosis.
- [ ] Client-level unhandled failures create a diagnostic report under Local Application Data.
- [ ] The failure report includes timestamp, build version, runtime/OS information, and exception details.
- [ ] Simulation and render owner failures still propagate to the platform owner.
- [ ] Save/load/replay recovery smoke validation passes.

## Automated Validation

- [ ] Client settings validation/recovery tests pass.
- [ ] Existing input tests pass.
- [ ] Existing camera tests pass.
- [ ] Existing lifecycle and client execution-owner tests pass.
- [ ] Existing DPI/window tests pass.
- [ ] Complete solution tests pass.
- [ ] Packaged client smoke test passes from the publish directory.
- [ ] Headless diagnostics and 10,000-entity smoke tests pass.
- [ ] Canonical 80,000-tick terminal-match validation passes.

## Full Match Acceptance

- [ ] Launch from a clean published package.
- [ ] Start the match from setup.
- [ ] Complete camera/selection/movement interaction.
- [ ] Construct and power additional industry.
- [ ] Process physical resources.
- [ ] Produce combat and logistics units.
- [ ] Maintain battlefield supply.
- [ ] Scout and engage the opponent.
- [ ] Destroy the opposing Command Core through normal combat.
- [ ] Confirm that the terminal result is clear.
- [ ] Restart once and verify that no prior session state survives.
- [ ] Exit cleanly.
