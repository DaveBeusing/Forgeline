# Notification stack qualification

## Current presentation revision

A later notification-row refinement uses contract version 4. The placement-context hint uses its own copied, session/tick-scoped preview state; its former tall panel capture is not retained beneath the single-row visual. Onboarding guidance stays a separate control and animation. It replaces multiline cards with single centered icon/text/control rows, caps the container/background alpha at 0.30, and reuses the authorized Operations Notifications full-text path for ellipsized messages. The existing global notification bounds remain authoritative; there is no dependency on a removed rectangular action bar. Visibility transitions are 180 ms SmoothStep on presentation time and do not alter actionable bounds; retired surfaces may show a fading background only, without text, target, or pointer capture. No alert type, priority, history, or simulation authority changes. The prior qualification below remains historical and must not be represented as current-source graphics validation.

## Result and source identity

The right-dock notification migration is implemented in version **0.1.385**.
Integration acceptance remains open: the hosted texture-budget gate, natural
terminal-match gate and human visual/accessibility checks are not all satisfied.
No merge, release, approved golden or physical-monitor acceptance is claimed.

| Identity | Value |
| --- | --- |
| Base master | `03b0c668c0d84eeb347bda9aec2e0f3f71e42e23` |
| Branch / PR | `ui/action-bar-notifications` / [PR 219](https://github.com/DaveBeusing/Forgeline/pull/219), draft |
| Final product source | `3b5c545e4396c56a40f8ac54cb82fa285370e4da` |
| Main rendering / complete-suite source | `7a421bef309118dea32726dcd292321975b76a75` |
| Product version | 0.1.385, exactly one PATCH from 0.1.384 |
| SDK / observed runtime | .NET SDK 10.0.401 / .NET 10.0.12, Windows x64 |

The only code change after the main rendering/complete-suite revision fits the
notification detail Close control to scaled text. The entire affected
Presentation/Client projects and three detail recaptures verify the final source.
Evidence-only commits after that source do not consume another PATCH. Records
bind explicit source SHAs; inspect the live PR head checks before integration.

## Contract

`GameplayHudLayout` and `ActionableAlertLayout` publish contract version 3.
`NotificationBounds(expanded)` begins at the visible dock bottom plus `Gap`,
shares its width and safe right edge, and stays above the minimap. Maximum height
is 224 times actual dock content scale, with a minimum scale of 1. Expanded dock
capacity, general dock width and global world-input sequencing are preserved.
`AlertStack` identifies the collapsed reservation; consumers with current dock
state resolve `NotificationBounds(expanded)`.

One value-type list contains at most three regular cards plus MORE. Existing
font metrics provide a 14-physical-pixel floor, actual dock content scaling, two
text lines, ellipsis and complete accessible detail text. Constrained space
reduces card count. A summary-only stack retains the highest hidden severity,
including CRITICAL/WARNING text and the existing rail/skin state. No critical
condition disappears through toast expiry or an outside world click.

Operations stays on the left and exposes Notifications even without facility
data. MORE opens it immediately, including while the pointer remains over the
source card. Its cache retains at most six lines; compact panels reduce visible
line count while retaining the same font floor/scale. Paging reaches all current
authorized text, including strings beyond the common wrap helper's 256-line cap.
There is no notification history or new event database.

Five copied conditions, existing severity priority, authorized targets and the
80-tick command-result lifetime are preserved. Accepted/partial/rejected remain
command results, not completed work. Local command/navigation results share this
stack; placement guidance keeps its placement context. No ECS reads/mutations,
new commands, textures, fonts, sound, animations or polling threads are added.

Rendering, pointer ownership and nonmodal routing use the same actual card list.
Gaps and unused reservation remain world surface. Down/up requires matching card
geometry/revision, lifecycle identity, target generation and action. Expiry,
reflow, layout/session changes and older session/tick snapshots cannot transfer a
gesture. FOCUS retains existing authorized navigation; missing targets use OPS.
Consumed input is distinct from modal/focus suppression of Operations.

## Executed validation

| Check | Executed result and scope |
| --- | --- |
| Restore / references | Restore passed; 43 projects, no cycles or architecture-boundary violations |
| Final Release build | Final product source: zero warnings and errors |
| Version policy | One PATCH validated against the base master; no tags |
| UI matrix contract | 61 named states × 45 display/DPI/scale combinations; metadata coverage contract, not 2,745 executed native images |
| Notification geometry | 90 combinations: five outputs, DPI 96/144/192, scale .75/1/2, both dock states; shared bounds, gaps, minimap/Operations separation, font scaling and Close-label fit |
| Focused notifications/input | 156 passed before final caption fitting; includes identity, target generation, expiry, hidden-count change, stale sessions, full long text, immediate MORE and summary-only critical severity |
| Presentation, final source | 1,140 passed / 0 failed / 7 opt-in performance checks skipped; 1,147 observed |
| Client, final source | 308 passed / 0 failed |
| Complete solution, rendering revision | 2,295 passed / 0 failed / 9 opt-in checks skipped; 2,304 observed, canonical command, 7m 23.840s |
| Runtime assets | Forced canonical rebuild and process contracts passed; 1,337 records; inputs remain unchanged by this PR |
| Normal client / headless smoke | Normal client Ready/exit 0; 64 ticks / 1,000 entities and 16 ticks / 10,000 entities passed |
| Final package | Reproducible source-less publish passed; 844 packaged UI payloads and four existing PCM cues verified; fresh isolated settings, Ready/exit 0 |
| Natural terminal match | Seed 2026, validation profile, 80,000 ticks: still Active, winner 0, exit 3; criterion failed |

The natural match used the earlier `2cd5a2ade90420e4cd4e3c3b9e92c447842dfd2c`
binaries. Authoritative gameplay/headless source is unchanged throughout this PR;
the result remains an open gameplay gate, not a current-source success claim.
The previous [nonmodal qualification](NonModalHudQualification.md) records the
same Active outcome. No scenario, balance, timeout or terminal assertion changed.

## Native images and cost measurements

Fifteen original captures at the rendering revision cover 1080p bright/dark
collapsed/expanded docks, long detail text, actual FOCUS, 720p scale .75, 720p
192-DPI scale 2, and 4K 192-DPI scale 2. Three final-source detail captures verify
caption fitting at 1080p, 720p maximum scale and 4K. Each has eight equal fixed-state
native readbacks. Independent audits verify original PNG, decoded RGBA and region
hashes, dimensions, shared card bounds, text/skin completeness and stable warm
texture resources. These are authored read-model fixtures, not live gameplay or
physical-display acceptance.

[Three original PNGs, pinned image identities and records](evidence/action-bar-notifications/README.md)
are checked in. All original capture directories remain under
`D:/Code/Forgeline/artifacts/notifications-qualified-*` and
`D:/Code/Forgeline/artifacts/notifications-completion-*`.

The whole-runtime fingerprint is
`ADE1441B64F93A5BD649686DA1AD9FD2B924CE7C01B6AC50FCCBBBBAD12DDA67`.
Native font size spans 14 to 39.65684 physical pixels in these cases. Skin pages
retain 28 MiB shared residency. Main native cases record 23–39 skin draws,
60,972–64,236 total HUD vertices, 91,008–140,736 skin upload bytes and
98,304–154,176 text upload bytes. Warm texture-upload delta is zero. The complete
native frame records 296 allocated bytes; this is not a zero-allocation claim for
the entire client and does not identify the allocating subsystem.

Final-source `--alert-map-hotpaths` measurements cover 56 full HUD/minimap/input
cases: 1/10/100/1,000 entities, seven layers, both dock states, five conditions,
one command result and one local result. With 128 warmups, 256 samples and
process-local `DOTNET_TieredCompilation=0`, all report **zero warm managed bytes**.
CPU/null-graphics recording reports eight draws, 3,564–4,194 vertices and
101,712–121,680 upload bytes per frame. This excludes production atlas/native GPU
costs, extraction, waits and Present; it does not establish sustained FPS or art
quality. No new per-frame asset preparation or GPU texture residency is added.

## Package identity

`artifacts/notifications-qualified-package/FORGELINE-0.1.385-dev-win-x64.zip`
is approximately 38 MiB, source-less, reproducibly published from the clean final product
source. Its provenance pins SDK, source revision, version and runtime signature.

SHA-256: `8b14abde48f689373be8a1bb2ec7b77f25211b98c5dea4c2306d6370025e866c`.
Runtime asset signature:
`5cb87ff663e609ff3c0842be2671e3eabc9a9389e3562a66e0fba46733700932`.
The startup report records `0.1.385-dev+3b5c545e4396c56a40f8ac54cb82fa285370e4da`.
User settings were isolated under artifacts; no user settings migration or
replacement was required for this qualification.

## Hosted checks and open gates

At the final product source:

- [Frame hot paths](https://github.com/DaveBeusing/Forgeline/actions/runs/38967607644),
  [CPU scalability](https://github.com/DaveBeusing/Forgeline/actions/runs/38967607605)
  and [movement quality](https://github.com/DaveBeusing/Forgeline/actions/runs/38967607592)
  passed. Scalability's opt-in native-reference job was skipped, not qualified.
- [Required build-test](https://github.com/DaveBeusing/Forgeline/actions/runs/38967607740)
  passed steps 1–36, then failed step 37, Windows window-mode qualification:
  **99,608,050 compiled gameplay texture bytes exceed the 56 MiB catalog budget**.
  Later full-suite/package/match steps were skipped. Local passes do not replace
  that required hosted result. The asset inputs and budget are unchanged; the
  preceding nonmodal qualification records the same footprint failure.
- The natural terminal-match criterion remains failed as recorded above.
- Human typography/contrast, accessibility, physical monitor/DPI/focus transitions,
  live alert-to-world/save-load journeys and sustained performance remain pending.
  No approved baseline or image-similarity threshold has been introduced.

The PR stays draft. Do not merge, tag, release or promote these candidates until
required gates and human approvals are satisfied. Check the current evidence-only
head independently; the linked runs identify the qualified product source.

## Retained failures and resume

Original development/failing attempts remain in the records archive, distinct
from the final results:

- Missing font-metric argument and a detail-cache local-name collision were
  repaired before implementation qualification.
- The first full Presentation run had 69 failures: 66 from a null default local
  hint, two stale contract-version assertions and one 1,984-byte building-state
  allocation sample. Client had two failures from that null hint. The null path
  has a dedicated regression; subsequent complete affected projects pass.
- Initial hosted CI rejected the unextended fixture list. All three scenarios
  were added to the explicit matrix; its 61-state contract now passes.
- One native invocation passed a comma-joined string instead of a scenario array;
  the invocation was corrected in a fresh directory. Earlier valid captures with
  the provisional 1.5 text-scale cap are superseded, not approved goldens.
- The first complete solution run reported 2,291 passed / two native startup or
  session-handoff timeouts / nine skipped. The second canonical run passed with
  unchanged timeouts/assertions; the initial failures remain in its original log.
- One intermediate build overlapped the running headless verifier and hit file
  locks. The verifier was allowed to finish; later Release builds pass cleanly.
- Native review removed the provisional text-scale cap, retained critical
  severity in summary-only space and fitted Close text to its actual control.

The immutable archive has **209 records**, 1,459,134 bytes, SHA-256
`70f5a61ffd666c85f3129b47823a24a416f85caed6e108e2c4c515d135df1803`.
[Its index](evidence/action-bar-notifications/records.json) pins every original
record. The original hosted CI log stays in artifacts; its failure excerpt and
full-log hash are archived. Original images and source-less ZIP remain at their
audited local paths, with three unchanged PNGs also committed for remote review.

Resume from the named final product source to reproduce these binaries. Build
Release and prepare/validate the same runtime root before capturing. Use
`build/Invoke-RtsReferenceQualification.ps1` with a fresh output directory,
`BattlefieldCombat`, Medium, NormalGameplay, the relevant named notification
states, explicit raster DPI/UI scale, 32 warmups, 32 samples and eight repeats.
Use actual arrays for multiple scenarios. Human review stays pending.

Next: resolve the production texture footprint and natural-match lifecycle in
their respective owners, rerun the required current-head checks, then obtain
source/manifest-bound visual and accessibility decisions. Existing historical
left-stack images and earlier qualification ledgers remain unchanged.
