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
