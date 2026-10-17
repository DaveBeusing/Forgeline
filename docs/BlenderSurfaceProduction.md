# Bounded surface and LOD production

The production entrypoint extends the pinned Blender launcher and validated source
importer. It prepares reviewable source clones under `artifacts/`; the player and
normal asset build have no Blender dependency. It does not apply results to
`assets/source` or replace the existing `build/Author-CommandCore.py` and vehicle
kit generators. Run those generators only as their existing production guides
prescribe, review their source changes, then import the resulting baseline here.

```powershell
dotnet build ForgeLine.sln --configuration Release
pwsh tools/blender/Invoke-BlenderProduction.ps1 -OutputRoot artifacts/surface-review -RenderPreviews -TimeoutSeconds 480
pwsh tools/blender/Test-BlenderProduction.ps1 -OutputRoot artifacts/surface-qualified -RuntimeSeedRoot assets/runtime -RenderPreviews
python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -v
```

The default profile, `tools/blender/fixtures/surfaces.json`, prepares the existing
LOD0/1/2 of Main Battle Tank and Command Core without artistic changes. Its triangle,
section and geometry byte ceilings are the measured source baseline; texture payload
is capped at the same 7,325,040 bytes. These are fixture-specific preservation
budgets, not universal content limits. State variants and their material/texture
closure are included unchanged; an explicit job can also prepare a selected state.
Collision targets and sources are rejected. Missing references, changed source
hashes, invalid UVs/normals/tangents and failed limits produce a nonzero exit and
`production-report.json` with the error; launcher logs remain alongside it.

## Authoring and controlled changes

Copy the profile to a review-specific path and pass `-Profile`. Each job is keyed by
its stable output asset ID. Available options are `ratio` (0..1, excluding zero),
`method` (`COLLAPSE` or `DISSOLVE`), `bevelMetres` (0..0.05 canonical metres),
`bevelSegments` (1..3), `weightedNormals`, `protectedParts`, and explicit `limits`.
The limits require `maxTriangles`, `maxSections`, `maxGeometryBytes`,
`minSilhouetteIou` and `maxRelativeBoundsError`. Unknown options fail. Review any
intentional budget increase separately; the tool never adjusts limits to fit output.

Geometry preparation evaluates a copied mesh for each existing primitive/part,
with bevel, optional decimation, weighted normals and triangulation in that order.
UV and material boundaries are retained by the source vertex splits; planar dissolve
also explicitly delimits normals, materials, seams and UVs. No automatic welding or
UV repacking occurs. Bevel therefore affects connected eligible edges, not detached
hard-surface components. Hard-edge topology may legitimately yield no bevel or no
useful decimation. New UV degeneracy, atlas chart boundary escape or silhouette loss rejects
the result. It does not bake arbitrary shader graphs.

Socket-bound moving parts and named protected parts bypass shape modifiers. Parts
remain separate, keep local pivots and preserve hierarchy; index ranges are rebuilt
for their new contiguous partitions. Mechanical meshes require one production
primitive. `sourceId` can select another imported mesh for LOD generation only when
ordered materials and part definitions match, and all target sockets match; incompatible root-to-LOD conversions
are rejected rather than discarding moving bindings. Authoring a compatible LOD
baseline remains an explicit source production decision.

The `.blend` contains separate per-asset source collections and hidden prepared
copies with their modifier stacks. Asset roots are not translated to make a gallery.
Use collection visibility to inspect a state or LOD. The saved project is an
inspection/editing workspace; this entrypoint regenerates from authoritative source
and profile, and does not export arbitrary edits from a saved project. The strict
preservation exporter remains available for its original supported workflow.

## PBR inspection and evidence

Packed Base Color, Normal, ORM and Emissive images use the source color spaces.
The preview multiplies base color and ORM roughness/metallic factors, applies UV
scale/offset including the V-axis conversion, and uses the emissive multiplier.
ORM R remains the authoritative game's occlusion input; Blender Principled has no
equivalent direct occlusion input. This preview is not shader parity certification.
Materials, IDs, atlas sources and texture compiler settings are copied unchanged.

Close, normal and strategic perspective cameras share a neutral world, area light,
CPU Cycles renderer, eight samples and 384x216 resolution. Camera distances derive
from the asset bounds and vertical FOV; optional PNGs are authoring inspection,
not D3D12 acceptance images or performance measurements. Tank inspection includes
its separate turret source. The game's fixed RTS reference scenes remain the
runtime visual authority.

Reports list all meshes, states, LOD links, ordered material bindings, triangle,
vertex and section counts, source hashes and texture mip costs. Geometry cost uses
48-byte compiled vertices plus uint32 indices, excluding container/driver overhead.
BC7 estimates include minimum 4x4 blocks at small mips. Native compiler validation
and runtime measurements remain authoritative for actual payload and residency.
Silhouette IoU uses 96x96 orthographic triangle coverage at seven documented views;
relative bounds protect major forms. This finite sampling cannot replace art review
or identify every internal detail loss. No gameplay, footprint, collision, simulation,
selection or renderer contract is redefined.

The qualifier performs two independent production exports, compares source and
compiled file bytes, validates source-less assets and runs the existing compiled
geometry/metadata/mechanical batching comparison for all 19 meshes. Native modifier
cases cover planar reduction, collapse, calibrated bevel, normals, invalid options
and moving-part protection. A full runtime seed enables a sealed overlay for the
existing native capture/smoke tools. Normal CI executes Python contracts; the manual
Blender workflow also executes native production qualification. Runtime visual,
resource and package results are recorded separately in the qualification report.

Do not copy a failed export into production. Keep its logs, fix the profile or source
cause, and rerun into a fresh output root. Reimport after changing authoritative
sources. Applying a successful candidate requires reviewing its source definitions,
compiler output, runtime captures and budgets through the normal content PR process.
