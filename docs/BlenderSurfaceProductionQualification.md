# Surface and LOD production qualification

Version **0.1.395**. The default content preserves the existing Main Battle Tank
and Command Core families; this is production-tool qualification, not a new art
quality approval. The [workflow](BlenderSurfaceProduction.md) describes supported
operations and recovery. [Machine-readable evidence](evidence/blender-surfaces/qualification.json)
contains source hashes, profile limits, compiled comparisons, resource measurements,
image hashes, package provenance and retained raw-log hashes.

## Source and environment

- Fresh base: `a47c7afd88373e50440887ca7be966e5c545da06`.
- Implementation, Release solution build, hardware captures and loaded resource
  comparisons: `24a625893050cd2adc01b8f8f202b62a9edc3407`.
- Additional automated generated-LOD compilation and clean reproducible package:
  `eb6a5366b2501adea342a66aca3742fa35b8b4b0`. That additional commit changes
  qualification scripts/documentation only; runtime source and production art do
  not change between the two revisions.
- Windows x64, .NET SDK 10.0.401; pinned Blender 5.2.2 LTS,
  build `d13f752e3b9c`, bundled Python 3.13.13, background/factory/offline execution.
- NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, 25,309,478,912 VRAM bytes.
  D3D debug layer was disabled/unavailable; no debug-layer acceptance is claimed.

## Executed gates

| Gate | Result |
| --- | --- |
| Standard-library source and production contracts | 46 passed |
| Actual Blender modifier, protected-part and generated-LOD cases | 11 passed |
| Release solution build | Passed, zero warnings/errors |
| Complete asset test project, serialized | 117 passed, zero failures/skips |
| Two independent production source and compiled exports | Byte-identical trees |
| Compiled geometry, metadata and mechanical batching | 38 mesh comparisons passed |
| Reduced LOD compiler + source-less validation | Passed |
| Runtime process locking/interruption contracts | Passed |
| Complete production asset catalog | 1,337 assets validated, unchanged source/cache |
| Headless smoke | 64/64 ticks, seed 12345, logical 20 Hz, 1,000 entities |
| Paired loaded resource smokes | Four passed |
| Native before/after images | Four cases, repeat pixels stable; details below |
| Reproducible package and installed player | Passed |
| Hosted WARP CI gate | Failed on both base and PR; details below |

The native compiled fact passes; the separate older round-trip fact is explicitly
skipped in this surface run because its own environment variable is unset. That
skip is not a missing surface check. The complete local solution test suite was
not run. Historical failures in other projects are not cleared by the focused
results here. The prior asset File.Move failure did not occur in this complete
117-test run; the earlier evidence remains in its original qualification report.

The final durable entrypoint was executed into
`artifacts/blender-surfaces-compiled-qualification`. Rendered authoring inspection
and earlier repeated checks used `artifacts/blender surfaces/final Grüße [a]`, also
exercising Unicode, whitespace and literal brackets in output paths. The native
qualifier now compiles its genuinely reduced fixture: **8 to 2 triangles**, **9 to
4 vertices**, authored UVs/tangents, zero generated/fallback channels, one section,
**1,035 container bytes**, followed by validation with an absent source directory.

## Geometry, material and resource baseline

The selected closure contains **29 assets: 19 meshes, six materials, four textures**,
**16,400 triangles** and **11 mechanical parts**. Six default jobs prepare the two
LOD0/1/2 families; other states, collision sources and dependency inputs are copied
unchanged. No result is automatically applied to production source or installed
into the standard runtime cache.

| Mesh family | LOD0 triangles | LOD1 triangles | LOD2 triangles |
| --- | ---: | ---: | ---: |
| Main Battle Tank | 6,272 | 892 | 244 |
| Command Core | 4,244 | 952 | 348 |

Default triangle, single-section and geometry-byte ceilings equal this measured
baseline. Texture texel/block payload remains **7,325,040 bytes**; the four compiled
texture containers total **7,328,479 bytes**, including metadata/headers. No texture
ID, atlas input, color-space declaration, mip setting or ordered material ID changes.
The compiled comparison preserves sockets, LOD references and canonical mechanical
batch assignments; maximum local position error is **3.7252903e-9**, maximum compiled
tangent-vector difference **6.017074e-6**, within existing tolerances.

All six default mesh silhouette comparisons produce IoU **1.0**. The seven 96x96
orthographic views use yaw/pitch degrees `(45,55)`, `(135,55)`, `(225,55)`, `(315,55)`,
`(0,0)`, `(90,0)`, `(0,90)`. These finite views and chart bounds do not substitute
for review of intentional future topology changes.

The same source client and isolated settings ran baseline and prepared catalogs at
each requested stress count, from empty working directories. Both catalogs produced
these identical resource results:

| Extra stress instances | Total instances | Visible instances | Instance draws | Peak texture bytes | Descriptors |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 651 | 10 | 9 | 45,718,384 | 69 |
| 1,000 | 1,551 | 121 | 9 | 45,718,384 | 69 |

All runs reached Ready/Ready, exited zero, left stderr and working directories
empty, and reported zero material/texture binding failures. Existing LOD-aware draw
validation, the 64 MiB scene-texture ceiling and 512-descriptor qualification ceiling
remain intact. These short loaded smokes record resource behavior; their timing
samples are observational, under concurrent asset compilation, and establish no
sustained frame-rate or throughput improvement.

## Visual review material

The native matrix uses High, 1920x1080, CloseTactical and Strategic, 32 warmups,
eight measured samples and two repeat captures per case. Every case repeats with
identical pixels and has zero fallback mesh/material/texture failures. Per-fixture
draw counts remain **29/29/15/36** before and after. Peak texture usage ranges from
47,912,856 to 47,937,128 bytes with 89..94 descriptors, exactly matching baseline.
Fixture draw counts are compared with their own production-scene baseline; they
are not the nine-draw loaded match case above.

| Native case | Before | After | Pixel result |
| --- | --- | --- | --- |
| Tank close | [PNG](evidence/blender-surfaces/before-MainBattleTankProduction-1920x1080-High-CloseTactical.png) | [PNG](evidence/blender-surfaces/after-MainBattleTankProduction-1920x1080-High-CloseTactical.png) | Exact file match |
| Tank strategic | [PNG](evidence/blender-surfaces/before-MainBattleTankProduction-1920x1080-High-Strategic.png) | [PNG](evidence/blender-surfaces/after-MainBattleTankProduction-1920x1080-High-Strategic.png) | Exact file match |
| Command Core close | [PNG](evidence/blender-surfaces/before-CommandCoreProduction-1920x1080-High-CloseTactical.png) | [PNG](evidence/blender-surfaces/after-CommandCoreProduction-1920x1080-High-CloseTactical.png) | Five pixels differ by at most one byte |
| Command Core strategic | [PNG](evidence/blender-surfaces/before-CommandCoreProduction-1920x1080-High-Strategic.png) | [PNG](evidence/blender-surfaces/after-CommandCoreProduction-1920x1080-High-Strategic.png) | Exact file match |

The Command Core close difference has mean absolute channel delta
**6.028163580246913e-7 byte values**. The zero-tolerance observational comparison
correctly records `WithinTolerance: false` for that case. No threshold was relaxed,
no accepted golden was created and no human visual approval was fabricated. The
image gate was not requested; human acceptance remains pending.

CPU Cycles authoring images use eight samples at 384x216 with consistent neutral
lighting and FOV-derived close/normal/strategic framing. All six images were
visually inspected for framing; they are previews, not renderer parity evidence.

| Preview family | Close | Normal | Strategic |
| --- | --- | --- | --- |
| Tank | [PNG](evidence/blender-surfaces/blender-unit.directorate.main_battle_tank-close.png) | [PNG](evidence/blender-surfaces/blender-unit.directorate.main_battle_tank-normal.png) | [PNG](evidence/blender-surfaces/blender-unit.directorate.main_battle_tank-strategic.png) |
| Command Core | [PNG](evidence/blender-surfaces/blender-building.directorate.command_core-close.png) | [PNG](evidence/blender-surfaces/blender-building.directorate.command_core-normal.png) | [PNG](evidence/blender-surfaces/blender-building.directorate.command_core-strategic.png) |

## Package and installed execution

`FORGELINE-0.1.395-dev-win-x64.zip`, **54,646,381 bytes**,
SHA-256 `bc6b75fd7b8a67cf2298b0de9b7d72e66a4254100321bbc095ff9a32dcac7d12`.
Two publish trees compare identically; all **1,461 extracted file checksums** pass.
Provenance identifies the clean `eb6a5366` source revision. The package contains
original qualified runtime art and no `.blend`, Python authoring file or Blender
executable. An invalid Blender override does not affect the installed player.

Installed execution from an empty working directory, with isolated settings and no
runtime override, passes Ready/Ready, zero exit/stderr/dropped startup events and
unchanged resource budgets: nine instance draws, 45,718,384-byte peak texture
residency, 69 descriptors, zero binding failures. No release/tag or content migration
was performed. An initial absolute package output argument was rejected by the
existing containment guard before publish; the supported relative invocation
passed and both logs were retained.

## CI limitation and remaining decisions

[PR run 39057703528](https://github.com/DaveBeusing/Forgeline/actions/runs/39057703528)
passes build, Python contracts, launcher protocol, asset compilation and the earlier
native material/UI contracts, then fails `Native WARP reference capture` with:
`GraphicsDeviceException: GPU fence did not complete within ten seconds`.
[The unchanged master run 39056262782](https://github.com/DaveBeusing/Forgeline/actions/runs/39056262782)
at the exact base SHA fails in the same step with the same error. The renderer,
capture helper, production sources, timeout and WARP scenario are unchanged by
this PR. The failure is evidenced on the baseline; its underlying cause is not
established here. No gate, timeout, assertion or budget was weakened or retried
until green.

The navigation, frame-contract and CPU-scale jobs pass. Hosted full-test, package,
installed-player and full-match steps after WARP are skipped, not passing results.
The manual Blender workflow was not dispatched; its expanded path was executed
locally. A later evidence commit starts normal CI again; this recorded failure is
not erased by a new pending run. Keep the PR draft while the WARP gate and human
visual review remain open. Natural match termination, sustained timing and the
entire display/quality matrix are outside the executed evidence.

## Reproduction

Build Release first, then run the commands in the workflow guide. The native image
pair used the existing capture helper with original `assets/runtime` and the
qualifier's sealed `overlay/runtime`, selecting `MainBattleTankProduction` and
`CommandCoreProduction`, High, CloseTactical/Strategic, 1920x1080, warmups 32, samples
8 and repeats 2. Compare with `Compare-VisualReferenceEvidence.ps1 -ComparePixels`;
no human image gate is implied. Loaded smokes use `--smoke-test --skip-splash
--render-stress 100` or `1000`, explicit settings/startup/visual output paths, then
`Validate-VisualDrawBudgets.ps1` plus the unchanged texture/descriptor ceilings.

Package with `Publish-PreAlpha.ps1 -VerifyReproducible -RuntimeAssetRoot assets/runtime`
and a fresh repository-relative output root. Verify the extracted `manifest.sha256`
and run the extracted client from a fresh empty working directory. Keep separate
outputs and logs for failures; never overwrite a qualified project to retry it.
