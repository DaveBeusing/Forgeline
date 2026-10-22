# Resource deposit cluster geometry

Implementation base: master `d4e100e7` (0.1.452). These sources replace the four
canonical resource shapes with modular geological clusters. Existing resource,
High/Reduced mesh, material and symbol IDs are retained. No renderer, field-spawn,
simulation, navigation, texture or material-source changes are included.

## Frozen geometry naming and morphology

`assets/source/world/authoring/resource_clusters/variants.json` owns the geometry
keys shared with later art-direction, field composition and rendering work. This
records an authored geometry contract, not independent art approval. Runtime seed
selection, occurrence probabilities and tint ranges remain outside this source set.

| Family | Four layout keys | Geometric identity |
| --- | --- | --- |
| Ferrous Ore | `layered_shelf`, `faulted_ridge`, `breccia_fan`, `offset_seam` | Heavy stepped beds, fractured layered shelves and offset seams |
| Silicates | `cleaved_plate`, `crystal_fan`, `stepped_bed`, `split_rosette` | Broad grounded plates, oblique cleavages and leaning crystal fans |
| Volatiles | `sealed_nodule`, `fractured_lens`, `laminated_pocket`, `weathered_saddle` | Rounded angular nodules, closed lenses and capped strata; no liquid pipes or implied transfer |
| Rare Elements | `inclusion_reef`, `split_matrix`, `radial_inclusions`, `faulted_pocket` | Substantial rock matrix with differently oriented penetrating inclusions |

Each layout has `small`, `medium` and `large` families. They change fragment count
and relative composition as well as occupied extent. Fragments have independent
centres, cross sections, yaw, lean, fractured cap heights and irregular ring radii.
Layouts form shelves, ridges, fans and separated lobes; they are not repeated
objects differentiated only by Y rotation or color.

Render IDs are `mesh.world.resource.<family>.<layout>.<size>`, with `_lod1` and
`_lod2` appended for lower detail. Material-free proxies append `.collision` to
the unsuffixed render ID, matching the existing non-rendered collision naming
contract. There are 144 render exports and 48 proxy exports.

The existing `mesh.world.resource.<family>` and `<family>_lod1` IDs point to the
first layout's medium GLBs. Additive `<family>_lod2` aliases provide far geometry.
LOD1 retains its existing 260m metadata threshold; additive LOD2 uses 520m. The
current renderer still selects its existing High/Reduced pair. Extra layouts,
sizes and LOD2 require the separate presentation dispatch integration; this change
does not claim that all of them are already selected in gameplay.

## Editable sources and export

Four native Blender 5.2.2 projects live in
`assets/source/world/authoring/resource_clusters/<family>.blend`. Each contains
the actual editable fragment meshes for all four layouts, all three sizes and all
three LODs, plus closed envelope proxies in separate hidden collections. The
default medium High collection is visible initially. Select another collection
in the Outliner to edit it. Existing shared PBR images use repository-relative
paths; the material definitions and image bytes remain authoritative and unchanged.

`build/resource-deposits/cluster_author.py --action Author` performed the initial
native creation and GLB source export. It refuses to overwrite existing masters.
Its Export action opens the committed native files and serializes their actual
vertices, hard face normals, explicit UV islands and tangents. It does not rerun
the shape recipe, discard hand edits, apply hidden modifiers or synthesize new
gameplay states. It requires identity transforms, metric unit scale and the
existing normalized envelope. Export destinations must be fresh directories
under repository `artifacts`, preserving canonical sources during manual checks.

GLBs are under `assets/source/world/meshes/sources/clusters/<family>/`; descriptors
remain under `assets/source/world/meshes/resources/`. `sources.json` records native
master hashes, every export hash, actual source triangle counts and bounds. This
is an authoring inventory, with validation explicitly marked **NOT EXECUTED**.

## Frame, surfaces, states and LOD cost

The existing centered pivot remains `(0,0,0)`, with reference ground at local
Y = -0.5. Blender uses Z-up/-Y-forward and exports to engine Y-up/+Z-forward.
Units are metres in the existing normalized source frame; gameplay transforms
retain their existing physical placement scale. Small, medium and large occupy
at most 0.68, 0.86 and 1.00 of that unit frame respectively. All LODs of a given
layout/size use one common normalization, not separately recentered/rescaled meshes.
Core fragment bases contact the ground plane; rare inclusions penetrate their host.

Source geometry stays within `[-0.5,0.5]` on all axes. Proxies conservatively enclose
all LODs of each size. They are metadata for downstream selection/collision use,
not simulation obstacles; authoritative resource bounds, picking identity,
quantities, extraction range and navigation are unchanged.

Triangulated hard fracture faces carry normals, tangents and explicit nondegenerate
UV islands inside the existing family's mineral atlas rectangle. No material,
texture, ORM packing or shader permutation is added. Untouched/Active/Depleted
continue to use the existing copied presentation state/tint; no resource amount
or cosmetic state is invented by these assets.

The initial authoring inventory reports the following actual source counts:

| Family | LOD0 triangles | LOD1 triangles | LOD2 triangles |
| --- | ---: | ---: | ---: |
| Ferrous Ore | 180–324 | 60–84 | 24 |
| Silicates | 130–234 | 60–84 | 24 |
| Volatiles | 220–396 | 60–84 | 24 |
| Rare Elements | 208–312 | 96 | 40 |

Each closed proxy has 12 triangles. The four compressed masters total 800,555
bytes; the 192 GLB files total 2,944,520 bytes. These are source-file measurements,
not runtime container, GPU residency, draw-call or frame-time observations.

The local authoring ceilings are 400/100/48 triangles per LOD, using the former
400-triangle Ferrous High and 100-triangle Reduced as reference maxima. Other
families intentionally gain geometry relative to their older single shapes.
These source ceilings do not increase or establish any runtime/GPU budget.
Repeated occurrences share fixed variant IDs instead of per-deposit generated
megameshes. Native 100/1000-instance, startup residency and package-budget
qualification remain required; no resource or performance limits are relaxed.

## Historical sources and prepared contracts

The prior native masters, lineage and GLB copies in `assets/staged/resource-deposits`
and `meshes/sources/blender` remain untouched historical source material. Their
old signatures and screenshots qualify their original geometry, not these clusters.
`retained.py` routes current geometry to `cluster_contract.py`, which separately
hash-checks those historical files and the new master/export contract. It does not
pretend that a historical accepted export matches new topology.

Prepared source contracts cover 192 unique stable IDs, family/material bindings,
three LODs, default alias resolution, source hashes, finite channels, nondegenerate
triangles, winding/normals, tangent bases, ground contact, conservative proxies,
source geometry ceilings and rotation-invariant layout signatures. The existing
round-trip fixture includes three default LODs and one proxy per family; its
optional qualification and typed comparisons retain exact expected mesh sets.
The old pinned Ferrous silhouette assertion now checks the deliberate bounded,
grounded volumetric contract rather than the previous shape's coordinates.

**No builds, automated tests, independent Blender reopening/export comparisons,
CI dispatches, native rendering or GPU qualification were executed.** Only initial
Blender source authoring/export and reading its generated inventory were performed.
No visual golden was changed and human art approval is pending.

## Manual acceptance commands — NOT EXECUTED

From a clean checkout, after saving intended native edits:

```powershell
python build/resource-deposits/cluster_contract.py
$blender = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
& $blender --background --factory-startup --disable-autoexec --offline-mode --threads 1 --python-exit-code 23 --python build/resource-deposits/cluster_author.py -- --action Export --output-root artifacts/cluster-reopen-a
& $blender --background --factory-startup --disable-autoexec --offline-mode --threads 1 --python-exit-code 23 --python build/resource-deposits/cluster_author.py -- --action Export --output-root artifacts/cluster-reopen-b
python build/resource-deposits/cluster_contract.py --source-root artifacts/cluster-reopen-a/source
python build/resource-deposits/cluster_contract.py --source-root artifacts/cluster-reopen-b/source
```

Compare both isolated source trees and their GLB hashes against each other and
the committed inventory. A saved deliberate vertex edit must survive Export;
unexpected transforms, modifiers, nonfinite geometry, changed envelope or missing
collections must fail. Approve and promote intended source changes explicitly.
Then run the canonical build/asset compiler/source-less package tests from
`Development.md`, plus the prepared optional `Qualify-Masters.ps1` and typed
resource tests. Their historical results are not new-cluster acceptance evidence.

Review each family × layout × size × LOD at Close/Normal/Strategic zoom and 1080p/4K,
including low/medium/high quality, light/dark/wet terrain and all three existing
resource states. Check grounded rims, leaning fracture readability, depletion tint,
LOD/shadow transitions, proxy/picking envelopes and native CPU/GPU/resource costs
in ResourceDepositsGallery, ResourceDepositSingle, ResourceDeposits100 and
ResourceDeposits1000. Default reference scenes exercise the retained standard IDs;
the additional variants need their separate presentation dispatch before claiming
full native matrix coverage.
