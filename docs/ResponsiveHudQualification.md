# Responsive HUD qualification

## Result and identity

The version-2 right-column HUD is implemented. Integration/visual acceptance is
pending: mandatory CI, complete-suite and natural-match gates are not all green.
No golden, release, merge or physical-monitor approval is claimed.

- Base master: `2d698c839c0b3427579fd309b72e0befa55d0f4d`.
- Code, native captures and final verification source:
  `c20b74d7a099d871e6833a7c29ecefce9e67ca32`.
- Branch: `ui/right-aligned-hud`; [PR 217](https://github.com/DaveBeusing/Forgeline/pull/217), draft.
- Product version: 0.1.383, exactly one PATCH from 0.1.382. Later evidence-only
  commits do not consume another PATCH or change the qualified product source.

## Delivered contract

`TopStatusBar`, the visible right dock and `NotificationBounds(expanded)` share
the safe right edge. Status width never exceeds twice dock width. The dock starts
one physical `Gap` below status; the notification reservation starts one gap below
its visible expanded/collapsed bounds and stays above the minimap. Collapsed
status is actually drawn, including the no-target context prompt. No invisible
full card height is reserved when closed.

`OnboardingBounds` owns the upper-left corner and supplies existing guidance and
its input capture. Notification drawing remains on its existing left stack;
its subsequent migration must consume the published target instead of inventing
new offsets. Primary commands, selection inspection and minimap retain their
owners. Simulation, navigation, copied-fact authority and command submission are
unchanged.

All seven actual resource quantities and power remain. Full resource names use
at least 11-pixel captions; numeric values stay at least 12 pixels in the required
45-case matrix. Zero remains zero, unavailable remains N/A; billion/scientific
notation bounds large values without clipping digits. The same 1064/640 design
widths select one/two rows in geometry and rendering. Extra 1360-pixel and small
scale transition cases reject the earlier full-name truncation. Only the nine
1024x720/96-DPI/.75 structural fixtures change at that final transition; the
other 126 existing fixture keys remain byte-identical.

Resource icon/text groups, power, dock tabs, card text regions, header and footer
labels use the existing atlas and font metrics. Bindings have separate centered
rows, and paging has symmetric title slots. General text alignment retains its
existing contract; the unused bitmap approximation was removed. Compact cards
retain progress meters. No new fonts, artwork, framework or per-frame GPU asset
preparation is introduced. Normal FPS/SIM/TPS drawing and its formatting/column
reservation are removed. Sampling and independent Shift+F1 diagnostics remain.

## Executed verification

- Final Release solution build: zero warnings/errors. Standalone Presentation: 1,104 total, 1,097 passed, zero failed, seven skipped. Client: all 308 passed.
- Final complete solution: 2,261 total, 2,251 passed, one failed, nine skipped. `SelectedCombatGroupTests.RenderAndControlsShareBoundedGeometryWithZeroWarmAllocation(1920,1080,96,1)` measured 8,104 bytes against zero. It passes in the standalone Presentation run. Its complete-run cause remains unqualified; the failure is retained.

- Restore, the 43-project reference graph and one-PATCH validation pass.
- All 45 combinations of five outputs, DPI 96/144/192 and UI scale .75/1/2 check
  resource/power bounds, text widths, reserved rectangles and shared hit targets.
  Expanded/collapsed states, unusable clients, real zero, unavailable and maximum
  finite quantities are covered. Font tests use I/WWW, diacritics, numbers,
  multiline power and bounded long labels, within one physical pixel of the
  defined logical/ink text block. Brief left/right Shift+F1 gestures, repeated
  toggles, plain F1, modal suppression and resize retain regression coverage.
- Twelve final hardware native cases / 24 identical readbacks cover 720p, 1080p,
  1440p, ultrawide and 4K, all three raster DPIs and all three scales. They include
  onboarding at normal/extreme scale and 960x540 world output under a native
  1920x1080 HUD. This is targeted coverage, not the entire 45-case native union.
- Independent original PNG and decoded RGBA checksums, dimensions, repeat equality,
  zero missing/dropped glyphs, skin assets and quads pass for all twelve cases.
  The same captured profiles are pixel-identical to source 03996cbb; the final
  threshold changes only other resize positions. Reviews remain pending.
- All 1,337 assets compile/qualify and asset process contracts pass. Map compilation,
  normal-path session-ready smoke and headless smoke/stress pass. The source-bound manifests retain their reported runtime fingerprints;
  package asset signatures and source provenance are recorded independently.
- Reproducible source-less 0.1.383-dev package passes integrity/exclusion checks with 844 UI payloads. Archive SHA-256: `07df14f8b0aa6b8b0c42b1f925cf301e005f2ebd9c5f2523249ba9558d7a32b8`.
- Final normal-path and packaged clients both exit zero, reach ApplicationReadiness/SessionReadiness Ready and report source c20b74d7 with 1,337 runtime assets. Each uses fresh separate artifact settings; user settings are untouched.
- Final UI benchmark: 120 warmed cases, zero managed allocations, with glyph/upload requests recorded.

## Cost observations and limits

The matched original-master/addb61fa checkpoint uses the same runtime/hardware
fingerprint, eight warmups and sixteen CPU samples. Text uploads change from
129,984 to 125,568 bytes for ResourcePressure, and 100,608 to 96,960 for DockFocus.
Later full names increase those final values to 131,904 and 103,296 bytes. Final
HUD geometry is 63,054 / 61,284 vertices respectively. Skin draws remain 29 / 18,
and the shared skin pages remain 29,360,128 bytes. Scoped text diagnostics and
whole HUD geometry are separate counters.

Whole captured frames report 296 allocated bytes at the matched checkpoint,
unchanged from baseline; that is not a claim of zero whole-scene allocation.
UI-only warmed guards are recorded separately. No sampled texture residency or
upload-count growth occurs. Short scene CPU/GPU timings, including final captures
near other local work, do not certify sustained FPS or an accepted speedup.
The final native manifests report runtime fingerprint
`ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`,
the same recorded runtime identity as baseline. Package asset signatures remain
a separate identity. Neither identity grants visual acceptance.

## Retained failures and open gates

- Initial changed-source runs retain their obsolete geometry/height/alignment
  failures. A measured supply-depot allocation case reported 1,984 bytes. The
  general alignment contract was restored; only the new helper centers ink.
- At addb61fa, the complete solution has 2,257 total / 2,245 passed / three native
  failures / nine skips. Failures: studio-video decoding, branded startup handoff,
  and session snapshot handoff. A separate seven-case native control passes;
  it does not erase or explain the complete-run failures.
- At 03996cbb, the complete solution has 2,259 total / 2,248 passed / two native
  failures / nine skips: branded startup handoff and cancellation of a queued
  graphics continuation. The original logs remain distinct from final results.
- Final full-suite status is recorded above. No assertions, timing limits,
  allocation budgets or test scheduling are weakened to obtain a pass.
- The unchanged natural seed-2026 validation executes 80,000 ticks and exits 3
  with MatchStatus Active. It does not reach a terminal outcome. The run is at
  addb61fa and the authoritative/headless source is unchanged throughout this PR.
  No forced outcome, resource grant or result substitution is used. Root cause
  remains unqualified and the mandatory match gate stays open.
- At code c20b74d7, [CI run 38925810705](https://github.com/DaveBeusing/Forgeline/actions/runs/38925810705) passes steps 1–36 and fails Windows window-mode qualification: 99,608,050 compiled gameplay texture bytes exceed the unchanged 56 MiB production budget. Later full tests/publish/match steps are skipped. Frame hot paths (38925811536), Scalability (38925810797) and Movement quality (38925810772) pass. The same earlier budget failure is retained separately. Evidence-only commits may trigger fresh runs; they do not inherit a green required check.
- Physical displays/input transitions, live diagnostic-overlay readability,
  motion/listening and attributable human visual/accessibility acceptance remain
  pending. The local D3D12 debug layer is unavailable. Native captures are
  candidates; exact repeat pixels and updated structural hashes grant no golden.

One package invocation rejected an absolute output argument before publishing;
the corrected relative path remains inside repository artifacts. A normal client
build was blocked by a running game; after the user closed it, a separate clean
build succeeded. Both failed logs are retained without rewriting them as passes.

## Evidence and resume

[Original records](evidence/responsive-hud/records.zip), [checksums](evidence/responsive-hud/checksums.json) and unmodified [normal onboarding](evidence/responsive-hud/normal-onboarding.png), [compact onboarding](evidence/responsive-hud/compact-onboarding.png) and [normal dock](evidence/responsive-hud/normal-dock.png) are retained. The archive contains source-bound capture manifests, comparisons, native startup reports, package provenance and original validation logs including failures.

Original local evidence roots are `artifacts/right-hud-before`, `right-hud-after`,
`right-hud-complete-*` and `right-hud-qualified-*`. Keep each source/runtime identity
and all failed attempts distinct. Representative PNGs are unmodified native
images. Source-bound manifests, hashes and original logs are in the accompanying
record archive; full local capture sets remain in their original roots.

Resume by checking master/branch/PR and their diffs, then use the code source and
records above. Next technical work is to resolve the full-suite/CI/match failures
in their owning domains and perform physical/human review. Notification/onboarding
follow-ups consume this layout API. Do not increment PATCH again, stack a branch,
merge, relax gates or promote these images automatically.
