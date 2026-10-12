# Early native bootstrap qualification

Recorded on 2026-10-11. This report qualifies the published client startup path;
it does not establish a portable startup SLA or cold-cache acceptance.

## Revisions and conditions

- Baseline: master `1e63a8e41f38278252a44b64ce5c74261744004c`, version 0.1.344.
  An untracked observation-only hook exits after the existing input owner observes
  a presented interactive menu. It does not alter the baseline initialization path.
- Updated packaged client: `ca9df3a9b0a0f36129b01ce431c81cefad477515`, version
  0.1.345-dev. Later qualification-script and documentation changes do not change
  that executable. Release and Debug use separately published output.
- Windows build 26200, .NET 10.0.12; Intel Core Ultra 9 285HX, 24 logical processors;
  NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653;
  approximately 63.5 GiB visible physical memory. Storage/cache topology was not
  controlled for this observation series.
  Both configurations selected the hardware adapter, feature level 12_1,
  1600 x 900 window, three buffers, VSync, graphics debug layer disabled.
- Identical precompiled runtime manifest for both revisions: 341 assets,
  SHA-256 `5F0B6DBA8E2CDA868C1609054A95B027204CE19BEB8B8A8D23DEB44ADDCB082A`.
  No source compilation is part of these launch samples.
- Each group excludes one warmup and records five subsequent process launches.
  Settings, assets and display configuration remain fixed. No reboot or cache
  eviction occurred. Other CPU work on this machine was not fully isolated;
  these sequential groups are observations, not a controlled general benchmark.

## Warm results without studio playback

Times are milliseconds from managed entry unless explicitly marked external.
P95 uses nearest rank; with five samples it is the observed maximum.

| Configuration / milestone | Baseline median | Updated median | Baseline P95 | Updated P95 | Updated range |
| --- | ---: | ---: | ---: | ---: | --- |
| Release native window visible | 695.4 | 271.5 | 1069.7 | 344.4 | 256.6–344.4 |
| Release native branded paint | unavailable | 271.7 | unavailable | 344.7 | 256.8–344.7 |
| Release first D3D12 presentation | 2754.4 | 2697.8 | 2937.0 | 2918.0 | 2423.5–2918.0 |
| Release runtime manifest ready | 1107.5 | 1072.2 | 1584.4 | 1142.0 | 1032.4–1142.0 |
| Release interactive menu | 3936.9 | 2784.4 | 4206.2 | 2989.0 | 2479.7–2989.0 |
| Release external process launch to HWND observation | 1478.6 | 693.0 | 2011.6 | 862.9 | 628.8–862.9 |
| Debug native window visible | 498.2 | 251.2 | 1415.6 | 324.4 | 220.8–324.4 |
| Debug native branded paint | unavailable | 251.6 | unavailable | 325.1 | 221.2–325.1 |
| Debug first D3D12 presentation | 1909.8 | 1965.1 | 3517.1 | 2712.6 | 1828.5–2712.6 |
| Debug runtime manifest ready | 842.2 | 988.2 | 1770.8 | 1197.5 | 816.8–1197.5 |
| Debug interactive menu | 2613.4 | 2009.8 | 4183.7 | 2776.4 | 1878.0–2776.4 |
| Debug external process launch to HWND observation | 902.8 | 477.0 | 2762.2 | 777.3 | 436.9–777.3 |

Release menu median improved approximately 29%; Debug approximately 23%.
The early branded paint is now measured before settings, runtime asset work and
boot graphics initialization. Baseline window visibility meant a black native
background; its first graphics presentation is not a native branding milestone.
The 100–300 ms aspiration is met by the updated managed-entry median, not every
sample and not external process-to-visible-pixel timing. Native painting completion
does not measure monitor scan-out. External HWND polling includes runtime startup
and observation latency; it also does not prove first visible pixels.

## Warm results with studio playback

Release groups each contain three measured launches after one excluded warmup.
Every launch records an actual StudioSplashFirstFrame; fallback-only playback
cannot pass this qualification. The existing approximately ten-second intro
remains skippable and contributes to these menu times.

| Milestone | Baseline median | Updated median | Baseline P95 | Updated P95 | Updated range |
| --- | ---: | ---: | ---: | ---: | --- |
| Native branded paint | unavailable | 257.1 | unavailable | 276.7 | 141.7–276.7 |
| First studio video presentation | 3086.9 | 3281.8 | 6056.0 | 3472.4 | 2865.7–3472.4 |
| Interactive menu after playback | 14602.5 | 13599.9 | 17065.4 | 13746.1 | 13070.3–13746.1 |

The updated video-start median is slightly later in this small sample. The
improvement is immediate independent branding and a shorter post-intro path;
these results do not claim faster video decoding or reduced intro duration.

## Validation

- Canonical Release solution build: zero errors and warnings.
- Full serial Release suite at `24ecd80`: 1730 tests, 1721 passed,
  9 optional skips, no failures. Duration 16m16.658s.
- Final Client suite at packaged revision `ca9df3a`: 245 passed, no failures or
  skips. Includes bootstrap Escape cancellation and queued-input draining.
- Native platform coverage includes same HWND through configuration/handoff,
  failure repaint, owner-thread enforcement, borderless mode, recreation and
  synthetic 96/144/192/288 DPI transitions. This does not replace physical display
  movement or manual Alt+Tab/taskbar acceptance.
- Project-reference checks, version syntax/allocation/SemVer checks and whitespace
  checks passed. The PR increments PATCH once from 0.1.344 to 0.1.345.
- Release and Debug reproducible package verification passed: independently
  generated publication trees match in each configuration.
- All updated warm samples report application Ready, session NotRequested,
  zero dropped startup events, native feedback before heavy phase starts and a
  presented interactive menu. Gameplay is not constructed to qualify menu timing.
- Published Release runs with an authoritative missing runtime root and a corrupt
  runtime manifest both terminate with exit code 1, application Failed, native
  bootstrap milestone and StartupFailed, without menu interactivity. The terminal
  checkpoint and stderr retain the failure. Native failure repaint is covered by
  the platform tests; these automated exits do not qualify manual error readability.

## Evidence and reproduction

Local evidence is retained under `artifacts/bootstrap-release-before`,
`bootstrap-release-after`, `bootstrap-debug-before`, `bootstrap-debug-after`,
`bootstrap-studio-before`, `bootstrap-studio-after`, and
`bootstrap-package-failures`. Each qualification group contains its summary,
per-launch structured checkpoints and stdout/stderr. Reproducible packages are
under `artifacts/bootstrap-release-final` and `artifacts/bootstrap-debug-final`.

```powershell
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/bootstrap-release-final/win-x64/publish/ForgeLine.Client.exe -Samples 5 -OutputRoot artifacts/startup-release
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/bootstrap-debug-final/win-x64/publish/ForgeLine.Client.exe -Samples 5 -OutputRoot artifacts/startup-debug
pwsh ./build/Invoke-StartupQualification.ps1 -ClientPath artifacts/bootstrap-release-final/win-x64/publish/ForgeLine.Client.exe -Samples 3 -IncludeStudioIntro -OutputRoot artifacts/startup-studio
```

BaselineExitHook is only for a locally instrumented comparison executable;
ordinary published clients use the supported menu smoke argument.

## Remaining acceptance

Cold Release/Debug observations require a controlled reboot/cache protocol.
Physical second-monitor placement, mixed DPI movement, Alt+Tab/minimization and
taskbar behavior need manual display qualification. A saved window size/mode may
be applied once after settings load, on the existing HWND. There is no artificial
bootstrap duration. The required build-test workflow must pass on the final PR
head before integration; local success is not a substitute for that gate.
