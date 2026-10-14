# Typography qualification

## Method and native evidence

The renderer was qualified on Windows with an NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU and separately exercised on D3D12 WARP by tests. The native capture report pins its compiled informational version/source revision. It covers 1280×720, 1920×1080, 2560×1440, 3840×2160 and 3440×1440; each resolution runs all combinations of requested 96/144/192 HUD DPI sampling and 0.75/1/2 UI scale, for menu and an IndustrialBase HUD over real terrain: 90 captures, each checked against a repeated readback. Every case passed, with no missing or dropped frontend/HUD glyphs and no graphics submission faults. The actual window DPI on this machine was 240; requested DPI sampling is not a claim of physical-monitor certification at those settings.

The capture command also exports loading, settings, controls and pause. Visual inspection checks readable regular/bold glyphs, German accents, arrow/digit/punctuation coverage, focus/disabled treatment, panel text limits and text contrast over dark menu and busy terrain. This is native rendered evidence, not an accepted cross-driver pixel golden. Existing reviewed bitmap geometry fixtures remain explicitly bootstrap/debug fixtures; they were not replaced by new hashes and are not presented as atlas evidence.

Machine-readable captures and representative native PNGs are retained under `docs/evidence/ui-typography/`. The usage guide documents how to reproduce the complete matrix from the Windows player.

## CPU and requested upload comparison

Values below are medians across the declared cases, not frame-rate claims. Timings are one-host CPU measurements with `DOTNET_TieredCompilation=0`, 128 warmups and 256 samples; no GPU execution, copies, Present or waits are included in these CPU measurements. Frontend comparison uses the same final layout and palette, with the explicit bitmap debug backend versus the normal atlas backend. The dense-HUD baseline was taken from the inspected master implementation before text adoption, across the same 120 selection/action/focus/hover/pressed/pending cases.

| Workload / path | Vertices | Requested upload bytes/frame | P50 CPU μs | P95 CPU μs | Warm allocations |
| --- | ---: | ---: | ---: | ---: | --- |
| Main menu, bitmap debug | 19,482 | 467,568 | 60.3 | 66.1 | 120 B/frame |
| Main menu, atlas | 1,704 | 50,976 | 17.8 | 18.6 | 0 |
| Dense HUD, bitmap master | 37,590 | 902,160 | 64.95 | 84.6 | 0 |
| Dense HUD, atlas | 4,824 | 133,632 | 46.7 | 57.25 | 0 |

Menu vertices drop about 91%, requested uploads about 89%. Dense-HUD vertices drop about 87%, requested uploads about 85%. The final renderer avoids measuring an untruncated line twice. Frame upload/draw calls rise from one to two for the measured menu and five to ten for the dense HUD. The immutable shared font texture is 8 MiB; each surface has a bounded 8,192-glyph batch and up to four upload slots. It introduces no warm managed allocation in these cases. Capture/readback durations in the native report include waits and are explicitly not isolated text GPU timings. Results do not establish a cross-machine FPS or GPU-time speedup.

An initial pre-change HUD run using the default tiered runtime failed its allocation guard by 128 bytes. The controlled rerun using the CI runtime configuration passed all 120 cases; the failure is not counted as a passing run. A local full-suite run also saw one unchanged asset duplicate-manifest recovery case fail under concurrent workload; the entire 25-case recovery class passed on retry, and the subsequent full-solution run passed. A simultaneous local build/test attempt encountered an output-file lock; the clean follow-up build passed without warnings.

## Resource/publish correction

An intermediate implementation exported an unused 32 MiB RGBA catalog copy of the font atlas. CI correctly rejected the resulting catalog footprint against its unchanged 56 MiB budget. The unused catalog entry was removed; the current 492-asset catalog is unchanged by font packaging. The player instead uses the precompiled embedded coverage/metrics contract, with source-font/PNG/alpha hashes and redistribution notice validation at CI and publish time. No budget was relaxed, and the font TTF files and authoring dependencies are excluded from shipping output.

## Checks

The full-solution verification completed with 1,931 passed, zero failed and nine existing opt-in hardware/performance skips (1,940 total). Targeted font coverage, scalar-safe fitting/wrapping, kerning, alignment/baseline/ellipsis, batch bounds, allocation, texture sharing, failed buffer replacement and font-fallback ownership tests pass. Client coverage includes viewport-fit geometry with pointer targets at all declared frontend sizes/scales and existing focus/session/interaction contracts. Graphics R8 row/payload validation and native sampling pass. Asset authoring/recovery and project/version-boundary checks pass.

Packaged-player reproducibility/startup evidence is recorded alongside the native evidence after publishing the qualified source revision. CI results must be read at the current PR head; historical failed or superseded runs are not passing evidence.
