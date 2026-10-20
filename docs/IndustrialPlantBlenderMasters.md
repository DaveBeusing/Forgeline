# Fuel refinery and ammunition plant Blender masters

## Scope and audit

The checked integration anchor is remote `master`
`d3acf9b0fb94da75ecfd26173cc387398f317096`, version `0.1.426-dev`.
The default and integration branch is `master`. The remote branch inventory
contained only `master` and `testing/render-performance-qualification`.

PR #182 already integrated the industrial component kits, three LODs and eight
construction/operational/damage companions per family. PR #229 integrated bounded
PBR/LOD inspection. PR #269 integrated the per-ID source inventory and preservation
pilot. These integrated sources are reused without geometry reauthoring. The
demonstrated gap is the absence of bound, saved Blender masters for these two
families. Existing sources, material slots, sockets, collision, state selection,
loading lanes and simulation authority remain the baseline.

At this checkpoint PR #268 is still an open draft. Its latest source is
`37ad6e0428894ecbc12aac8181c9092a1924230b`; required checks were in progress and
physical performance/visual acceptance remained open. No unresolved review threads
were returned for #182, #229, #269 or #268. The current ownership manifest reserves
`assets/source/` and the compiler for the render residency lane. This work therefore
uses isolated output and does not promote sources or change renderer contracts.

The integration base subsequently advanced to
`538ddc0bd49a722462bf185cfc118cf5a3e5008f` (PR #268 merged, `0.1.427-dev`).
This branch synchronized that complete base and reconciled its single net PATCH
to `0.1.428-dev`. The incoming delta is qualification code/tests/documentation,
not renderer, mesh, material or canonical source changes. Its five source-bound
workflows passed, but physical jobs were skipped and the declared physical,
complete-frame/memory/lifecycle and human visual gates remain open. Merge status
does not grant production promotion.

## Reproduction

Use the pinned Blender 5.2.2 x64, .NET SDK 10.0.401 and PowerShell 7.4+:

```powershell
pwsh tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/industrial-plants/toolchain
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh build/industrial-plants/Qualify-Masters.ps1 -OutputRoot artifacts/ip1
python build/industrial-plants/prepare_profiles.py artifacts/industrial-plants/profiles
```

Each family fixture explicitly names all eleven existing completed LOD/state
meshes; material, texture and collision dependencies resolve through the established
catalog. Representative instance scales follow current gameplay footprints:
refinery `(18,10,16)`, ammunition plant `(18,10,18)`. The noncardinal yaw of 37 degrees
also exercises the existing socket coordinate mapping without changing placement.

The family producer delegates import/export and all tolerances to the existing
preservation workflow. It organizes the native master into collections by stable
asset ID, initially showing the completed primary mesh. Enable another collection's
objects to inspect its state/LOD. All geometry stays at its original pivot; scene
units are metres with engine `(x,y,z)` mapped to Blender `(x,-z,y)`.

The strict master retains editable geometry, authored UV/normal attributes, ordered
materials and named sockets. Its scalar material preview is not a PBR bake or shader
parity claim. A separately generated `surfaces.blend` provides packed source PBR
inspection using the existing production entrypoint:

```powershell
pwsh tools/blender/Invoke-BlenderProduction.ps1 -FixtureContract build/industrial-plants/fuel_refinery.json -Profile artifacts/industrial-plants/profiles/fuel_refinery.json -OutputRoot artifacts/industrial-plants/review-fuel -RenderPreviews -TimeoutSeconds 480
pwsh tools/blender/Invoke-BlenderProduction.ps1 -FixtureContract build/industrial-plants/ammunition_plant.json -Profile artifacts/industrial-plants/profiles/ammunition_plant.json -OutputRoot artifacts/industrial-plants/review-ammunition -RenderPreviews -TimeoutSeconds 480
& ./tools/blender/Invoke-Blender.ps1 -Action Script -Script build/industrial-plants/pack_reviews.py -InputRoot assets/source -OutputRoot artifacts/ip-pbr -ScriptArguments @((Join-Path (Get-Location) 'artifacts/industrial-plants')) -TimeoutSeconds 480
python build/industrial-plants/retain_masters.py artifacts/ip1 artifacts/industrial-plants artifacts/ip-pbr
python build/industrial-plants/validate_retained.py
```

Surface jobs use the measured unchanged source triangle/section/geometry ceilings
and the existing 7,325,040-byte texture payload ceiling. No new texture, emissive
map, material, state or process gameplay is introduced. In particular, existing
active VFX remain presentation consumers of real state; a new emissive appearance
is not implied by creating a master.

Retained files live under `assets/staged/industrial-plants/<family>/`: the strict
preservation master, a separate packed PBR inspection master, eleven GLBs and
explicit lineage. The inspection master includes source PBR images and three
cameras; it is not an unrestricted material-graph exporter. The lineage retains
the exact native source signature and also a portable signature which ignores
only Git checkout CRLF conversion in JSON/glTF text. Binary maps, geometry values,
definitions and all other bytes remain covered. The strict reexporter's original
byte-signature and geometry tolerances are unchanged.

## Qualification and promotion

The qualifier saves two independent native masters, reopens/reexports the first,
and compares deterministic source and compiled trees. It compiles all four trees,
validates each with an absent source directory and checks production source hashes
before/after. The opt-in typed test checks every dependency, material, LOD,
collision, socket, mechanical record, index, vertex, UV, normal and tangent against
the original compiled baseline. Native `.blend` bytes are not expected to be
deterministic; GLB/source/runtime outputs are.

This preservation checkpoint does not qualify new in-game art. Production adoption
requires current-master render qualification and source ownership release, reviewed
canonical source bindings, full CI, native before/after and moving-camera/state
inspection, all four loading rotations and real cargo/refueling fixtures, paired
physical performance/resource evidence, a reproducible source-less installed player,
and explicit human visual acceptance. Do not merge, release or approve goldens while
these gates remain open. No existing budget or tolerance is raised to admit artwork.

## Observed execution checkpoint

Native authoring/export checkout: `a1cfd0f89e4c047733520063073cbfe7b654f384`.
The family producer and generic exporter are unchanged from the initial producer
commit. The complete Release solution was built at `0fc68148`; the later overlay
comparison adds test-only code, with no production `src/`, compiler or canonical
source changes. The asset signatures and retained master/export hashes bind the
actual content independently of those commit boundaries.

| Check | Observed result |
| --- | --- |
| Pinned native Blender | 5.2.2 x64, build `d13f752e3b9c`; executable SHA-256 `f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0` |
| Release restore/build | Passed; zero warnings/errors |
| Project graph | Passed, 44 projects |
| PR version | Reconciled to 0.1.428 against current master 0.1.427, exactly one net PATCH |
| Portable source/export contracts | 29 passed |
| Existing building presentation regressions | 26 passed |
| Native scene mutation contracts | 19 invalid scenes rejected; two-slot/tangent positive passed |
| Native projects | Two independent strict masters per family saved; first reopened/reexported |
| Scoped compiler trees | Eight 16-asset catalogs compiled and validated with absent source roots |
| Determinism | First/second/reopened source and runtime files are byte-identical per family |
| Typed compiled contracts | Both family theory cases passed; all eleven family meshes plus dependencies checked |
| Full catalog overlay | Both family cases passed again with full isolated catalogs sealed and validated |
| Canonical catalog | 1,357 assets, 75,122,812 bytes; no source or compiler modification |
| Runtime process/integrity contracts | Passed |
| PBR review | Six 384x216 CPU Cycles authoring images; 7,323,648 texture payload bytes per family |
| Packed PBR projects | Both reopened; all three embedded image hashes and semantic color spaces match source; relative image references retained |
| Retained source lineage | Both masters and all 22 GLBs match current source geometry and recorded hashes |
| Complete Release suite before base synchronization | 3,207 total: 3,184 passed, 23 optional skips, zero failures; 38m 47.689s |
| Natural headless match | Seed 2026, Victory / CommandCoreDestroyed, winner 1 at tick 55,210; exit 0 |
| Native D3D12 preservation views | 24 images / 12 before-after pairs, High at 1080p/4K and three zooms; all repeat reads identical and structural checks pass |
| Native pixel observations | Fuel: one changed pixel across six pairs; ammunition: 14 across six pairs; maximum channel delta 1, unchanged zero-tolerance observation policy |
| Installed package / physical performance | Pending |
| Render integration and human art approval | Blocked/open; no production promotion |

Original source qualification, packed-image checks, native mutation results, PBR
reports and unchanged authoring PNGs are retained under
[`industrial-plants/evidence/`](industrial-plants/evidence/). These PNGs are
authoring inspection, not native game captures, accepted goldens or FPS evidence.
The actual commands above generate raw compiler, source-less and typed comparison
logs under `artifacts/ip1/`; canonical, build, full-suite and overlay logs are under
`artifacts/industrial-plants/`.

The first native matrix binds executable source
`de5991b07b01b0b30c5be3feab4cd6a419d0b06e`, version `0.1.427-dev`, on NVIDIA RTX
PRO 5000 Blackwell Generation Laptop GPU. It uses three warmup frames, eight
samples and eight independent readbacks per case. Source, hardware and settings
match within each before/after pair; the runtime catalogs intentionally differ.
The 15 one-byte pixel observations remain recorded without causal attribution,
image tolerance changes or golden approval. CloseTactical retains the existing
gallery framing that clips the nearest foundation at the viewport edge. These
short functional runs do not establish sustained performance or moving-LOD quality.
The D3D12 debug layer was unavailable.

Keep qualification output paths short on Windows. The longer initial
`artifacts/industrial-plants/qualified` path exceeded the native file path limit
for `fuel_refinery.structural_frame.glb`; the failed report/logs are retained there.
This is an output-path failure, not a passing export or a geometry failure.
The direct native-mutation invocation passed after correcting an initial native
PowerShell `-File` array-argument binding error; the failed attempt remains under
`artifacts/ip-negative/`, and the successful actual Blender run under
`artifacts/ip-neg2/`. Earlier restricted restore/Python attempts did not establish
valid results; regular local toolchain repeats produced the results above.
