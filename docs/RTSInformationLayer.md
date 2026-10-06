# RTS Information Layer

## Purpose

FORGELINE's initial RTS information layer communicates selection, commands, resource and role identity, supply/readiness, battlefield intelligence, minimap state, and strategic overlays without owning gameplay state.

All information-layer output is presentation-owned. Authoritative selection targets, supply state, health, fuel, ammunition, building state, logistics, navigation, sensors, fog of war, and objectives continue to come from existing game/simulation read models and immutable presentation snapshots.

## Stable UI asset convention

Editable UI semantic assets live below:

```text
assets/source/ui/
```

The initial baseline uses stable `ui.icon.*` identifiers and compiles them through the existing asset pipeline as material assets. This avoids parallel icon copies and keeps color/tint data runtime-resolvable while the current renderer draws compact vector-like glyphs procedurally.

The catalog is centralized in `RtsUiIconCatalog`. Features reference semantic icon kinds rather than file paths.

Current families include:

- raw and processed resource icons;
- unit-role icons;
- building-role icons;
- command icons;
- cursor icons;
- supply-state icons;
- minimap/strategic symbols;
- health/fuel/ammunition/power/alert status icons.

The current asset runtime has no dedicated UI-atlas or vector-icon asset type, so no second atlas format is introduced. A future texture/vector pipeline can replace the visual payload while retaining the stable semantic IDs.

## Resource icons

The resource set covers resources already exposed by the Vertical Slice:

- Ferrous Ore;
- Volatiles;
- Silicates;
- Rare Elements;
- Steel;
- Fuel;
- Electronics;
- Ammunition.

## Unit and building roles

Unit roles currently map existing Directorate content into:

- Infantry;
- Reconnaissance;
- Armor;
- Artillery;
- Logistics;
- Repair/Engineer.

Air-defense presentation is not authored because the current Vertical Slice exposes no dedicated air-defense unit role.

Building roles map existing structures into:

- Command;
- Extraction;
- Processing;
- Factory;
- Storage;
- Supply;
- Power.

The mapping is presentation-only and does not create a second gameplay taxonomy.

## Commands and cursors

The semantic command inventory includes:

- Move;
- Attack;
- Attack Move;
- Stop;
- Hold;
- Patrol;
- Build;
- Repair;
- Supply;
- Cancel.

Icons may exist before a command becomes interactively available. The client exposes only actions supported by current gameplay/read models.

The cursor set includes:

- Default;
- Select;
- Move;
- Attack;
- Attack Move;
- Build;
- Repair;
- Supply;
- Invalid;
- Pan;
- Drag Select.

`RtsCursorResolver` derives the cursor from current presentation interaction state. Building-placement validity and tactical-target validity take precedence over generic movement. Unsupported gameplay actions are never created by cursor selection.

## Selection and world markers

World-space information markers supplement the existing selection/input model:

- unit selection rings;
- building footprint/selection outlines;
- hover highlights;
- tactical target markers;
- invalid-order markers.

Marker shape communicates meaning in addition to tint. Gameplay-critical state therefore does not rely on color alone.

World markers are presentation geometry only and do not alter collision, navigation, command validation, or simulation footprints.

Picking, hover, selection markers, and renderer culling share the same presentation-bounds calculation. Unit bounds include conservative role-specific expansion for silhouette features such as weapons and sensors, and rotated transforms are converted to world-space axis-aligned bounds consistently. Unit rings and building footprint outlines are anchored just above the visual ground plane; building outlines also preserve authored orientation. This keeps interaction geometry aligned with visible objects without changing gameplay authority.

## Health and supply information

Supply-state presentation maps the authoritative `BattlefieldSupplyStatus` values:

- Supplied;
- Low Supply;
- Critical;
- Unsupplied.

Each state has a distinct semantic glyph/label as well as tint.

Health, Fuel, and Ammunition indicators are shown only where the current selection/player read models expose those values. Power status likewise consumes existing building/player read models rather than querying or mutating power simulation from UI code.

## Minimap model

`RtsMinimapModelBuilder` creates a presentation-only minimap model from:

- the current immutable `PresentationSnapshot`;
- faction-bounded `FactionIntelligenceSnapshot`;
- current local selection;
- the canonical world bounds.

Supported strategic symbol classes include:

- friendly unit;
- visible enemy;
- detected contact;
- building;
- command structure;
- resource;
- depot;
- objective;
- selected group;
- attack notification.

Detected contacts use opaque intelligence data. An unidentified contact is not promoted to a live simulation entity ID or hidden transform.

Minimap coordinates are derived from canonical world bounds. The model never owns movement, objective, resource, or intelligence state.

## Fog of war

Fog presentation follows the intelligence states currently exposed by simulation:

- `Unexplored` -> high-opacity solid cover;
- `Explored` -> lower-opacity hatch treatment;
- `Visible` -> clear.

Detected/identified contact knowledge remains symbol/intelligence metadata rather than inventing additional terrain-visibility states.

Pattern plus opacity is used so visibility state is not communicated by color alone.

## Strategic overlays

`RtsInformationLayerController` owns only presentation enablement. The current strategic overlay cycle is:

```text
None -> Logistics -> Supply -> Sensors -> Navigation -> All -> None
```

The overlay renderer delegates to existing read-model/debug visualization paths:

- logistics routes/nodes, cargo transport, automated distribution, and capacity;
- battlefield supply providers/units;
- sensor/intelligence coverage;
- navigation cells/routes.

These overlays never become a second authority for logistics, supply, intelligence, or navigation.

Additional power/buildable-area overlays should be added only when the existing product-facing read model provides an appropriate stable presentation contract. Debug data must not be converted into new gameplay authority merely to satisfy an overlay.

## DPI and readability

UI geometry uses a 96-DPI reference scale through `RtsUiLayout.ScaleForDpi`, bounded to avoid pathological sizes.

The baseline favors:

- compact geometric silhouettes;
- short semantic labels;
- shape changes in addition to tint;
- world-space outlines for selection/targets;
- fog patterns plus opacity;
- strategic symbols distinct from detailed unit/building art.

This keeps small-size meaning readable across terrain/background variation without making color the only carrier of state.

## Runtime integration

The Windows client composes the information layer after immutable presentation extraction. It consumes current interaction state, selection, player experience, intelligence, and debug/read-model snapshots.

The layer does not add simulation systems and does not require the headless runtime to reference UI or graphics.

## Validation

Automated coverage includes:

- compile/load validation for all stable UI semantic assets;
- uniqueness and semantic mapping of icon IDs;
- cursor lookup precedence;
- DPI scaling bounds;
- supply-state icon mapping;
- fog pattern/opacity mapping;
- minimap symbol mapping;
- opaque detected-contact handling;
- selection/target/invalid marker generation;
- strategic-overlay enable/disable cycling;
- client/render integration through the standard Windows smoke path.

The complete source-asset count includes the UI semantic material assets and is validated by the existing asset-pipeline test suite.

## Current boundaries

This is the initial information layer, not the final HUD art pass. The following remain outside this baseline:

- final typography/branding;
- final texture/vector icon rendering;
- full technology-tree UI;
- advanced combat-group UI;
- minimap-issued commands;
- new gameplay systems created solely for UI;
- unsupported Patrol/Repair gameplay behavior.

Future UI visual replacement should preserve stable semantic icon IDs and the simulation/presentation boundary.
