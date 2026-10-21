# Battlefield decal atlas

Original Blender-baked RGBA atlases, 1024 x 1024, four columns and two rows. Row-major cells are tire tracks, tracked-vehicle marks, road wear, oil stain, blast mark, shell impact, scorch mark and concrete crack. Each stable decal material selects its cell with the unchanged `uvScale` and `uvOffset`; the materialless shared quad retains normalized UV0 and accepts the placement material.

The existing color/straight-alpha map remains capped at 512 with four mip levels.
Shared tangent Normal and ORM maps are capped at 256 with four levels, using
lossless RGBA8. Their combined resident texel storage is 2,088,960 bytes. Color
is sRGB; normal and ORM are linear (R AO, G roughness, B metallic). Soil, soot,
oil and concrete remain nonmetallic. Oil uses a low roughness, while dusty wear
retains a rough response. Source color contains no directional lighting/specular.

The unchanged 0.018 normalized inset, bounded mip depth and transparent gutters
keep sampling inside each assigned cell. Transparent edges use straight alpha
with ordered pixel coverage in the existing depth-tested instanced pass. Fully
transparent pixels never write color or depth. Live opacity/lifetime and receiver
visibility remain owned by the existing presentation pool.

Editable geometry and exact bake reproduction: [saved masters](../../authoring/decals/README.md).
