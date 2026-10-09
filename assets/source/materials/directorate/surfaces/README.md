# Directorate concept surface atlas

The production surface atlas follows the Directorate palette and functional material vocabulary: olive painted armor, graphite structural steel, rubber, canvas, blue sensor glass, restrained warning stripes, concrete and maintained wear. Native 4096 x 4096 Base Color, Normal and ORM sources replace the historical 1254-pixel color/128-pixel numeric baseline. Historical sources remain editable references. See [Production surface masters](../../../../../docs/ProductionSurfaceMasters.md) for authoring, geometry and qualification policy.

The 4 x 4 cells are, in row-major order:

| Row | Column 1 | Column 2 | Column 3 | Column 4 |
| --- | --- | --- | --- | --- |
| 1 | Painted armor | Structural steel | Rubber tread | Canvas |
| 2 | Sensor glass | Warning stripes | Concrete | Heat-treated metal |
| 3 | Worn armor | Vented equipment | Pallet wood | Mineral rock |
| 4 | Conifer | Scrub | Grass | Service panels |

Authored UV seams place each component inside its intended cell with a 0.018 normalized inset. Wheels/tracks use rubber; infantry and cargo coverings use canvas; sensor/cab faces use glass; barrels and hot process machinery use heat-treated metal; service access and barrier faces use warnings. Prop and vegetation primitives have their own UV layouts while preserving stable mesh, material and LOD IDs.

The historical `surface_orm.tga` is retained as reference data. Production ORM uses shared height/wear fields with the R = AO, G = roughness, B = metallic contract. Rubber, canvas, concrete, wood, glass and vegetation have zero metalness; glass has a smoother response; exposed structural steel has higher metalness. The old reference cell triples were:

```text
(240,184,85) (240,152,220) (255,240,0) (255,245,0)
(255,55,32) (255,200,30) (245,235,0) (220,195,200)
(220,230,100) (235,185,190) (245,230,0) (240,230,0)
(240,245,0) (245,245,0) (245,245,0) (240,180,110)
```

Offline compilation derives BC7 Base Color at 2048, Normal at 1024 and ORM at 512. Their terminal mips retain 128 x 128 dimensions and existing UV insets protect neighboring cells. All atlas materials use the matching Normal atlas. Runtime never reads PNG/TGA sources. The three texture chains occupy 7,323,648 resident bytes; this is not a total VRAM metric.
