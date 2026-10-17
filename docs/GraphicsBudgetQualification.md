# Graphics budget qualification

Qualified product source: `7e4f6f70da61bdf8eba248e03d8bf1725d287a5b`, based on
`c98da7229d330b2ede30e2427053b87298d1f3de`. Version **0.1.392** (one PATCH).
The final documentation commit adds evidence only. Raw local logs and report hashes,
per-texture errors, batch identities and exact package provenance are indexed in
[evidence/graphics-budgets/qualification.json](evidence/graphics-budgets/qualification.json).

## Budget result

| Measurement | Before | Qualified | Original limit |
| --- | ---: | ---: | ---: |
| Compiled gameplay textures | 99,608,050 B | 57,665,206 B | 58,720,256 B (56 MiB) |
| Studio intro textures | 1,786,417 B | 1,786,417 B | 3,145,728 B |
| Native scene peak texture residency | — | 45,718,384 B | 67,108,864 B (64 MiB) |
| Peak shader-resource descriptors | — | 69 | 512 |
| Canonical instance draws | 15 | 9 | 12 for captured LOD counts |
| Production part instances | 24 | 24 | All retained |
| Production triangles | 14,728 | 14,728 | All retained |

The LOD-aware draw rule is unchanged: 11 base batches plus at most 11 additional
batches for each additional LOD, bounded by that LOD's instance count. Captures
contain one LOD1 instance and zero LOD2 instances. Terrain, shadow and composite
accounting also pass the unchanged validator. Nine refers to instance draws;
measured total scene draws are 65 windowed and 66 borderless.

Seven terrain base maps and four portrait pages use BC7 at unchanged 1024-pixel
runtime dimensions and unchanged mip policy. Source PNGs, UV regions and padding
are untouched. Core, icons, fonts and HUD frames remain lossless. All eleven
changed textures were independently compiled against RGBA references: minimum
RGB PSNR **50.0404 dB**, maximum and mean alpha error **zero**. The quality tests
retain a 32 dB floor and strict alpha limits. Native WARP readback verifies BC7
portrait upload, color sampling and release. The logical skin allocation stays
28 MiB while actual seven-page texture residency falls to 16 MiB.

## Native coverage

Windowed **1600 x 900** and borderless **5120 x 2160**, DPI 144, both pass the
original catalog, resident texture, descriptor, LOD draw and window-surface checks.
Both retain 24 textured production part instances, report nine instance draws and
zero texture/material binding failures. The native debug layer was requested but
unavailable; these captures do not establish debug-layer qualification. Short
smokes do not establish sustained FPS, subjective artwork approval or natural
match termination.

## Test results and open gate

Release solution build: **zero warnings and errors**. Focused mechanical, UI,
compressed-texture and native HUD coverage: **55 passed**. Draw-budget and
qualification-input PowerShell contracts pass, including missing/failed asset
reports, both path forms and unsafe settings roots.

The first full solution run executed 2,515 tests: 2,502 passed, two failed and 11
were skipped. Its only failures were obsolete exact residency expectations; these
were corrected to 16 MiB for HUD pages and 22,004,272 bytes for the unchanged 24
production PBR textures. Logical dimensions, asset counts and limits remain tested.

The final parallel full run on the qualified source executed **2,515 tests:
2,499 passed, five failed, 11 skipped** (11m14.994s, exit 2). Failures:

- `DirectorateBuildingPresentationTests.RepeatedProductionStateResolutionDoesNotAllocateAssetNamesPerFrame(command_core)`: 1,984 B versus zero expected.
- `NonModalHudInteractionTests.WarmMenuRoutingAllocatesNoManagedMemory`: 6,112 B versus zero expected.
- `RtsInformationLayerTests.RenderScratchRebuildsAcrossFogSelectionAndSessionChangesWithoutMutatingOwnedModels`: 7,960 B versus zero expected.
- `AssetPipelineTests.FollowingCleanWriterWaitsUntilBuildQualificationCompletes`: cleanup encountered a held `runtime.compile.lock`.
- `ClientStartupNativeTests.BrandedFramePresentsWhileAssetWorkerIsBlockedAndMenuWaitsForHandoff`: startup presentation exceeded its existing 30-second timeout.

One diagnostic rerun per affected project, serially and with collection
parallelization disabled, passed **15 presentation cases, one asset case and one
startup case**. No assertion, zero-allocation limit or timeout was changed. These
same cases passed in the earlier full run on identical product C# sources; the
intervening commits only corrected residency assertions and a comment. This
suggests load-sensitive behavior but does not establish a root cause. **The full
parallel-suite gate remains failed; isolated passes do not clear it.** Existing
11 opt-in skips remain visible. The pull request stays a draft.

## Reproducible package and installed smoke

`Publish-PreAlpha.ps1 -VerifyReproducible` produced identical file manifests from
two publishes of the clean qualified source. UI skin, four UI cues, 60 industrial
source clips and 56 world playback bindings pass package contracts. The archive
was safely extracted into a separate installation directory; **all 1,461 manifest
files** match SHA-256 checksums.

- Archive: `FORGELINE-0.1.392-dev-win-x64.zip`
- Size: **54,646,665 bytes**
- SHA-256: `148a182f36ba902a50f92d101564f51f0ba5881d05ab3ccffafc68bcc857ed99`
- Source: `7e4f6f70da61bdf8eba248e03d8bf1725d287a5b`, `sourceTreeClean: true`

The installed executable ran from an empty working directory with isolated
settings and the runtime-asset override removed. It exited zero, reached
application/session **Ready/Ready**, dropped zero startup events, wrote no stderr
and left its working directory empty. Its visual report independently passes the
original draw validator and texture/descriptor ceilings: nine instance draws,
45,718,384-byte peak residency and zero binding failures. Audio stopped with 50
blocks, zero failures/drops and no retained resources. No installed-start-path
replacement, merge, tag or release is implied.
