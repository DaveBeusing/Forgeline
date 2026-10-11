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

## Implemented refinement

Smoke uses slow upward drift and a 2.8-second cycle; dust uses a low radial
spread; sparks use a 0.45-second gravity arc and faster opacity decay. Heavy
muzzles use a 0.15-second cone aligned to visible aim. Blast size grows to
twice its admitted radius while hot color transitions to charcoal. Fire uses
a staggered 1.2-second upward cycle. All velocities are normalized before
applying the declared speed, so conservative reach estimates bound motion.
Existing curl and maximum twofold size growth remain included in fill limits.

The muzzle socket applies cannon elevation about gun_pivot and turret yaw
about the catalog pivot before the body transform. A shot starts at the
unrecoiled position; existing mechanical recoil follows the same visible shot.
Moving vehicle dust reuses contiguous mechanical history and is rooted on
sampled terrain. Idle entities and teleport discontinuities produce no dust.
This is a ground-dust presentation policy, not a terrain wetness simulation.

The existing synthetic density baseline exposed bright mesh fallback under
fill saturation. Explosion/spark material emission multipliers are reduced
from 1.2 to 0.35/0.45 without changing atlas IDs, UVs, resolution or bindings.
Per-visual particle populations supplement existing rejection, fallback,
coverage, memory, CPU and completed-fence GPU counters.

Repeated extraction of a tick cannot emit combat events twice. Rewind/session
changes clear transient extraction history and particle time. Persisting pool
entries recheck terrain visibility, including both tracer endpoints. Visible
impact positions cannot reveal hidden target classes or hidden shooter socket
transforms. These filters do not change combat, intelligence or replay data.
