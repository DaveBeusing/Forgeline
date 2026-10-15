# Directorate skin artwork

## Scope and identity

The foundation is based on master `27d80f8b04d77404b5287e66d533dcfea3788250`
(0.1.372). It adds independently redrawn, text-free skin sources and compiler
definitions, not live frontend/HUD integration. The five original boards remain
unchanged in `assets/reference/ui/directorate/`. Their recorded source hashes
remain authoritative; the original import was completed in the preceding change.

Production sources are in `assets/source/ui/directorate/`. Each sprite has an
editable SVG, a native-size RGBA PNG and an ordinary texture `.asset.json`.
`skin.catalog.json` is an **authoring contract**, not a new runtime format or
loader. The existing compiler alone creates the runtime textures. The [compiled atlas renderer](UiAtlasRendering.md) now consumes these sources
through validated compiled mapping and never reads this source JSON in the player.

## Visual contract

`ForgeLineUiPalette` remains the color authority. The authoring script reads its
normalized linear RGBA tokens, including alpha; the PNGs declare linear display
image usage. Do not interpret these numeric bytes as an sRGB color palette.
Production sprites use dark gunmetal interiors, rectilinear chamfered corners,
two restrained metal edge lines and occasional dim warm-metal corner accents.
The center remains uniform behind text. No reference pixels, marks, labels,
prices, illustrative units or population limits were copied. Canonical marks and
`Build. Supply. Conquer.` remain owned by their existing renderers.

Reproduce with Python and Pillow (authoring only):

```powershell
python build/Author-DirectorateUiSkin.py --preview artifacts/directorate-ui-skin/contact-sheet.png
```

The PNG/SVG pairs are emitted from the same integer-coordinate primitives.
Editable SVGs use canonical LF with scoped Git attributes; editable SHA-256
values match the actual Git blob bytes independently of Windows text conversion.
Rasterization is directly at declared dimensions, with no image enlargement or
claim of new reference detail. The generator updates source hashes in the catalog.
Do not use it to overwrite later approved edits without reviewing the resulting diff.

## Families and scaling

There are 37 individually declared textures: 10 panels, 16 controls, two progress
components and nine overlays. IDs use `ui.skin.directorate.<family>.<name>`.
The catalog is the exhaustive filename, dimension, hash and inset table.

| Family | Names |
| --- | --- |
| panel | primary, compact, raised, inset, dialog, tooltip, portrait, minimap, resource, banner |
| control | button, button_compact, tab, row, row_compact, input, dropdown, checkbox, checkmark, toggle_track, toggle_thumb, slider_track, slider_thumb, scrollbar_track, scrollbar_thumb, separator |
| progress | progress_track, progress_fill |
| overlay | hover, pressed, focus, selected, disabled, warning, error, pending, accepted |

Insets and content padding are **left, top, right, bottom**, in source pixels.
Minimum sizes are width/height at scale 1. Nine-slice corners retain physical
pixel size after DPI/user-scale conversion; stretch edge strips along their
length and stretch the quiet center. There is no tiled scratch pattern. Reject
negative/overlapping slices; choose compact artwork or relayout below minimum
size. Glyph-sized components are fixed-size. Portrait/minimap centers are truly
transparent and must not cover the image or live map beneath them. Progress fill
is neutral white coverage, clipped to the live fraction and multiplied by the
semantic color. It is not an authored progress value.

The outermost pixel perimeter is transparent. Alpha is straight, never obtained
by deleting black pixels. The future atlas must extrude appropriate edge pixels
outside source regions and prevent neighboring sampling; source margin is not
an alternative to atlas bleeding qualification. Keep normal HUD at native output
resolution regardless of world render scale.

State overlays target the interactive wrapper (minimum 24 x 24), not a narrow
decorative track or 20-pixel thumb. Keep those pieces inside the shared hit
region; do not shrink focus brackets below their minimum or enlarge hit geometry
independently of the owning layout. Minimum sizes describe frame integrity,
not sufficient space for arbitrary live labels; text metrics/padding may require
a larger region.

## State composition

Normal is the base sprite without an overlay. Add hover **or** pressed, selected,
semantic status, keyboard focus and disabled treatment in that order; draw live
text last. Disabled suppresses hover, pressed, pending and accepted. Selection,
focus and a retained error may coexist. Warning/error/pending/accepted are
mutually exclusive per status region; rejection reuses the error layer. Never
reinterpret an accepted command as completed work.

Focus uses bright corner brackets; selection an amber underline; warning a single
rail; error/rejection a double rail; pending a broken rail. Keep the corresponding
live status/reason text. Disabled adds a dim layer and short edge hatching, and
retains a readable disabled reason. These patterns preserve meaning beyond color.

Checkbox checkmarks and toggle thumbs are independent fixed sprites. Slider and
scrollbar position are live geometry. Dropdown chevrons belong to semantic icon
art; this scope provides the field shell. No new input widget or gameplay action
is introduced. Input/dropdown/checkbox/toggle/slider/scrollbar artwork anticipates
existing settings' later graphical controls; current settings remain row/stepper
based as recorded in the surface catalog.

## Provenance and review

The boards establish visual direction only. Their catalog records rights evidence
as `not_recorded`. This change asserts no additional license, third-party approval
or redistribution permission. Derivatives are independent geometric redraws;
reference pixels and decorative insignia are not incorporated. Legal provenance
and human art approval remain distinct from source validation.

Contact sheets use the actual sprite sizes over both bright and dark backgrounds.
Composite previews exercise minimum-size nine-slice, combined states and live
sample labels in an inspection image only; those labels never enter source PNGs.
Source validity, compiler round-trip, packaging exclusion and human acceptance
are reported separately in [qualification](UiSkinQualification.md).

## Consumer handoff

[Surface catalog](UiSurfaceCatalog.md) assigns existing owners and migration lanes.
Semantic icon/portrait production and the compiled atlas renderer are implemented;
only the representative first menu control is migrated. Remaining surfaces stay
in their documented migration lanes. Bootstrap, diagnostics and failure paths must stay independent
of skin availability. No simulation/headless dependency changes are necessary.
