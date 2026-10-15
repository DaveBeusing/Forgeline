# Directorate skin production sources

These 37 native-size text-free PNGs and their editable SVG masters are original
geometric redraws following the imported reference direction. No reference pixels,
labels, marks, illustrative quantities or gameplay data are incorporated.

`skin.catalog.json` records every actual filename, stable ID, source SHA-256,
editable-source SHA-256, dimension, minimum size, slice/padding contract and state
composition. It is authoring metadata, not a player dependency. PNGs compile only
through their adjacent ordinary texture `.asset.json` definitions. The existing
Asset Compiler emits lossless linear RGBA8, single-level runtime textures.

Regenerate using `python build/Author-DirectorateUiSkin.py` from the repository
root with Pillow installed. `--preview` writes inspection contact/state sheets
outside this directory. The renderer's `ForgeLineUiPalette` supplies colors;
do not edit generated PNGs as a substitute for updating the editable source.

Reference provenance remains in `assets/reference/ui/directorate/catalog.json`,
which records rights evidence as `not_recorded`. No new license, reference
redistribution permission or final human approval is asserted here. The original
boards remain unchanged and are not compiled or packaged with the player.

See `docs/UiSkinArtwork.md`, `docs/UiSurfaceCatalog.md` and
`docs/UiSkinQualification.md` for the complete integration boundary and results.
