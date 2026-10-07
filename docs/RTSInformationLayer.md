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

The semantic resource asset set covers:

- Ferrous Ore;
- Volatiles;
- Silicates;
- Rare Elements;
- Steel;
- Fuel;
- Electronics;
- Ammunition.

The active player resource summary currently exposes Ferrous Ore, Volatiles, Silicates, Steel, Fuel, Electronics, and Ammunition from the authoritative Command Core inventory. Rare Elements remain a valid catalog/resource/icon identity, but the current skirmish starting inventory does not contain an authoritative Rare Elements quantity. The production HUD therefore does not fabricate or display a Rare Elements counter until the active inventory/read model exposes one.

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

## Player action dock

The production command surface uses the existing semantic icon catalog but exposes only actions backed by current gameplay/read-model contracts. The six mode buttons are Build, Process, Units, Logistics, Supply, and Combat. Semantic Patrol or Repair icons remain available to future UI work, but the dock does not manufacture unsupported gameplay behavior from those assets.

Cards and footer controls render enabled/disabled state from the same model used by pointer/keyboard dispatch. When the immutable action snapshot already knows that a build lacks materials, a recipe or unit lacks inputs, an Attack lacks an identified target, artillery has no ammunition, or there is no active fire mission to cancel, the control is visibly disabled with an explicit reason instead of silently failing.

Build, processing, unit-production, logistics, supply, and combat views use only existing copied read-model values: costs and captured availability; queue/progress/block state; rally presence; stock thresholds/priority/distribution/bottlenecks; cargo lifecycle/wait/failure; supply thresholds/provider/priority; tactical eligibility/order/target counts; and artillery ammunition/range/mission state. Resolved acceptance/rejection remains command feedback, not renderer-owned state.

Pointer hit regions come from the shared DPI-aware ActionDock layout. Cards select context, footer buttons perform the corresponding action/edit operation, and the entire open dock captures pointer input before the world interaction layer.

## Selection and world markers

World-space information markers supplement the existing selection/input model:

- unit selection rings;
- building footprint/selection outlines;
- hover highlights;
- tactical target markers;
- invalid-order markers.

Marker shape communicates meaning in addition to tint. Gameplay-critical state therefore does not rely on color alone.

World markers are presentation geometry only and do not alter collision, navigation, command validation, or simulation footprints.

Player-facing world markers use a dedicated gameplay-overlay path rather than the engineering debug buffer. Selection, hover, placement preview, tactical target feedback, and player strategic overlays therefore remain visible when developer diagnostics are disabled. The gameplay world-overlay renderer does not depth-test its line geometry so critical command feedback remains readable over terrain and unit geometry; engineering debug lines keep depth testing enabled.

Picking, hover, selection markers, and renderer culling share the same presentation-bounds calculation. Unit bounds include conservative role-specific expansion for silhouette features such as weapons and sensors, and rotated transforms are converted to world-space axis-aligned bounds consistently. Unit rings and building footprint outlines are anchored just above the visual ground plane; building outlines also preserve authored orientation. This keeps interaction geometry aligned with visible objects without changing gameplay authority.

## Selection inspector

The production selection inspector is a dedicated gameplay HUD surface, separate from world-space selection markers and from engineering diagnostics. Its header uses the stable semantic role icon for a common unit/building identity when available, plus the copied display name and authorized selection count.

For exactly one selected owned entity, the inspector may render Health, supply/Fuel/Ammunition, readiness, power, and current work/progress/block reason from the copied player read model. For multiple entities it renders only count and semantically safe common/generic identity; it does not infer group Health, readiness, supply, or work from the first selected entity.

Selection disappearance requires no presentation history cleanup. If filtering removes the entity because it is destroyed, stale, hidden from the authorized selection path, foreign-owned, or the session/selection is cleared, the following snapshot contains an empty or changed selection summary and the inspector redraws from that state.

## Health and supply information

Supply-state presentation maps the authoritative `BattlefieldSupplyStatus` values:

- Supplied;
- Low Supply;
- Critical;
- Unsupplied.

Each state has a distinct semantic glyph/label as well as tint.

Health, Fuel, and Ammunition indicators are shown only where the current selection/player read models expose those values. Power status likewise consumes existing building/player read models rather than querying or mutating power simulation from UI code.

## Production status and notifications

`ResourcePowerHudSurface` owns the production top status bar and alert stack. It consumes only the completed-tick `PlayerExperienceSnapshot` published with the presentation snapshot.

The top bar pairs each currently authoritative player resource quantity with its stable semantic icon. Quantities use bounded compact formatting and the bar reduces the visible resource-cell count when the safe area is too narrow, exposing an overflow count rather than drawing outside the HUD region. Power presentation shows generation and demand plus an explicit `STABLE` or `CONSTRAINED` label from the authoritative player power summary.

The notification stack renders the existing causal player alerts for constrained power, blocked production, critical supply, and Command Core damage/destruction. Production-blocked and supply-critical alerts retain their authoritative counts. Severity is communicated through text labels and marker shape as well as color.

The most recent resolved player command is shown through the same production stack as `OK`, `PART`, or `FAIL` feedback with accepted/rejected counts and available construction, placement, logistics, supply, tactical, or artillery failure causes. Visibility is derived from completed simulation ticks and expires after 80 ticks; no wall-clock timer or presentation-owned command history is introduced.

A durable generic attack-history feed remains deferred. Presentation does not synthesize historical attacks by polling current health or transient render state.

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

These overlays never become a second authority for logistics, supply, intelligence, or navigation. They remain player-facing presentation and are independent of the developer-diagnostic master switch. Presentation extraction requests only the diagnostic read-model families needed by the active strategic overlay rather than enabling every development capture path.

Additional power/buildable-area overlays should be added only when the existing product-facing read model provides an appropriate stable presentation contract. Debug data must not be converted into new gameplay authority merely to satisfy an overlay.

## Development diagnostic overlays

Engineering visualization is owned separately from player-facing gameplay presentation.

The developer overlay controller exposes these categories:

- Navigation: ground movement, formations, navigation cells/routes;
- World: construction sites, resource diagnostics, battlefield/crossing diagnostics;
- Logistics: logistics graph, cargo transport, automated distribution, capacity, and battlefield supply;
- Sensors: intelligence cells, contacts, sensors, and metrics;
- Combat: artillery, tactical readiness, resupply decisions, weapon/projectile/impact diagnostics;
- Entities: spatial-grid diagnostics, opponent objectives, entity IDs, and presentation bounds;
- Rendering: renderer-oriented diagnostics such as camera target and terrain chunk visualization.

The master developer switch is disabled by default. F2 toggles that master. Shift+F2 toggles Rendering; Shift+F4 through Shift+F9 toggle Navigation, World, Logistics, Sensors, Combat, and Entities. Shifted shortcuts intentionally do not activate the existing unshifted F4–F8 building-placement shortcuts or the F9 footprint-rotation shortcut.

Disabled categories do not request their simulation debug-capture paths. Rendering-only diagnostics require no simulation debug snapshot. When all developer diagnostics are disabled, the debug draw path collects no primitives and performs no debug line draw call. F1 development metrics expose gameplay-overlay line count, developer-debug line count, dropped debug lines, and measured developer-overlay CPU submission time.

Render ownership and ordering are explicit: world geometry renders first, depth-tested engineering debug lines render next, player-facing world markers render afterward without depth testing, and screen-space RTS information/UI renders above both. Diagnostic visibility never writes to simulation state.

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
