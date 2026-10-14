# Battlefield HUD qualification

Qualified player code: `a4685918b9c171ca37150041b470014e02693a04`, product **0.1.366**.
The following evidence/formatting commit preserves behavior and normalizes two source
files to their original LF line endings. Existing command-dock geometry fixtures and
accepted image goldens are unchanged.

## Review rationale

The earlier resource strip combined labels and values in a single small line, hid
resources at high scale and left a large empty span. The new strip separates muted
11px captions from bold aligned 14px quantities and gives power a labelled block.
Narrow/tall strips use two rows so all seven captured totals remain visible.
Selection identity uses a raised header and explicit health, fuel/ammunition, readiness
and work labels. Critical health adds an exclamation marker; supply/blocked work retain
text and semantic symbols. Compact inspector height fits its actual width while the
separate command-dock band stays stable.

Alerts have bounded card width, explicit severity and rails; critical supply precedes
command feedback. Pointer capture follows populated rows, leaving gaps and empty space
available to the battlefield. Operations uses an opaque fitted panel, selected rails and
double blocked markers with the captured cause. Its entry hides while open, and the
panel clears selection/command controls. Native high-scale review caught and corrected
both overlap and text showing through the earlier panel. Minimap frame/selector/legend
fit its local size; intelligence colors, unknown/detected/identified filtering and marker
sources remain unchanged. Tooltips fit available width and height.

- [Previous supply HUD, 1080p](evidence/battlefield-hud/before-supply-1080p.jpg)
- [Revised supply HUD, 1080p](evidence/battlefield-hud/after-supply-1080p.jpg)
- [Operations, 720p / 192 layout DPI / 2x scale](evidence/battlefield-hud/operations-720p-high-dpi.jpg)
- [Mixed selection at strategic zoom](evidence/battlefield-hud/strategic-mixed-1080p.jpg)
- [Machine-readable cases, timings and provenance](evidence/battlefield-hud/qualification.json)

JPEG previews re-encode full-resolution native PNGs without composition edits.
Recorded RGBA hashes refer to original native pixels, not JPEG bytes. The original PNGs
are reproducible. These are review evidence, **not reviewer-approved pixel goldens**.
The expanded command dock remains separate work; its existing extreme-scale appearance
is visible in the captures and was not redesigned here.

## Native and state coverage

The packaged player passed **138 repeated native cases**: Mixed / Supply / Production
at five sizes (1280x720, 1920x1080, 2560x1440, 3440x1440, 3840x2160), three layout DPIs
(96/144/192) and three user scales (.75/1/2), plus three strategic-zoom 1080p cases.
All fixed repeats matched; no missing/dropped glyphs or submission faults occurred.
The native fixture includes hostile contact and explosions and retains its original
world intelligence/instances. Additional owned HUD observations are explicitly authored
inspection inputs, not a natural match or newly inferred gameplay facts. Mixed selection
without component coverage remains aggregate-only.

The actual host window DPI was 144. Requested layout DPI and backbuffer sizes do not
certify physical multi-monitor behavior. Visibility, session/tick rejection, cleared
selection, immutable copied data, nonoverlap and pointer-origin/lifecycle behavior are
covered by the affected suites. Forty-five geometry combinations separately check all
seven resource cells, power, inspector sizing and operations controls/bounds.

Four unchanged canonical IndustrialBase/BattlefieldCombat normal/strategic reference
cases passed before (master 7cc7574) and after (implementation 00d71de0). Those captures
retain their original source identities; final operations corrections are qualified by
the packaged 138-case run. No fixture fingerprint or golden was silently regenerated.

## Costs and limits

The instrumented baseline uses the current diagnostic client with the previous
Presentation binary from `5e7d4f7`. Its Presentation source is identical to starting
master `7cc7574` (verified with Git). It is not an unmodified baseline player package.
Both renderer assembly identities and SHA-256 values are recorded.

The final same-device cohort ran baseline / current / current / baseline, with 64
warmups and 128 submitted frames per state. Only distinct completed GPU fences after
warmup count; available samples are recorded. This measures the complete clear/HUD/text
GPU frame without a world. CPU submit includes waits/Present.

| State | Previous GPU p50 range, ms | Revised GPU p50 range, ms |
| --- | ---: | ---: |
| Mixed | 2.530–2.573 | 2.482–2.557 |
| Supply | 2.894–4.781 | 3.915–4.627 |
| Production | 2.776–2.794 | 2.864–2.991 |

Supply timings vary substantially in both cohorts; these short observations do not
establish a portable regression threshold or a general speedup. Production has a modest
observed increase. At 1080p the first paired native geometry counts were 61,716 to 61,746
(Mixed), 62,052 to 62,124 (Supply) and 64,074 to 64,134 (Production); exact final counts
remain in the report. No sustained whole-match FPS, motion or device-removal claim is made.

The 120-case CPU null-graphics comparison retained zero warm managed allocations.
Median vertices changed 4,824 to 4,857; median requested uploads 133,632 to 134,688 bytes.
Per-case geometry increased by 18–1,602 vertices, including newly visible resource cells.
Median case p50 was 57.3 to 52.9 microseconds, observational only. The eight-case operations
measurement also passed its allocation contracts; retained full HUD/input allocates zero,
while bounded snapshot-copy allocation is reported separately. These CPU measurements
precede the final operations overlap correction; final focused tests and native output
cover that correction. There is no new texture, shader family or per-frame GPU owner.

## Build and package

Release solution build passed with zero warnings/errors. Full affected suites passed:
**893 Presentation tests**, seven existing opt-in skips, and **261 Client tests** with no
failures. Final operations/layout checks passed all **33** cases. Project reference
graph (42 projects), bundled font integrity, version grammar/allocation/idempotence and
one-PATCH SemVer checks passed. The first local overlapping build/test invocation hit
Windows file locks; the subsequent serialized build passed. That failed orchestration
attempt is not relabelled a successful build.

The clean-source Windows package passed the existing two-publish reproducibility check
and compiled-asset qualification (493 assets). Archive SHA-256:
`9b4cdef5b0c024c5ba2f555a8e26d7661eb10b432081103d72142c1c3107c27b`.
Startup and playable window-mode results are retained in the accompanying report.

GitHub CI on the qualified implementation passed the new HUD interaction step and
preceding build/version/font/layout checks, then reached the general Windows window-mode
smoke. Final current-head status is shown on the pull request; pending jobs are not
claimed as a complete green CI run. No outstanding review threads were present at the
last inspection.

## Reproduction

```powershell
ForgeLine.Client.exe --battlefield-hud-capture <empty-output> <runtime-root>
ForgeLine.Client.exe --battlefield-hud-timing <empty-output> <runtime-root>
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks --configuration Release -- --gameplay-ui-hotpaths <report.json>
dotnet run --project benchmarks/ForgeLine.Rendering.Benchmarks --configuration Release -- --operations-hotpaths <report.json>
```

Use the same adapter/driver and sequential workloads for timing comparison. Native
captures do not grant visual golden approval, physical-monitor certification, sustained
combat/motion acceptance or additional intelligence access.
