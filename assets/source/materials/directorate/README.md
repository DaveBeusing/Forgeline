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

A complete RGBA8 32 x 32 mip chain contains 5,460 texel bytes. The nineteen shared textures therefore represent 103,740 bytes of uncompressed runtime texel data before container metadata.
