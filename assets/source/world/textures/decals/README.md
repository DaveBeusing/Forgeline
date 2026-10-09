# Battlefield decal atlas

Original RGBA authoring atlas, 1254 x 1254, four columns and two rows. Row-major cells are tire tracks, tracked-vehicle marks, road wear, oil stain, blast mark, shell impact, scorch mark and concrete crack. Each stable decal material selects its cell with `uvScale` and `uvOffset`; the materialless shared quad has authored normalized UV0 and accepts the placement material.

The normal compiler caps runtime resolution at 512 and retains four mip levels. A 0.018 normalized inset keeps sampling inside the assigned cell. Transparent edges use ordered pixel coverage in the existing depth-tested instanced pass. Fully transparent pixels never write color or depth. Coverage avoids adding a separate sorted transparency renderer, while retaining gradually fading alpha at RTS scale.
