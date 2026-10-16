# Responsive HUD qualification

## Identity and scope

Base master: `2d698c839c0b3427579fd309b72e0befa55d0f4d`.
Branch: `ui/right-aligned-hud`. Version: 0.1.383, one PATCH from 0.1.382.
Implementation and evidence commits are recorded by Git history and the pull
request. No merge, tag, release or human golden approval is performed here.

The version-2 layout owns right-aligned resources/dock capacity, visible collapsed
bounds, top-left guidance and the reserved notification target below the visible
dock. Notification rendering remains on its current owner/stack pending migration.
Primary commands, minimap, copied facts and command authority remain independent.

## Initial executed checks

- Baseline native source: `2d698c839c0b3427579fd309b72e0befa55d0f4d`.
  Release client build: zero warnings/errors. Two 1920x1080, DPI-96, scale-1
  BattlefieldCombat/Medium captures (ResourcePressure, DockFocus), two repeated
  readbacks each; structural comparison passes. Evidence:
  `artifacts/right-hud-before`, log `artifacts/right-hud-before.log`.
- First changed full Presentation run: 1,083 total, 1,068 passed, eight failed,
  seven skipped (`artifacts/right-hud-initial-tests.log`). Four old fixed-height
  expectations and the general center-alignment regression were addressed;
  ink-aware centering is restricted to the new helper. The queue test now covers
  compact and expanded native output and progress is retained in compact cards.
  The unchanged supply-depot allocation test reported 1,984 bytes. That failure
  remains recorded; a later pass cannot erase it.
- Subsequent focused run: 146 passed, zero failed/skipped, Release build zero
  warnings/errors (`artifacts/right-hud-focused-build.log`,
  `artifacts/right-hud-focused-tests.log`). Coverage includes all 45 display/DPI/
  scale combinations, collapsed versus expanded reservations, shared input bounds,
  unusable viewports, guidance, every resource and power, and font-metric centers.
  Additional client gesture and header changes require the next validation run.

## Validation remaining at implementation checkpoint

Full updated solution build, client gesture regression, affected project suites,
canonical assets/process/package/headless/terminal gates, current-head CI and
bounded after-captures are pending. Native captures are candidates for review,
not approved goldens. Half-resolution world output must retain full-resolution
text. Warm allocation and before/after submission costs must be recorded without
asserting a sustained FPS improvement.

Historical broad visual evidence in GraphicalInterfaceQualification.md describes
its original source and is not acceptance of this layout. Required checks, native
readbacks, resource budgets and physical/human review remain separate. Existing
texture-budget and allocation failures are not waived by this revision.

## Structural fixture migration

The first committed source `eb27dc06a873ad9d18cb14db551a208f3617d12c`
passes all 308 Client tests. Its full Presentation run has 1,096 total,
1,088 passed, one failed and seven skipped: the old command-dock structure hash
still represented the superseded layout. Original log:
`artifacts/right-hud-presentation-tests.log`. All 135 existing bootstrap fixture
keys are retained; regenerated structural hashes describe the new layout and
are not raster goldens or human visual acceptance. Card labels now share the
metric centering helper. A closed dock always draws its published status strip,
including the no-target context prompt, so input does not reserve invisible
full-card height. The updated fixture and native evidence need validation below.
