# Frontend layout contract

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

`ForgeLine.UI` owns focus, list windows and hit testing. `ForgeLine.Presentation` exposes the
passive `FrontendGeometry`/`FrontendRegion` value contract and renders it. This uses the
existing UI → Presentation reference without adding a reverse reference. The Client platform
owner resolves one physical-pixel layout and passes its exact value through
`FrontendSurfaceView.Layout` to the render owner. A renderer-only caller may resolve a default
96-DPI layout; normal window frontend and modal input always publish their actual layout.
`GameplayHudLayout` and its command/action dock are independent.

<details>
<summary>On this page</summary>

- [Regions and state](#regions-and-state)
- [Responsive rules](#responsive-rules)
- [Old-to-new mapping](#old-to-new-mapping)
- [Lifecycle](#lifecycle)
- [Menu sections and full-screen presentation](#menu-sections-and-full-screen-presentation)
- [Production skin integration](#production-skin-integration)

</details>

## Regions and state

| Region | Meaning | Input |
| --- | --- | --- |
| SafeArea, Header, Title | Bounded composition and heading slots | Visual only |
| Navigation / MenuRow(i) | Up to seven existing menu cards | Enabled menu command only |
| Details / DetailRow(i) | Visible list window, separated by real gaps | Selectable save/settings rows |
| DetailLabel / DetailValue | Measured label and value slots | Visual only |
| Decrease / Increase | Separate bounded edit buttons | Only corresponding visible affordance enabled |
| PrimaryAction / SecondaryAction | Existing Start, Load, Apply, Keep, Back | Present/enabled actions only |
| Footer | Existing Back/close hint and shortcut text | Preserved secondary-screen Back shortcut |
| Feedback | Warnings and font failure recovery | Visual only |
| Decorative | Clipped industrial schematic, sacrificed before controls | Visual only |

Coordinates are physical client pixels; pointer positions use the same space. Hit rectangles
are rounded once and half open: left/top included, right/bottom excluded. Gaps, ultrawide
margins, unused rows and decorative space cannot identify a command. Geometry describes
potential slots; model enablement and actual view affordances gate activation.

## Responsive rules

The viewport fit factor uses 1920×1080 as a density reference, not a virtual monitor.
Requested density multiplies viewport fit, user scale and DPI / 96. At supported sizes the
minimum is the smaller of 0.75 and viewport fit. Density is capped by height / 850,
width / 1100 and 3 to keep every menu command and both modal actions visible. Thus a
requested scale can reach a deliberate fit limit; it is never silently allowed to clip actions.

The safe horizontal extent is centered and bounded on ultrawide displays. Header spacing
compresses and menu stride changes from 72 to 64 density units when the logical height falls
below 1000. Ordinary detail rows use 58-unit stride; controls use 150 and full available width
for measured, wrapped descriptions. One to seven rows fit above the fixed action band.
Details/settings/saves retain keyboard focus inside the resulting list window. Controls and
credits use Up/Down or wheel to scroll. Settings retain the same 18 fields; no new action or
persistent setting is added. Long save labels and values use measured ellipsis. Footer
feedback wraps to two bounded lines. Wrapped text is cached with a 64-entry bound.

## Old-to-new mapping

| Superseded literal | Shared replacement |
| --- | --- |
| 82, 338, 560×52, 72 stride | Navigation / MenuRow |
| Input Y390 vs drawn Y377, 58-row bucket | DetailRow; gaps are excluded |
| Label X112 / value X460 / 320/1370 width | DetailLabel / DetailValue |
| X830 and X916, 72×42 edits | Decrease / Increase |
| X782/Y838, 290×58 | PrimaryAction |
| X92/Y838, 220×58 | SecondaryAction |
| X82/Y900..970 shortcut strip | Footer |
| Independently centered renderer canvas | Published physical-pixel geometry |

At 1920×1080, 96 DPI, scale 1, menu/action/edit rectangles retain their familiar positions.
The intentional change to detail hit bounds removes the old 13-pixel vertical mismatch and
10-pixel gap capture. At other densities/aspect ratios, consumers must use named regions.
Legacy `FrontendLayout` scale/content fields remain compatibility metadata; offsets are zero
and are no longer a coordinate transform. The old duplicate literal hit path is removed.

## Lifecycle

`FrontendInputLifetime` compares layout, focus availability and screen/modal owner. Changes
suppress held keys/buttons through the existing `InputState` mechanism and clear pointer
position/hover; release is required before another gesture. Navigation model focus survives.
A mouse must move or click to change menu/save focus, so a stationary cursor cannot undo
keyboard navigation. Minimize suspends the render surface; restore publishes fresh geometry.
No transition animation translates functional controls away from their hit rectangles.

Use `ForgeLine.Client.exe --frontend-layout-capture <empty-output-directory>` for repeated
native captures of all seven screens across the complete matrix, real windowed/borderless
DPI/size sampling and zero-size GPU-surface restore. Capture warms resized surfaces before
readback. It uses shipped resources only and does not compile/import assets.

## Menu sections and full-screen presentation

Settings has visible General, Graphics, Audio and Bindings tabs. Click a tab or
use Tab / Shift+Tab, then Up/Down to choose a row and Left/Right to adjust it.
Audio exposes the implemented interface-sound enable and volume controls;
these persist and apply on save, including Pause Settings. They do not claim
music or battlefield-audio channels that the engine does not implement.
Tab changes consume the gesture and retain unsaved values across sections.
Graphics retain next-match application and display trials remain reversible.

Controls is an Action / Input table with one action per row. Camera and gameplay
bindings use the current mapping. Up/Down or the mouse wheel scrolls the list.
Compact table rows replace the former three-line paragraphs.

The title background covers the entire native viewport, including ultrawide and
borderless outputs. Content uses edge margins instead of a centered 16:9 canvas.
The navigation backplate sits over the artwork; no image can cover input labels.
Missing optional artwork retains a full-viewport rendered fallback.

The menu header draws embedded vector artwork from
`assets/source/branding/forgeline/menu/wordmark.svg` through the existing overlay
batch. It uses no runtime font, bitmap enlargement, texture upload or optional
runtime asset. The application icon and studio splash remain separate.

Credits shows only Undefined Behavior Studios, lead developer Dave Beusing,
the product version from the built client, and ForgeLine Engine. It does not list
platforms, libraries, the tagline or optional title artwork.

## Production skin integration

All normal frontend screens now use the [compiled production skin](GraphicalFrontend.md).
The geometry and hit rectangles above are unchanged. Typed value visuals keep the
existing +/- and keyboard workflow; sliders introduce no additional hit targets.
TotalDetailRows/DetailRowOffset publish the existing settings/save list window for
scrollbar rendering. PauseContext extends the opaque menu backing to paused
settings/controls while preserving visible battlefield space beside it. Bootstrap,
studio and explicit failure paths remain independent and visibly diagnosable.
