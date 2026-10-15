# Compiled UI atlases and skin rendering

## Scope and contract

Based on master `810011c8471a4cc5e1291c96411e47a7008fe9b2` (0.1.374),
including the original reference import, skin sources and semantic artwork.
Version 0.1.375-dev introduces a compiled skin path and one real menu control.
The remaining frontend and HUD migration stays in the surface catalog.

`ForgeLine.Assets.RuntimeUiSkinData` owns payload version 1, inside the existing
FLAS container/integrity contract. `RuntimeAssetType.UiSkin` is an additive type;
the existing mesh/texture/material payloads, manifest/header and aggregate runtime
version stay unchanged. Compiler 1.10.0 invalidates previous build-cache identities.
The skin contains stable sprite IDs, page texture IDs/groups, integer regions,
logical dimensions, extrusion padding, slice/content insets and cursor hotspots.
States are separate sprites selected by `UiSkinState`; they never bake labels,
values or gameplay authority into the texture. Cursor metadata does not install
a cursor or activate another pointer.

The source `assets/source/ui/directorate/atlas/directorate.skin.json` is consumed
only by the compiler. The player reads `ui.skin.directorate.compiled` through
`RuntimeAssetCatalog` and bounded binary parsing. Page dependencies participate
in source/build hashes, topological compilation, sealed containers and manifest
integrity. Qualification verifies every page, not only metadata. Duplicate IDs,
overlapping padded regions, invalid bounds, insets, hotspots, counts, versions,
texture semantics, missing dependencies and truncated/trailing payloads fail.
The binary payload limit is 1 MiB, 2,048 sprites and 16 pages; page dimensions
are at most 2,048 square with a combined 32 MiB residency ceiling.

## Authoring and color

`python build/Author-UiAtlas.py` verifies both existing source catalogs' hashes,
then packs all 836 runtime-required sprites in ordinal-ID shelf order, isolating
core, icons and portraits. It emits seven 1024-square PNG pages and ordinary
texture definitions. `lineage.json` retains source identities. The compiler
validates authored regions and page texture payloads; it does not import SVGs or
reference boards. Source pixels are copied unchanged, with two-pixel boundary
extrusion including corners. Authored atlas JSON uses canonical LF.

Pages are display-image RGBA8, linear texture views, exactly one mip. Existing
display numeric color bytes are preserved without sRGB decoding or world tone
mapping. Alpha is straight: the shader samples once, multiplies by tint, and
existing SrcAlpha/InvSrcAlpha blending applies coverage once. Neither authoring
nor upload premultiplies. Thin glyphs never pass through BC7. Mipless sampling,
extrusion and padded nonoverlap prevent cross-region filtering. The atlas adds
no new texture format or image decoder to the player.

Two old content-padding values exceeded their separator/fill image height.
Those decoration-only components now have zero content padding; their pixels,
slice insets and persistent IDs are unchanged.

## Ownership and preparation

`UiSkinResources` acquires a creating-thread lease keyed by graphics device,
sealed catalog signature and group. Required metadata/pages are validated and
uploaded during preparation, before frame recording. Core, icon and portrait
groups are independent. Multiple surfaces share the group's textures; last
release uses existing graphics disposal/fence retirement. Cancellation or partial
upload failure disposes every newly created texture and never publishes a partial
cache entry. Cross-device use, cross-thread access and disposed leases fail.
No resource survives its creating device's intended owner scope.

`FrontendOverlayRenderer.PrepareBackground` prepares the required core group
through the existing render-thread preparation path after the initial interactive
fallback menu. Skin preparation failures propagate as actionable asset failures;
they are not successful fallback qualification. The asset-independent initial,
diagnostic and emergency geometry remains available. The existing optional menu
image keeps its separate lifecycle. Dispose batches before releasing page leases,
and dispose both before the device. No synchronous GPU wait occurs per control.

## Drawing and painter order

`UiSkinBatch.Begin(width,height,frameIndex)` starts one output-resolution frame.
`Draw` emits a sprite or nine-slice frame; `fill` clips the original geometry from
the left without stretching progress content. `Overlay` selects separate state
art. Callers decide state precedence and disabled interaction using existing UI
contracts. `Tile` repeats the whole sprite at logical size and crops the last
row/column. Nine-slice centers/edges stretch, corners retain native physical-pixel
inset sizes; undersized destinations proportionally reduce opposite corners.
Zero-area geometry is discarded and UVs are never inverted.

`SetClip` intersects a supplied physical-pixel region with output bounds. CPU
clipping adjusts UVs; commands also apply scissor rectangles. Only contiguous
commands sharing page and clip merge. There is no global texture sort. `Flush`
submits pending commands and restores the full output scissor. Flush before
external text/geometry, render that layer with the existing `UiTextBatch`, then
continue adding sprites without another Begin. Each flush uses a distinct preallocated upload buffer, preserving the native
whole-resource write guard. Configure `maximumFlushes` during construction for
text/sprite interleaving (default one, maximum eight). Exceeding that bound fails
without allocating or overwriting a recorded resource. Callers owning another clip must explicitly restore it afterwards.

Each batch allocates a fixed 4,096-quad CPU array, a bounded command array and
four 786,432-byte upload buffers per configured flush during construction
(3 MiB default; 6 MiB for the two-flush fixture core). There is no per-frame file
read, decoder, texture/pipeline/buffer creation or unbounded geometry growth.
Excess quads increment diagnostics rather than grow buffers. Warm production
geometry has an allocation test; readback/fixture reports are not hot-path timing.
`UiTextBatch` and font metrics remain unchanged.

The first main-menu button is a representative integration: both skin drawing
and pointer handling consume `FrontendGeometry.MenuRow(0)`. Backplates precede
the skin and live text follows it. Other menu rows, pause/modals and gameplay
surfaces retain their current paths. The existing overlay pass renders at full
output resolution after world scaling; no HUD world-effect path is introduced.

## Budgets and diagnostics

| Group | Pages / texture descriptors | Decoded page bytes |
| --- | ---: | ---: |
| Core | 1 | 4,194,304 |
| Icons | 2 | 8,388,608 |
| Portraits | 4 | 16,777,216 |
| All groups | 7 | 29,360,128 |

The frontend prepares core only. Existing individual source texture IDs remain
compiled for compatibility; they are not automatically GPU-resident. Atlas pages
add runtime/package storage while later consumers may replace individual uploads.
No package-size saving is claimed. Font residency (8 MiB) and vertex buffers are
additional and separate from the 32 MiB page ceiling.

`UiSkinDiagnostics` exposes group residency, texture count, shared leases,
cumulative group texture uploads, frame draws/vertices/upload bytes, dropped
quads, missing required sprites and the configured frame/flush buffer count. The creating device's
existing `GraphicsResourceDiagnostics` reports actual descriptor use, texture
uploads/releases and binding failures. `RuntimeAssetQualificationReport` includes
skin metadata count/bytes separately from page texture bytes. Missing required
draws throw and increment missing count; no successful production fallback is
invented. Native timing, sustained GPU cost and human art acceptance are separate
from storage/count/allocation checks.

## Qualification

Targeted tests: `UiSkinPipelineTests` (binary integrity, malformed data, exact
packed source/extrusion pixels, deterministic compilation, missing/deleted pages)
and `UiSkinBatchTests` (ordered draws/text barrier, clipped UVs, small geometry,
partial fills, shared leases, cancellation/rollback, bounded allocation, native
straight-alpha readback and retirement). Existing text and input metrics/goldens
remain unchanged.

`ForgeLine.Client --ui-skin-capture <empty-output> <runtime-root>` uses the normal
device and production skin batch. It requires hardware and captures both bright
and dark output at 1280x720/1920x1080 over three fresh sessions, checks repeated
pixels and descriptor/texture return to baseline, then captures the real menu
control. `UiSkinFixtureRenderer` is a qualification fixture, not another UI engine.
See [qualification evidence](UiAtlasQualification.md) for exact outcomes and
source identities; structural success is not human visual approval.

## Frontend consumer update

The [graphical frontend](GraphicalFrontend.md) extends the original single-control
fixture to all normal frontend screens. The core group, binary contracts, device
ownership, text metrics and page budgets remain unchanged. The first-control-only
scope above records the original renderer integration; current frontend coverage
is listed in the surface catalog. Battlefield HUD migration remains separate.

## Battlefield consumer update

The [battlefield HUD](GraphicalBattlefieldHud.md) now shares the existing core,
icon and portrait page leases. `UiSkinBatch` accepts a bounded per-consumer quad
capacity; its default limit, ordering, overflow and alpha contracts are unchanged.
See [native and package qualification](GraphicalBattlefieldHudQualification.md).

## Economic HUD consumers

[Production and operations](GraphicalProductionAndOperations.md) share the same seven atlas pages. Current composed HUD preparation is twelve bounded batches / 48 buffers / 5,603,328 upload-buffer bytes. Thin progress tracks resample complete sprites below the authored height and still clip the fill fraction. Operations records its opaque underlay before skin and remaining text. [Qualification](GraphicalProductionAndOperationsQualification.md) preserves source and package identities.
