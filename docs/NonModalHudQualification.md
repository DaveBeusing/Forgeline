# Nonmodal HUD qualification

## Implementation checkpoint

Base master: `874b1652bc272ef648120a0b1de9bc85edf5f70c`.
Branch: `ui/nonmodal-world-interaction`. Version: 0.1.384-dev, one PATCH from 0.1.383.
Remote branches and recent open/closed PRs were checked before implementation;
PR 217 is integrated and supplies layout contract version 2. No competing input
implementation or uncommitted work was present. Planning reference commits are
not used as the implementation base.

The central presentation coordinator owns transient menu dismissal and gesture
ownership, using the existing InputState press sequence/origin. Its retained
record includes desktop pointer 0, button, Shift/Control at press, layout revision,
session and selection revision. Selection revisions avoid a per-frame entity
scan or hash collision. It never mutates simulation or dispatches commands.

## Event contract

| Gesture | Menu result | Existing world owner |
| --- | --- | --- |
| Left press on free world | Close all transient menus | Selection/deselection; drag starts immediately |
| Right press on free world | Close all transient menus | One existing move/context request |
| Left press with placement/target intent | Close menus, retain intent | One validated placement/target request; invalid targets retain retry |
| Right press with placement/target intent | Close menus | Cancel only; no move |
| Enabled menu control | Keep its existing activation/navigation | No world request |
| Disabled control | Retain existing reason | No request behind it |
| Menu down, world release | Cancel retained press/capture | No release-generated world action |
| Select another world object | Close old context | Existing selection; new controls need a new press |
| Escape | Close the entire transient stack first | Target cancellation/Pause only on subsequent eligible events |
| True modal/settings/confirmation | Existing modal owner | No click-through |

The visible Operations background deliberately owns input. Dock background,
empty card capacity and gaps do not; actual rendered controls including disabled
ones own input. Persistent HUD owners, onboarding and alerts are not dismissed.
A logically closed context menu cannot own input during a closing animation.
Later ring menus use ITransientHudMenu rather than adding outside-click handlers.

## Executed checkpoint

- Restore and 43-project reference graph pass.
- Release solution build: zero warnings/errors (`artifacts/nonmodal-ready-build.log`).
- Focused sequence and related existing controller tests: 169 passed, zero failed
  or skipped (`artifacts/nonmodal-ready-tests.log`). This includes 28 new sequence,
  lifecycle, selection revision and warmed zero-allocation cases.
- Initial implementation/compiler failures and earlier passing attempts remain
  distinct in `artifacts/nonmodal-initial-build.log`, `nonmodal-build-v2/v3/v4/v5.log`,
  `nonmodal-focused-tests.log` and `nonmodal-focused-v2-tests.log`.

The first broad verification attempt at 7596cb91 was deliberately interrupted
during Presentation tests before completing any suite or native/asset/package
gate. Target-source identity validation was changed from nested comparisons to
a linear comparison of the existing ordered copied selection. The interrupted
logs under `nonmodal-final-*` are not passing evidence. Final verification uses
a separate `nonmodal-verified-*` prefix.

The subsequent focused linear-identity control passes 34 tests; shared
selection-aware card ownership adds a stale-selection regression and passes 52
related tests (`nonmodal-linear-tests.log`, `nonmodal-ownership-tests.log`).
There are now 29 new nonmodal sequence/guard cases. These runs do not replace the
full affected suites or the required hosted check.

At 5d294fd8, complete Presentation (1,133 total / 1,126 passed / seven skipped),
Client (308 passed), Input (22 passed), build/version and map pass. Its canonical
asset rebuild was interrupted to correct footer slots omitted by the renderer
but still present in hit tests. Those `nonmodal-verified-*` suite results retain
their identity; they do not qualify the subsequent correction. Final validation
uses `nonmodal-qualified-*` and includes the corrected shared footer visibility.

The footer/gap correction also routes wheel input through the same actual
control predicate, preserving world zoom without paging over decorative gaps.
It is covered by dedicated omitted-footer and wheel-routing regression cases.

## Remaining verification

Complete affected suites, full solution, canonical assets/map, native normal and
packaged startup, reproducible package, warm interaction measurements, natural
terminal match and hosted checks are pending at this checkpoint. Physical native
interaction with Operations, dock, a notification and minimized onboarding is a
human integration check; automated controller sequences do not certify that journey.
Earlier integrated HUD qualification retains its CI texture-budget, complete-suite
allocation and natural-match failures. These must not be relabelled as passes or
attributed to this change without fresh evidence. No budgets, assertions, timeouts,
modal guards or golden decisions are weakened. No merge/tag/release is authorized.
