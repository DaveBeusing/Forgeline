# Battlefield Intelligence

## Purpose

FORGELINE battlefield intelligence is authoritative simulation state owned per faction. It determines what terrain has been explored, what terrain is currently visible, which enemy contacts are currently detected or identified, and whether a combat target may use exact enemy entity state.

Rendering, selection, UI, and effects consume faction-specific intelligence output. They never decide visibility.

## Canonical Intelligence States

The first intelligence layer uses five canonical states:

- `Unexplored`: terrain has never been visually observed by the faction.
- `Explored`: terrain was observed previously but is not currently inside retained visual coverage.
- `Visible`: terrain is currently inside faction visual-sensor coverage.
- `Detected`: an enemy contact is currently known as a contact, but its identity is not available.
- `Identified`: an enemy contact is currently observed with enough information for exact entity targeting.

Terrain visibility and entity contact state are related but intentionally separate. Radar does not make terrain visually visible.

## Ownership

`ForgeLine.Intelligence` owns the reusable intelligence data model:

- `IntelligenceState`
- `IntelligenceGridSettings`
- `VisibilityCellCoordinate`
- `VisualSensorState`
- `RadarSensorState`
- `IntelligenceSignature`
- `IntelligenceContactKey`
- `IntelligenceContact`
- `FactionIntelligenceStore`
- `FactionIntelligenceSnapshot`

`ForgeLine.Game` owns the simulation integration:

- `BattlefieldIntelligenceSystem`
- `IntelligenceTargetAvailabilityPolicy`

`ForgeLine.Presentation` owns faction-safe extraction and debug visualization.

## Faction Intelligence Store

`FactionIntelligenceStore` keeps independent state for every faction.

Each faction owns:

- a persistent set of explored visibility cells;
- a current set of visually visible cells;
- a contact history keyed internally by authoritative entity and exposed through an opaque `IntelligenceContactKey`;
- last-known contact position;
- last observed simulation tick;
- detection/identification state;
- identity key only after identification.

Calling `BeginTick` clears current visible terrain but does not clear explored terrain or historical contacts.

Visual coverage then repopulates the current visible set for the tick.

## Intelligence Grid

Fog-of-war terrain state uses a configurable logical grid independent from render resolution.

The current development default is 32 meters per visibility cell.

World positions map to stable integer `VisibilityCellCoordinate` values. A visual-sensor footprint marks all intersecting cells as both:

- currently visible;
- permanently explored.

Presentation can request a bounded snapshot for the active terrain world. Bounded snapshots explicitly include `Unexplored`, `Explored`, and `Visible` cells so the renderer does not need access to hidden simulation state to reconstruct faction knowledge.

## Visual Sensors

`VisualSensorState` defines:

- owning faction;
- visual range;
- update interval in simulation ticks.

Visual sensors identify compatible enemy intelligence signatures inside range.

A visual scan therefore:

1. queries nearby candidates through the existing spatial index when available;
2. rejects self, friendly entities, stale entities, and entities without a visual-detectable intelligence signature;
3. records an `Identified` contact at the observed simulation position;
4. updates faction visual terrain coverage.

The ECS fallback remains available for headless compositions without a spatial index.

## Controlled Update Frequency

Sensor update intervals are deterministic simulation-tick values.

Scans are staggered by stable entity index so groups of sensors with the same interval do not all need to execute on exactly the same tick.

Visual terrain coverage is retained from the most recent visual-sensor footprint between scans. Exact enemy entity availability is stricter: a contact is considered current for exact targeting only when it was actually observed on the current simulation tick.

This prevents lower-frequency sensing from becoming a hidden transform leak.

## Radar Sensors

`RadarSensorState` defines:

- owning faction;
- detection range;
- optional identification range;
- update interval in simulation ticks.

A radar-detectable enemy inside detection range produces a `Detected` contact.

If an identification range is configured and the target is inside that smaller range, radar may produce `Identified` instead.

Detected-only contacts expose:

- opaque contact key;
- last-known position;
- last-seen tick;
- detection state.

They do not expose the target identity key.

Radar does not mark terrain as visually visible.

## Detection and Identification

Detection and identification are distinct authority levels.

`Detected` means the faction knows a contact exists at an observed location.

`Identified` means the faction has enough current intelligence to use exact entity targeting through the current V1 combat-targeting bridge.

If multiple sensors observe the same target in one tick, `Identified` wins over `Detected`.

## Contact Loss and Last-Known State

Contacts are not deleted immediately when sensing is lost.

The store keeps:

- the last-known position;
- the strongest last observed state;
- the last-seen tick;
- the identity key if it had previously been identified.

`IntelligenceContact.IsCurrent` indicates whether the contact was actually observed on the current intelligence tick.

A stale contact is therefore useful for map markers, search behavior, artillery planning, or later tactical systems without granting access to the target's current hidden transform.

## Targeting Integration

`IntelligenceTargetAvailabilityPolicy` implements the existing combat `ITargetAvailabilityPolicy`.

When composed with `TargetAcquisitionSystem` and `CombatExecutionSystem`, an enemy target is available only when:

- the observing entity resolves to a valid faction;
- the target entity is alive;
- the target is currently `Identified` for that faction.

A radar-only `Detected` contact is deliberately not enough for direct entity targeting.

This is the central hidden-state safety rule: targeting cannot discover or track a hidden enemy by scanning authoritative ECS transforms and then treating presentation state as permission after the fact.

## Sensors-Phase Ordering

The current composition uses:

```text
Movement
  ↓
Spatial synchronization
  ↓
Sensors
  - BattlefieldIntelligenceSystem updates visual/radar observations
  - TargetAcquisitionSystem consumes current intelligence availability
  ↓
Combat
  - final target availability is revalidated
  - Ammunition is consumed only after validation
  ↓
DamageResolution
  ↓
EntityLifecycle
  ↓
SnapshotEvents / presentation extraction
```

Systems registered in the same simulation phase execute in stable registration order. Composition roots therefore register battlefield intelligence before target acquisition.

## Presentation Extraction

`PresentationExtractor` can be configured with:

- a `FactionIntelligenceStore`;
- one viewing faction;
- optional world bounds for bounded Fog-of-War extraction.

When faction intelligence is configured:

- friendly or unsigned render entities remain visible under their normal render rules;
- enemy entities with `IntelligenceSignature` are extracted only while currently identified;
- detected-only enemies are omitted from normal render instances;
- contacts are exported separately through `FactionIntelligenceSnapshot`;
- bounded Fog-of-War snapshots include unexplored, explored, and visible cells.

The render world therefore cannot leak hidden enemy entity transforms through its normal instance list.

## Debug Visualization

The F2 development path can display:

- visual-sensor range circles;
- radar detection circles;
- radar identification circles;
- Fog-of-War cells;
- detected radar contacts;
- identified contacts;
- stale last-known contacts;
- current sensor scan/contact/visible-cell counts;
- measured sensor-update duration when timing diagnostics are enabled.

These visuals consume read models only.

## Diagnostics

`BattlefieldIntelligenceSystem.Metrics` exposes:

- active visual sensors;
- active radar sensors;
- sensor scans this tick;
- candidates considered this tick;
- detected contacts this tick;
- identified contacts this tick;
- currently visible cells;
- explored cells;
- optional measured sensor-update duration;
- cumulative scans;
- cumulative candidates;
- cumulative detections;
- cumulative identifications.

Timing is diagnostic observation and never influences simulation decisions.

## Correctness Coverage

Automated tests cover:

- persistent explored terrain;
- loss of current visual visibility;
- radar-only detection;
- Detected-to-Identified transition;
- identity suppression before identification;
- stale last-known contact retention;
- faction isolation;
- direct targeting blocked for hidden targets;
- direct targeting blocked for radar-only detected contacts;
- targeting enabled once visual identification becomes current;
- deterministic lower-frequency radar scan counts;
- presentation removal of an enemy when identification is lost.

## Performance Coverage

The simulation BenchmarkDotNet host includes 100- and 1,000-unit battlefield-intelligence scenarios with mixed visual and radar sensors, spatial-index queries, visibility-grid updates, and contact generation.

Correctness remains separate from timing. Benchmark results are measurement evidence rather than hardware-specific CI gates.

## Extension Boundary

Later intelligence work may add:

- thermal sensing;
- electronic warfare and jamming;
- SIGINT;
- stealth and signature strength;
- weather attenuation;
- terrain occlusion for visual sensors;
- confidence/uncertainty models;
- contact aging policies;
- allied intelligence sharing;
- final minimap and intelligence UI;
- artillery workflows that consume contact data without resolving hidden entities directly.

Those systems should extend the existing faction store, contact model, sensor scheduling, and target-availability boundary rather than bypass them.
