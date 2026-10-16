# Notification stack qualification

## Scope and identity

Version 0.1.385 on `ui/action-bar-notifications`, based on master
`03b0c668c0d84eeb347bda9aec2e0f3f71e42e23`. Both right-column layout and
nonmodal gesture prerequisites are integrated. Exactly one PATCH increment.
Source SHA, PR, native evidence and remaining gates are recorded below as checks
complete. This document does not grant merge, release or visual acceptance.

## Contract

`GameplayHudLayout` and `ActionableAlertLayout` publish contract version 3.
`NotificationBounds(expanded)` starts at visible dock bottom plus `Gap`, shares
the dock's width and safe right edge, and ends above the minimap. Its maximum
height is 224 times the actual dock content scale with a minimum of 1. Expanded dock capacity
and global input sequencing are unchanged. `AlertStack` identifies the collapsed
reservation; consumers with current dock state use `NotificationBounds`.

One value-type list contains at most three regular cards plus a compact MORE row.
Text uses the existing font metrics, a 14-physical-pixel floor and the actual dock content scale, at most
two lines, and ellipsis. Overflow reduces card count instead of text size.
Unshown complete content is accessible through Operations → Notifications.
The detail owner retains at most six lines, with a 14-pixel floor and the actual dock content scale and
fewer visible lines when the available panel height is constrained, with cached source text from the existing
five authorized conditions and existing command/local results. It pages all text,
including strings longer than the common typography helper's 256-line limit.

Condition priority, five-record extraction cap and 80-tick command-result lifetime
are preserved. Accepted/partial/rejected remain command results, not work
completion. No ECS reads or commands, event database, sound, animation or new
textures are added. Current snapshot semantics remove resolved conditions.

Rendering, pointer ownership and the nonmodal coordinator resolve the same card
list. Gaps and unused reservation are world surface. Down/up requires the same
stack revision, copied lifecycle identity, target generation and action region;
expiry, reflow, session/layout changes and older session/tick snapshots cannot
transfer gestures. FOCUS uses existing authorized navigation; unavailable targets
use OPS. MORE opens current details. Operations does not hide the right stack.

## Verification ledger

- Initial compilation found a missing font-metric argument; repaired.
- Detail-cache refactoring found a local-name collision; repaired.
- Initial related geometry/input regression run: 165 passed.
- Updated focused run: 152 passed, including the new notification cases.
- The notification matrix checks 90 display/DPI/scale/dock combinations.
- First full run: Presentation 1067 passed / 69 failed / 7 skipped; Client
  306 passed / 2 failed. A default null local hint caused 66 Presentation and
  both Client failures; two assertions still expected contract version 2. Those
  issues are repaired and covered. One unrelated building-state allocation check
  reported 1984 bytes; it is retained in the initial log, not suppressed.
- Second full run: Presentation 1137 passed / 0 failed / 7 opt-in skipped;
  Client 308 passed / 0 failed. No structural command-dock fixture was changed.
- Complete suite, source-stamped native repeat captures, runtime assets, package,
  headless and hosted checks: pending final execution.
- Compact detail geometry and Operations regression run: 100 passed.
- Human typography/contrast, physical monitor/DPI, accessibility and live
  alert-to-world journeys remain pending. New captures are candidates, not goldens.

Earlier left-position screenshots remain historical evidence. See
[responsive qualification](ResponsiveHudQualification.md),
[nonmodal qualification](NonModalHudQualification.md) and
[prior graphical qualification](GraphicalInterfaceQualification.md) for baseline
failures and earlier artifact identities. Do not substitute their results for
this version's checks.

## Native scaling correction

The first 15 source-stamped images at `5089a3682975259be1792dc3cd72eacf610c2e71`
passed structural/repeated-pixel checks. Review found that the provisional
1.5 text-scale cap made notification text disproportionately small beside the
4K large-scale action dock. That cap is removed: both notification and detail
text now follow the existing actual dock content scale, with a 14-pixel floor.
The display matrix requires that exact relationship. New captures must use the
corrected source; the earlier images are retained as prior candidates.
