# Battlefield effect atlas

Original RGBA authoring atlas, 1254 x 1254, four columns and four rows. Cells contain fire, smoke, dirt impact, sparks, explosion, muzzle flash, projectile streak, dust, logistics pulse, metal impact, destruction smoke, ammunition flash, bullet tracer, concrete impact, refuel pulse and loading pulse.

The ten stable effect material families select their corresponding regions through `uvScale` and `uvOffset`. All existing effect meshes have authored normalized face UVs. Fire, flash, spark, projectile and transfer materials also reference the atlas through a separate Emissive-usage declaration. Color and Emissive share the editable PNG but compile with their own semantic metadata. No runtime image loader is introduced.

Compilation caps both runtime textures at 512 with four mip levels. Cell insets and ordered alpha coverage use the same contracts as world decals. Existing effect pool, lifetime, distance reduction, event mapping and gameplay ownership remain authoritative. Reserved cells can support finer material differentiation without adding another image family.
