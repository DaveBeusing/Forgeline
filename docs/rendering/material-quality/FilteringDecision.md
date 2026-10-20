# Bounded material and shadow filtering

Status: implemented numerical path; visual and hardware qualification pending.

## Ownership and active product wiring

The shared embedded PBR source is compiled by the existing textured-instance,
plain-instance, terrain and material-reference pipelines. The typed vertex and
root data, descriptors, buffers, output formats and shader compilation lifetime
are unchanged. No resource, queue, cascade, texture sample or material ID is added.

Textured instances pass the normalized interpolated surface normal separately
from their tangent-space normal-map result. Terrain passes its normalized height
normal separately from blended maps and microdetail. Only shadow slope bias uses
the surface normal. The BRDF, hemisphere, wetness and normal derivative filtering
continue to use the shading normal. The existing overload forwards an untextured
normal for both roles, preserving the material-reference caller. These are private
HLSL call signatures; they add no vertex varying or GPU constant.

## Numerical decisions

Normalization rejects non-finite or near-zero squared lengths and returns its
finite caller-provided fallback. Cofactor inverse-transpose and determinant-sign
handling remain; tangent handedness still includes the instance orientation. A
zero decoded tangent normal falls back to the surface normal. The GGX denominator
is evaluated as `(1-NoH²)+alpha²*NoH²`, avoiding cancellation of a small positive
term at the specular peak. The 0.045 perceptual roughness floor remains.

The existing specular variance kernel is retained exactly:
`rFiltered = sqrt(saturate(r² + 0.25*max(|ddx(N)|²,|ddy(N)|²)))`.
Two explicitly measured alternatives were rejected. Squared-slope filtering
increased bare-steel mean motion by 71–128% and cost 3–7% GPU time in the
analytic workload. A conservative sum-of-axes variant still increased bare-steel
mean motion by 1–10%, with mixed tails and unstable GPU cost. Their improvements
on other panels do not establish a reliable material-quality benefit. Both
complete experiments remain evidence; neither becomes the product default.
A future kernel needs a stronger predeclared perceptual reference and demonstrated
benefit. This change therefore adds no new specular blur or required filter cost.

Receiver-plane conditioning compares squared determinant with the derivative
footprint rather than a fixed determinant threshold. Nearly collinear/zero
derivatives or non-finite results produce a zero plane. Per-tap depth correction
is limited to two shadow texels in world-distance units, using the existing
radius, resolution and depth range. The four bilinear PCF weights, direct-shadow
coverage fade, texel-snapped projection and bounded ambient contact term remain.
The caster retains opaque coverage; adding alpha-tested vegetation shadows would
require material bindings in the frozen caster interface and is not claimed here.

## Color and authority contracts

Base Color/Emissive retain their compiled color spaces; Normal/ORM remain linear
data. ORM is AO/Roughness/Metallic in R/G/B. AO affects hemisphere ambient, not
direct illumination or emissive. Linear scene output retains unclamped lighting
and emissive until the existing composite performs exposure, tone mapping and
one output transfer. The direct-output fallback retains its existing equivalent
transfer. No IBL/environment asset integration, energy compensation or hidden
light/exposure adjustment is introduced. Viewer authorization, mechanical pose,
LOD/culling and simulation remain outside this change.

## Validation and limits

New GPU-output tests cover finite fallbacks, zero transforms, roughness boundaries,
GGX peaks, emissive linearity, geometry/shading normal routing, scale-independent
receiver planes and bounded corrections. Existing independent inverse-transpose,
reciprocity, energy, direct/linear parity and shadow lifecycle tests remain.
Eight combinations of shadow enabled/disabled, scene linear/direct and wet/cloud
environment compile the actual instance, textured and terrain pipelines, including
terrain detail on/off, with the same root/slot declarations. Projection tests
cover negative coordinates and nearly vertical lights without a projection change.

The optional material sequence test uses the exact production BRDF strings from
a frozen baseline assembly and the candidate assembly. Analytic controls isolate
normal-frequency effects across paint, steel, rubber, concrete, emissive, normal
detail, a seam, mirrored/nonuniform bases, a thin barrel, coverage and a slope.
They are not authored game assets or real camera distances. Mean absolute channel
change includes legitimate motion; a lower number alone cannot establish quality.
The real-client references and separate human material/shadow-contact reviews
remain required. Native resource and completed-fence timing observations are
reported without substituting CPU/Present waits for GPU execution.
