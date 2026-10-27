# Explicit cover and visual occlusion

Status: implemented; manual acceptance pending.

## Decision

Author ground tactical areas explicitly in World and carry them through BattlefieldMapArtifact format 4, immutable BattlefieldDefinition and the normal registered match composition. Both built-in maps advance to revision 2 with four cover regions and two visual screens; registered complete-file digests and sidecars include those bytes. Decorative objects do not acquire rules from appearance.

Use one bounded TacticalAreaSet per map. The existing damage system queries target position and applies the strongest unstacked infantry multiplier after armor for direct weapons only. The existing intelligence system queries VisualOcclusionPolicy for exact visual or radar identification. Radar detection remains independent and stores cell-rounded contacts rather than exact hidden transforms. Retain existing terrain weapon line of fire, Health/suppression/lifecycle and current-identification target authorization.

Keep static map areas in the authoritative checkpoint as world.tactical_areas. Cover occupancy is derived from current transforms, not a second ticking component or presentation-owned authority. Admission derives from tick and current sensor slots with fixed per-tick/per-sensor/per-trace limits. Missing work grants no exact identification. Presentation receives owned scalar state and public bounded outlines; it never receives hostile occupancy or queries a hidden entity to refine a contact.

## Consequences and alternatives

The first rule is omnidirectional infantry occupation cover, not physical wall geometry. Screens are ground columns with a 32 m close exception; terrain still blocks that close sight ray. Fixed eye offsets do not claim roof/window/aerial sensing. Terrain fog coverage remains a range/exploration layer separate from contact authorization. A radar upgrade alone cannot reveal precise identities through a visual blocker, though it can retain coarse detection.

A dedicated cover command, extra inventory economy, automatic decorative-tree bonuses and a second combat/visibility state store are unnecessary. Existing move/attack/fire-mission commands remain authoritative. Original bridge projectile intent and paid infrastructure lifecycle are preserved; bridges and other structures do not receive infantry cover.

Bounded admission can delay exact observations in crowded or long-range cases. It is neither proof of invisibility nor an Unreachable result. Current operational bounds, authored locations and manual scenarios are described in [Cover and visual occlusion](../CoverAndVisualOcclusion.md). No measured performance or balance result is claimed.

## Compatibility and validation

Map format 4/revision 2 and authoritative schema 15 are explicit boundaries. Envelope 1 and payload 11 remain, including complete registered map identity. Older authoritative states and old compiled maps are rejected rather than reinterpreted. Historical fingerprints are not rewritten.

Current master already includes the bridge action helper's CA1859 return-type correction. The integration retains it without changing tests or gates. Product compilation is unavailable because the exact current master tree is absent locally. No test, smoke, benchmark, native qualification or manually triggered workflow/rerun was performed. Automatic PR CI is separate evidence; manual acceptance remains outstanding.
