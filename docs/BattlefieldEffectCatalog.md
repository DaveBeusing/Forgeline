# Battlefield effect catalog

## Ownership and source audit

The completed-tick extractor is the sole event consumer. Renderers consume its
copied, viewer-authorized instances, never combat or logistics state directly.

| Source | Treatment | Identity / visibility |
| --- | --- | --- |
| ShotFired | one muzzle emitter; heavy-weapon ground dust | current tick, source or event position visible |
| Hitscan shot followed by matching impact | one connecting tracer | both endpoints must be visible |
| Impact | surface fragment layer plus heavy-weapon blast | impact position/target authorized independently of shooter |
| ProjectileState / IndirectFireProjectileState | existing projectile mesh | current authorized projectile only |
| Newly observed wreck | blast, debris, dust; sustained smoke/fire from wreck state | full entity generation, once; no collision-bearing debris |
| Damaged / Critical | smoke, fire and restrained sparks | only current visible read model |
| Actual vehicle displacement | low ground dust | contiguous visible extraction history; no dust from idle/teleport |
| Cargo Loading / Unloading | existing transfer meshes at cargo socket | authoritative lifecycle, current visible entity |
| Fuel / ammunition fraction increase | existing transfer/refuel/rearm meshes | completed transfer only; never initiates gameplay |
| Infrastructure Disabled / Restoring | existing infrastructure state geometry | actual StrategicInfrastructureState; no inferred damage or repair |

Impact fragments and the blast are separate deliberate layers, not two copies
of one particle emitter. Vehicle/building burst and debris remain distinct
mesh layers. Sustained smoke/fire must not also be spawned as transient wreck
layers. Terrain visibility is rechecked for retained pool entries each tick.

## Visual contracts

Reuse the 36 stable effect IDs and their compiled mesh/material fallback. No
new atlas or external source is necessary: procedural smoke, dust, sparks,
blast, heavy muzzle and fire use the existing 64-byte initial-condition stream.
The 512/2048/4096 populations, 2048 emitter identities, single draw and fixed
frame uploads remain the limits. Directional plumes use copied rotations;
smoke rises slowly, dust spreads low, sparks lose brightness quickly, and
blast heat transitions into dark smoke. Large events retain normal-view
readability while strategic distance suppresses detail under existing policy.

The billboard solution remains the bounded baseline. Volumetric smoke is
deferred until equivalent native captures demonstrate a material intersection
or parallax deficiency and profiling establishes a target/time/memory budget.
A future alternative must reuse graphics-owned depth/lifetime contracts;
neither a second scheduler nor gameplay-affecting debris is justified here.
