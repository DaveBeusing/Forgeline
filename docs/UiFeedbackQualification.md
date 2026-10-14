# UI feedback qualification — 0.1.368

Qualified package source: `4642b5fe71677b33a01d76d31a82ec72a396d704`.
Starting master: `b86c2eb25178594fe0ecbe665c46ab12a7280da4` (0.1.367).
One PATCH increment covers the complete pull request; no release or merge is performed.
See [motion, settings and audio ownership](UiMotionAndAudio.md).

## Behavior and review rationale

Text, focus identity, enabled state and hit rectangles change immediately. Short
accent reveals and in-place focus rails explain navigation without moving controls.
The first menu/modal frame remains actionable while decoration begins. Low quality,
software frontend fallback and reduced motion settle immediately. Resize, focus loss,
minimize, modal transitions, time rollback/stalls and session replacement cancel
decoration rather than queue an old transition.

Neutral pending intent remains separate from copied accepted/partial/rejected
results. A monotonic consumed-result watermark, coherent session/tick checks and
silent re-establishment after interruption prevent old or future results from
generating success cues. Partial/rejected retain explicit text/counts and warning
sound; accepted request feedback does not promise completed work or delivery.

The audio integration probe found a real shared-gain issue: muting one legacy
stream changed a second stream's reported volume from `FFFFFFFF` to `00000000`.
The studio owner now restores its opening gain before closing. The final packaged
probe observed **4294967295 → 0 → 4294967295** across studio open/mute/disposal.
Studio content, fade/skip timing and startup sequence are retained. UI software gain
does not adjust studio/system volume. This device-specific result is recorded,
not inferred from a successful buffer submission.

## Executed validation

- Release solution build: zero warnings/errors at runtime revision `02b5d910`.
- Complete Presentation suite: **902 passed**, seven existing opt-in hardware skips.
  Complete Game suite: **393 passed**. Their runtime sources are unchanged afterward.
- Final complete Client suite after the audio interoperation correction: **273 passed**.
  The final probe-report labeling change adds no client behavior change.
- Focused entry/lifecycle/interaction checks: **26 passed** after the first-menu fix.
  Injected time, 30/60/144 Hz end states, reduced/Low/software paths, zero warm
  motion/feedback allocation, pending/result separation, stale/future/older results,
  session replacement, missing clips, muted output and additive settings defaults
  are covered. Existing reviewed geometry candidates were not regenerated.
- Project graph (42 projects), version grammar, isolated allocation/idempotence,
  exact one-PATCH SemVer, whitespace, PCM/provenance and font contracts passed.
- The final clean-source Windows x64 package passed two-publish reproducibility,
  493 canonical runtime-asset qualification, four exact PCM cue copies and notices.
- A fresh extracted package reached menu **Ready** with interface sound enabled.
  An earlier extracted source `a4431e2` also quarantined invalid UI volume 2 and
  reached Ready using the existing recovery/default path. Test roots were isolated.
- Final installed 1,000-instance stress smoke passed Windowed **1600×900** and
  BorderlessFullscreen **5120×2160** at 144 DPI, with matching submitted/presented
  frames, no pending resize/occlusion and zero texture/material binding failures.

Archive SHA-256:
`206ad84c3abf0d3ce4c1a7a5e25b875f341f7eacc0c1cac9f01df8aa57e78e30`.
[Package provenance](evidence/ui-feedback/package-provenance.json) records the exact
source, clean tree, SDK and compiled asset signature. The archive remains local.

## Native sequences and costs

The final package passed **60 sequences**, each twice: Menu / Panel / Dock / Accepted /
Rejected, normal/reduced motion, 30/60/144 Hz injected presentation rates, at
1280×720 / 192 layout DPI / scale 2 and 1920×1080 / 144 layout DPI / scale 1.
Three sampled images per sequence repeat exactly. Normal decoration visibly changes;
reduced motion does not; every final image equals the corresponding non-animated
state. All glyphs are present and no submission faults occur.

Dock sequences retain the frozen BattlefieldCombat fixture and its intelligence.
Command states are explicitly authored owned copied data and never dispatched.
Frontend sequences use the existing schematic fallback, not a cinematic-menu-motion
approval. The actual monitor is 144 DPI; layout DPI is not physical certification.

All four original PCM cues played, rejected immediate busy overlap, restarted after
an in-flight stop and released native buffers. The studio shared-gain restoration
probe also passed. This establishes device/lifetime behavior, not subjective sound
quality or support for every driver/device-removal case.

Short sequential native CPU submit/GPU cohorts are in the full report. Submit time
includes waits/Present; selected readbacks are outside the timed submit call.
These small cohorts have variable power/cache conditions and no matched pre-change
performance baseline. Injected rates are sampling inputs, not measured live FPS.
The final stress smoke measured about **30.8 FPS** windowed and **2.6 FPS** at
5120×2160 borderless. The latter is slow; neither run establishes sustained 60 FPS
or a performance improvement from this change.

## Evidence and reproduction

- [Native sequence hashes, timing, device and audio interoperation](evidence/ui-feedback/native.json)
- [Sanitized startup/recovery and window reports](evidence/ui-feedback/startup-and-window.json)
- [Panel reveal at compact high scale](evidence/ui-feedback/panel-reveal.png)
- [Authoritative rejected-state reference](evidence/ui-feedback/rejected-result.jpg)

The panel PNG is an unchanged native image. The JPEG is a format-only derivative
of an unchanged full-resolution native PNG; original pixel hashes remain in the
native report. Neither image is an accepted pixel golden or a composite mockup.

```powershell
ForgeLine.Client.exe --ui-feedback-capture <empty-output> <compiled-runtime-root>
pwsh ./build/Validate-UiAudio.ps1 -PublishRoot <installed-root>
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Client.Tests.UiInteractionFeedbackTests
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.UiMotionTests
```

## Remaining acceptance and CI

Human motion feel/listening, physical-monitor transitions, complete live command
journeys and sustained populated-match pacing remain unqualified. Initial durations
are engineering values retaining the existing 140 ms entry interval, not completed
usability approval. No golden approval, 60 FPS guarantee or real device-removal
qualification is claimed.

At final package inspection, functional-head Frame hot paths, Scalability and Main CI
were still running. Main CI had reached canonical asset qualification; no
implementation failure was reported at that inspection. Earlier superseded runs
were cancelled by later commits and are not counted as final success. Inspect the
current PR checks after the evidence commit; keep draft until required checks and
human acceptance are resolved.
