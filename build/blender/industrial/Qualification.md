# Industrial Blender source preservation

Status: saved source masters and compiler-ready exports are staged and inspected
through isolated runtime overlays. Production promotion and human visual approval
remain blocked. This is not a claim of newly authored artwork or completed game
integration.

## Source and scope

The freshly fetched integration/default branch was `master`,
`d3acf9b0fb94da75ecfd26173cc387398f317096`, tracked version `0.1.426-dev`.
Branch `assets/industrial-blender-masters`, PR #270, initially prepared
`0.1.427-dev`. After master advanced through PR #268 to
`538ddc0bd49a722462bf185cfc118cf5a3e5008f` / `0.1.427-dev`, synchronization
`477d9711` merges the updated baseline while preserving the measured `e8d277cb` ancestor
and reconciles exactly one
net PATCH to **`0.1.428-dev`**. No renderer or asset source changed in that
master delta; only qualification tools/docs/workflow/tests and version changed. PR #182 already integrated the industrial geometry, states and
presentation bindings. PR #269 supplies the inventory/preservation contract.
The demonstrated remaining source gap was the independently saved Blender masters.

The three masters and 33 family exports are indexed, with original definitions,
material/LOD/socket metadata and SHA-256 values, in [SourceBindings.json](SourceBindings.json).
Canonical source definitions, family kits, collision references, shared PBR maps,
compiler/runtime formats, renderer, gameplay and budgets are unchanged. No source
file under `assets/source/` is authored by this checkpoint. The existing ownership
hold still applies. PR #268 subsequently merged into master `538ddc0b`, version
`0.1.427-dev`; its published plan contains zero native records and all twelve
product gates remain open. Its merge is not performance/visual acceptance.

| Family | Saved master | Family GLBs | Compiled meshes per isolated catalog, including collision | Triangles across those meshes |
| --- | --- | ---: | ---: | ---: |
| Extractor | [extractor.blend](masters/extractor.blend) | 11 | 12 | 2,508 |
| Smelter | [smelter.blend](masters/smelter.blend) | 11 | 12 | 2,588 |
| Electronics Plant | [electronics_plant.blend](masters/electronics_plant.blend) | 11 | 12 | 2,152 |

Each catalog contains 16 assets: the 12 meshes, one existing material and three
existing texture maps. Each master retains all family states/LODs in separate
collections. Source positions remain unchanged; the engine/Blender axis mapping,
unit scale, ordered slots, collision and socket frames use the existing adapter.

## Executed source checks

Real Blender **5.2.2 release / Windows AMD64**, bundled Python **3.13.13**,
build `d13f752e3b9c`. Executable SHA-256:
`f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`.
This is native `bpy` execution, not a protocol fixture.

- Two independently saved projects and an export after reopening the organized
  master produce identical source and runtime file trees within each family.
- Each family saves, reopens and exports an actual 0.001-canonical-Y vertex edit.
  This separate edit test is compiled but is not the staged artwork.
- All 57 native negative scene cases reject, including removed UVs, moved sockets,
  changed pivots/materials, missing states and unsupported scene data. All three
  synthetic two-slot/tangent positives pass. The shared UV negative now explicitly
  selects geometry with a UV channel, rather than a UV-less collision proxy.
- All 15 baseline/first/second/reopened/edited catalogs compile and validate with
  an absent source root. No missing references or qualification issues are reported.
- The three opt-in typed runtime tests pass; four unrelated authoring opt-ins skip.
  They compare vertices, normals, UVs, generated tangents, indices, material and
  dependency identity, sockets, collision, state/LOD records and batching.
  Maximum position error is zero. Maximum tangent-vector errors are
  1.4564313e-6 / 1.386689e-6 / 1.2262487e-6, within the existing 2e-5 contract.
- 88 portable Blender contracts and 26 existing building-presentation tests pass.
- Complete Release solution build: zero warnings/errors. Project-reference graph:
  44 projects, no cycles. The one-PATCH validation passes.
- The canonical compiler independently checks the reused baseline cache against
  current source: **0 compiled / 1,357 unchanged**, 75,122,812 runtime bytes,
  successful full catalog integrity qualification. Cache presence alone is not
  used as that result.

The complete serial Release suite on executable source `e8d277cb` passes:
**3,184 passed / 24 skipped / zero failed**, 3,208 total, 21m45.851s. The complete
log SHA-256 is retained in the JSON evidence. Opt-in/environment skips do not
certify the corresponding physical or debug-layer gates.

After synchronization to master `538ddc0b`, source `477d9711` builds the complete
Release solution with zero warnings/errors. The ten newly integrated qualification
boundary tests pass, and the three typed industrial comparisons pass again
(four unrelated opt-ins skip). Current-head hosted CI is a separate pending gate.

## Native game display

Executable source: `e8d277cbd2f8cc868afd3c298547361a5abbbd40`, rebuilt cleanly.
The native capture source/build identity matches that SHA. Later evidence-only
changes do not change the executable source.

Nine before/after pairs use the real D3D12 renderer, the existing
`ExtractorProduction`, `SmelterProduction` and `ElectronicsPlantProduction`
galleries, High quality, 1920x1080, and CloseTactical/NormalGameplay/Strategic.
Before uses the canonical runtime; after uses independently sealed/validated
overlays containing the compiled Blender exports at the same stable IDs.
The canonical production source/bindings are not replaced.

Device: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, feature level 12_1,
driver 32.0.15.9653; hardware adapter, debug layer disabled. Each case uses three
warmups, eight samples and two repeated pixel captures. These short static
cohorts establish bounded display behavior, not a performance baseline or FPS
acceptance. Raw timings, GPU data and resource observations remain in the local
manifests; selected observations and manifest hashes are retained in the
[machine-readable evidence](evidence/Qualification.json).

| Family | Normal | Tactical close | Strategic |
| --- | --- | --- | --- |
| Extractor | pixel-exact | pixel-exact | pixel-exact |
| Smelter | pixel-exact | 2 pixels, maximum channel delta 1 | pixel-exact |
| Electronics Plant | pixel-exact | 2 pixels, maximum channel delta 1 | pixel-exact |

All nine cases repeat exactly within their own capture, with zero fallback mesh
instances, material binding failures and texture binding failures. Seven cross-source
pairs are pixel-exact. The two close pairs **do not pass the unchanged zero-pixel
tolerance** and require visual review. Their exact coordinates/RGBA values and
both original PNGs are retained; no golden or tolerance is changed. The cause of
the small differences has not been isolated.

The gallery is a static state fixture, not a live logistics demonstration. Its
closest construction foundation is partly outside the tactical viewport; this
does not establish complete ground-contact/traffic clearance. Fixed cameras do
not establish moving LOD transitions or animation acceptance.

## Reproduction and retained attempts

Use the pinned SDK and the existing commands from the repository root:

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/new-industrial-check
& ./build/blender/industrial/Qualify-Masters.ps1 -OutputRoot artifacts/new-industrial
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master
dotnet test --solution ForgeLine.sln --configuration Release --no-build --max-parallel-test-modules 1
```

Actual family outputs are retained in `artifacts/im1/extractor` and
`artifacts/im3/{smelter,electronics_plant}`. A combined, byte-checked copy is
`artifacts/iq`; its three `runtime-comparison.json` reports and the typed test log
retain the observed comparisons. The corrected Extractor negative run is
`artifacts/inx2`. Native originals are `artifacts/ivb`, `ivb2`, `iva` and `iva2`.
The 18 source-controlled PNGs are exact copies with verified original SHA-256s.

For overlays, set `FORGELINE_INDUSTRIAL_BLENDER_ROOT` to the combined qualification
root and `FORGELINE_BLENDER_RUNTIME_SEED` to the fully qualified canonical runtime,
then run `BlenderRoundTripQualificationTests` with `dotnet test --project` and
`--filter-class`. The existing helper seals and validates each overlay. Capture
with `Invoke-RtsReferenceQualification.ps1 -Capture`, the three scene names,
`-Profiles High -Displays 1920x1080`, the three zooms, `-Warmups 3 -Samples 8
-RepeatCaptures 2`, and the selected `-RuntimeAssetRoot`. Comparison uses
`Compare-VisualReferenceEvidence.ps1 -ComparePixels` with zero tolerance;
`-GateImages` and new approval records are not used.

Earlier failures/cancellations are preserved and listed in the JSON evidence:
Windows path length during an edited export; a collision-first UV test fixture;
incorrect native PowerShell array forwarding; a case-insensitive parameter/loop
variable collision; an overlapping build/active compiler DLL lock; and incomplete
full-suite prerequisites in the initially sparse checkout. The checkout is now
complete and the baseline runtime is qualified. Earlier canceled/failed suite
attempts are not reclassified as passes. An early portable test attempt lacked
the tank reference file, and restricted temporary-directory permissions caused a
separate portable failure; the completed local portable run passed all 88.

## Integration decision

Do not merge or promote these staged sources yet. Required remaining gates are
the actual rendering milestone product acceptance and release of source
ownership, current-head CI including its full suite, human review of the two differing close
images and complete art acceptance, live adjacent traffic and four rotations,
moving LODs/states, the full resolution/profile/weather/stress matrix, sustained
paired GPU/CPU/residency evidence, fixed-seed scenic comparisons and an installed
reproducible source-less player package. A source-less catalog or static pixel
repeat is not a substitute for those independent results.

Logical commit boundaries retain pipeline/fixtures, one PATCH, targeted runtime
tests, source masters/exports (`79e92b4c`) and the native executable checkpoint
(`e8d277cb`) separately. No merge into master, tag, release or golden approval is performed.
