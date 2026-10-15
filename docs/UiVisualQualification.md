# Native frontend and HUD qualification

## Decision and sources

The production reference lane now covers named frontend and HUD states, immutable
native images, bounded regions/text, sampled overlay costs and explicit human
baseline review. **Structural qualification passes; visual, physical-monitor and
sustained-performance acceptance remain pending. No native golden is approved.**

| Evidence | Exact source |
| --- | --- |
| Fresh master baseline, version 0.1.368 | `4f9d8605e4ef0ee1d716e7e9dfd048d209fbb157` |
| Complete UI image matrix, version 0.1.369 | `61f67dfa6f81031b9d798ea92c4410393f84763f` |
| Capture lifecycle/atomic-manifest hardening and final installed verification | `dcdd5b81b8e076e3a0d80baff3fcd9306b302459` |
| Original pause comparison candidate | `8ba1892b1b4e2b276431694da0ff6063e58bfa87` |

The last source adds capture-only execution requirements, atomic progress metadata,
actual host-window DPI, failure-stage diagnostics and stricter failed-root rejection.
It does not change the rendered UI or simulation. Eight additional installed native
cases exercise that exact source; the 1,305 image records retain their original
source rather than being relabelled as a later build.

## Complete native matrix

The matrix is 29 states x five outputs x three raster DPI values x three UI scales:
**1,305 distinct cases and 2,610 exact fixed-state readbacks**. Medium is requested,
with three warmups and eight timed frames per case. The frontend uses the compiled
cinematic artwork; the separate Low/WARP subset exercises schematic fallback.

| Raster DPI | Complete requests | Qualified cases |
| --- | --- | ---: |
| 96 | One request across all five displays | 435 |
| 144 | One request across all five displays | 435 |
| 192 | Five independent complete requests, one per display | 435 |

Outputs are 1280x720, 1920x1080, 2560x1440, 3440x1440 and 3840x2160. Requested
scales are .75, 1 and 2. Every case has the requested native dimensions, identical
repeated pixels, complete atlas text, bounded regions, zero texture/material binding
failures, zero fallback meshes and zero submission faults. The union is checked for
exact coverage and duplicate keys. No failed or partial request contributes a row.

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653,
Windows build 26300, .NET 10.0.12, SDK 10.0.401; optional D3D12 debug layer absent.
The actual capture HWND was independently observed at 144 DPI during all three
cohorts. Raster DPI is an explicit layout input; the small host window does not
certify physical 4K or real monitor-DPI transitions.

The installed runtime fingerprint is
`C50A837AD74D0A8F3F95EF00947499F69957D6CBA89185DEFA1E07FABD189B39`.
Source, assembly hashes, driver, camera, per-region hashes, glyph/upload counts,
vertices and distributions are retained in the [summary](evidence/ui-reference/summary.json)
and [96 DPI](evidence/ui-reference/native-dpi96.json),
[144 DPI](evidence/ui-reference/native-dpi144.json) and
[192 DPI](evidence/ui-reference/native-dpi192.json) records. Each request's original
manifest SHA256 is retained. These are selected nonvisual records, not replacements
for the raw manifests or approved baselines.

Raw originals remain in `artifacts/ui-reference-stable-dpi96`,
`artifacts/ui-reference-stable-dpi144` and the five
`artifacts/ui-reference-dpi192-<width>x<height>` roots. The repository keeps only
three original PNG examples and compact measurements, not the large raw image set.

## Before/after and discovered defects

Native inspection found HUD text entering gaps between separate pause header/title
surfaces. A continuous quiet navigation backplate closes those gaps while preserving
world context beside the menu. Input regions and existing geometry fixtures remain
unchanged. [Before](evidence/ui-reference/pause-before.png) and
[after](evidence/ui-reference/pause-after.png) are original 1280x720 / 192 DPI /
scale 2 PNGs, copied byte-for-byte. [Menu example](evidence/ui-reference/menu.png).

The [UI comparison](evidence/ui-reference/ui-before-after.json) reports 29 matched
before/after cases with exact pixel deltas and region changes. Changes include
cinematic versus schematic art, source/version footer text and corrected fixtures;
this cross-source diagnostic does not inherit baseline approval. The original
master world images are **byte-for-byte identical** at 1080p and 4K, with matched
canonical runtime payloads and zero changed pixels; see the
[world comparison](evidence/ui-reference/world-before-after.json).

The native pair check exposed a hover fixture aimed at a technology-locked card,
then a focused card whose focus correctly took precedence. Hover/pressed now use
a separate enabled construction card. Targeting uses an explicit active tactical
view. Named interaction pairs must visibly change native pixels in the same scope.

German culture exposed a fractional-scale ValidateSet conversion error. Numeric
validation now accepts .75/1/2 under de-DE and en-US and rejects unsupported values.

Two long runs remain failed and unchanged: 397 completed rows before Present
occlusion, and 353 before an observer's read handle blocked manifest publication.
Neither enters the complete matrix. Thread-owned display/system requirements now
limit idle display suspension during explicit capture; user lock/sleep and occlusion
still fail capture. Atomic file replacement preserves a reader's complete old
snapshot. The regression test keeps an old read handle open while publishing a new
manifest. [Failure provenance](evidence/ui-reference/provenance.json).

## Observed cost and validation

| Surface group | Cases | UI vertices | CPU overlay p50 range, ms | GPU overlay p50 range, ms |
| --- | ---: | ---: | ---: | ---: |
| Frontend, including pause | 630 | 600..2,154 | .0228..2.2186 | .10768..3.718528 |
| HUD | 675 | 61,866..64,818 | .3629..1.8408 | .065664..3.20192 |

Maximum sampled warm frame allocation p99 is 296 bytes in either group; this includes
the reference host/graphics diagnostics and is not a claim of zero total frame
allocation. Readback, PNG encoding and export are excluded from timings. The short
three/eight window does not establish sustained FPS, thermal behavior, causal speedup
or a portable performance budget. Existing strict hot-path allocation assertions
were not relaxed.

Full solution validation at `0d3e13a7` passed 1,968 tests with nine existing opt-in
skips. After the pause change, complete Presentation validation passed 902 with
seven skips. One earlier rerun observed 7,072 bytes in the vehicle-factory allocation
test; its isolated 26-case cohort and unchanged standalone full suite then passed.
The observation is retained and is not labelled a reproduced master failure.
Final complete Client validation passed **282**. The 12 focused capture, owner-thread,
exception cleanup, atomic-reader and request-contract cases passed. Architecture
(42 projects), version grammar and the single PATCH step pass.

The Low/WARP 1280x720 / 96 raster DPI / .75 scale subset passed two frontend cases.
It is software functionality evidence only. The native resource lifetime case without
the debug layer passed; the debug-required variant could not start because the
optional layer is unavailable. No debug-clean claim is made.

## Fresh installed package and open acceptance

Both matrix and hardened Windows x64 packages pass two-publish reproducibility,
493-asset qualification, fonts, original PCM copies and notices. Matrix package SHA256:
`8a2d9f7f399197035f44e2ea05c20168148cb1128f0777d3a62311dad6716fa5`.
Hardened package SHA256:
`2aab28cd144e58d0883f3939e86d9b3b376dc26b3d13cb1b1647d68f7ee84232`.
See [package provenance](evidence/ui-reference/final-package.json).

The newly extracted hardened package reaches application Ready with no match
requested. Eight native 720p/4K cases repeat exactly and record actual host DPI 144.
The installed 1,000-instance stress smoke presents 70/70 frames at windowed
1600x900 and 20/20 at borderless 5120x2160, with zero binding failures. Observations
are about 4.38 FPS / 225.11 ms CPU / 15.34 ms GPU and 3.32 FPS / 298.06 ms CPU /
34.60 ms GPU respectively. This synthetic stress includes fallback proxy meshes;
it is not natural full-match art or sustained 60 FPS acceptance. The throughput is
slow and remains a performance gap. [Installed verification](evidence/ui-reference/installed-verification.json).

Human typography/hierarchy/contrast/color-vision decisions, keyboard/pointer live
journeys, listening/motion, physical monitors and sustained populated-match pacing
remain pending. Use the [review handbook](UiVisualReview.md) and export pending
reviews from the original complete roots. No automated process fills real approval
decisions. Keep the PR draft until these reviews and required current-head CI are
resolved; source/build/runtime/device mismatches cannot silently inherit a golden.
