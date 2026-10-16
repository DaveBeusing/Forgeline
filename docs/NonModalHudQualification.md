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

## Qualified source and executed results

Product source: `2edaa4cf3d02b0d4e21ebfcfab8b961ef863c068`.
[PR 218](https://github.com/DaveBeusing/Forgeline/pull/218) remains draft.
Subsequent documentation/evidence commits do not change the measured product or
consume another PATCH. Normal and packaged startup diagnostics independently
report 0.1.384-dev and this complete source SHA; each reaches ApplicationReadiness
and SessionReadiness Ready with fresh isolated settings. Personal settings are
untouched. Source-less package provenance reports a clean source tree.

- Restore and the 43-project reference graph pass. The final Release build has
  zero warnings/errors; one-PATCH validation passes.
- Initial related controller coverage passes 169 tests. Subsequent linear-source
  identity and selection-aware ownership controls pass 34 and 52 tests. The
  pre-commit omitted-footer and wheel-gap correction passes 57 relevant tests,
  including all 31 new nonmodal cases and warmed zero-allocation routing. That
  build is stamped 5d294fd8 with the uncommitted final bodies; the clean final
  build and broad project results below are stamped 2edaa4cf.
- At final source, the pre-asset standalone runs report Presentation 1,135 total /
  1,116 passed / 12 failed / seven skipped, and Client 308 total / 306 passed /
  two failed. They ran against the incomplete cache left by the interrupted
  rebuild. Failures identify absent runtime manifests, skin/font content and
  optional menu art. These failed setup attempts remain original records.
- After the complete canonical asset rebuild, the full solution reruns those
  projects: Presentation, Client and Input each report success. The complete
  solution reports 2,275 observed tests / 2,261 passed / five failed / nine skipped,
  then exits -1073741819 (0xC0000005). Full-suite completion/acceptance is not
  established. Five native Graphics failures concern scene-composition setup
  and four composition variants: GPU fence timeout followed by unresolved GPU
  shutdown. No graphics lifetime code is changed, and no cause is certified.
- Canonical map compilation passes 71 reachability checks and two alternative
  navigation/road links. All 1,337 runtime assets qualify (309 meshes, 900 textures,
  127 materials, one skin; 113,308,083 total bytes). Asset process contracts pass.
- Native normal-path smoke with 1,000 stress instances passes. Headless smoke
  (64 ticks / seed 12345 / 1,000 entities) and stress (16 / 67890 / 10,000) pass.
- Local interaction measurements pass all 36 cases with zero managed allocation
  and no dropped lines. Local full-HUD measurements pass all 120 warmed cases
  with zero managed allocation and recorded glyph/upload requests. This is CPU
  recording with null graphics, not GPU timing or a sustained-FPS qualification.
- The reproducible source-less package passes integrity, source exclusion, 844
  UI payload and four original PCM cue/provenance checks. Its fresh-settings
  native smoke passes. Archive: `FORGELINE-0.1.384-dev-win-x64.zip`, 40,007,143 bytes,
  SHA-256 `f168c71f6b3bc1a98015c0217a8ab0de68fed1f390a2b4d63df37c472a0348a5`.
- Natural terminal validation executes all 80,000 ticks for seed 2026, reports MatchStatus Active and exits 3 (319.259 seconds). It does not reach a terminal lifecycle. No forced outcome, resource grant or replacement gate is used; authoritative/headless source is unchanged.

## Hosted checks and acceptance boundary

At product source 2edaa4cf:

- [CI 38932223102](https://github.com/DaveBeusing/Forgeline/actions/runs/38932223102)
  passes steps 1–36, then fails Windows window-mode qualification: 99,608,050
  compiled gameplay texture bytes exceed the unchanged 56 MiB production budget.
  Later tests/package/match stages are skipped. This is the previously recorded
  catalog-budget failure; the new input work does not change assets or budgets.
- [Frame hot paths 38932223434](https://github.com/DaveBeusing/Forgeline/actions/runs/38932223434)
  passes the new/existing input and dock coverage, compilation and preceding
  allocation guards, then fails full-HUD allocation step 14 with 5,480 bytes for
  `(1024,720,96)/1/1/Pending`. Later measurements are skipped. The local 120-case
  result is retained separately and does not explain or erase the hosted failure.
  Its cause remains unqualified; no assertion, warmup, scheduling or budget is
  weakened and no passing local run overrides the failed hosted result.
- [Scalability 38932223205](https://github.com/DaveBeusing/Forgeline/actions/runs/38932223205)
  and [Movement quality 38932223084](https://github.com/DaveBeusing/Forgeline/actions/runs/38932223084)
  pass. Evidence-only heads may trigger new runs; none inherits a green required
  check from these product-source records.

The client routes the original retained input once and preserves existing command
validation. Targeting source identity is compared linearly over the ordered copied
selection, including entity generations, when current tactical facts are available.
Existing command validation remains authoritative. Selection revision changes
invalidate retained presses without a per-frame entity scan. The visible Operations
background intentionally owns input, while missing footer labels use the same
visibility predicate as rendering. No simulation/navigation/headless authority,
artwork, font, keybinding repertoire or general event system is introduced.

The local debug layer is unavailable. Physical monitor/input transitions and a
native manual sequence with Operations, dock, a notification and minimized
onboarding remain pending. Controller sequences and native startup do not certify
that physical journey, accessibility, motion or golden approval. Required check,
full-suite, performance and human acceptance remain open. No merge, release, tag,
repository-protection or automatic visual approval is performed.

## Retained attempts and evidence

- At 7596cb91, the initial broad attempt was deliberately interrupted during
  Presentation tests before a complete suite or native/asset/package gate.
  A nested targeting identity comparison was replaced with a linear comparison.
- At 5d294fd8, standalone Presentation passes 1,126 cases with seven skips;
  Client passes 308 and Input passes 22. Build/version/map pass. Its asset rebuild
  was interrupted to fix omitted footer controls. Its original compiler progress
  is preserved separately. Neither interrupted attempt qualifies the final edit.
- Initial compiler errors, including analyzer, call syntax and unavailable internal
  test access failures, remain distinct in the original build logs. They were
  corrected before implementation commits and are not passing evidence.
- Final pre-asset failures are followed by the valid asset rebuild and the broad
  rerun above; they are not silently overwritten with successful results.

Original evidence roots are `artifacts/nonmodal-final-*`, `nonmodal-verified-*`,
`nonmodal-qualified-*` and `nonmodal-package`. [Original records](evidence/nonmodal-hud/records.zip)
retain failed/passing logs, native diagnostics, asset qualification/progress,
benchmark records, hosted logs and package provenance. [Checksums and identity](evidence/nonmodal-hud/checksums.json)
bind the archive and underlying source/binary/package records. Full product ZIPs,
user settings and private credentials are not included in repository evidence.

Resume by comparing master, this branch, PR 218 and their actual diffs. Product
implementation is complete at the source above; integration/physical acceptance
is pending. Resolve the full-suite Graphics failure/exit and hosted full-HUD
allocation cause in their owning domains; retain the catalog-budget and natural
match result independently. Register later ring menus with ITransientHudMenu and
require a fresh press for newly opened controls. Do not stack a competing input
implementation, increment PATCH again, relax gates or merge automatically.
