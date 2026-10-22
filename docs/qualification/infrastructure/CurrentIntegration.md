# Current master and installed-player integration

## Master synchronization at 0.1.440

PR #285 integrates master `8647d39aafe642661589ea688ce1c11e95f53135`
(version 0.1.439). The PR version is 0.1.440, exactly one PATCH above that base.
The complete master scene-ID range is preserved: existing scenes remain 0–106,
and the nine integrated decal scenes remain 107–115. The fifteen infrastructure
scenes follow at 116–130 in their original relative order. Both scene factories
and the decal-specific terrain profile are retained.

Infrastructure scene fingerprints include the numeric scene ID. Their IDs move
by nine positions to preserve the already integrated decal identities. This
integration also inherits the current master's decal atlas and material changes.
The native images, timing samples, package checksum, runtime catalog counts and
source receipts below describe the earlier 0.1.439 source boundary; they are
retained historical evidence, not a fresh qualification of the merged 0.1.440
catalog or its infrastructure fingerprints. No native GPU, installed-player or
Blender qualification has been repeated for this synchronization.

Regression coverage preserves the integrated numeric scene identities and checks
that the historical numeric decal selection and appended infrastructure scenes
still dispatch to their correct factories and terrain-profile behavior.

## Retained 0.1.439 qualification

Source revision `8e85a329a9a8d86497f432ba904703f3f95c1dda` integrates master
`34c69dd97db417d1982fd7bf75cb1baa197082f6` and version 0.1.439, exactly one
PATCH above master 0.1.438. Subsequent evidence-only commits do not change the
qualified production files. Existing vegetation and prop scene IDs are preserved;
infrastructure scenes occupy the following contiguous range.

The six shared transition definitions and their upstream native masters are
reused unchanged from #283. No assets/source/world file differs from this base.
[SharedTransitions.json](SharedTransitions.json) records exact positions,
normals, tangents and topology; maximum UV rounding is 2.981e-8.
[CurrentSourceIdentity.json](CurrentSourceIdentity.json) binds the two active
native road/bridge masters and twelve GLBs to their completed real Blender
qualification. The older three-master receipt remains historical.

The current Release solution builds with zero warnings/errors (1m55.25s).
All 127 portable Blender contract tests pass (53.724s). All 26 focused
infrastructure and prop-scene tests pass without skips (24.673s), including
unchanged authoritative traffic, source bindings and physical crossing checks.
Runtime process contracts, the 44-project reference graph and canonical map
qualification pass: reachability 71, two alternate navigation and two alternate
road routes. The current catalog qualifies 1,363 assets, 309 meshes, 901 textures,
132 materials, 18 animations, two skins and one UI skin: 74,844,005 total bytes,
11,306,611 mesh bytes, 59,454,336 texture bytes. Gameplay texture bytes remain
57,667,919 under the unchanged 56 MiB gate.

## Native integration and installation

Eight current-source hardware cases pass, each repeat-stable with no fallback
meshes or material binding failures: 30m bridge/ford/curve detail, road gallery,
and actual cargo bridge/ford approach and exit snapshots. All eight are
pixel-identical to the corresponding final-after frames in the original paired
matrix, despite integrating later vegetation/prop sources. This is selected
integration equivalence, not a new complete performance baseline.

[PackageIntegration.json](PackageIntegration.json) binds the reproducible archive,
clean source revision, runtime signature and four installed/local image pairs.
[Integration samples](integration-samples.json.gz) retain complete native result
values for eight local and four installed cases. Only checkout/install path
prefixes are normalized in this derived copy. The device/settings match the
recorded native matrix; debug layer unavailable, hardware rendering.

Two deterministic publish trees pass the repository's reproducibility comparison.
Archive SHA-256:
`fe33702ceb79d5ffd982dbdd99fb600e17d6502e204b50d677a84128bccc0d34`.
A fresh extraction outside the checkout verifies all 1,487 manifest checksums.
Source-less compiler validation resolves all 1,363 runtime assets. No Blender,
source GLB/glTF/master or asset compiler is packaged. The installed client runs
from an empty directory with private APPDATA/LOCALAPPDATA. All four installed
PNG files and pixel hashes exactly match the local packaged-client captures.

| Current original image | Retained file |
|---|---|
| Bridge deck, 30m | [image](current/bridge.png) |
| Ford deck, 30m | [image](current/ford.png) |
| Connected curve, 30m | [image](current/curve-long.png) |
| Road gallery, 420m | [image](current/roads.png) |

```powershell
./build/Publish-PreAlpha.ps1 -Configuration Release -RuntimeAssetRoot assets/runtime -VerifyReproducible -OutputRoot artifacts/infrastructure-package-439
./build/Invoke-RtsReferenceQualification.ps1 -Capture -ClientPath artifacts/infrastructure-package-439/win-x64/publish/ForgeLine.Client.exe -RuntimeAssetRoot assets/runtime -Warmups 8 -Samples 32 -RepeatCaptures 2 -Profiles High -Displays 1920x1080 -Scenes InfrastructureBridgeDeck,InfrastructureFordDeck,InfrastructureCurveLong -Zooms CloseTactical -InspectionDistanceMeters 30 -YawDegrees 90 -OutputRoot artifacts/in/current439/details
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

The complete serialized solution test run and final fixed-seed terminal run are
reported in the PR validation results. The earlier vegetation-base solution run
was deliberately interrupted when master #283 superseded its source boundary;
that incomplete cohort is not counted as a pass. Full-suite and hosted CI
results are separate from native Blender and physical-device qualification.
The opt-in Blender hosted workflow remains undispatched.

Keep the PR draft. Human art and continuous-motion review, sustained FPS,
enclosing-frame cost, thermal stability, long-session resource churn and renderer
lifecycle acceptance remain open. Existing short performance samples contain
some regressions; no resource gate or budget is increased. No merge, tag,
release or new golden is authorized by these technical results.
