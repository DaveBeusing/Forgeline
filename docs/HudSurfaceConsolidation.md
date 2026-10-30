# HUD surface consolidation

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Integration baseline](#integration-baseline)
- [Functional parity contract](#functional-parity-contract)
- [Early checkpoint (historical)](#early-checkpoint-historical)
- [Implemented composition and checks](#implemented-composition-and-checks)
- [Integrated qualification, 19 October 2026](#integrated-qualification-19-october-2026)
  - [Native method and coverage](#native-method-and-coverage)
  - [Inspection and corrections](#inspection-and-corrections)
  - [Reproduction and remaining acceptance](#reproduction-and-remaining-acceptance)
  - [CI viewport regression](#ci-viewport-regression)

</details>

## Integration baseline

Initial source baseline: `master` `d4c7b62077c98500d6309cdd6c6dbf5e6bd6490c` (0.1.406).
During implementation, `master` advanced through PR #241 to
`09b5f1ae51164dadeb56d2d37249c114f509a5dc` (0.1.407). That history is merged;
this PR increments PATCH once to 0.1.408. Branch: `ui/hud-surface-consolidation`.
[Pull request #242](https://github.com/DaveBeusing/Forgeline/pull/242).
The player SelectionPanel maps to `SelectionInspectorHudSurface`; ActionBar maps to
its contextual command band, seven mode buttons and rectangular action dock in
`PlayerActionDockHudSurface`. The existing single/group ring shares that renderer
and must survive removal. F2 and Shift+F1 remain diagnostic paths.

## Functional parity contract

| Function | Previous entry | Remaining entry | Information and input |
| --- | --- | --- | --- |
| Build | Command band, B, dock | Object ring category, B | Existing catalog tooltip, mouse sector or Enter, existing placement controller |
| Processing / units | P/U, mode bar, dock | Object ring, P/U; Operations controls | Recipe costs, progress, block reasons and requests in existing action tooltips; parameters and cancellation in ring |
| Logistics / loading / supply | L/Y, dock | Object ring, L/Y; Operations controls | Existing copied queue/stock/policy models and tooltips; existing +/- and parameter shortcuts |
| Tactical / artillery | K, command band, dock | Single/group ring, K | Existing stance/order sectors and parameters; existing targeting owner |
| Technology | H, global mode bar | Object ring or H without selection | Existing global technology catalog; ring uses current player/session snapshot |
| Group filtering / attention / formation / focus | Inspector controls | Existing group ring | Existing full entity identities and selection adapter, no new simulation commands |
| Single identity / health / supply / readiness / power / work / inventory | Inspector | Existing tooltip beside open ring | Read-only copied selection details, including keyboard invocation |
| Group composition / coverage / damage / supply / formation | Inspector | Existing tooltip beside group ring | Cached selected-group aggregate; unknown component coverage remains explicit |
| Accepted / partial / rejected counts | Inspector and dock | Ring status tooltip and existing notification feedback | Authoritative result; no optimistic execution claims |
| Facility navigation | Operations entry / inspector shortcut | Operations entry and existing keyboard binding | Operations owns its own panel and hit geometry |
| Guidance / notifications / minimap | Shared layout | Reflowed shared layout | Independent existing owners; no removed panel captures |

The ring center, gaps, absent sectors and former rectangular panel locations pass
world input through. Transient menu dismissal retains the original gesture once.
Modal, focus, session, selection revision, full entity generation and displayed
ring revision checks remain required. No new bindings or gameplay requests.

## Early checkpoint (historical)

Selection-status tooltip: Release presentation project build succeeded without
warnings; 29 hover-tooltip tests passed, including safe-area display profiles,
keyboard status, stale identity rejection and zero warm managed allocations.
These results qualify the first status-access change only. Later integrated
qualification is recorded below; this checkpoint is not a current full-suite result.

## Implemented composition and checks

`PlayerActionRingHudSurface` retains the original ring renderer, bounded to 4,096
geometry vertices and eight icon quads. Its unused core-panel/portrait batches
are removed. The inspector renderer and old rectangle geometry/capture paths are
deleted. `HudOverlayPrimitives` retains the shader/glyph helpers still used by
Operations/tooltips; semantic labels and request/read-model authorities survive.
`GameplayHudLayout` version 4 owns only remaining persistent bounds. Resource,
notification, minimap, guidance and Operations consumers use that contract.

Status resolution requires a real owned/group/global ring context, current tick,
menu/selection revisions and full identity. Group coverage comes from the existing
retained selected-group aggregate. `SelectionStatusPrivacyTests` moves the old
native hidden-field privacy regression to the real tooltip. Deleted rectangle
geometry tests are replaced by remaining-owner safe-area/separation and actual
ring sector/absent-sector/closed-render checks. Existing object/group ring suites
retain all seven request kinds, parameters, cancellation, ownership, stale
identities, disabled controls and exactly-once release. The global technology
shortcut now has a direct empty-selection dispatch regression.

Checkpoint results (before final capture build): 372 focused presentation cases,
65 client ring/journey/reference cases and one native WARP status privacy case
passed. A full Release solution build has zero warnings/errors; the 46-project
reference graph is valid. The explicit native matrix contract covers 90 states
and 45 display/DPI/scale combinations (4,050 planned cases); this synthetic
coverage check does not claim those captures were executed. CPU null-backend
measurements pass 24 group cases, 120 full HUD cases and 54 active hover cases
with their unchanged allocation/geometry limits. Native timing, framebuffer
inspection, broad tests and final CI are recorded separately after completion.

The first PR CI build exposed a retained benchmark call to the deleted inspector
pointer adapter (`CombatGroupCardController.Update`, CS1061). The benchmark now
records the actual retained ring status and measures summary rebuild separately.
No budget or assertion was weakened. Failed local/CI evidence remains historical.

## Integrated qualification, 19 October 2026

Native source: `11f878e14beccc573ecc8ca3bd9248c86681b796`, build
`0.1.408-dev+11f878e14beccc573ecc8ca3bd9248c86681b796`.
The final production rendering/input change is `4daf3ad6e304d19ff4ad6896bc28744dd5eed819`;
the subsequent commit only writes absent ring geometry as JSON null. Master was
rechecked at `09b5f1ae51164dadeb56d2d37249c114f509a5dc`. No tag or release was created.

| Check | Executed result | Local evidence under `artifacts/hud-surface-consolidation/` |
| --- | --- | --- |
| Release solution build | Zero warnings/errors | `nonoverlap-build2.txt` |
| Project reference graph | 46 projects, no cycles or boundary violations | `Validate-ProjectReferences.ps1` |
| Version contract | 0.1.408, one PATCH above current master 0.1.407 | `Validate-SemVer.ps1 -BaseRef origin/master` |
| Full Presentation project | 1,293 passed, 12 skipped, 0 failed (1,305 total) | `presentation-full-tests4.txt` |
| Full Client project | 429 passed, 2 skipped, 0 failed (431 total) | `client-full-tests3.txt` |
| Native resource regression | 6 passed after correcting obsolete expectations downward | `native-resource-tests.txt` |
| Native ring alpha regression | 12 passed, including real full/half/zero-opacity pixels | `ring-alpha-tests.txt` |
| Group / complete HUD / active hover hotpaths | 24 / 120 / 54 cases passed; warm HUD/input allocation remains zero | `combat-group-final-hotpaths.json`, `gameplay-ui-final-hotpaths.json`, `hover-final-hotpaths.json` |
| Native display matrix | 121 structural passes, zero failed captures | `native-final-11f878e/` |
| Native motion and lifetime | 336 transition samples, 128 context cycles passed | `native-final-11f878e/motion-and-resources/qualification.json` |

The full local solution run before the final fixes recorded 2,801 passed, 17 skipped
and one failure out of 2,819. That failure was the obsolete 16 MiB native HUD skin
expectation after removing the portrait page. The corrected native contracts and
both affected complete test projects pass above. The failed run remains in
`full-tests.txt`; it is not reported as a passing full-solution run. PR CI checks
exercise the broader solution, packages, runtime assets, terminal match and native
contracts; their per-head result is available on [PR #242](https://github.com/DaveBeusing/Forgeline/pull/242/checks).

### Native method and coverage

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver
32.0.15.9653, .NET 10.0.12, Release x64, real D3D12 swap-chain backbuffer readback
before Present. Physical host-window DPI was 144; raster DPI 96/144/192 was injected.
The debug layer was unavailable. Runtime fingerprint:
`DDA6CF3F2A2DEB662A3190637A15D800F5D6B8D7DD8EAE66CEC7D3061EEBCE99`.
UI fixture version 3, shared layout contract 4; world fixture version 7 is unchanged.
Each case used three warmups, eight measured samples and two exact readbacks.

The compact96, standard144, compact192 and ultrawide192 cohorts each execute nine
states at UI scales .75, 1 and 2: construction ring, single status, mixed group
status, group tools, assigned-group overview, onboarding, expanded notifications,
Operations and long selection name. Dimensions are respectively 1280x720,
1920x1080, 1280x720 and 3440x1440. The additional commands96 cohort executes all
seven command categories, parameters, action tooltip, edge placement, partial
group feedback and opening/closing midpoint frames at 1920x1080, DPI 96, scale 1.
This is 121 executed cases; the separate 4,050-case synthetic contract is planned
coverage, not 4,050 executed native images.

Evidence root: `artifacts/hud-surface-consolidation/native-final-11f878e/`.
The manifest records each image hash, exact source, runtime, adapter, capture and
published HUD geometry. SHA-256 checksums of the immutable result manifests:

| Manifest | Captures | SHA-256 |
| --- | --- | --- |
| `compact96/manifest.json` | 27 | `457a2162a7a1673024b78a3139d24d2b97e2371c788a9c5b55535be2fc861ba5` |
| `standard144/manifest.json` | 27 | `fcd4f5915c18b68fa2c791fb24b17bf1b783e4ba743cb4adb48b36ca930039d2` |
| `compact192/manifest.json` | 27 | `0576809a1bdbcf734857d90ce30b0282c24cea4e3061bd25df98c0d50b905695` |
| `ultrawide192/manifest.json` | 27 | `8f445fb928f01bbe00b5faafe414db389c72905cab8306058232edbd15607444` |
| `commands96/manifest.json` | 13 | `5fb0b16a1c5599adbab541e43f7e50333b2352b0d624b330e7f43a8b0df51414` |

Motion/lifetime JSON SHA-256: `c2089613aadb3d8b22e57d05e5213722cc258429ea4234383e69264d03f85435`.

Native single/group ring motion was exercised at injected 30/60/144 Hz, normal
and Reduced Motion, opening and closing. Normal motion contains intermediate
alpha; Reduced Motion settles immediately. Six current-state comparisons cover
single/group closed, ring and ring-with-status on the same device (32 warmups,
128 samples each). These measure the current retained HUD, with the world excluded;
CPU includes waits/Present. They are not a measured old-dock comparison, isolated
HUD GPU timing, live gameplay or sustained-FPS acceptance.

Live resources stayed at 81, loaded textures/descriptors at 4, skin buffers at 32
and skin batches at 8 across 128 context cycles. Missing assets, dropped quads,
texture-binding failures and submission faults are zero. Skin resident pages are
12,582,912 bytes (12 MiB), including zero portrait bytes. Total texture residency
including the font is 20,971,520 bytes (20 MiB). Closed states emit no replacement
ring/inspector geometry. These categories must not be conflated.

### Inspection and corrections

Representative final PNGs were inspected directly: compact96 single status at .75,
expanded notifications and Operations at 2; standard144 mixed-group status at .75
and group tools at 2; compact192 long name/assigned-group overview at 2 and
onboarding at .75; ultrawide192 single status at 2 and group status at .75;
commands96 logistics, supply and technology. The panels are absent; status remains
clear of present ring sectors, notification, minimap, guidance and Operations bounds.
Ellipsis/wrapping uses existing bounded text behavior. This inspection is distinct
from human art, readability, accessibility and physical-monitor acceptance.

Earlier native compact previews at `615f633` are retained in
`native-compact-preview/` and were rejected for status overlap with the ring/guide.
`4daf3ad` fixes placement using actual painted sectors, populated notification
height and assigned-group bounds. Its four corrected compact previews remain in
`native-fixed-preview/`. The first extended `4daf3ad` run failed during JSON export
because absent geometry exposed an infinite angular step. Its partial output remains
in `native-final-4daf3ad/`; `11f878e` exports absent geometry as null and the complete
new run above passes. No failed evidence was relabeled or overwritten.

The local pending-press regression first failed (`local-pending-before.txt`): a
press while locally pending could arm a control and activate after the next snapshot.
`d241e5f` checks the existing availability guard before arming; the reproducer and
fresh-gesture regression pass. `615f633` enables existing overlay alpha compositing;
native pixel tests verify actual fading. Neither change adds a new request path.

### Reproduction and remaining acceptance

Use the existing `build/Invoke-RtsReferenceQualification.ps1 -Capture` entry point
with a clean checkout and an isolated Release client built from that same commit.
For each display/DPI cohort above pass `-Scenes BattlefieldCombat -Profiles Medium
-Zooms NormalGameplay -Displays <size> -RasterDpi <dpi> -UiScales .75,1,2`, the nine
listed `-UiScenarios`, `-Warmups 3 -Samples 8 -RepeatCaptures 2`, `-ClientPath` and a
fresh `-OutputRoot`. For commands96 use scale 1 and the command/midpoint states above.
Then run `ForgeLine.Client.exe --action-ring-capture <fresh-output> <absolute-runtime-assets>`.
The capture entry point validates source/build/runtime identity; never reuse an old
binary as evidence for a later source commit.

Next required acceptance step is a real interactive playthrough and visual/input
review in windowed and borderless mode, including held inputs, drag selection,
modal/Escape/targeting, alt-tab, physical monitor/DPI moves, remapped keyboard,
bright/dark terrain and sustained target-refresh load. Physical DPI 96/192 monitor
moves, human motion/readability/accessibility approval and that live playthrough
have not been performed. The PR remains draft for these explicit acceptance gates;
no merge/release approval is implied by the automated/native results.

### CI viewport regression

[CI run 39300039286](https://github.com/DaveBeusing/Forgeline/actions/runs/39300039286)
on `75b3d526ac551b04d5ad39c660cb3e619d16a5a9` passed asset compilation and the atlas
contracts, then reported 26 passes and one failure in the native HUD skin step.
The new opacity test hardcoded a 1600-pixel row stride and a 1600x900 layout;
the runner's actual window/backbuffer was 1028x749. It sampled a different pixel.
The same assertion failure is reproduced locally after settling an explicit
1028x749 resize (`ci-opacity-repro2.txt`); the original 1600x900 case passes.
The first local resize attempt, before settling the surface, is also retained in
`ci-opacity-repro.txt` and is not treated as the pixel-assertion reproducer.

The native test now creates layout, ring center and sample coordinates from the
actual capture context, uses the actual row stride, and checks framebuffer length
and pixel index. Both 1600x900 and 1028x749 are explicit cases. Full/half/zero alpha
ordering remains strict in all RGB channels; no assertion, native gate or budget
was weakened. All three native opacity/privacy cases pass (`ci-opacity-fixed.txt`).
This is a test-coordinate correction; production rendering and the 121 source-pinned
native captures above are unchanged. Final per-head CI is tracked on the PR.

The complete Presentation project was rerun after the viewport correction:
1,293 passed, 12 skipped, zero failed out of 1,305 (`presentation-full-tests4.txt`).
