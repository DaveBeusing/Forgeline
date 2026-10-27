# Cover and visual occlusion

## Player operation

Open the Sensors strategic layer (cycle layers with F10) to find authored ground gameplay areas. Green outlines mark infantry cover; amber outlines mark visual screens. The labels describe public map data, not enemy occupancy. Sensor rings remain maximum ranges rather than guarantees of identification.

Move units through the existing movement commands. A single owned unit's selection-status or hover tooltip reports its current cover and screen from a completed simulation tick. No new stance, resource, order queue or UI-owned protection state is introduced. An infantry unit in cover shows its direct-damage reduction; a vehicle in the same region explicitly has no infantry protection. A screen at an owned unit's position is not a promise that every opposing observer is blocked.

## Infantry cover rule

TacticalAreaDefinition is explicit authored World data. A covered Infantry target receives the strongest overlapping multiplier, never their product. Current maps author a 0.65 multiplier: 35% less direct weapon damage. The lookup uses authoritative horizontal target position at damage resolution. It is an omnidirectional occupation rule, including fire from within the same area, rather than wall-facing or ballistic simulation.

The existing damage owner applies this after directional armor and before Health, suppression, retaliation and destruction. Suppression consequently follows the actual reduced health loss through its existing rule. Only normal known direct weapons participate. Artillery/area damage, vehicles, buildings, bridges and developer damage do not receive this modifier. Cover neither blocks a projectile nor adds weapon accuracy, morale, movement or supply effects.

Cover and screens are independent fields. A green area need not conceal anyone; an amber screen need not reduce damage. Decorative trees, rubble, materials, meshes, shadows and atmospheric fog have no implicit protection or intelligence effects.

## Visual sensing and radar

Normal match runtime supplies VisualOcclusionPolicy to BattlefieldIntelligenceSystem. Enemy candidates still require range, signature, faction and scheduled-scan eligibility. Identification additionally requires:

- no authored visual-screen intersection in horizontal ground geometry, except within 32 m horizontally;
- a clear sampled ground sight ray, using source eye height 2 m and target eye height 1.5 m above their authoritative transforms, with 0.1 m terrain clearance;
- available bounded trace work.

Screens act as ground visibility columns. A ray entering, leaving or crossing one is blocked outside the close-distance exception, including two distant units inside the same screen. The exception bypasses screens only; intervening terrain can still prevent observation. This first implementation does not model foliage density, material transparency, roofs, windows, aerial sensing or an elevation range bonus.

TerrainLineOfFirePolicy remains the separate weapon eligibility rule. Visual screens do not become movement obstacles or ballistic walls. Once exact identification is lost, ordinary tactical availability prevents direct entity tracking; existing coordinate artillery missions may still use legitimate last-known information.

Radar detection retains signature, range, scan cadence, power and technology gates. It ignores visual screens and terrain for detection. Detected-only contacts store the nearest point on the logical intelligence grid (32 m spacing under current match settings), with Y=0, an opaque key and observation tick; they carry no identity key or resolvable current target. Rounding is deterministic and idempotent for existing grid points, including the origin. Existing artillery processing projects the copied coordinate onto public terrain before firing. Actual hostile transforms are not read to refine that coordinate.

Radar identification still requires its configured/technology-enabled identification range, and now also a successful bounded visual trace. A blocked or unadmitted identification attempt can therefore produce only coarse Detected information. A different permitted observer may identify the same enemy normally. Previously known positions remain historical observations with their original tick, not fresh hidden tracking.

Terrain fog cells retain the existing explored/range-coverage semantics. A Visible terrain cell does not authorize an enemy entity: current Identified contact state remains the presentation/selection/direct-target gate. Hidden enemy health, inventory, readiness, cover occupancy and other private state are never added to these read models.

## Bounds and ordering

A TacticalAreaSet copies, validates and ordinally sorts at most 32 unique areas. Bounds and multipliers are finite; multipliers lie between 0.5 and 1. Areas must fit the map. A direct infantry impact performs at most 32 area checks. Sight traces perform at most 32 screen checks and 64 interior terrain samples at nominal 8 m spacing. Longer terrain traces or missing samples do not grant visibility.

New trace work is capped at 2,048 attempts per simulation tick and 128 per sensor, shared with radar-identification checks. A deterministic rotating sensor-slot window divides admission across current visual/radar components; inactive or not-due slots can leave work unused. Spatial candidate starts rotate by scheduled scan ordinal and stable source index; the existing spatial query remains the broad phase and the retained bridge list contains at most 32 entries. There is no growing admission history or new persisted cursor. Index-free compositions retain the existing fallback candidate enumeration. Budget pressure may delay exact acquisition; exhaustion means no new identification, never proof that a location is clear.

Copied overlay geometry contains at most 32 regions, four terrain-projected corners each, 128 outline segments and at most 16 labels (eight in All). Existing rendering limits remain unchanged. No performance gain or final layout acceptance is claimed.

## Authored geography and identity

Both registered maps use map revision 2 and artifact format 4. Existing terrain recipes, resources, road topology, decorative instances and Directorate catalogs remain unchanged. Each map deliberately contains four cover areas and two screens.

| Map | Cover rectangles, X/Z meters | Screen rectangles, X/Z meters |
| --- | --- | --- |
| Central Divide | west bridge 1280–1400 / 850–895; east bridge 1672–1792 / 850–895; west ford 1260–1380 / 2300–2345; east ford 1692–1812 / 2300–2345 | west 890–1010 / 650–740; east 2062–2182 / 650–740 |
| Split Basin | west basin 840–940 / 1175–1220; east basin 1108–1208 / 1175–1220; west outer 820–920 / 1815–1860; east outer 1128–1228 / 1815–1860 | west grove 560–660 / 1540–1680; east grove 1388–1488 / 1540–1680 |

Registered map SHA-256:

- Central Divide: `0f64c67943ddd0e399f7cc7579f65c43feded449e6bb43182de3bdd9ff4d0682`
- Split Basin: `635ca23e5fa63285c4bb6b7e4b00b8615804af67cf638ee48e36dda21573a36e`

Artifact capture, reconstruction and authoring comparison include tactical areas and map revision. The runtime JSON, mandatory sidecars and registered identities change together. Format 3 and earlier compiled maps are explicitly rejected; no reader silently supplies tactical rules to an old registered artifact.

## Save/replay and acceptance

Envelope 1 and payload 11 remain. Authoritative schema 15 covers changed damage/sensing rules, grid-rounded detection and the world.tactical_areas checkpoint domain. Existing movement, attack and fire-mission commands reconstruct the behavior. No new player command or dynamic cover component is necessary. Schema 14 and earlier saves/replays are incompatible before reconstruction; historical hashes and qualification records remain unchanged. See [recovery](SaveLoadReplayAndRecovery.md#cover-and-visual-occlusion-compatibility) and the [architecture decision](adr/ExplicitCoverAndVisualOcclusion.md).

Implemented — manual acceptance pending. The exact current master source tree is absent from the local mirror, so affected product compilation could not be performed here. Source and authored JSON/sidecar preparation are not MapCompiler qualification. No tests were created, changed, built or run; no smoke, benchmark, native qualification, workflow dispatch or rerun was initiated. Existing gates and fixtures are unchanged.

Manual acceptance should cover both maps, entering/leaving/overlapping cover, infantry versus vehicle and artillery damage, the screen boundary and 32 m exception, hills and clear rays, multiple opposing observers, pure radar versus identification, last-known contact privacy, unavailable trace work, UI scales and Save/Replay continuation. Prior map, combat and intelligence measurements do not qualify these new rules.

Automatic CI on initial head e8af4c1 completed its Build step. Later Save/Replay cases exposed the existing congestion-checkpoint span-constructor JSON failure; the authoritative projection now reuses the prepared fleet-work DTO copy. The two old contact-hover fixtures also exposed the difference between shifted cell centers and existing copied grid points; deterministic nearest-grid rounding keeps those observations stable while retaining coarse radar measurements. No fixture or assertion was changed. The separate movement comparison failed while serializing Vector3 spans in its unchanged baseline benchmark; that baseline qualification remains unresolved and is not claimed as passing.
