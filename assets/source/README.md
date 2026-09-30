# FORGELINE Source Assets

This directory contains editable authoring assets and their `*.asset.json` declarations.

Do not place compiler-generated runtime files here. See `docs/AssetPipeline.md` for the supported source formats, metadata schema, stable-ID rules, validation behavior, and compiler commands.

The first Vertical Slice world asset set is authored under `world/`. It intentionally uses compact production-pipeline primitives and stable replacement IDs so terrain, props, vegetation, decals, and resource presentation can exercise the real compiler/runtime path before final art replacement.
