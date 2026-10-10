# Directorate Shared Material Sources

This directory contains deterministic repository-authored texture sources used by the existing Directorate and production world asset set.

## Provenance

All TGA files in `textures/` are procedurally generated for FORGELINE from deterministic mathematical patterns. They contain no external imagery, third-party texture data, fonts, logos, or licensed source material. Redistribution and modification therefore follow the repository license.

## Shared production families

- painted metal
- structural metal
- damaged metal
- reinforced concrete
- resource rock
- vegetation
- functional status emissive

Each physical family provides Base Color, tangent-space Normal, and packed ORM with R = ambient occlusion, G = roughness, and B = metallic. The status map is Emissive.

The sources are deliberately compact 32 x 32 tileable RGBA textures. They are a reusable baseline for RTS-scale material separation rather than unique baked detail. The Asset Compiler generates complete mip chains, while material factors, UV scale, geometry silhouette, state overlays, and faction/readability treatment provide higher-level variation.

A complete BC7 32 x 32 mip chain contains 1,392 bytes. The nineteen shared textures therefore occupy 26,448 runtime bytes before container metadata, compared with 103,740 bytes in RGBA8. Editable TGA sources remain lossless. These small sources are a material foundation; they do not satisfy the higher-resolution production-master acceptance requirement.

## Concept surface completion

The physical production mesh set uses the component-specific [production surface atlas](surfaces/README.md), with native 4096-pixel Base Color, Normal and ORM masters. All atlas users bind its matching tangent-space Normal map. The original nineteen textures remain stable reusable assets and retain their independently validated BC7 budget. See [Production surface masters](../../../../docs/ProductionSurfaceMasters.md) for the explicit runtime budgets and acceptance limits.
