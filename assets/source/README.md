# FORGELINE Source Assets

This directory contains editable authoring assets and their `*.asset.json` declarations.

Do not place compiler-generated runtime files here. See `docs/AssetPipeline.md` for the supported source formats, metadata schema, stable-ID rules, validation behavior, and compiler commands.

The Vertical Slice source sets are organized by presentation domain:

- `world/` — terrain materials, decals, props, vegetation, and resource presentation;
- `units/directorate/` — Directorate unit meshes, materials, LODs, collision, sockets, and strategic symbols;
- `buildings/directorate/` — Directorate building families and shared construction/state modules;
- `infrastructure/directorate/` — Directorate road/bridge presentation and strategic symbols;
- `vfx/` — combat, destruction, persistent damage, projectile, and logistics VFX meshes/materials;
- `ui/` — semantic RTS resource, role, command, cursor, supply, minimap, and status material assets.
- `branding/undefined_behavior/splash/` — studio startup atmosphere, separator, monogram, wordmark, and subtitle textures.

These baselines intentionally use compact replacement-ready geometry and stable IDs so presentation exercises the production compiler/runtime path before final high-detail content replacement.
