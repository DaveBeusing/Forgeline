# Onboarding qualification

The functional onboarding change is implemented and locally qualified. Integration
acceptance remains open because required CI, the natural terminal-match gate and
human visual/interaction review are not all satisfied. Keep
[PR 220](https://github.com/DaveBeusing/Forgeline/pull/220) draft. No merge, release,
tag or pixel-golden approval is claimed.

## Source and ownership

- Base master: `3b34a562470921bcf3efa2339b918ab9886a3636`.
- Branch: `ui/collapsible-onboarding`; version 0.1.386, exactly one PATCH.
- Qualified product source: `6aad37feebcce50adbb02dcd90c60cf85694fcd1`.
- Implementation commits: `f2b41cdf` (shared clipped geometry/translucent skin),
  `0fb9d875` (reversible session interaction), `9f0cf86b` (native sequence tools),
  `bad397aa` (disabled-content settlement and glyph clipping coverage),
  `6aad37fe` (complete high-scale restore label).
- The subsequent evidence commit changes documentation, original evidence and
  early CI test filters only. The compiled client/package/native records continue
  to identify the qualified product source above. Its final remote head and
  separately triggered checks are recorded in the PR.

The existing guidance interaction owns one transition. OnboardingPresentation
version 1 publishes physical panel/clip/intersection/tab/focus state; the renderer
and central transient input owner share it. Simulation, milestones, authoritative
commands, asset inputs and saved settings schema are unchanged.
[Interaction and lifecycle contract](OnboardingInteraction.md).

## Executed local validation

Commands ran from the repository root using the configured SDK 10.0.401 and
Microsoft.Testing.Platform. Final product-source logs are in
[the immutable evidence archive](evidence/collapsible-onboarding/README.md).

| Check | Actual outcome |
| --- | --- |
| Restore / project references | Pass; 43 projects, no cycles or boundary violations |
| Release solution build | Pass; zero warnings/errors, `onboarding-tab-qualified-build.log` |
| Complete solution, Release / no-build | 2,332 total; **2,323 passed, zero failed, nine existing opt-in skips**; 6m 38.119s |
| Focused final geometry/guidance/native text | 97 passed; subsequent early-CI geometry filter: 11 passed |
| Latest controller interaction subset | 17 passed; includes press ownership, reversal, session/focus/geometry, disabled content and keyboard priority |
| PR version | Exactly 0.1.385 → 0.1.386; one PATCH, no tags |
| Canonical Central Divide map | 71 reachability checks, two alternate navigation and two alternate road links |
| Forced runtime asset rebuild / pipeline contracts | Pass; 1,337 qualified records |
| Headless smoke / entity stress | 64 ticks / 1,000 entities and 16 ticks / 10,000 entities pass |
| CPU/null-graphics full HUD benchmark | 120 cases; 128 warmups / 256 samples; zero warm managed allocations; upload requests recorded |
| Active hover + guidance benchmark | 54 cases; zero warm allocation and bounded geometry |
| Animated renderer resource test | 100 measured round trips after warmup; zero managed bytes, one retained fallback buffer |

The complete test run includes Presentation 1,153 passed / seven opt-in skips and
Client 323 passed. The earlier standalone checks (1,148 Presentation / 320 Client)
precede the final regression additions and are historical checkpoints, not the
final totals. Null-graphics allocation measurements do not certify zero allocation
for the full live client or sustained GPU/FPS performance. Match/native/asset
work overlapped earlier; elapsed times are observations, not comparative gains.

## Native results

`--onboarding-capture <empty-output> <runtime-root>` exercises the production
interaction owner with injected presentation time, both slide directions,
normal/reduced motion, 30/60/144-Hz sampling inputs, and exact 0/55/110/165/220 ms
points. It traverses a permitted frozen battlefield fixture without commands.

At the qualified product source:

- 120 recorded quarter-time samples; two identical native readbacks each.
- 40 original 60-Hz-sampling PNGs for 720p high-scale and 1080p clients.
- 14 additional canonical images at 1080p, dark 720p/UI .75 and 4K/DPI 192/UI 2;
  eight identical repeats each, including expanded, collapsed and intermediate states.
- Independent PNG/RGBA/region hashes, source/binary identities, safe clipping,
  exact time-based transforms and dimensions pass.
- Zero missing/dropped glyphs, missing skin sprites, dropped skin quads or native
  submission faults. Native sequence ends with no pending retirements.
- Shared skin remains 28 MiB / 13 batches / 52 prepared buffers throughout sampled
  sequence states. Warm canonical texture uploads and residency remain stable.

The script rates are injected samples, not measured client FPS. Raster DPI is an
explicit test input, separately recorded from actual window DPI. Image inspection
confirms left alignment, translucent terrain visibility and the complete HINT tab.
Human motion, physical pointer/monitor, edge-scroll and accessibility approval
remain pending. Captures are candidates, not automatically approved goldens.

[Four unmodified original review images, audit and records](evidence/collapsible-onboarding/README.md).
All 68 named interface states / 3,060 display-DPI-scale combinations are in the
matrix plan; matrix contracts pass. That complete native matrix was not executed.
The existing early UI guards now include the 11 onboarding geometry cases, without
removing or weakening any prior case or acceptance stage.

## Reproducible package and actual startup

Frozen-source publication with `-VerifyReproducible -RuntimeAssetRoot assets/runtime`
passes both publish trees, UI integrity/provenance, source exclusion and 844 UI
payload checks. Package:

`artifacts/onboarding-qualified-package/FORGELINE-0.1.386-dev-win-x64.zip`

40,023,037 bytes; SHA-256
`b1c12578a4e6d71d081e86496654ab15b99fbcaf4edea5515cebd7756f548978`.
Provenance records clean source `6aad37feebcce50adbb02dcd90c60cf85694fcd1` and version
0.1.386-dev. Runtime signature:
`5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932`.
Whole-runtime fingerprint:
`ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`.
Both are unchanged from the preceding HUD qualification.

The normal start-path client and source-less packaged client each exit 0 with
ApplicationReadiness and SessionReadiness Ready, correct source/version, 1,337
assets and separately created fresh settings. The package runs from its own publish
directory and reads its own runtime assets. Personal settings are untouched.
Smoke mode suppresses onboarding; the native sequence and tests supply its specific
coverage, rather than misrepresenting startup smoke as a physical click journey.

## Required hosted checks and remaining gates

At the qualified product source, [required CI 38973274877](https://github.com/DaveBeusing/Forgeline/actions/runs/38973274877)
passes steps 1–36, then fails step 37 (Windows window mode/visual qualification):
**99,608,050 compiled gameplay texture bytes exceed the existing 56 MiB production
catalog budget**. Later hosted benchmark, full tests, packaging, smoke and match
stages are skipped; local results above do not make that check green. The asset
inputs, signature, footprint and assertion are unchanged. The original provider
log is retained locally; its SHA-256 and exact failure excerpt are archived.

[Frame hot paths 38973274996](https://github.com/DaveBeusing/Forgeline/actions/runs/38973274996),
[Scalability 38973275000](https://github.com/DaveBeusing/Forgeline/actions/runs/38973275000)
and [Movement quality 38973275121](https://github.com/DaveBeusing/Forgeline/actions/runs/38973275121)
pass at that source. The optional hosted native scalability lane is skipped.
These checks are separate from the later evidence-head reruns.

The natural seed-2026 validation match executes 80,000 ticks, remains Active with
winner 0, and exits 3. Its immutable headless binary identifies `bad397aa`; the final
tab patch changes no Headless/Game/Simulation source. Diagnostics record 569.179s
wall time with overlapping qualification work; no throughput improvement is claimed.
The terminal result remains a failed gate, matching the existing open qualification.
No forced result, resource grant, timeout change or replacement match is used.

Remaining acceptance: resolve the existing texture-budget and natural terminal
match gates through their respective owners, inspect final-head hosted results,
and obtain human motion/readability/physical-input acceptance. There are no known
remaining onboarding implementation changes. Full acceptance must not be claimed
until those gates are satisfied.

## Retained earlier attempts and resume

The first native sequence at `bad397aa` passed structural checks but image inspection
found HI… on the high-scale restore tab. `6aad37fe` supplies a separate label frame
and four complete-label fit cases; the corrected sequence and all final captures
were repeated in fresh directories. Original uncorrected images/metadata remain
local and their original qualification JSON is retained.

The first package attempt in `artifacts/onboarding-package` failed reproducibility
while source/build activity overlapped its two publication passes. It is invalid
qualification and is preserved unchanged. The separately named frozen-source
package above passes. Initial compile failures in new test/tool code (focus event
factory, compact-state scope and JSON-options analyzer) are retained with their
corrected builds. No budget, assertion, baseline, warmup or timeout was weakened.

Resume by verifying remote master, PR head and source diff first. Use the pinned
product source/binaries and new output roots for native repetition. Do not overwrite
these records, repeat the PATCH increment, create a competing PR, or count partial
or historical checks as current acceptance.
