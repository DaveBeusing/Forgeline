# Production texture and instance budgets

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The canonical compiled gameplay texture limit remains **56 MiB**, the native
scene texture peak limit **64 MiB**, and the canonical instance limit **11 base
batches plus bounded additional LOD splits**. This change reduces real texture
storage and mesh submissions. The draw-budget validator is unchanged.

## Texture storage

Seven 1024-pixel terrain base-color runtime maps and four 1024-pixel portrait
pages use BC7 Balanced. Authoring PNGs, runtime dimensions, mip policy, color
spaces and every sprite region/padding remain unchanged. Text, icons and HUD
frames retain lossless RGBA8. The production surface and atlas generators emit
the same selective compression settings when assets are regenerated.

Compiled gameplay textures fall from **99,608,050 to 57,665,206 bytes**, with
**1,055,050 bytes** remaining under the original 58,720,256-byte limit. The catalog
still contains 1,337 assets, including 900 textures. The 1,786,417-byte studio
intro texture allocation is accounted separately under its unchanged 3 MiB limit.

The UI contract accepts BC7 only for declared portrait pages and uploads them
using the matching GPU format. Core and icon pages still require RGBA8. The
skin's logical uncompressed page-byte bound stays at 32 MiB; resource diagnostics
report actual compressed resident bytes. Tests compare every changed production
texture to an independently compiled RGBA reference at identical dimensions and
mips, retaining a 32 dB RGB PSNR floor and strict alpha-error limits. Native WARP
readback verifies compressed portrait upload, display sampling and resource release.

## Mechanical geometry sharing

Cargo wheels previously generated eight separate draw batches per mesh/material,
although each side uses repeated translated triangle geometry. At mesh load,
`MechanicalGeometryBatching` finds equivalent indexed triangle ranges and assigns
the first matching range as their common geometry. The renderer adds every part's
existing pose matrix as a separate instance of that range.

Equivalence retains triangle order, UVs, normals and tangent handedness. Position
comparison allows 0.00001 local units of translation roundoff; tangent XYZ allows
0.000001 to account for importer rounding. Translation is derived from geometry,
independently of authored motion pivots. A reused part's matrix is
`Translation(actualGeometryOffset) * originalPartPose`, preserving pivot, parent,
nonuniform/mirrored scale, wheel motion and the shadow path. Geometrically distinct
parts keep their existing batches. No new GPU geometry buffers or per-frame
vertex reconstruction are introduced.

The canonical scene retains **24 production mesh-part instances and 14,728
production triangles**. Eight wheel batches become two side-specific geometry
batches; total instance draws fall from **15 to 9**. A requested
`FORGELINE_INSTANCE_BATCH_REPORT` JSON captures mesh/material/range identity and
instance counts at renderer shutdown; normal play adds no capture allocation or IO.

## Qualification inputs

The Windows qualification helper resolves both absolute and relative paths
against the repository. A missing, failed or invalid asset qualification report
fails before native launch instead of skipping the texture gate. Its disposable
settings directory must stay inside repository artifacts. Dedicated contracts
exercise these cases and the unchanged LOD-aware draw ceilings.

Windowed and borderless smoke, full-suite, image-error and package results are
recorded in the associated qualification report. Short budget smokes do not
establish sustained FPS, AAA visual quality or natural match termination.
