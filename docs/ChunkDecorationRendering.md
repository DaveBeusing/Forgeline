# Chunk decoration rendering

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

## Ownership and composition

ChunkDecorationSnapshot copies EnvironmentScatterChunk instances and species into
an immutable, canonically sorted presentation input. ChunkDecorationProvider
publishes a fixed snapshot through IChunkDecorationProvider. Inject that provider
into PresentationExtractor, or explicitly attach the snapshot to
PresentationSnapshot.ChunkDecorations. Provider implementations must return
immutable **public scenery**, never live entity or intelligence observations.

The Windows client's existing SimpleInstanceRenderer preparation/render/shadow
calls consume this property automatically. Existing scenarios do not generate
scatter or attach a provider implicitly. Scenario/compiler integration chooses the
source snapshot; renderer quality never calls the scatter generator.

Decoration is absent from PresentationSnapshot.Instances, entity lookup,
selectable metadata, minimap/hover/command inputs and simulation counts. The
private render world uses EntityId.None for every decoration; it never reserves
an allocator range. A stable ordinal supplies private LOD history while the
complete 64-bit cell identity supplies density selection. Zero is a valid scatter
identity. Decoration arrays and provider references are excluded from snapshot
JSON; no per-instance save/map JSON is added.

This is the established public static-scenery policy, not a new Fog of War
visibility service. No hidden gameplay state, cover, collision, navigation or
replay input is queried or changed.

## Placement, assets and quality

Chunk data is copied once and expanded only on resident admission. Chunk/instance
order is canonical Z/X then stable ID. Camera changes, eviction and return reuse
the same source transforms. Decoration never interpolates between unrelated
EntityId.None entries. A replaced source/session resets residency and private
history. Lower vegetation density hashes the full 64-bit identity, preserving a
stable subset; distance and density filters apply equally to color and shadow
submission. Props retain the existing vegetation-only density policy.

Every admitted compiled high/reduced mesh is normalized from its own manifest
bounds about horizontal center and the species' normalized vertical pivot, then
scaled to ScatterTransform.SizeMeters. Rooted vegetation remains on its ground
anchor; centered props retain their supplied height. Missing reduced content
falls back to the high mesh. Missing high meshes skip that instance; malformed,
flat or missing bounds fail during preparation. A null catalog retains the
explicit development cube path. Normalized bounds include all yaw rotations,
full rooted height and the existing wind sweep.

Asset IDs from the scatter palette are preserved, including asymmetrical tree
and leaning-rock variants. Declared compiled LOD references, projected-size
selection, hysteresis, material bindings, instance batches and visual-time wind
reuse the existing renderer. No new mesh, shader, impostor, texture, material or
third-party dependency is introduced. Private decoration batches are separate
from gameplay batches; both use the same existing instanced draw machinery and
shared device/catalog asset leases.

## Preparation and residency

Call SimpleInstanceRenderer.Prepare before recording, with the exact camera,
viewport and optional shadow projection. Preparation admits the union of color
and shadow chunk bounds. A stale source/session, camera, viewport or shadow is
rejected; no lazy decoration GPU allocation occurs during recording.

| Limit | Default / supported range |
|---|---|
| Copied source chunks | At most 4,096 |
| Copied source instances | At most 1,000,000 |
| Distinct source mesh IDs | At most 64 |
| Resident chunks | 32 / 1..256 |
| Resident expanded instances | 65,536 / 1..65,536 |
| New chunk admissions per Prepare | 32 / 1..256 |
| LOD/normalization bindings | At most 128 of each per generation |
| Instance upload layout | Existing 112 bytes per submitted part |

DecorationSettings exposes the three residency/admission limits. Required
coverage exceeding any limit fails before changing the resident generation; it
does not silently choose an incomplete visible subset. Optional cached chunks
evict by least-recent use and coordinate order. No frame streaming, asynchronous
partial population or occlusion feedback is implemented. Existing conservative
CPU clusters and individual frustum tests remain active.

Resident expanded arrays, current/previous private snapshot copies, correspondence,
bounds/pose indices, batch scratch and frame-indexed GPU instance buffers are
separate allocations. ResidentInstanceBytes counts expanded cache payload;
SnapshotInstanceBytes counts the current/previous snapshot payload. Work exposes
retained pose/spatial index bytes. These values exclude object/array headers,
allocator overhead and the compact source. A scatter wire record is 28 bytes;
managed ScatterInstance size may include padding.

Color/shadow instance upload buffers grow to bounded power-of-two capacities and
are reused per frame slot. Native frame slots are bounded by the existing 2..4
buffer configuration. They retain their high-water capacities until renderer
disposal; eviction releases CPU chunk arrays, not shared catalog mesh/texture
resources. SharedAssets reports the existing finite catalog/session cache, not
a per-chunk mesh eviction service or physical VRAM. Native releases retain the
existing submission-fence ownership.

Shadow casters/draws consume the remaining existing quality budget after gameplay
submission. Color and shadow use identical source geometry and visual time.
There is no new occlusion, DXR, mesh-shader, indirect or compute requirement.

## Diagnostics and reference fixtures

DecorationDiagnostics reports source/candidate/resident counts, admissions and
evictions, expanded/snapshot bytes, visible/LOD/wind counts, draws, upload bytes,
retained instance buffers and shared mesh/material payload. Work includes cluster,
bounds, interpolation, asset resolution and submission counts.

PreparationMilliseconds measures CPU chunk admission, copying and private index
publication. CullingAndAdmissionMilliseconds covers CPU index/frustum/density/LOD,
asset resolution and batch admission. UploadPackingMilliseconds covers CPU staging
packing, buffer growth and SetData; it is not a GPU transfer duration.
SubmissionMilliseconds additionally includes draw recording. Existing graphics
diagnostics supply completed-fence world/shadow GPU times and adapter memory.
These inclusive stages must not be added as independent nonoverlapping totals.

After a normal Release build:

```powershell
$env:FORGELINE_DECORATION_CAPTURE = "$PWD/artifacts/chunk-decoration"
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ChunkDecorationTests ForgeLine.Presentation.Tests.ChunkDecorationNativeTests --parallel none
```

The CI step runs these same contracts and preserves the PNG/JSON artifacts.
Tests include immutable source copying, signed chunk ownership, atomic budget
rejection, shadow-frustum union, eviction/return, unchanged alpha interpolation,
stable density subsets and explicit absence from simulation/selection snapshots.
Offset, nonunit mesh bounds differ between high and reduced fixture meshes, so
the root/center anchor tests cannot pass by assuming unit cubes.

The two dense fixtures use real seeded scatter with synthetic tall grass,
1,000 instances across four chunks and 10,000 across 25. The mixed fixture uses
Temperate scatter, five classifications and eleven real asset IDs: 1,664 instances,
11 color draws, 224 wind-animated instances and 899 visible shadow candidates in
one executed capture. All resolve through compiled fixture meshes with zero
fallback meshes and zero binding/submission faults. Repeat captures agree exactly.

The executed Windows x64 WARP cohort uses SDK 10.0.402, Release, source base
ea817c2b8bb23441c9d7b2ae66f5d79a61aa2de4 plus this presentation change, branch
feat/chunk-decoration-rendering. Four warmups and eight timed frames precede two
readbacks; CPU includes Prepare, submission, Present, diagnostic observation and
retirement. World GPU samples have distinct completed fences. These short,
unpaired software-adapter samples are advisory, not performance gates:

| Instances | Color draws | Color upload bytes/frame | Retained color+shadow instance buffers | CPU mean ms | World GPU mean ms |
|---|---:|---:|---:|---:|---:|
| 1,000 | 1 | 112,000 | 688,128 | 28.020 | 1.376 |
| 10,000 | 1 | 1,120,000 | 5,849,088 | 11.393 | 1.724 |

The inverted CPU ordering illustrates short-run scheduling variance; no scaling
speedup is claimed. Full logical frame/asset/CPU-stage records are exported by
the fixture rather than inferred from screenshots. Existing authored extraction,
visibility, wind, shadow/resize, biome, headless save/load/replay and architecture
regressions run separately.

These fixtures use synthetic compiled mesh/material resources, not production
textures or approved art. Physical GPU timing, production vegetation fidelity,
continuous traversal/resize soak, real device removal and sustained live-match
performance remain separate acceptance work. Inputs above 10,000 have explicit
bounds but no new measured performance qualification.
