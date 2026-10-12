# Directorate operational building production

Starting master: `63569ac2031b4b89c57c55c02b2aa20244d34df9`.
Ordered repository/branch/PR, implementation, architecture, tests, CI and
repository governance review completed before authoring. No reusable open PR.

Vehicle Factory (26 x 24 x 12m): wide vehicle-scale portal, open hall,
assembly lanes, internal gantry and roof ventilation. Preserve the existing
20m-forward gameplay spawn offset and original door/crane socket positions.
Power Plant (16 x 16 x 10m): separate generation hall, exhaust/cooling and
transformer service area. Barracks (18 x 16 x 8m): quarters with a central
infantry/service path and pitched roofs. Radar (14 x 14 x 18m): supported raised
concave array, equipment enclosure and an existing bounded radar motion range.
These are authoritative footprint dimensions, not redesigned gameplay bounds.

Use +Y up, +Z forward, scale 1 and existing normalized building conventions.
Retain primary/LOD IDs, horizontal bounds, simplified shared collision metadata,
selection/navigation/production/power/sensor rules and existing PBR masters.
Extend the cosmetic bases to normalized Y=-.5 to meet the existing terrain-ground
placement; this is an explicit lower mesh-bound change, not a footprint change.
LOD0 is manufactured functional geometry, LOD1 removes small fittings while
retaining role massing, LOD2 retains distinct strategic silhouettes. Rigid
factory/radar/fan ranges reuse existing mechanical pose and fade contracts.

Dedicated construction, power/damage overlays and wreck geometry follow the
existing state resolver. Idle remains meaningful only for the existing
production-capable Factory/Barracks. Do not introduce alternate animation,
damage physics, gameplay state or renderer/resource ownership.

Four opt-in state galleries use actual footprint scales and existing ECS
construction/power/health/wreck extraction. The repeated gallery contains 250
of each family. Capture the unchanged sources first at 120/420/1000m and retain
camera, seed, tick, lighting, GPU/driver and native surface identity for comparison.
Geometric/compiler/test evidence and still images do not establish human art,
continuous LOD/motion/live spawning, sustained 60-FPS or borderless acceptance.
