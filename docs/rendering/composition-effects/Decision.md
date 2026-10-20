# Composition neighborhood and effect admission

Status: implemented; hardware and visual acceptance remain open. The optional
shared-linear shader variant is not enabled in the product default.

## Context

Scaled composition repeats nonlinear display preparation for overlapping AA
neighborhoods. Fog, tone mapping, grading, authored output tags and final output
transfer have distinct semantics. Moving tone mapping after reconstruction would
alter the image. Sharing a naively clamped neighborhood also changes border AA.
Particle admission and alpha ordering must be independent from snapshot order and
must not restore visual access to a source absent from the authorized snapshot.

## Decision

Keep the existing single composite pass, constants, textures and depth output.
Retain per-sample fog followed by exposure/white balance, tone mapping, saturation
and contrast. Apply spatial AA in display-linear color; reconstruct that color
with normalized depth weights and perform final output encoding once in the
optional shared variant. Native negative-alpha authored pixels retain exact raw
output and restored alpha. Scaled authored pixels retain the existing linear
interpolation semantics.

In scaled AA, constrain a neighbor to the center surface before applying the
existing chromatic-edge/detail filter. Reuse the same perspective-depth tolerance
as reconstruction. Do not bind depth in a native variant that did not bind it
before. Keep native full-resolution AA and its thin-detail rule unchanged.

Share the twelve-position union only when all four reconstruction centers are
distinct and inside the image. At borders and for tiny targets, use independent
clamped center neighborhoods. The optional shared path removes intermediate
encode/decode operations; retain the reference path as the default until measured
hardware evidence accepts the candidate. Do not substitute DXIL counts for
performance qualification.

Effect preparation remains a render-owner consumer of copied authorized data.
Admission examines the complete bounded effect set before applying emitter,
particle and screen-coverage caps. Event priority precedes decorative density;
stable geometry/identity tie-breaks avoid dependence on snapshot iteration and
avoid frame-by-frame random or rotating selection. Analytic particle depth may be
evaluated as a scalar sort key from the immutable initial conditions and age;
particle evolution and drawing remain in the existing shader. Scratch and uploads
stay bounded and reused.

## Product wiring and lifetime

| Boundary | Active behavior |
| --- | --- |
| `SceneCompositeShader` | Existing 40 root constants, texture slots and one pass; native authored-output bypass and nearest-surface depth output remain intact. Scaled AA now constrains cross-depth neighbors before filtering. |
| `COMPOSITION_SHARED_LINEAR=0` | Four independent shaded centers remain the default. The value `1` is an isolated qualification candidate, not a new runtime setting. |
| `ParticleVfxRenderer.Prepare` | Delegates CPU selection and ordering to `ParticleEffectSubmission` using the same copied, authorized snapshot on the render owner. |
| Candidate and authorization scratch | Covers the existing 4,096-effect snapshot limit; compact snapshot indices avoid retaining complete instance copies in candidate scratch. |
| Emitter state | Retains at most 2,048 entries. Removal from the authorized snapshot retires state immediately; session changes and tick rewind reset state. Pause stops cosmetic age advancement. |
| Particle state | Existing capacities remain 512/2,048/4,096 for Low/Medium/High, with the same particle presets, radius and screen-coverage budgets. |
| `ParticleDepthOrdering` | Computes scalar current view depth once, sorts reusable depth/index keys with stable submission ties, then permutes the existing 64-byte payload array in place. |
| `GpuParticleRenderer` | Existing one indexed-instanced draw, frame-indexed upload buffers, shader evolution and soft depth fade; no modification. |

Definitions and palette values are cached once. Unsupported or unavailable known
effects use the existing capped mesh fallback after distance/frustum rejection.
Unknown catalog kinds cannot reserve that budget. Full emitter-state pressure can
retire an unselected lower-ranked source; a returning continuous source may then
restart its cosmetic phase. Copied spawn ticks still prevent expired transients
from replaying. No hidden source is consulted to retain or recreate an effect.

## Alternatives and consequences

A new multipass target, temporal history or compute system would change frozen
resource contracts and introduce costs this work does not justify. A blanket
particle-density reduction would change the visible workload rather than prove
an identical-output optimization. Neither is adopted.

Depth-constrained AA and corrected event admission/order intentionally change
some images. They require explicit visual review separate from runtime savings.
Shared linear reconstruction may differ by floating-point/encoding rounding and
can increase register pressure despite fewer repeated color preparations. Native
comparisons determine whether each profile is worth enabling; a single static
instruction total cannot decide that question.

Scalar center-depth ordering improves off-axis and moving-particle order but does
not solve every intersection between translucent sprites. CPU and GPU
trigonometric evaluation can differ slightly. These limits, admission priority,
fallback visibility and dormant-state replacement require native moving-image
review. The synthetic CPU benchmark uses stationary unique camera-axis depths so
the frozen previous sorter and new sorter produce byte-identical payload order;
that evidence does not attribute intentional visual changes to performance.

Re-evaluate this decision if fixed-state and moving comparisons reveal missing
thin geometry, haze discontinuities, unstable transparent overlap or a regressing
GPU distribution. Preserve the reference and all failed evidence while deciding.
