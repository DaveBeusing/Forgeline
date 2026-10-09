# Directorate concept surface atlas

The original surface atlas follows the Directorate Concept Sheet V1 palette and functional material vocabulary: olive painted armor, graphite structural steel, rubber, canvas, blue sensor glass, restrained warning stripes, concrete and maintained wear. It is a shared original authoring texture guided by the concept palette and materials. The editable RGB source is 1254 x 1254. Existing tangent-normal texture families remain shared.

The 4 x 4 cells are, in row-major order:

| Row | Column 1 | Column 2 | Column 3 | Column 4 |
| --- | --- | --- | --- | --- |
| 1 | Painted armor | Structural steel | Rubber tread | Canvas |
| 2 | Sensor glass | Warning stripes | Concrete | Heat-treated metal |
| 3 | Worn armor | Vented equipment | Pallet wood | Mineral rock |
| 4 | Conifer | Scrub | Grass | Service panels |

Authored UV seams place each component inside its intended cell with a 0.018 normalized inset. Wheels/tracks use rubber; infantry and cargo coverings use canvas; sensor/cab faces use glass; barrels and hot process machinery use heat-treated metal; service access and barrier faces use warnings. Prop and vegetation primitives have their own UV layouts while preserving stable mesh, material and LOD IDs.

`surface_orm.tga` is original numeric material data, not color imagery. It contains one AO/roughness/metalness triple per cell with the production R/G/B contract. Rubber, canvas, concrete, wood and vegetation have zero metalness; glass has a smoother response; exposed structural steel has higher metalness. The 128 x 128 map uses top-origin RGBA TGA storage. Its cell triples are:

```text
(240,184,85) (240,152,220) (255,240,0) (255,245,0)
(255,55,32) (255,200,30) (245,235,0) (220,195,200)
(220,230,100) (235,185,190) (245,230,0) (240,230,0)
(240,245,0) (245,245,0) (245,245,0) (240,180,110)
```

Offline compilation limits the color atlas to a maximum dimension of 512 (the authored odd dimension reduces to 313) and four mip levels. ORM uses three levels. The retained terminal dimensions and UV insets prevent filtering across neighboring cells. Runtime never reads PNG/TGA sources.
