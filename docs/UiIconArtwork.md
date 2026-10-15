# Directorate semantic UI artwork

## Implementation and source ownership

Based on master `859d6f6bf04d5f32b759a50b93337e57c4169f8d` (0.1.373),
including the integrated [skin contract](UiSkinArtwork.md) and
[surface catalog](UiSurfaceCatalog.md). This adds source artwork and explicit
semantic bindings, not consumer migration, enabled gameplay or an additional cursor.

`assets/source/ui/directorate/artwork/artwork.catalog.json` is the exhaustive
source inventory: 143 vector roles at 16/24/32/48/64 pixels, plus 20 genuine roster
portraits at 48/64/96/128/256 pixels. There are 815 actual source PNGs, 143 editable
SVG masters and 799 ordinary texture definitions. The 16 unregistered PNGs are
five patrol variants and eleven 16-pixel cursor inspection variants.

Stable production texture IDs are `ui.artwork.directorate.<logical-role>.p<size>`.
Existing `ui.icon.*` material IDs and their consumers remain unchanged; bindings
record their exact legacy IDs. Never change an existing material into a texture
under the same persistent ID. The new sources use the skin contract's common
`id/file/width/height/insets/contentPadding/minimumSize/scaling/alphaMode` fields.
Pictograms/portraits are fixed-size sprites with zero slice insets. Extensions
record semantic availability, visible bounds, cursor hotspot and portrait identity.
There is no second atlas loader or player-side JSON/SVG parser. The [compiled atlas path](UiAtlasRendering.md) consumes these authoring contracts
and ordinary compiled textures.

## Semantic coverage and consumers

| Domain | Current scope | Consumer / rule |
| --- | --- | --- |
| Resources | All eight `InitialResourceDefinitions` entries: ferrous_ore, volatiles, silicates, rare_elements, steel, fuel, electronics, ammunition | Resource strip, stock/production/operations; existing seven-cell HUD is unchanged |
| Power | Separate power/status role | Generation/demand, never a fabricated stockpile |
| Existing icon registry | Every non-None `RtsUiIcon`, including explicit patrol unavailability | Existing HUD/dock/info roles retain their IDs |
| Gameplay bindings | Every `GameplayAction` | Dock modes, setting changes, activation/cancel, formation, camera/base, layer/map, placement shortcuts |
| Submitted actions | Every non-None `PlayerActionRequestKind` | Build, production, unit queues, policies, resupply, combat, artillery, recovery, research and surrender |
| Tactical targeting | Every non-None `TacticalTargetingMode` | Attack, attack-move, retreat and fire mission remain distinct |
| Frontend | Main/pause menu commands, all settings fields and all four sections | Utility glyphs supplement live labels and reasons |
| Status | Supply, feedback, building and unit presentation enums | Existing authoritative states only; pending differs from accepted/partial/rejected |
| Technology | Four actual technology IDs | Existing domain glyph plus live technology name; no speculative unlocks |
| Cursor | All eleven `RtsCursorKind` values | Source only; no OS cursor change or second pointer activation |
| Minimap | All ten `RtsMinimapSymbolKind` values | Shape-coded allegiance/knowledge; generic unidentified contact |
| Information layers | Every non-None `StrategicOverlayMode` | Existing logistics/supply/sensors/navigation/power/all layers |
| World markers | Selection corner, ring segment, target, move, attack-move | Components for live geometry; no static map or baked range circle |

Exact enum/member/role and legacy-ID mappings live in the source catalog and are
tested against current code, not copied from labels on the reference boards.
`None` has an explicit no-action/no-visual exception. Construction stages share
the construction glyph while live stage labels and progress retain their meaning.
Directional utility aliases and common semantic roles (fuel, repair, supply)
may share geometry; different domain actions add their own pause/cancel/recovery
badge. Utility settings glyphs never replace the field label or bound key.

Patrol is a legacy icon-only role with no implemented submission. Its PNG/SVG
sources are inventoried as intentionally unavailable and have no `.asset.json`.
Railway and aviation remain reference-only concepts with no production sprites,
definitions or enabled actions. Conditional availability such as Continue remains
live model state, never baked into a picture.

## Native roster portraits

The separate `tools/ForgeLine.UiArtwork` executable references the existing
Presentation and Windows platform owners and reuses the current PNG writer.
It is neither referenced nor shipped by the player; simulation/headless project
dependencies are unchanged. It consumes compiled production models/materials,
not source meshes or externally created illustrations.

```powershell
dotnet run --project tools/ForgeLine.UiArtwork/ForgeLine.UiArtwork.csproj --configuration Release -- --runtime assets/runtime --output artifacts/ui-roster-portraits-fitted
python build/Author-DirectorateUiArtwork.py --portraits artifacts/ui-roster-portraits-fitted --preview artifacts/ui-artwork-review-fitted
```

Capture refuses nonempty output roots and requires a hardware adapter. Every
size is rasterized independently, without enlarging a smaller screenshot.
The camera uses 45-degree yaw, -35-degree pitch, 45-degree vertical FOV and a
bounding-sphere fit with 12% allowance. Lighting and pose are fixed; no terrain,
HUD, name, cost, damage, selection or progress is composed into the portrait.
Each PNG is captured twice and unequal output fails. Fallback meshes or missing
texture/material bindings also fail. Normal projected-size LOD selection remains
active: mesh identity/hash fields name the family's primary mesh, not a claim
that every small variant forcibly uses LOD0. The sealed runtime signature binds
the complete production dependency set.

| Units | Buildings |
| --- | --- |
| Rifle Squad | Command Core |
| Combat Engineer | Power Plant |
| Scout Vehicle | Extractor |
| Main Battle Tank | Storage Depot |
| Mobile Artillery | Smelter |
| Cargo Truck | Refinery |
| Supply Truck | Electronics Fabricator |
| — | Logistics Hub |
| — | Ammunition Plant |
| — | Supply Depot |
| — | Barracks |
| — | Vehicle Factory |
| — | Radar |

The authoring catalog retains exact gameplay IDs/keys, model-family IDs/hashes,
camera, runtime/device/build identity, PNG hashes and repeat results. Promotion
copies the native PNG bytes unchanged. Opaque, quiet portrait backgrounds are
intentional; alpha-framed pictograms remain separate. The 48-pixel card and
96-pixel inspector-frame variants supplement 64/128/256 versions without stretching.

Portraits require an identified, permitted read model. An unknown contact always
uses `marker.detected_contact`, a generic circle/ticks with no unit class, portrait
or private health. This source policy does not replace future consumer privacy tests.

## Raster, state and cursor contract

Vector shapes follow `ForgeLineUiPalette`. Normal icons have a quiet neutral
foreground with dark contrast edges; semantic enemy/warning roles use established
palette colors. Friendly square, enemy triangle and unknown circle remain
different in grayscale. Rejected uses a circled X, invalid a prohibition slash,
pending an hourglass, unavailable a lock. Disabled action overlays/reasons remain
owned by the skin and live UI, distinct from a rejected submission.

Each glyph size is independently rasterized from vectors with supersampling for
antialiasing, then given a transparent outer pixel perimeter. No reference bitmap
is enlarged, no black-pixel deletion is used, and there are no embedded text or
label plates. PNGs compile as lossless linear DisplayImage RGBA8 with one mip.
The SVG masters remain editable authoring data, excluded from the player.
Both generators and scoped Git attributes enforce LF before calculating editable
SHA-256, so the catalog matches actual Git blob bytes on fresh checkouts.

Cursor art has an arrow plus a separate action badge, dark/light contrast outline,
measured `visibleBounds` and an actual pixel `hotspot`. The hotspot is the arrow
tip, scaled from (8,6) on the 64-pixel master. Use the exact size variant and its
matching hotspot; choose 32/48/64 pixels for ordinary 96/144/192-DPI operation,
with 24 pixels as the minimum intended cursor size. The 16-pixel variants are
explicitly inspection-only because their badges are too small; they are not
registered runtime assets. These sources enable no extra pointer in gameplay.

Selection corners and ring sections are components. Range radius, contact
position, fog, terrain conformance and order paths remain dynamic geometry.

## Provenance, evidence and remaining scope

No reference pixels, embedded labels, example values or sample insignia were
copied. Vector glyphs are independent geometric drawings and portraits show
existing repository production models. The reference catalog's rights evidence
remains `not_recorded`; this work asserts no additional license or human approval.

Native-size light/dark contact sheets, grayscale and channel-loss inspection aids,
portrait pages and their hashes are retained with [qualification](UiArtworkQualification.md).
Channel-loss aids are diagnostic proxies, not calibrated color-vision simulations
or human accessibility acceptance. Pixel/source validity, native repeatability,
gameplay identity, integrated usability and human art decisions remain separate.

Atlas packing/rendering is implemented. Role-based live binding, actual cursor installation,
live contact privacy and final physical-display acceptance belong to their
subsequent integration scopes. The representative first menu control is migrated; the other surfaces remain
pending migration. No source-validity result grants final AAA acceptance.

## Battlefield consumer integration

The [graphical battlefield HUD](GraphicalBattlefieldHud.md) now consumes compiled
resource/selection artwork and actual roster portraits through `GameplayHudArtwork`
and `GameplayHudSkin`. Exact mappings are validated against this source catalog
and the packed sprites. Existing `ui.icon.*` IDs remain valid for independent
bitmap paths and the production/tactical consumers that have not migrated.
No artwork, definitions or catalog content changes in this consumer integration.
