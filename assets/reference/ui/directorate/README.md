# Directorate UI Reference Artwork

## Import status

All five original PNGs are present in this branch. The remote directory and every
Git blob were read back at `b4a462c7514ff8a86eb8e123e16f28c139d058bb` and compared with the original
SHA-256, byte count, PNG dimensions and Git blob identity. `catalog.json` records
this verification. The master board is RGB; the four other sheets are RGBA with
actual alpha. No images were resized, recreated or recompressed.

This completes reference import only. It does not implement the graphical UI,
create runtime assets, establish redistribution rights or approve final art quality.

## Original reference sheets

| Repository filename | Reference content |
| --- | --- |
| [`ui_visual_language.png`](ui_visual_language.png) | Master Directorate visual-language board, panel hierarchy, material direction and sample iconography. |
| [`hud_panel_reference.png`](hud_panel_reference.png) | Resource strip, selection/building panels, command slots, production queue, build menu, alerts, supply, minimap and auxiliary panels. |
| [`resource_status_reference.png`](resource_status_reference.png) | Resource pictograms and operational-state icon families. |
| [`command_cursor_reference.png`](command_cursor_reference.png) | Command pictograms, cursor concepts, selection graphics and order markers. |
| [`strategic_marker_reference.png`](strategic_marker_reference.png) | Minimap contacts, infrastructure markers and strategic-overlay legend references. |

The filenames above identify the original files in this directory. Preserve their bytes and recorded hashes; author production derivatives separately.

## Meaning of the references

These are art-direction reference sheets, not executable sprite atlases or final production assets. They contain baked labels, illustrative statistics, combined modules, example map imagery and concepts for systems outside the current playable roster. Some displayed motifs may require simplified redraws at actual HUD sizes.

Use grounded industrial command styling: dark gunmetal and desaturated green-grey surfaces, restrained warm-metal accents, reinforced rectilinear corners, readable silhouettes and quiet detail behind text. Preserve the established `ForgeLineUiPalette` semantic roles rather than copying arbitrary screenshot colors everywhere.

The game, content catalogs and approved branding remain authoritative. In particular:

- Retain `Build. Supply. Conquer.`; alternate slogans in a sheet do not rename the product or replace its tagline.
- Do not import illustrative resource types, population limits, capacities, prices, technology tiers or vehicle names as gameplay requirements.
- Retain all resources actually exposed by the current runtime. Power is a generation/demand system, not a fictitious stockpile or income counter.
- Portraits must match the real unit/building identity. A tank image is not an infantry portrait.
- A rail, aviation or technology symbol does not authorize a new gameplay system or an enabled command.
- Existing studio/product marks remain canonical. Decorative sample insignia do not silently replace approved branding.

## Binary import and verification

Import originals unchanged under the filenames above. For every file, record actual byte count, decoded width/height, color mode, alpha-channel findings, SHA-256 and Git blob identity. Inspect both transparent and opaque backgrounds; do not assume a displayed checkerboard or black background is transparency. Verify the remote file bytes against the original before changing `importStatus` to `complete`.

If an original is inaccessible, record the specific missing item and leave the import pending. Do not create an empty PNG, a text file named PNG, an external-image hyperlink presented as a local asset, a fabricated checksum or a recreated substitute. Preserve originals; all production cleanup happens in separate derivative files.

## Production boundary

This directory is reference-only and remains outside `assets/source/` and `assets/runtime/`. Do not add `*.asset.json` definitions for these sheets. Do not ship them in the player package or load them during startup.

Production derivatives belong under `assets/source/ui/directorate/` and must pass through the existing Asset Compiler and runtime integrity contract. Separate borders, fills, icons, portraits, progress fills, focus/hover/pressed/disabled overlays and cursor hotspots. Keep labels, values, queue times, minimap terrain, graph geometry and permitted intelligence live and data-driven.

Record derivative source relationships and truthful rights/licensing evidence before redistribution. No blanket license, third-party permission or final visual acceptance is established by this reference inventory.

## Implementation

See `docs/implementation/ui/README.md` for independently scoped implementation requests and dependency order. Existing engine and UI behavior remain unchanged until those requests are implemented and qualified.
