# Visual source contract and production readiness

Analysis anchor: `2b4b61ece625d1311c9ee5564cf513dadf2d1123`, remote `master`,
tracked version `0.1.425-dev`. This contract qualifies a preservation workflow;
it does not approve production artwork or the rendering optimization milestone.

## Source inventory

Run from the repository root, using a fresh output path:

```powershell
python tools/blender/readiness.py --output artifacts/readiness/inventory.json
# After the canonical compiler produces the requested runtime root:
python tools/blender/readiness.py --runtime artifacts/readiness/catalog --output artifacts/readiness/compiled-inventory.json
```

The report reads all source definitions for buildings, units, infrastructure,
world and shared Directorate materials. Every row records the stable ID, source,
format, owner domain, LODs, collision, sockets, mechanical partitions, material
references, texture settings and missing references. Static geometry is read
through the existing bounded importer, including actual bounds and vertex
channels. Skeletal sources retain their separate contract; their primitive
channels, skin count and named clips are reported without pretending that the
static importer validates a rig.

The source ledger includes definitions, referenced inputs and external static
buffers, saved Blender masters and retained component kits. Its aggregate SHA-256
is over sorted relative-path/content-SHA-256 pairs with compact JSON encoding.
The report checks the current 13 building, 7 unit, 8 terrain-material and 4 deposit
roots. A changed roster requires an explicit source and inventory review.

`blender-backed` means a known production export is bound to an existing saved
master, not that all companion states are Blender-authored. Other existing
sources are `present-without-bound-blend-master`; their measured triangle counts
distinguish a compact mesh from an absent source without a subjective fidelity
threshold. A saved material master is listed independently from geometry masters.
Presence, source maturity and visual qualification are separate facts.

Runtime rows without a supplied runtime manifest say `not-observed`. A container
found through a supplied manifest is only `container-present-not-qualified`, with
its observed hash and length. That does not assert successful compiler execution,
source correspondence, typed payload integrity, renderer selection or packaging.
Retain the canonical compiler's qualification and source-less validation reports
alongside the inventory. Never substitute file existence for those checks.

## Staged three-family pilot

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
./tools/blender/Test-BlenderReadiness.ps1 -OutputRoot artifacts/readiness/pilot
```

The reviewed fixture selects Command Core with its declared construction/damage
and destruction companions, Main Battle Tank with its LODs, turret and states,
and the existing world crate High/Reduced pair. It uses the existing pinned
Blender launcher, importer, exporter, dependency closure and negative native
contracts. It creates two independent saved masters, reopens/re-exports the first,
compiles baseline and exported source trees separately, validates all four runtime
trees with an absent source root, compares repeated/reopened source/runtime bytes,
and checks the production source tree was unchanged. Existing state IDs are listed
explicitly because not every presentation-selected state is a root dependency.

All output stays in a fresh `artifacts/` subtree. The pilot does not replace source
assets or publish an overlay into the player. Source-less catalog validation is
distinct from launching an installed source-less player. The summary leaves native
renderer, physical performance, installed-package and human art gates pending.
It is deliberately impossible to obtain those approvals from this script alone.

Preserve the documented mapping: engine `(x,y,z)` to Blender `(x,-z,y)`, metric
scale 1, inverse UV V mapping, exact stable IDs and ordered material slots. Retain
gameplay footprints, sockets, mechanical pivots and collision references. Existing
normal/tangent and coordinate tolerances remain authoritative. No new animation,
material graph, scene-transform or source-format capability is introduced.

## Observed renderer limits

Recheck these source anchors whenever the renderer changes:

| Contract | Current source anchor / meaning |
| --- | --- |
| Terrain | `TerrainMaterialPipeline.cs`: 13 textures, four active material layers, control texture plus four Base Color/Normal/ORM sets |
| Instance material channels | `InstanceMaterialPipeline.cs`: Base Color, Normal, ORM and Emissive; renderer-owned shadow bindings remain separate |
| Shared surface runtime maps | `ProductionSurfaceMasters.md`: 2048 Base Color / 1024 Normal / 512 ORM, BC7 full mips; 7,323,648 logical texture bytes |
| Gameplay compiled textures | Existing window qualification: 56 MiB container gate, with its existing explicit splash exclusions |
| Native scene texture residency | Existing 64 MiB peak-texture gate; a different metric from container file lengths |
| Prepared payloads | `AssetResidency/PreparedAssetSet.cs`: 16 MiB per container, 128 MiB admitted payloads |
| Pinned mesh and texture mips | `AssetResidency/ResidentAssetStore.cs`: 96 MiB per device/catalog/profile; separate skin/clip bounds |
| Skin / clip caches | 8 skins / 32 MiB, 128 clips / 16 MiB |
| LOD hysteresis | `ScreenSpaceLod.cs`: 12%; retain asset-specific selection thresholds and world High/Reduced conventions |

These are independent existing limits. Their sum is not measured process VRAM.
Descriptor capacity, transient uploads, render targets, allocator padding, terrain
residency and retiring generations require separate observations. Do not enlarge
a limit to admit art. Triangle/section measurements are not a sustained-FPS claim.

## File ownership and conflict policy

This is a proposed content ownership boundary, not a release of the existing
renderer ownership hold on `assets/source/` or the compiler. Recheck
`docs/rendering/parallelization/ParallelRenderOwnership.json` before source writes.
Authoring can proceed in isolated outputs while renderer qualification is open.

| Domain | Owned source files and fixtures |
| --- | --- |
| Buildings | `assets/source/buildings/directorate/`: per-family metadata, kits, exports and future family-scoped `.blend`; split Command Core, operational, industrial and logistics families by explicit file lists before simultaneous edits |
| Units | `assets/source/units/directorate/`: tank, infantry and support-vehicle metadata, kits, masters and exports; collision/material reuse is a shared dependency, not permission to edit it |
| Infrastructure | `assets/source/infrastructure/directorate/`: existing roads/bridge state metadata, geometry and materials; ford consumes existing road presentation |
| World | `assets/source/world/`: terrain material slots, four deposits, props, vegetation, decals and dressing; divide by exact files before simultaneous edits |
| Shared materials | `assets/source/materials/directorate/`, including the procedural material master and recipe/lineage; coordinate consumers before promotion |
| Common authoring contract | `tools/blender/` generic validation, readiness fixtures/tests and this contract; content-specific producers belong under family-scoped paths |
| Generic compiler | Existing `tools/ForgeLine.AssetCompiler/` owner; contract changes require a separate focused review and targeted validation |

The inventory gives each existing definition one domain owner and lists its
referenced files. Shared files are owned once. A content branch changes only its
agreed exact file list, uses existing stable IDs and retains provenance. New
metadata must not shadow another definition. A shared texture, generator, collision
proxy or test fixture needs coordination with its owner before modification.

Do not hand-merge generated `assets/runtime/manifest.json` or runtime containers.
Merge authoritative source definitions, reject duplicate IDs, recompile into a
fresh output, and compare dependency/source signatures. Serialize shared registry,
material and generic tooling changes. Rebase content PRs one at a time onto current
`master` and reconcile exactly one PATCH for each PR; preserve logical commits.

## Approval record required for production promotion

For each pilot/family retain the exact source SHA, source/asset signatures, compiler
and Blender identities, runtime and installed-package checksums, hardware/driver,
display/profile/camera/state/workload and commands. Capture actual imported objects
with explicit fallback/binding diagnostics. Compare close, normal and strategic
views, bright/dark/wet settings, 1080p/4K and Low/Medium/High; include 1/100/1,000
instances where appropriate. Keep every failed cohort.

Record silhouette/role readability, LOD transitions, material normals/tangents,
socket/pivot alignment, culling/bounds, collision independence, simulation/replay
checks, native images and moving-state review. Performance evidence needs paired
identical workloads/warmup, CPU/GPU distributions and resource observations. Null
or unavailable GPU metrics remain unavailable. WARP is functional evidence.

Promotion requires current-source CI, the integrated optimization baseline and its
required acceptance, an installed source-less player, unchanged simulation and
resource contracts, measured performance deltas and explicit human visual approval.
Every unresolved mandatory item remains a blocker. No schema or image-repeat pass
automatically grants human acceptance. Do not merge, tag, release or approve goldens
while these gates remain open.
