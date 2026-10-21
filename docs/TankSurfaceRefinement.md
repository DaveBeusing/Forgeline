# Tank armor finish and native review

Baseline master is `a6fe118583be842e00416bcb100d1210f3240a00`, version 0.1.431.
The saved tank, seven Blender exports and shared 4K PBR atlas already exist.
Tank PR 231 and procedural-atlas PR 235 are integrated; their historical native
observations do not establish acceptance of the current renderer. No related
open tank branch or PR existed at the audit. The separate logistics-building
PR 273 is outside this source family.

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
yaws; both sides of the existing 140/380m LOD thresholds; dark/wet conditions and
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

Executed results and source identities are retained separately in the native
qualification evidence once produced. A declared capture plan is not an executed
result, and a successful package is not human art approval.

The saved positive-edit contract omits only its redundant inspection project.
Both ordinary reopen exports retain full inspection/pose output. Strict source,
geometry, metadata and saved-edit validation still execute before this optional
review output; the 120-second native test deadline is unchanged.
