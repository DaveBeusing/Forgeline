# Operational building Blender masters

The four existing operational building kits lacked persistent native Blender masters.
This change retains their current geometry in four editable masters, four separate
packed PBR inspection projects and forty deterministic GLBs under
[assets/staged/operational-buildings](../assets/staged/operational-buildings).
Original kit module/part names survive as 319 vertex selection groups. Existing
LODs, construction, power, damage, wrecks, sockets and typed mechanics are retained.

These exports have been consumed by the real D3D12 presentation and an installed,
source-less player through an isolated sealed runtime catalog. Canonical production
sources are not promoted. Render-product acceptance, source ownership release,
strict image acceptance and human review remain open; this is not full production
Definition of Done.

## Audit, base and ownership

The fresh remote default/integration branch is master. Audited and rechecked base:
b32bf04671fb579e01a7e282eac76ff91c5b2f1d, version 0.1.428. Related surviving remote
branches were absent. PR #183 already integrated all four modular kits, LOD/state
geometry, Factory door/crane, Power fan and Radar rotation. PRs #270–272 are also
integrated and their shared inputs are retained. No review comments were returned
for #183, #268 or #272. #183's final main CI was cancelled; #272's final four
workflows passed. None is a current render acceptance record.

Current-master main run 39547596264 failed at step 4, "Validate one version
increment per PR", before build/tests/package. Its original REST run/job responses
are retained in [evidence](operational-blender/evidence). This inherited failure is
separate from this PR's passing exact one-PATCH gate: 0.1.428 → 0.1.429.

PR #268's qualification tooling is merged, but physical/frame/memory/lifecycle and
human product acceptance remain open. The current
[ownership manifest](rendering/parallelization/ParallelRenderOwnership.json)
holds assets/source and the compiler for the render residency lane. Authoring and
review therefore remain isolated. No gameplay rules, footprints, collision,
spawn conditions, sensing, compiler formats, shared render technology or budgets
are changed. No merge, tag, release or golden approval is performed.

## Retained families and contracts

| Family | Engine instance scale (X,Y,Z) | Owned LOD/state GLBs | Component selection groups | Existing LOD0/1/2 triangles |
| --- | --- | ---: | ---: | --- |
| Vehicle Factory | 26,12,24 | 10 | 107 | 540 / 240 / 204 |
| Power Plant | 16,10,16 | 10 | 83 | 1016 / 488 / 180 |
| Barracks | 18,8,16 | 10 | 69 | 276 / 140 / 92 |
| Radar | 14,18,14 | 10 | 60 | 812 / 348 / 156 |

Each family preserves the primary, lod1, lod2, foundation, structural_frame,
partial_shell, unpowered, damaged, critical and destroyed identities. Factory and
Barracks continue using the existing shared idle attachment. Power/Radar gain no
invented idle state. Brownout and offline still share the existing Unpowered
treatment; a separately captured brownout appearance is not implemented here.

Engine (x,y,z) maps to Blender (x,-z,y), metre scene scale, preserving the existing
normalized source geometry and gameplay instance scale. State/LOD collections are
independently inspectable; initially only the completed primary is visible.
Vertex groups select original kit modules/parts without splitting or renumbering
runtime primitives. Existing rigid part frames, ordered slots, named sockets,
terrain grounding and collision references remain unchanged. Barracks has zero
named sockets; no production socket is fabricated.

The strict master is the retained geometry source. Its scalar material preview is
not a PBR bake. The separate surface project embeds the same shared Base Color,
tangent Normal, ORM and existing Emissive inputs; content hashes and sRGB/linear
semantics are verified. No independent family atlas files or new material/texture
IDs are introduced. Shared BC7 tiers and all existing limits remain intact.
Source artwork and maps retain the repository's original provenance; no third-party
art input is introduced.

## Reopen, export and reproduce

Use pinned Blender 5.2.2 x64, build d13f752e3b9c, SDK 10.0.401 and PowerShell 7.4+.
Executable SHA-256: f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0.
The family adapter delegates to the existing importer/exporter and master organizer;
shared production modules are not forked. The family-specific negative fixture
adapts the existing mutation suite to the real socketless Barracks contract.

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/op-toolchain
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
./build/operational-blender/Qualify-Masters.ps1 -OutputRoot artifacts/om-new
python build/operational-blender/prepare_profiles.py artifacts/profiles-new
foreach ($family in @('vehicle_factory','power_plant','barracks','radar')) {
    ./tools/blender/Invoke-BlenderProduction.ps1 -FixtureContract "build/operational-blender/$family.json" -Profile "artifacts/profiles-new/$family.json" -OutputRoot "artifacts/reviews-new/$family" -RenderPreviews -TimeoutSeconds 480
}
./tools/blender/Invoke-Blender.ps1 -Action Script -Script build/operational-blender/pack_reviews.py -InputRoot assets/source -OutputRoot artifacts/packed-new -ScriptArguments @((Join-Path (Get-Location) 'artifacts/reviews-new')) -TimeoutSeconds 480
./build/operational-blender/Test-RetainedMasters.ps1 -OutputRoot artifacts/retained-new
python build/operational-blender/validate_retained.py
```

Use fresh short contained outputs. After preservation, reopen the checked-in
master; generating a new master does not preserve artist edits. Changed source
signatures fail rather than silently discarding edits. Lineage ignores only Git
CRLF conversion in JSON/glTF text; binary images and all geometry values remain
covered. The native exporter retains its original byte signature and tolerances.
The opt-in native workflow explicitly reopens checked-in masters and checks all
forty GLBs, while hosted retained-contracts checks lineage and actual geometry.
Local native execution does not imply the provisioned workflow was dispatched.

The recorded successful inputs are om1/vehicle_factory, om1/power_plant,
omb/barracks and omr/radar. Complete-Qualification.ps1 independently validates their
original reports, current source hashes, twenty compiler/integrity results and
repeated trees before copying them into fresh artifacts/oq and running all four
typed comparisons. No original incomplete attempt is relabelled as a passing run.
Component organization used complete inputs artifacts/oi2 and fresh artifacts/og2;
retention used oq, opr, opb and og2. The saved +.001 canonical-Y vertex edits are
qualification proofs and are not staged artwork.

## Actual local validation

| Check | Observed result |
| --- | --- |
| Real pinned Blender | Four independent family masters saved/reopened, saved edit proofs and forty retained exports pass |
| Editorial organization | 319 original groups across all forty meshes; reopening each final master reproduces its ten GLBs byte-for-byte |
| Native mutation cases | 74 reject; four two-slot/tangent positives pass; three socket mutations explicitly not applicable to Barracks |
| Scoped compiler | Twenty 20-asset catalogs: eleven meshes, five materials, four textures each; build and absent-source validation pass |
| Determinism | First/second/reopened source and runtime trees are byte-identical per family |
| Typed baseline comparisons | Four pass, eight unrelated opt-ins skipped; materials, states, LODs, sockets, mechanics and batch identity retained |
| Whole-catalog comparisons | Five pass, seven unrelated opt-ins skipped, including four sealed overlays and the combined forty-export catalog |
| Portable authoring contracts | 95 pass |
| Building presentation regressions | 31 pass |
| Complete serial Release suite before eight diagnostic additions | 3,233 total: 3,202 pass, 31 optional skips, zero failures; 46m48.517s |
| Updated complete Presentation module | 1,597 total: 1,572 pass, 25 optional skips, zero failures |
| Final diagnostic context regressions | Eight pass |
| Client capture contracts | Three pass |
| Release builds | Complete native-source builds have zero warnings/errors |
| Architecture/version | 44-project graph acyclic; exact one-PATCH gate passes |
| Compiler process contracts | Lock exclusion, interrupted writer recovery, source-less and corruption behavior pass |
| Natural headless match | Seed 2026: Victory / CommandCoreDestroyed, player 1, tick 55,210; exit 0 |

Per-family triangle totals including the shared collision proxy are 1556 / 2288 /
1056 / 1920. Maximum source-space position error is 7.450581e-9; maximum tangent
vector error is 8.885891e-7. Existing tolerances are unchanged. Canonical catalog:
1,363 assets / 74,844,350 bytes. Candidate: 1,363 / 74,844,770 bytes, a 420-byte
mesh-container metadata increase with unchanged geometry counts and texture bytes.
Texture containers are 59,453,925 bytes; gameplay minus studio splash is 57,667,508,
below the unchanged 58,720,256-byte gate. This is compiled-container accounting.

## Native D3D12 evidence

Main executable and package source: cf32514d2ac39fbb0fd2d94410c8b1069279610a.
The earlier control and natural match retain source 1d9f7bedef03b1f3418e9521ba06c76274fb38ee;
the later code difference is family diagnostic HUD context, with no Game/Headless or
canonical source changes. Subsequent report-only commits do not relabel measured
binary identities.

[Native registry, exact case results and raw-report references](operational-blender/evidence/native/summary.json)
retains the complete 162-pair main matrix, source/runtime/image hashes, actual timing
summaries and original manifests. All 324 successful main cases repeat exactly
across eight readbacks, with zero mesh fallbacks and material/texture binding failures.
All paired camera/lighting/profile inputs, instance/draw/material counts and tracked
resource counters match. Hardware is NVIDIA RTX PRO 5000 Blackwell Generation Laptop
GPU, native D3D12 feature level 12_1, driver 32.0.15.9653; software false, debug layer
false. Requested native 1080p/4K backbuffers are distinct from the clamped host window.

89 main pairs are pixel-exact; 73 are not. Across those pairs there are 1,066 changed
pixels, maximum channel delta 3. Largest pair: 117 changed pixels at 4K. These
nonexact pairs fail the unchanged zero-pixel/channel tolerance. No tolerance or
golden is approved and the numerical cause is not isolated. The
[pixel-coordinate observations](operational-blender/evidence/native/pixel-coordinates.json)
retain the highest-channel sample and the initial baseline failure exactly.

The matrix includes all four state galleries at three profiles, three zooms and
1080p/4K, exact single/100-instance family cohorts, the existing mixed 1,000-building
cohort, dark/wet cases and four camera-side inspections. Each 100-instance family
cohort contains 25 actual placements at every cardinal rotation. Camera yaw itself
is independent of placement. Snapshot counts describe logical buildings; actual
render-instance/draw counters also reflect parts, LOD and other scene content.

The new family fixtures copy the existing extracted Operational plot and valid
gallery player read context with empty selection; they add no game authority.
The existing MechanicalSeconds override does not support these operational scenes.
Static readbacks therefore do not certify live door/crane/fan/radar animation,
spawn/traffic, continuously moving LODs, physical-display legibility or interaction.
Close gallery framing clips the nearest foundation in the retained baseline;
single-building side views provide separate unclipped model inspection. The
inherited hover ring is not a new modeled component or selection acceptance.

Tracked resident texture bytes span 22,469,216–44,812,076 and descriptor use 53–93,
identical within every pair. These scoped counters are not complete physical VRAM.
Eight warmups and 32 samples per case provide bounded functional observations,
not sustained FPS or accepted paired performance. Full live-client frame, raw GPU
fence/timestamp, complete memory, thermal and lifecycle requirements remain open
under the existing render qualification contract.

Selected unchanged originals:

| Family | Original | Export | Single front inspection |
| --- | --- | --- | --- |
| Factory | [before](operational-blender/evidence/native/images/vehicle_factory-before.png) | [after](operational-blender/evidence/native/images/vehicle_factory-after.png) | [front](operational-blender/evidence/native/images/vehicle_factory-single-front.png) |
| Power | [before](operational-blender/evidence/native/images/power_plant-before.png) | [after](operational-blender/evidence/native/images/power_plant-after.png) | [front](operational-blender/evidence/native/images/power_plant-single-front.png) |
| Barracks | [before](operational-blender/evidence/native/images/barracks-before.png) | [after](operational-blender/evidence/native/images/barracks-after.png) | [front](operational-blender/evidence/native/images/barracks-single-front.png) |
| Radar | [before](operational-blender/evidence/native/images/radar-before.png) | [after](operational-blender/evidence/native/images/radar-after.png) | [front](operational-blender/evidence/native/images/radar-single-front.png) |

[Mixed 1,000 before](operational-blender/evidence/native/images/thousand-before.png) /
[after](operational-blender/evidence/native/images/thousand-after.png).
The checked-in PNGs are selected unmodified copies, not a complete standalone
capture directory. All complete original PNGs, request files and raw logs remain
under artifacts/opvb2, opva2 and the installed roots named in the registry.

## Reproducible source-less installed candidate

Canonical Publish-PreAlpha -VerifyReproducible completes successfully, compares both
publish manifests and removes its temporary verification trees. The final publish
manifest and original command log are retained. Archive:
FORGELINE-0.1.429-dev-win-x64.zip, SHA-256
fe05e154c842d0990998be8905b27b6a72f16c94d5d4ad605610528c57a3e8ce.
All 1,487 recorded fresh-extraction file hashes verify. Published and freshly
extracted players load their own candidate runtime and exit 0 with fresh settings,
source/compiler/Blender files absent, and child PATH limited to system/.NET.
System-wide Blender was not removed; the observed package and dependency scope is
recorded precisely in the [package evidence](operational-blender/evidence/package).

The installed binary adds sixteen successful native cases / eight matched pairs:
four High/1080p single-family views and four Low/4K strategic 100-instance views.
Six pairs are pixel-exact; Factory/Barracks strategic pairs differ at six/one pixels,
maximum delta 1. All repeat exactly without binding failures/fallback meshes. These
remain separate zero-tolerance observations, not an installed golden approval.

```powershell
./build/operational-blender/Prepare-RuntimeReview.ps1 -QualifiedRoot artifacts/oq -OutputRoot artifacts/ov
./build/operational-blender/Capture-Review.ps1 -OutputRoot artifacts/opvb2 -RuntimeAssetRoot assets/runtime
./build/operational-blender/Capture-Review.ps1 -OutputRoot artifacts/opva2 -RuntimeAssetRoot artifacts/ov/combined/runtime -BaselineRoot artifacts/opvb2
./build/Publish-PreAlpha.ps1 -Configuration Release -OutputRoot artifacts/oppkg -VerifyReproducible -RuntimeAssetRoot artifacts/ov/combined/runtime
```

Outputs above name the retained execution. Use fresh roots to reproduce. The
original command log records the fresh ZIP extraction, every manifest hash, both
restricted-PATH smoke calls and installed native comparisons. No generated canonical
runtime buffer is edited as artwork.

## Retained failures and open decision

- Initial generic Barracks negative fixture: StopIteration on nonexistent named
  sockets. Its failed native report/stderr remain unchanged. The family fixture
  reports three mutations as not applicable and rejects an unexpected socket.
- First complete-suite attempt was cancelled without a result while confirmed
  closure counts and the combined-catalog test were prepared. The later full serial
  result above passes; the cancelled attempt is not a pass.
- New workload test build first failed xUnit2012. Assert.Contains corrected the
  collection assertion without changing expectations or thresholds; build and all
  eight workload tests then passed.
- Bulk editorial-input copy timed out, leaving artifacts/oi partial; its dependent
  artifacts/og attempt was cancelled. Fresh complete oi2 → og2 succeeds. Originals
  were neither overwritten nor deleted.
- First hardware smoke exposed missing HUD read context in the new pure workload.
  Source 1d9f7be is preserved; cf32514 copies existing context with empty selection,
  fingerprints it explicitly and passes eight regressions. Main/installed evidence
  uses only the latter source.
- First main baseline stopped after 22 completed cases at Radar/Medium/Normal1080p:
  repetition 2 differed in one channel by 1 at (260,48), with identical input
  fingerprints. [Failure](operational-blender/evidence/native/failures/initial-repeat-failure.json)
  and both original failed PNGs are retained. The complete unchanged fresh repeat
  passed all 162 cases. The first result remains failed and its cause is not isolated.

Keep draft. Canonical ownership release, actual render-product acceptance, exact
current-head CI, all nonexact-image review, human art/physical-display acceptance,
live mechanics/traffic and moving LOD/lifecycle acceptance remain independent gates.
A separately captured brownout appearance is also still absent. The isolated saved
masters and installed candidate are not a claim of production integration approval.
