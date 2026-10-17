# World anchored action menu qualification

Qualified source: `e1bb70913770725da58c7b81370cd4fd290ee97a`. Base master: `ce254914be080f92d5d3cd0ff76ccaa7ffe65938`. Branch: `ui/world-anchored-action-rings`. Version: **0.1.387-dev**, one PATCH increment. [PR 221](https://github.com/DaveBeusing/Forgeline/pull/221) remains Draft. This document/evidence checkpoint changes no product code.

Own single buildings and units use one world anchored ring; the old contextual cards and expanded action dock are suppressed for those selections. The global category bar, informational inspector and temporary group/global fallback remain available. [Action parity, geometry, input and lifecycle](WorldAnchoredActionMenus.md) and [architecture decision](adr/0043-world-anchored-action-rings.md) describe the contracts.

## Executed checks

| Check | Result / evidence |
|---|---|
| Release solution build | Zero warnings/errors, `rings-final-build.log` |
| Full solution tests | **2,368 total: 2,359 passed, zero failed, nine existing opt-in skips**, 7m 57.989s; all modules, sequential module scheduling |
| Interaction and geometry/motion | 19 ring integration cases and 17 geometry/motion cases are included in the full run; all seven action modes, queue IDs, parameter editing, split/coalesced selection events, dismissal and stale releases are covered |
| Native graphics test isolation | All 78 cases executed: 76 passed, same two opt-in skips. Collections use the pinned xUnit parallelization contract; no assertion, budget, timeout or explicit in-test concurrency changed |
| Semantic version / architecture | One PATCH validated; 43 projects, no cycles or boundary violations |
| Map qualification | Central Divide: 71 reachability pairs, two alternate navigation routes and two alternate roads; authoritative map code unchanged |
| Runtime assets | Forced rebuild/qualification: 1,337 assets. Runtime process contracts passed. Signature `5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932` |
| Whole runtime fingerprint | `ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`, unchanged |
| Headless smokes | Current source: 64 ticks / seed 12345 / 1,000 entities; 16 ticks / seed 67890 / 10,000 entities; both exit zero |
| Save/load/replay | 256 ticks / seed 4242, creation, restore and replay pass on 5445b66e; subsequent changes affect only native test scheduling/docs |
| Source and packaged startup | Both exit zero, application/session **Ready**, zero dropped startup events, 1,000 requested render stress instances. Package runs from an empty working directory and loads its own 1,337 runtime assets |
| Personal settings | SHA-256 unchanged; fresh isolated settings roots used |
| Package | Reproducible win-x64 publish and all package/atlas/audio contracts passed; 40,046,179 bytes; SHA-256 `275a4ebb5bfc4f7d05fb70e717b542571be3698ce4246f72a928d1c3e67e5c56` |

Commands and complete successful logs are in [records.zip](evidence/world-anchored-action-rings/records.zip). The package remains local at `artifacts/rings-final-package/FORGELINE-0.1.387-dev-win-x64.zip`; this is a qualification candidate, not a release.

## Native evidence and bounded resources

The archived audit covers **37 original capture candidates across recorded source revisions**, each with eight equal pixel repeats. PNG, RGBA and region hashes were independently checked. The 7b889a31 set covers every category plus parameters/pending/edges/tooltips/open/close/offscreen, compact Low at 1280×720 / raster DPI 192 / scale 2, and dark/haze/high-particle/half-world-resolution scenes. Later sources requalify changed input/render/motion behavior; the final e1bb7091 set contains construction, parameters, tooltip and edge candidates. Raster DPI is not physical monitor certification.

The final native motion trace uses the production owner: **168 records** at injected 30/60/144 Hz, opening and closing, ordinary/reduced motion. Every ordinary track contains intermediate alpha and follows the exact 160 ms SmoothStep; reduced tracks settle immediately. Injected sampling rates are not measured FPS.

After **128 open/close/context cycles**, live graphics resources remain **112 → 112**, atlas buffers **52 → 52**, eight resident UI textures and **28 MiB** shared atlas pages. No missing/dropped glyphs or skin quads or submission faults were observed.

| Same-device clear/HUD/text sample | CPU submit p50 µs | Complete GPU frame p50 ms | Skin draws | Skin upload bytes | Total HUD vertices |
|---|---:|---:|---:|---:|---:|
| Previous expanded dock | 7,914.6 | 5.169664 | 18 | 108,480 | 61,572 |
| Single object, closed | 7,392.2 | 4.844384 | 16 | 60,480 | 58,350 |
| Single object, ring | 6,230.6 | 4.841536 | 16 | 64,896 | 60,564 |

Each comparison uses 32 warmups and 128 samples, no world rendering, sequentially on the same device without other qualification processes. CPU includes waits/Present; these measurements are not gameplay FPS or a general speedup claim. Earlier loaded-system timing records are retained rather than substituted for this final comparison.

Representative original candidates (no annotations or golden approval):

- [Construction](evidence/world-anchored-action-rings/construction-e1bb7091.png), [parameters](evidence/world-anchored-action-rings/parameters-e1bb7091.png), [complete tooltip](evidence/world-anchored-action-rings/tooltip-e1bb7091.png), [edge/tether](evidence/world-anchored-action-rings/edge-e1bb7091.png).
- [Compact high-DPI parameters](evidence/world-anchored-action-rings/compact-parameters-7b889a31.png), [dark/haze/half-world tooltip](evidence/world-anchored-action-rings/dark-half-tooltip-7b889a31.png).

## Unsuccessful attempts retained

368973ff and 5445b66e full runs terminated in the native Graphics.Tests process with `0xC0000005`; completed assertions reported no failures, but both runs were unsuccessful. Isolated graphics runs passed. Serializing modules alone did not eliminate the process failure; native graphics collections now have one test owner. The final full invocation passes with the same test coverage. The first isolation annotation used an API rejected by pinned xUnit 4; it was corrected before the qualified build. The no-build run started after that rejected build was canceled and is not evidence. Failed logs remain archived; no assertions or production limits were relaxed.

## Remaining acceptance gates

- **Required CI is not green.** Qualified source e1bb7091: Frame hot paths ([39016027458](https://github.com/DaveBeusing/Forgeline/actions/runs/39016027458)), Movement ([39016027415](https://github.com/DaveBeusing/Forgeline/actions/runs/39016027415)) and Scalability ([39016027438](https://github.com/DaveBeusing/Forgeline/actions/runs/39016027438)) pass. Required `build-test` fails at Windows window/visual qualification ([39016027448](https://github.com/DaveBeusing/Forgeline/actions/runs/39016027448)): **99,608,050 gameplay texture bytes exceed 56 MiB**, the unchanged baseline budget failure. Full job log SHA-256: `2331cbae65c61e47874b809044798c9e388713b166fc7d89b0b88834b2118f78`; failure excerpt and workflow identities are archived.
- **Natural terminal gate remains unsuccessful.** Seed 2026 / validation profile / 80,000 ticks stays Active, winner 0, exit 3. Reports from 7b889a31, c76fc198 and 368973ff agree. No authoritative Game/Simulation/Headless code changed afterward. The limit and game rules are unchanged; successful smokes do not satisfy this gate.
- **Full matrix and human acceptance remain open.** The explicit matrix plans 82 named states × 45 display/DPI/scale combinations = 3,690 cases. The 37 audited candidates are not that complete matrix. Physical monitor, in-game owned building/unit interaction, readability, motion and final visual/golden approval require review. Group selection still intentionally uses its existing controls.

Next integration steps: resolve the existing texture/terminal gates without weakening them, complete the remaining native matrix and human review, then reevaluate Draft status. No merge, auto-merge, tag or release is authorized by this checkpoint.
