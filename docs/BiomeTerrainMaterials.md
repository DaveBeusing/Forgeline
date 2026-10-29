# Biome terrain materials

The opt-in `terrain.biome.v1` presentation profile derives existing terrain material
weights from an immutable environment query and copied infrastructure masks. It
does not modify heightfields, navigation, gameplay identity, saves or decoration.

## Selection and ownership

Use `TerrainVisualProfileResolver.CreateBiomeDefinition()`, then the new
`TerrainRenderer(visual, graphics, world, environmentQuery, infrastructure, ...)`
overload, or resolve a profile and pass it to the existing renderer constructor.
The environment query must describe the same immutable world for its lifetime.
`CreateEnvironment(world, recipe)` validates grid compatibility and supplies a
BiomeField adapter that reads the stored outer heightfield samples. It leaves
TerrainWorld's exclusive maximum-boundary query semantics unchanged.

The existing renderer constructor and authored profile math remain intact.
Known `central_divide.production` and `split_basin.production` IDs retain the
existing renderer's Central Divide presentation defaults. They reject procedural
arguments. Unknown IDs and incompatible material lists fail explicitly. The
client scenario registry is unchanged; selecting a generated scenario requires
the caller to wire this explicit visual definition and environment query.

## Weights and palette admission

Forest uses dirt/grass at 65/35, meadow grass/dirt at 90/10, dry terrain
dirt/gravel at 80/20, rock rock/gravel at 90/10 and wet terrain mud/dirt at 80/20.
Normalized biome contributions mix continuously. Natural surfaces gain rock from
slope and altitude above 750 m, and up to 15% mud from high moisture. Explicit
rock, water and built surface classifications select rock, mud/rock and industrial
ground respectively. Invalid or missing environment samples fail explicitly.

Infrastructure inputs are copied, deduplicated and canonically sorted. Overlays
apply mud, dirt, gravel, industrial ground, then concrete; concrete therefore wins
over roads. Rotated rectangles support cubic feathering. Road helpers produce
gravel masks. Existing immutable surface marks apply afterwards and participate
in palette admission.

Each chunk preflights all 1,089 control samples in Z/X order. The dominant
material at every sample and all intersecting infrastructure/surface-mark priority
materials are required. More than four required materials is an explicit error,
rather than a silent loss. Remaining slots use aggregate weight, then stable
material-slot order. The final palette is sorted by slot ID.

Shared level-zero edges use the intersection of adjacent palettes; four-way
corners include the diagonal neighbor. Shared dominant materials survive admission.
Minor materials absent from an adjacent palette are omitted and the retained
weights renormalized. Different local channel indices therefore decode to equal
eight-slot semantic edge weights. This contract does not guarantee identical
filtered edge colors at every mip level.

Narrow masks conservatively expand by half the 8 m control-cell diagonal
(about 5.657 m). This preserves off-grid roads but may make them visually wider;
gameplay footprints do not expand. During mip reduction a required channel that
contributed positive weight retains at least one byte, transferred from the
largest channel when quantization would erase it. Every RGBA texel still sums
to 255. Authored profiles use the unchanged reduction path.

## Bounds and budgets

| Contract | Version 1 |
|---|---:|
| Complete rectangular world | At most 256 chunks, each 256 m |
| Coordinate magnitude | At most 1,000,000 m |
| Infrastructure inputs | At most 4,096 regions |
| Candidate regions per chunk | At most 64 |
| Materials / active layers | Existing eight IDs / four |
| Control texture | 33x33 RGBA, six mips, 5,720 logical bytes |
| Terrain bindings / maximum shader samples | 13 / 13 |
| Preparation environment queries | 1,089 per chunk for admission, 1,089 per control build |
| Environment queries per rendered frame | Zero |

No shader, albedo/normal/ORM binding, texture slot or draw split is added.
Existing surface-priority LOD protection also covers infrastructure masks;
protected chunks can retain more triangles than unprotected chunks. This is not
a frame-time performance guarantee. WithSurfaceMarks creates a new immutable
palette generation; callers own its preparation cost and lifetime.

CaptureBiomePalette exposes required bits, selected slots, candidate counts and
sample count. The existing MaterialPalette debug view displays layer weights.

## Qualification and reproduction

The Windows x64 qualification uses SDK 10.0.402, Release, source master
`569375d4ef673ccea87a90a57eae06d3053bcb2a` plus these presentation changes,
branch `feat/biome-terrain-materials`, version 0.1.525.
Restore, full solution build, project-reference validation and selected tests
are recorded with the pull request. The source contains no new asset dependency.

BiomeTerrainMaterialTests cover normalized controls and mips, five biomes,
altitude/slope, deterministic ties, negative edges and four-way corners, thin
roads, one-texel features through the 1x1 mip, immutable marks, actual generated
terrain/BiomeField integration, invalid data and budget rejection.

BiomeTerrainMaterialNativeTests runs D3D12 on WARP at 640x360 with a fixed camera,
two chunks, a smooth meadow/wet transition, a narrow road and a concrete apron.
It asserts repeatable normal/debug captures, two-or-fewer draws, 13 bindings,
13 maximum samples, no submission faults and no per-frame environment queries.
One local setup measured 94.6203 ms and 2,564,312 managed allocated bytes;
this includes renderer/shader/resource setup, is advisory and is not a GPU or
isolated palette benchmark. Two resident controls occupy 11,440 logical bytes.

To write local captures while running the three biome test classes:

```powershell
$env:FORGELINE_BIOME_TERRAIN_CAPTURE = "$PWD/artifacts/biome-terrain-capture"
$env:FORGELINE_BIOME_TERRAIN_PHASE = "changed"
```

BiomeTerrainLegacyCaptureTests is opt-in. First capture phase `baseline` on
the parent authored presentation implementation, then `changed` with the same
fixture, camera, SDK and adapter. The changed phase asserts the baseline manifest.
The ordinary CPU tests also pin authored control hashes without local files.

| Authored map | All-chunk/all-mip control SHA-256 |
|---|---|
| Central Divide (144 chunks) | `9276b7effd34e3520eae35453cd31b4c7a167fb6e7569bba37c8dba03252032f` |
| Split Basin (64 chunks) | `2d93e94abe550fd76f3eea3480e13636f9b2d50d34c37ec59de4fd9672629273` |

The paired WARP normal/MaterialPalette captures also compare byte-for-byte.
These captures use deterministic fallback material colors and the real D3D12
pipeline. They qualify control/render compatibility on WARP, not production
texture appearance or physical graphics acceptance. Production asset textures,
physical GPU timing and representative hardware art review remain separate.
