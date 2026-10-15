# Directorate semantic pictograms and roster portraits

`artwork.catalog.json` inventories actual files, stable production texture IDs,
legacy role bindings, sizes, bounds/hotspots, availability, source SHA-256 and
native portrait identity. It extends the common source-sprite fields from the
parent skin catalog; it is not a runtime atlas format or player dependency.

Vector glyphs are independent drawings using the existing semantic palette.
Native portraits are unchanged production-renderer exports. Source SVGs contain
no text, reference image fragments, names, prices or gameplay values. PNGs pass
through the existing Asset Compiler only. No new gameplay or cursor is enabled.

Reproduce with `build/Author-DirectorateUiArtwork.py` and the separate
`tools/ForgeLine.UiArtwork` capture tool. See `docs/UiIconArtwork.md` for commands,
scope and provenance; see `docs/UiArtworkQualification.md` for exact results.
Reference rights remain as recorded; no new rights or final art approval are asserted.
