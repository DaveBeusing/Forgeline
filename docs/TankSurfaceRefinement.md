# Tank armor finish and native review

Current qualification base is `a391a5bb0c0e25f079fa394613a5f84dfe7e9728`, version 0.1.432.
The PR prepares exactly one PATCH to 0.1.433. The initial audit used
`a6fe118583be842e00416bcb100d1210f3240a00` before logistics PR 273 integrated.
The saved tank, seven Blender exports and shared 4K PBR atlas already exist.
Tank PR 231 and procedural-atlas PR 235 are integrated; their historical native
observations do not establish acceptance of the current renderer. No related
open tank branch or PR existed at the audit. Logistics-building PR 273 is integrated in the current base and remains outside
this source family; its complete source and reference-scene additions are preserved.

## Demonstrated gap and bounded change

The retained native 30m inspection shows strongly mottled weathering across large
intact supplemental armor plates. Those plates use atlas cell 8 (weathered armor),
including the skirt fittings, glacis overlay and turret cheeks. Their surface
identity is made coherent with the existing painted hull by translating these
static charts to cell 0. Chart shape, scale, inset and orientation remain fixed.
Damage, critical and wreck surfaces retain their separate weathering and geometry.
No arbitrary texture repaint or alternate tank identity is introduced.

The explicit migration changes 832 source UV vertices on the chassis, 416 on the
turret and 80 on LOD1. LOD2, damaged, critical, wreck and collision remain unchanged.
Positions, normals, topology, indices and all moving-part UVs remain unchanged.
Source limits stay 5,528/1,736 chassis/turret triangles, 892/244 reduced triangles
and 88/180/300 damage-state triangles. Material IDs, slots, three shared texture
sources, BC7 formats, mip counts, texture caps and gameplay definitions do not change.
No procedural atlas pilot is promoted across unrelated asset families.

The saved `.blend` owns the edited UVs. Ordinary `Invoke-BlenderTank.ps1 -Action Export`
continues to use the unchanged strict exporter and current source baseline.
The one-time `tank_surface.py` migration validates the old saved master before
editing, accepts only the declared static chart translation, writes the three
canonical GLBs and saves the resulting rest master. It refuses an already-migrated
source. The original kit remains the reconstruction seed; after an intentional
seed reconstruction and `Author`, first adopt its reviewed GLB baseline and saved
master through the standard pipeline, then apply this explicit finish migration again.
Never replace the saved master with an unreviewed reconstruction.

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/tank_surface.py `
  -InputRoot assets/source -OutputRoot artifacts/tank-finish-candidate
./tools/blender/Test-BlenderTank.ps1 -OutputRoot artifacts/tank-finish-check
```

## Review loads and scope of evidence

`MainBattleTankSingle`, `MainBattleTankHundred` and `MainBattleTankThousand` copy
the existing gallery's intact extracted state and authoritative scale, with exact
family counts, cardinal orientations and terrain-relative grounding. The optional
mechanical time samples existing wheel travel, turret aim, elevation and recoil;
they do not implement circulating tracks, new clips or suspension. Existing
gallery and formation layouts and default poses remain unchanged.

`build/Invoke-TankSurfaceReview.ps1` declares paired native gallery observations at
120/420/1000m, Low/Medium/High, 1080p/4K; isolated 1/100/1000 loads; four inspection
yaws; two supported camera sweeps; dark/wet conditions and
four sampled recoil phases. Each case requests eight warmups, 32 timing samples
and two repeat readbacks. Identical source binaries consume independently sealed
before/after runtime catalogs. Deliberate picture differences require visual
review; a repeatability check is not a golden approval or a timing qualification.

Cadence interpolation tests at 30/60/144 Hz exercise copied fixed-tick pose data.
They do not certify live native frame pacing, motion readability or LOD judder.
Those remain human/native-session acceptance gates.

## Integration gate

The resource-scheduling decision still records native performance acceptance and
required checks as open. Composition/effects still records hardware and visual
acceptance as open, with its shared-linear shader variant disabled by default.
The integrated render-qualification PR 268 supplies measurement contracts, not
completed physical acceptance. This candidate may be authored and reviewed in
isolation; it must remain draft until those renderer gates and human tank art,
live motion and sustained performance acceptance are satisfied. No budget,
assertion, threshold, renderer contract or approved golden is relaxed.

Executed source-bound records are retained in [native evidence](qualification/tank-finish/native-review.json)
and [validation](qualification/tank-finish/validation.json). Package and current-head
CI results are distinct from native and human acceptance.

The saved positive-edit contract omits only its redundant inspection project.
Both ordinary reopen exports retain full inspection/pose output. Strict source,
geometry, metadata and saved-edit validation still execute before this optional
review output; the 120-second native test deadline is unchanged.

The native inspection override remains bounded to 20–120m. Exact 140/380m
selection boundaries are covered by existing unit tests and retained metadata;
exact native threshold-crossing/judder approval remains pending. An initial
review plan requested unsupported distances and was stopped before those cases.
A following attempt exposed the missing capture-request whitelist entries for
the new tank time scenes. Admission now explicitly includes those three scenes,
with unchanged time, camera, sampling and evidence-preservation limits. Neither
interrupted cohort is counted as the final paired matrix.

## Observed current-base results

Native/product source: 92bff60f7f78f813a88c6d6a10bee47a89c9b3a9, version 0.1.433-dev.
Windows x64, .NET 10.0.12 / SDK 10.0.401, pinned Blender 5.2.2. Hardware is
NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, 25,309,478,912 dedicated
VRAM bytes, native D3D12 Level 12_1, software false, debug layer disabled.

The full current-base Release build has zero warnings/errors. All 115 portable
Blender contracts, 99 scene/unit-LOD cases, six native-request cases and three
compiled cadence comparisons pass. The compiled comparisons exercise all eight
tank meshes, exact metadata/positions/normals/indices, preserved wheel batching
and identical mechanical matrices at 30/60/144-Hz sample grids across all four
damage states. They do not measure live display pacing.

The complete real saved-master qualifier passes: two reopen exports and compiled
outputs are byte equal; nine invalid edits reject against an unchanged control;
a saved supported edit survives and changes only the damaged compiled mesh;
source-less integrity passes. After the logistics-base synchronization, both
the saved master hash and complete selected source-closure signature still match
that qualified run exactly. The unchanged normal export path retains inspection
projects and pose records.

Both current catalogs contain 1,363 assets: 309 meshes, 901 textures, 132 materials,
18 animations, two skins and one UI skin. Both occupy 74,844,665 runtime file bytes.
Only the chassis, turret and LOD1 runtime hashes change. The other 1,360 containers
are byte equal. Shared image/material bytes and allocated texture policies are
unchanged; total container bytes are not a claim of device residency.

All 41 planned native cases on each side complete structurally and reproduce
their pixels on the second readback. Nine main before/after pairs are pixel equal;
the other 32 include the intended finish differences. A supplementary paired
20m detail capture also repeats exactly within each side. Recorded mesh, instance-draw, material,
texture and descriptor counts match in every paired case, with zero mesh fallback
or material/texture binding failure. Peak recorded texture residency is 44,812,076
bytes and peak descriptors are 93, below the unchanged 64-MiB/512 scene gates.
Recorded local/nonlocal device-memory observations remain in the raw archives;
complete-memory and long-lifecycle acceptance remain separate gates.

| Isolated High 1080p close load | GPU mean before / after ms | GPU p95 before / after ms | Instance draws |
| --- | ---: | ---: | ---: |
| 1 tank | 6.71 / 6.70 | 9.47 / 8.79 | 9 / 9 |
| 100 tanks | 12.52 / 12.28 | 20.46 / 15.95 | 9 / 9 |
| 1,000 tanks | 26.11 / 22.86 | 34.50 / 24.98 | 9 / 9 |

These are observations, not a qualified speedup. CPU tails contain substantial
outliers; valid GPU samples differ from the 32 requested samples in some cases.
Across the complete cohort, 50 statistics exceed the unchanged 15% diagnostic
comparison (43 CPU, seven GPU). Every value is retained. One paired cohort and
concurrent host activity do not establish controlled performance or sustained FPS.
The 1,000-tank close GPU mean also remains above a 16.67ms analysis target.
The two camera sweeps record timing samples but zero motion-image readbacks;
primary readbacks occur after camera reset. They do not approve moving-image
seams, shimmer, exact threshold crossings or LOD judder.

The runtime asset process/cache/error/source-less contracts pass. The strict
seed-2026 full match ends at tick 55,210 with Completed/Victory/CommandCoreDestroyed,
winner 1, 119 completed cargo orders, zero failed cargo orders and zero route
failures. Package provenance and checksums accompany the generated archive; PR 278 records
the executed reproducibility and fresh-install outcome. Its live required checks
are the current-head CI authority.

Original native PNGs: [before detail](qualification/tank-finish/before-detail.png),
[after detail](qualification/tank-finish/after-detail.png),
[before gallery](qualification/tank-finish/before-gallery.png),
[after gallery](qualification/tank-finish/after-gallery.png). Raw native manifests
and both catalog qualification reports are retained as lossless gzip files with
original and compressed SHA-256 identities. No approved golden or human art
acceptance is inferred from these records.

## Distribution verification

Publishing uses the qualified runtime root and writes source/build provenance,
checksums and two-tree reproducibility results alongside the local archive.
Fresh-install verification checks every file and runtime hash, rejects authoring
formats/compiler files and launches the extracted player from outside the checkout.
The package and its required CI remain independent from the open renderer and
human art gates above. The delivered archive identity and executed outcomes are
recorded on [PR 278](https://github.com/DaveBeusing/Forgeline/pull/278).

```powershell
./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible `
  -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/tank-package-433
```
