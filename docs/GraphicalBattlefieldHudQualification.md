# Graphical battlefield HUD qualification

## Source and review state

Base master: `476f3646e16dd47eb40fbf7319f3f56349bf1eca` (0.1.376-dev).
Production, native captures and package source:
`7e271dbdd9ad2c59810118a6d774398655ff7505`.
Branch `ui/graphical-battlefield-hud`, [PR #211](https://github.com/DaveBeusing/Forgeline/pull/211),
one PATCH to 0.1.377-dev. Later evidence/documentation commits do not change the
player. See [rendering contracts and ownership](GraphicalBattlefieldHud.md).

Human graphical, physical-monitor/accessibility and live-motion approval remains
pending. Structural/pixel repeatability does not grant that approval or an
isolated performance budget. The PR remains draft while required CI or review is
failed/pending. No merge, tag, release or approved golden is implied.

## Verification

| Check | Result |
| --- | --- |
| Release restore/build | Restore passed; final solution build has 0 warnings/errors |
| Project graph | 43 projects passed, no cycles or architecture-boundary changes |
| Canonical runtime qualification | 1,337 assets unchanged, compiler 1.10.0 |
| Focused HUD/group/tooltip/atlas/native/geometry tests | 142 passed, 0 failed/skipped |
| Current Client suite | 298 passed, 0 failed/skipped |
| Production HUD native contracts (included above) | Six passed: real roster IDs, unavailable identities, hidden fields, long/empty/changing selection, page sharing/lifetime, missing assets and warm geometry |
| Reviewed emergency geometry | Existing command-dock fixture hashes retained and passed; no fixture regeneration or assertion weakening |
| Full solution, isolated modules | FAILED: 2061 total, 2051 passed, 1 failed, 9 skipped; see retained log |
| Native state/layout qualification | 187 structural passes, each with equal repeated pixels |
| Reproducible source-less package | Passed; two identical publish trees, 1,403 files and 1,337 registered runtime payloads independently hash/size verified; source/authoring content excluded |
| Packaged native rendering | Six cases match development pixels exactly |
| Packaged fresh-settings smoke | Passed, exit 0; createdDefaults=True in a new isolated settings root; no recovery |
| Current required CI | [Source run 38810967020](https://github.com/DaveBeusing/Forgeline/actions/runs/38810967020) FAILED at unchanged 56 MiB catalog budget; HUD hierarchy and new native lifetime gates passed |

The six native production tests exercise WARP. The requested debug layer was
unavailable on this host, so these are not debug-layer certification. Hardware
captures use the NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU. Raster DPI
is explicitly authored, while the host window DPI is recorded separately; a
requested 4K backbuffer is not a physical 4K-monitor readability review.

## Runtime and resource identity

Runtime content signature (unchanged):
`b6cbe90cbbd4dfc780abb28f5d42f7bc3b469ae0cf4e663f594975a2bfd219d9`.
Development runtime-directory fingerprint (separate identity):
`7E9E2DF8E7569BD0F8992ED30488040A2CBC4ED7DE071F54A7853D2EC9AD4E3C`.
Packaged runtime-directory fingerprint:
`E98FAE074E6B4AAB7FA89F02B522856EDFC79B4D8E7F83CC94A0E9947F2DB46A`.
The package adds the four approved UI PCM playback files and studio MP4/WAV
sidecars to the runtime tree. All 1,338 shared files (manifest plus registered
payloads) are byte-identical. This explains `sameRuntime: false` in the directory
comparison; the registered asset content signature remains unchanged. Do not
normalize that directory identity or remove required playback media to force a
match. All six paired PNG files and independently decoded RGBA images match.
The source artwork, compiled asset definitions and registered runtime payloads
are unchanged.

Seven shared atlas pages occupy 29,360,128 bytes (28 MiB): core 4 MiB, icons 8 MiB,
portraits 16 MiB. A frontend already holding core reuses that page. Eight prepared
batches reserve 32 frame-upload buffers, 4,374,528 bytes total. Repeated native
sessions release all textures/descriptors after idle; warm skin geometry records
zero managed allocations and no additional texture uploads. These statements are
about the skin, not all existing world/text/legacy buffers.

The 21 named 1080p states record 2–14 skin draws, 1,512–2,742 skin vertices and 48,384–87,744 vertex-upload bytes per frame. Missing assets and dropped quads are zero. Text/world/legacy geometry is accounted separately in the original records.

At 1920x1080/DPI96/scale1, the Mixed/Supply/Production runs each use 64 warmups and 128 samples (126 GPU samples). GPU p50 is respectively 2.719296/2.658400/2.878688 ms; CPU submission p50 is 7,013.4/5,254.8/7,679.5 microseconds. These are measurements on the recorded hardware, not an approved frame budget.

Timing is the existing complete clear/overlay/text GPU frame without world;
CPU submission includes waits/Present. The small named before/after world captures
are evidence of appearance, not a statistically isolated renderer-cost comparison.
No performance threshold is raised or acceptance inferred from these timings.

The final full run fails the unchanged
`DirectorateBuildingPresentationTests.RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame`
for `power_plant`: expected 0, observed 8,064 allocated bytes. Neither the building
resolver nor this test changed. Similar failures in that existing allocation
contract were retained before this migration; root cause is not inferred. The
failed full run remains failed. An isolated rerun of all 26 building-class cases
passes with the same assertions; this does not reclassify the complete run.

## Immutable captures

| Local root | Source and coverage |
| --- | --- |
| `artifacts/battlefield-hud-before` | Base 476f3646: five 1920x1080/DPI96/scale1 states |
| `artifacts/battlefield-hud-current` | Production source: 21 named states, 1920x1080/DPI96/scale1 |
| `artifacts/battlefield-hud-current-compact` | Seven states at 1280x720/DPI144, UI scales .75/1/2, world resolution .5 |
| `artifacts/battlefield-hud-current-4k` | Seven states at 3840x2160/DPI192, UI scale2, world resolution .5 |
| `artifacts/battlefield-hud-current-matrix` | 138 resource/selection cases: five resolutions, three DPI values, three UI scales; three additional strategic cases |
| `artifacts/battlefield-hud-current-packaged` | Six source-less runtime cases at the same production source |

Named coverage includes resource pressure/unknown, low power, supply critical,
mixed selection, targeting, dock focus/hover/press/pending/accepted/rejected,
onboarding, correct unit/building identity, empty/very long selection name,
missing player data, shared gameplay tooltip, assigned group summary and actual
multi-selection group controls. The latter uses existing owned entity identities
and authored copied member facts; no additional simulation members or commands.
The dedicated matrix covers Mixed/Supply/Production at 720p, 1080p, 1440p,
ultrawide 3440x1440 and 4K, DPI96/144/192, UI .75/1/2.

The half-resolution world cases retain native-resolution HUD rendering. Tooltip
placement and control hit geometry remain the shared existing models. The long
name fixture exceeds the selection-caption buffer, and the native test proves it
renders a fitted prefix instead of silently becoming an empty name.

[Retained evidence index](qualification/graphical-battlefield/evidence.json)
contains exact hashes/bytes, original local roots, package identity and source
IDs. [Original native records](qualification/graphical-battlefield/native-manifests.zip)
are losslessly archived with execution logs. The PNG samples are copied byte for
byte; their original image/pixel hashes remain in the manifests. Other original
images remain in the immutable local roots. None is an approved golden.

## Failures, corrections and remaining gates

The initial e9ce83ad full run failed/incompletely reported 2,038 cases: 2,029 passed,
9 skipped, zero assertion failures. The unchanged `ForgeLine.Graphics.Tests`
process exited -1073741819 (access violation). Its isolated rerun passed 76 of 78
with two opt-in skips; the original process failure remains retained. Root cause
is not claimed. An obsolete 8b472730 full run was deliberately interrupted after
discovery of the very-long-name defect and is not counted as a complete pass.

Native qualification found a closed-dock zero-geometry Draw(0); this was corrected
by flushing skin/text while omitting empty legacy draws. The 300-character test
then exposed a bounded-caption append dropping the entire name; the fitted prefix
now remains visible. CI at 2b176942 found a changed bootstrap geometry hash after
native caption fitting. The bitmap path now keeps its original font sizing and
coordinates; the existing reviewed hashes pass unchanged. Initial empty-filter,
formatter-accessibility and capture-fixture span build attempts were corrected
and are not counted as passed validation.

The inherited window-mode qualification catalog budget counts 99,608,050 gameplay
texture bytes against 58,720,256 allowed bytes. Both the catalog/signature and that
threshold are unchanged by this migration. This is separate from the measured
28 MiB atlas residency, and is not resolved by excluding UI or raising the budget.
Report the actual current CI result above; downstream checks that did not execute
are not passes. Full-match, release and human-review gates remain required.

## Package identity

Local ZIP: `artifacts/graphical-battlefield-current-package/FORGELINE-0.1.377-dev-win-x64.zip` (39981298 bytes).
SHA-256: `2bf265986da748e939d54fafbbefaea55491ce2e7f7297dfffb6e6c2d936ddd9`.
The package is a local validation artifact; no release/upload/tag was created.
