# Operational building Blender masters

Audit base: master b32bf04671fb579e01a7e282eac76ff91c5b2f1d, version 0.1.428.
Remote default/integration branch is master. No surviving related remote branch was found.
PR #183 already integrated all four component kits, state geometry, LODs, factory door/crane,
power fan and radar rotation. PR #268 qualification tooling is merged, but physical,
complete-frame, memory/lifecycle and human acceptance remain open. PRs #270–272 are
now integrated; their content and shared atlas inputs are retained. No review comments
were returned for #183, #268 or #272. #183's final main CI was cancelled; historical
CI is not a current acceptance claim. #272's final four workflows passed.

The demonstrated gap is four persistent native Blender masters, not missing gameplay
or a need to replace existing artwork. Authoring is isolated while the ownership
manifest holds assets/source and the asset compiler. Production promotion remains
blocked by render acceptance and ownership release. This checkpoint does not satisfy
full production Definition of Done.

## Reproduce

Use the repository-pinned Blender 5.2.2 x64, SDK 10.0.401 and PowerShell 7.4+.

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/op-toolchain
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
./build/operational-blender/Qualify-Masters.ps1 -OutputRoot artifacts/om1
```

The family adapter reuses the existing importer/exporter and industrial master organizer.
Each fixture lists ten existing primary/state/LOD IDs. Shared collision and PBR dependencies
remain shared. Idle attachments for factory/barracks retain the existing catalog binding;
power/radar do not gain an invented idle or brownout state. No new emissive semantics.

Masters retain source attributes, ordered slots, sockets and rigid triangle ranges.
Collection visibility initially shows LOD0; enable another state/LOD for inspection.
Coordinates map engine (x,y,z) to Blender (x,-z,y), metre scale. Instance scales follow
existing gameplay dimensions. The saved +.001 engine-Y vertex edit is qualification only,
never the staged art. Bounds, placement, four cardinal orientations, production, power,
sensor and spawn authority remain unchanged.

Two independent exports plus reopened export must produce identical source/runtime bytes.
Five scoped catalogs per family (original, first, second, reopened and edit proof) compile
and validate without sources. Native invalid scenes reject. Typed comparison checks
geometry, normal/UV/tangent conventions, stable IDs, collision, LODs, sockets, materials,
mechanical metadata and batching using existing tolerances. No limits are relaxed.

## Acceptance

Native galleries and sealed overlays verify candidate export consumption independently
of canonical source promotion. Still captures are not live door/rotor/array animation,
physical spawn/circulation or moving-LOD acceptance. Full package, CI, repeated workloads,
paired physical performance and human visual acceptance remain independently reported.
No merge, release, tag or golden approval is implied.

Observed baseline CI: master b32bf046 main run 39547596264 failed at step 4,
"Validate one version increment per PR", before build/tests/package. Movement passed.
This inherited integration-version failure is recorded independently of this PR's
0.1.428 → 0.1.429 check; no master history or unrelated version policy is changed.

Current power-state limit: brownout and offline both resolve to the existing
Unpowered treatment. A separately captured brownout appearance is not implemented
or certified by this preservation checkpoint. Factory door/crane, power fan and
radar array retain the existing typed real-state contracts; static inspection
projects do not establish live temporal acceptance.

The first complete-suite attempt was cancelled before a result so the newly
confirmed 20-record closure and combined-catalog test could be built first.
Its partial log is retained as artifacts/op-full-tests.log; it is not a pass.

## Saved projects and reproduction details

The strict master also retains every original module/part label as a vertex selection
group, without splitting or renumbering the runtime primitive stream. Mechanical
part frames remain separate existing frames. Organizing these editorial groups
must reopen the saved file and reproduce all ten GLBs byte-for-byte.

```powershell
python build/operational-blender/prepare_profiles.py artifacts/opp
foreach ($family in @('vehicle_factory','power_plant','barracks','radar')) {
    ./tools/blender/Invoke-BlenderProduction.ps1 -FixtureContract "build/operational-blender/$family.json" -Profile "artifacts/opp/$family.json" -OutputRoot "artifacts/opr/$family" -RenderPreviews -TimeoutSeconds 480
}
./tools/blender/Invoke-Blender.ps1 -Action Script -Script build/operational-blender/pack_reviews.py -InputRoot assets/source -OutputRoot artifacts/opb -ScriptArguments @((Join-Path (Get-Location) 'artifacts/opr')) -TimeoutSeconds 480
./tools/blender/Invoke-Blender.ps1 -Action Script -Script build/operational-blender/organize_components.py -InputRoot assets/source -OutputRoot artifacts/og -ScriptArguments @((Join-Path (Get-Location) 'artifacts/om1')) -TimeoutSeconds 480
python build/operational-blender/retain_masters.py artifacts/om1 artifacts/opr artifacts/opb artifacts/og
python build/operational-blender/validate_retained.py
./build/operational-blender/Test-RetainedMasters.ps1 -OutputRoot artifacts/or1
```

After preservation, export from the checked-in master; generating a new master is
not a substitute for retaining artist edits. The native opt-in workflow explicitly
reopens those saved files. A changed authoritative source signature fails rather
than silently discarding edits. JSON/glTF lineage tolerates only Git CRLF conversion;
the existing native importer/exporter keeps its original byte signature and tolerances.

The separate packed surface projects are inspection files, not unrestricted material
graph exporters. Four embedded inputs per project are byte-identical to the same shared
Base Color, tangent Normal, ORM and existing Emissive sources, with sRGB/linear semantics
verified. No new atlas files, material identities or texture budget are introduced.

Exact review workloads: VehicleFactorySingle/Repeated, PowerPlantSingle/Repeated,
BarracksSingle/Repeated and RadarSingle/Repeated copy their existing extracted
Operational plot into one or 100 instances. Every 100-instance family workload
contains 25 actual placements at each cardinal rotation, terrain-relative grounding
and unchanged full gameplay scale. The existing OperationalBuildingsRepeated retains
its 1,000-building mixed-family workload. These are immutable diagnostic snapshots,
not new gameplay scenarios. Camera yaw is independent of building placement. The
existing MechanicalSeconds override does not support operational galleries, so no
temporal-pose sweep is claimed.

Retained development failure: artifacts/op-scenes-build.log records xUnit2012
for the new cardinal membership assertion. It was corrected to Assert.Contains
without changing any expected value or tolerance. The second targeted build has
zero warnings/errors and all eight new workload contracts pass. The complete
presentation module is rerun after these diagnostic scene additions; the earlier
whole-solution run remains separately bound to its preceding C# source.

Observed complete serial Release suite (preceding the eight additional diagnostic
workload cases): 3,233 total, 3,202 passed, 31 optional skips, zero failures,
46m48.517s; artifacts/op-full-tests2.log. After the diagnostic additions, the
complete Presentation module passes 1,572 / 25 optional skips / zero failures
(1,597 total), and all three Client VisualReferenceCaptureTests pass. Both
updated targeted builds have zero warnings/errors.

The original generic native negative fixture stopped on Barracks with StopIteration
because the unchanged Barracks definitions contain no named sockets. The failed
report and stderr remain under artifacts/om1/barracks/native-contracts/. The family
fixture preserves all existing applicable mutations, explicitly reports three
socket mutations as not applicable, and adds rejection of an unexpected socket.
No fake production socket or relaxed exporter assertion is introduced. Factory
and Power completed their five catalogs before that later family failure; their
individual original reports remain valid. Barracks and Radar run independently
under artifacts/omb and artifacts/omr; verified per-family results are aggregated
into fresh artifacts/oq, retaining every original report and incomplete attempt.

The first bulk editorial-input copy timed out in the local file-processing host,
leaving artifacts/oi incomplete. Its dependent organization attempt under artifacts/og
was cancelled and is not counted as a pass. A fresh complete copy under artifacts/oi2
feeds artifacts/og2; no original failed output is overwritten or deleted.

## Qualified authoring checkpoint

Four strict masters, four separate packed PBR inspection projects and 40 GLBs are
retained under assets/staged/operational-buildings. Original module groups per family:
Factory 107, Power 83, Barracks 69, Radar 60 (319 total). The four checked-in masters
reopen and reproduce all 40 retained GLBs exactly. Source lineage validates against
current master. All 20 compact catalogs pass real compilation and source-less validation;
first/second/reopened source and runtime trees are byte-identical within each family.

All four typed comparisons pass (eight unrelated opt-ins skipped). Across each
family's eleven meshes, triangle totals including the shared collision proxy are
1556 / 2288 / 1056 / 1920. Maximum source-space position error is 7.450581e-9;
maximum tangent-vector error is 8.885891e-7. Existing position/normal/UV/tangent
tolerances remain unchanged. Materials, sockets, LODs, mechanical metadata and
batch identity match. There are 74 real native mutation rejections, four two-slot
positives and three explicitly inapplicable socket mutations for socketless Barracks.

```powershell
./build/operational-blender/Prepare-RuntimeReview.ps1 -QualifiedRoot artifacts/oq -OutputRoot artifacts/ov
./build/operational-blender/Capture-Review.ps1 -OutputRoot artifacts/opvb -RuntimeAssetRoot assets/runtime
./build/operational-blender/Capture-Review.ps1 -OutputRoot artifacts/opva -RuntimeAssetRoot artifacts/ov/combined/runtime -BaselineRoot artifacts/opvb
./build/Publish-PreAlpha.ps1 -Configuration Release -OutputRoot artifacts/oppkg -VerifyReproducible -RuntimeAssetRoot artifacts/ov/combined/runtime
```

The family review matrix declares 162 cases per source: all four real-state galleries
at Low/Medium/High, close/normal/strategic and 1080p/4K; exact single/100-instance
family workloads; the existing mixed 1,000-building workload; dark/wet cases; and
four camera-side inspections. The 100-instance scenes themselves contain real
cardinal placements. Each case uses eight warmups, 32 measured samples and eight
readbacks. This is bounded functional evidence, not sustained FPS, thermal/lifecycle
or renderer-product acceptance. Declared commands are not claimed executed until
the later native/package evidence record reports their actual results.
