# Graphical tactical interface

The tactical interface uses the existing Directorate atlas for minimap panels, selector controls, layer glyphs, permitted map symbols, primary/hover footprint corners, targeting and transient order glyphs. World Health/Fuel/Ammunition frames use copied, current, owned single-entity facts. Windows installs one native pointer from the existing cursor art. No second pointer, new command or intelligence authority is introduced.

## Rendering and interaction

`RtsInformationOverlayRenderer` records Core panels, then live procedural geometry, then clipped Icons artwork, then live text. `GameplayHudSkin.FlushPanels`/`FlushArtwork` reuse separately prepared groups; an upload buffer is written once per group per frame. Terrain/fog cannot paint over contact sprites. Map glyphs are clipped to `RtsMinimapInteractionLayout.GetMapRect`; decorative panels change neither that rectangle nor its world transform. Selector glyphs occupy a separate vertical band from their three-letter labels in the same existing controls.

The existing `RtsMinimapScratch` and model builder supply symbols, selected/active groups, known positions and contact currency. Unidentified contacts retain the generic contact glyph; hidden objects are absent. Historical copied contacts use half alpha. Camera target position and the camera rays' footprint on the current target-height plane remain dynamic; the footprint is fitted through the same normalized world-to-map transform. It is an approximation to the ground plane, not a terrain-occlusion or visibility calculation.

`TacticalFootprint` derives local X/Z extents and validated rotation from each permitted render instance. Four small corner patches are projected through the current camera. Primary selection/current permitted hover receives this bounded emphasis; all selected group members keep terrain-sampled, oriented L-shaped corner marks rather than elliptical outlines. The markers respect each object's two local axes instead of a radius derived from its diagonal. No fixed building photograph, retained entity cache or assumed square footprint is used. Projected corner patches use the copied ground plane; terrain-sampled outlines remain authoritative for terrain conformance. Offscreen/behind-camera patches are omitted. Art and meters are clipped away from the reserved top/bottom HUD bands.

Health and supply facts require the current experience tick, a matching entity, local ownership and explicit availability flags. A group aggregate is never applied to one member. Hover details additionally require the current session/tick. Nonfinite numeric facts are omitted. Health occupies the upper track; Fuel and Ammunition retain separate lower tracks. Foreign hover may show permitted corners, with no private operational bars.

`RtsCommandFeedback.View` copies existing local movement/tactical feedback into the published information view. Move, Attack and AttackMove use their own supported art; locally invalid intent uses the invalid symbol. Visibility still expires after the existing 0.8 seconds, driven by its existing owner. Local intent is not relabeled as an authoritative accepted/pending/rejected result. Those acknowledgements, control states and lifetimes remain on the existing command/action feedback paths. Art is static and adds no decorative motion; reduced-motion and simulation timing are unchanged.

## One native cursor

`TacticalCursorSet` validates all eleven supported roles and four sizes against compiled atlas metadata, copies atlas RGBA regions during session preparation, and passes `WindowCursorImage` values across the platform boundary. The player never parses authoring JSON or SVG. Choose 24/32/48/64 pixels from effective DPI and user scale, capped at the largest authored variant; 16-pixel inspection artwork stays unavailable.

`WindowCursorImage` validates dimensions/hotspots, converts straight RGBA into bottom-up premultiplied BGRA and an AND mask. Windows owns the resulting cursor handles. Client-area `WM_SETCURSOR` installs the selected handle; window-class/nonclient ownership remains Windows. Focus loss restores the system arrow. Menu/pause/HUD ownership clears the tactical cursor. DPI and window transitions choose the prepared variant on the next platform update. No `ShowCursor`, simulated pointer movement, coordinate offset or software cursor is introduced. A bounded 44-handle cache avoids recreating active variants; session exit clears it, including after the native window has closed.

## Deliberate dynamic components

Terrain, fog/explored hatching, camera footprint, green drag selection, terrain-sampled group corner markers, development range/coverage circles, route/network links, placement validity/footprints and local order cross/ring geometry remain data-dependent geometry. Atlas symbols supplement their semantic caps/targets without deriving range, paths or validity from painted pixels. Existing developer DebugDraw/bootstrap paths remain asset-independent. Rail, aircraft and unavailable patrol roles stay inactive.

## Resource contract

The information owner prepares one 1,024-quad Core batch and one 4,096-quad Icons batch, each with four frame buffers. Whole-HUD preparation is 13 batches / 52 buffers / 8,749,056 reserved upload bytes, compared with the preceding 12 / 48 / 5,603,328. The increase is bounded recording capacity, not texture residency. Seven shared atlas pages remain 28 MiB; the font remains a separate shared page. Atlas uploads are shared and stable across repeated updates. Saturation is exposed by existing dropped-quad diagnostics, never by unbounded growth or per-unit textures. The native thousand-unit contract checks repeated updates without extra texture uploads or growth in prepared skin buffers, and with no dropped quads.

The cursor set retains 352,000 bytes of authored RGBA pixel arrays plus object overhead; Windows handles are created on first use and released per session. This is not a driver-memory measurement. See [qualification](GraphicalTacticalInterfaceQualification.md) for source-bound tests, captures, draw/upload measurements, package identity and remaining human/CI acceptance.

## Animated tactical minimap

The client publishes a per-frame physical-pixel minimap rectangle resolved by `MinimapHoverMotion`. The map, selector, legend, fog and permitted contact symbols, native clip bounds, camera footprint and pointer/world transforms use the same current rectangle, not an independently interpolated render-only offset. A stationary lower-right edge anchors expansion and neighboring HUD surfaces and live action rings limit its extent. Geometry is recomputed for every animated frame; glyphs and text are rendered at native UI scale instead of stretching a cached image. The animation never changes the snapshot, identification rules, targeting requests or command semantics. Hover-only decorative gaps do not capture world commands.

## Industrial docking-zone world overlays

Current owned docking slots and confirmed loading visits may present a rotated yellow service hatch; assigned FIFO queue slots and valid holding targets use reverse-direction cyan hatching. This is bounded, depth-tested, terrain-sampled presentation geometry taken from copied tick data, not proximity-generated ellipses or a new navigation footprint. The default battlefield stays uncluttered; selected/hovered owned facilities or transport units and the Logistics action context reveal the applicable zones. Hidden/foreign/stale/destroyed participants do not supply a visual, and the render overlay never mutates gameplay. Source-specific native D3D12 and human readability/contrast acceptance remain open.

## Current player selection geometry

Player-owned units/buildings and all selected group members use the four `TacticalFootprint` corners as compact L-shaped selection markers; no normal selection/hover ellipse is submitted. Hover uses diagonal corner notches, with a separate tint and outward ticks for permitted foreign hover. The primary selection and current hover can still receive the authored corner atlas glyphs; the authoritative copied owner/visibility/session checks for these glyphs do not change. Procedural markers terrain-sample each endpoint; construction/collision and F2 debug-circle systems remain separate. A single green token controls selected corners and the drag rectangle. Its marquee fill is 0.12 alpha and its one 1.25-physical-pixel border is 0.92 alpha. No changes to world selection criteria, engine simulation or draw-command authority are introduced.
