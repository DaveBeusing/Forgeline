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

## Reproduction

Use the pinned Blender 5.2.2 x64, .NET SDK 10.0.401 and PowerShell 7.4+:

```powershell
pwsh tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/industrial-plants/toolchain
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh build/industrial-plants/Qualify-Masters.ps1 -OutputRoot artifacts/industrial-plants/qualified
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
```

Surface jobs use the measured unchanged source triangle/section/geometry ceilings
and the existing 7,325,040-byte texture payload ceiling. No new texture, emissive
map, material, state or process gameplay is introduced. In particular, existing
active VFX remain presentation consumers of real state; a new emissive appearance
is not implied by creating a master.

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

Measured execution results and retained evidence are recorded after qualification;
unexecuted or interrupted checks must remain pending.
