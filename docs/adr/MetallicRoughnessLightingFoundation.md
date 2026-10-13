# Metallic-roughness lighting foundation

Status: accepted implementation; visual acceptance pending.

## Context

Runtime material records already carry linear factors and Base Color, Normal, ORM and Emissive references. ORM means ambient occlusion, perceptual roughness and metallic in R/G/B. Texture decoding, mip generation and GPU ownership are established contracts. The previous terrain, textured-instance and fallback-instance shaders duplicated a heuristic response: roughness primarily changed brightness and metallic reduced diffuse. Normal transformation also assumed uniform scale.

The renderer already has directional lighting, a bounded directional shadow pass, frame-indexed FP16 scene targets, manual exposure, ACES, bounded grading, spatial anti-aliasing and an explicit sRGB output boundary. Replacing those systems or changing asset formats is unnecessary for this material correction.

## Decision

Use one presentation-owned `PbrMaterialShader` for all three world paths and the explicit material control fixture. Direct lighting uses the GGX normal distribution, height-correlated Smith visibility and Schlick Fresnel. Dielectric F0 is 0.04; metallic F0 is linear Base Color. Diffuse is `(1-F)*(1-metallic)*albedo/pi`; the specular term is `D*V*F`. Multiply their sum by incident directional radiance and `max(dot(N,L),0)`. This is a single-scattering model: rough metals can lose energy, and no multiple-scattering compensation or environment-map integration is claimed. The equations are independently implemented; their mathematical reference is [Filament's physically based rendering treatment](https://google.github.io/filament/main/filament.html).

Keep the practical hemispheric ambient approximation, with Fresnel/metallic-aware diffuse and a bounded roughness-dependent specular contribution. AO affects ambient visibility, not the directional BRDF or emissive term. This approximation is not measured irradiance or image-based lighting. Existing sunlight intensity 1.10 and manual exposure remain unchanged; normalization by pi makes some surfaces darker than the heuristic baseline. Avoid compensating with hidden exposure or saturation changes.

Use camera-relative view vectors for reflection. Transform normals with the cofactor inverse transpose and determinant sign; transform tangents forward, orthogonalize them against the normal, and multiply tangent handedness by the orientation sign. Safe normalization supplies a finite fallback for degenerate transforms. Preserve linear normal/ORM sampling and existing sRGB color decoding.

Clamp perceptual roughness to at least 0.045 in the BRDF. Broaden it by bounded screen-space normal variance: `sqrt(saturate(r*r + 0.25*max(lengthSquared(ddx(N)),lengthSquared(ddy(N)))))`. Evaluate derivatives before divergent coverage returns in the sphere control shader. Keep the existing five-tap spatial AA and full-resolution overlays. There is no motion history, temporal accumulation or ghosting mitigation; moving specular edges can still shimmer.

Directional shadow receiver bias now uses the actual surface-to-light dot product. Retain the existing texel-stable single cascade, receiver-plane filtering, coverage fades, caster/draw caps and frame-fence lifetime. Low/Medium/High retain 1024/2048/4096 maps and 180/450/750 m radii. No additional cascade or render target is introduced.

## Resource and module consequences

`ForgeLine.Graphics` continues to own pipelines, shader compilation, targets, barriers, descriptors and synchronization; `ForgeLine.Presentation` owns material interpretation and reference controls; the client supplies camera/light settings. Simulation and headless dependencies are unchanged.

Instances use 35 root constants, with camera XYZ aligned at DWORD32. Terrain repacks its existing header and uses 51 direct constants plus 13 texture tables, exactly the 64-DWORD D3D12 limit. Its buffered shadow path remains 18 constants. Existing 256-byte constant-buffer stride and bounded frame arena remain sufficient. No texture, descriptor, per-frame upload or persistent allocation is added to production shading.

Linear world output remains unclamped FP16 through lighting and emissive composition. The shared composite applies atmosphere, manual exposure, grade, ACES, spatial AA and one linear-to-sRGB transfer; UI and tactical overlays render afterward. The direct capability/pressure fallback applies equivalent exposure/ACES/transfer in the material shader. Swap-chain output remains UNorm SDR, not HDR monitor output. Existing resize, device-loss and resource retirement behavior remains authoritative.

Shaders remain embedded HLSL compiled by the established runtime DXC path. Packaging ships its native DXC dependencies. No new external shader authoring file, offline shader format or runtime asset schema is required; packaged execution still performs runtime shader compilation.

## Validation

Native WARP tests compile production shader functions and numerically integrate incident hemispherical energy, test reciprocity and backlighting, compare mirrored/nonuniform normal transforms to an independent matrix inverse transpose, and compare direct/FP16 sphere output within one 8-bit channel level. The sphere fixture also verifies exact repeats, resize and zero fixture-owned textures. CI explicitly runs the material and HDR contracts.

Use production scene captures for art and performance decisions. Analytic spheres are controlled material inputs, not replacement game assets or a game workload. See [qualification and measured limitations](../PbrFoundationQualification.md).
