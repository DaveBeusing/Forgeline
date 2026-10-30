# Resource deposit variant rendering

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Final integration base: `master` at `c8da15fce589b661de2813683b22aa77fa1bd7e2`
(product 0.1.457), including the field producer and renderer inventory. Geometry,
visual-language and PBR source PRs are integrated.
This change binds those assets through the existing instanced renderer. No new
renderer, pass, vertex format, simulation component, authoritative resource rule,
HUD action, cargo behavior, save format or gameplay picking volume is introduced.

<details>
<summary>On this page</summary>

- [Copied selection and stable identity](#copied-selection-and-stable-identity)
- [LOD, bounds, visibility and lifetime](#lod-bounds-visibility-and-lifetime)
- [Failure matrix](#failure-matrix)
- [Diagnostics and review scenes](#diagnostics-and-review-scenes)
- [Field integration](#field-integration)
- [Execution and manual acceptance — NOT EXECUTED](#execution-and-manual-acceptance--not-executed)

</details>

## Copied selection and stable identity

`WorldFeaturePresentationMetadata.ResourceVariant` carries a schema version,
ordinal/case-sensitive `VariantKey`, `VariantCount` and independent surface index.
The key is `resource-visual/1/<family>/<layout>/<size>` from
[the art specification](ResourceDepositVisualLanguage.md).
`VariantCount` is **12 per family** (four layouts × three sizes); it does not mean
48 families/forms, fragments per field or the number of simultaneously visible
instances. `SurfaceIndex` is 0–3: weathered, fresh fracture, patinated, inclusion rich.

The fixed catalog builds the allowlist once. It never accepts arbitrary asset
paths or opens source JSON at runtime. A binding contains a matching High/Reduced
pair and one family surface. Meshes retain their single canonical family material
slot, while the renderer's explicit material override selects the surface atlas.
The mesh material must not overwrite this override. All variants share the same
three family textures, neutral emissive and existing instance shader layout.

`ResourceDepositVariantSeed.TryCreate` implements the documented length-prefixed
ASCII/little-endian SHA-256 protocol. Form/size weights and bounded tint ranges
match the visual-language fixture. The independent additive `surface` channel
selects one of four patches without changing the existing form/size/tint hashes.
The authored visual salt defaults to 2026 and never advances simulation RNG.
Malformed/missing keys and unsupported resource IDs retain canonical presentation.

The scenario-bound extractor caches selections once from authored map/deposit
keys and generation-aware resource entities, with anchor ordinal zero for the
existing single cluster. It copies values only after the existing extraction
visibility filter. State, remaining amount, camera, quality, tick and transient
entity indices do not choose a new variant. Recycling an entity with a new
generation does not inherit the old selection. Plain/legacy extractors without
authored map keys keep canonical root metadata. Field clusters carry their own
copied selections as described below.

`ResourceTint` is a separate bounded RGB multiplier, not a material/batch identity.
Zero means the legacy neutral multiplier; nonfinite/out-of-family-range inputs
also resolve to neutral. Composition is family factor × instance tint, followed
by existing state tint. Depleted remains the existing replacement gray.

## LOD, bounds, visibility and lifetime

Resources use the existing projected bounding-sphere diameter, 65-pixel High to
Reduced threshold, 12% hysteresis and quality diameter scale. The lower 18-pixel
classification still maps to Reduced; `_lod2` remains asset-only. Display-height
normalization preserves LOD across 1080p/1440p/4K. LOD history remains scoped to
render world/session, camera and generation-aware entity/debug identity.
Changing LOD never changes form, surface, seed, transform or copied state.

All source variants/LODs fit the common centered `[-0.5,0.5]` envelope, including
ground Y=-0.5. Existing minimum resource visual heights are applied before
extraction publishes transforms. Existing rotated-cube world bounds conservatively
contain all eight source corners; cluster/spatial indexes also retain swept
rotation bounds. No picking, hover, extraction or navigation volume is enlarged.
Nonfinite, nonpositive or invalid-quaternion resource transforms are rejected
before projected-size evaluation, asset demand or draw submission.

Selection is used in both Prepare and recording, including shadows. The existing
mesh/material/texture residency store performs all reads/uploads before recording.
There is no alternate resource cache or per-fragment GPU allocation. Existing batch
keys combine actual mesh/material IDs; compatible instances retain one instanced
draw. Distinct form/LOD/surface bindings can legitimately add batches. No 96-MiB
residency, 128-MiB preparation, descriptor, retained-scratch, shadow, caster,
draw or quality limit is raised. The existing immutable-catalog preparation
already includes these assets; this change does not assert a new residency saving.
Actual costs and all existing global budgets require manual qualification.

## Failure matrix

| Input/asset condition | Behavior |
| --- | --- |
| No copied key | Stable canonical family binding |
| Wrong schema/count/case/family, unknown key or surface outside 0–3 | Count invalid selection; canonical binding |
| Variant High/Reduced absent, wrong family slot, multiple sections, missing normals/UV/tangents, or unavailable selected material | Reject entire variant; count canonical fallback; never combine variant High with canonical Reduced |
| Canonical High and material valid, Reduced absent | Retain canonical High; count retained-High when Reduced was requested |
| Canonical High/material unavailable, or no runtime catalog/textured pipeline | Omit 3D visual; count missing resource; preserve already-authorized inspection and existing symbol policy; no generic cube |
| Invalid resource transform | Skip before demand/submission; count invalid transform |
| Corrupt declared runtime payload, missing declared dependency/texture, wrong texture usage/color space, preparation/residency limit, shader construction failure | Existing startup preparation fails with asset/error diagnostics; no partially ready frame or budget bypass |

Optional-asset absence and corruption are different cases: a catalog omitting an
unreferenced optional pair can fall back, while a declared corrupt dependency is
still rejected by strict preparation. GPU/shader exceptions are not swallowed.
Native capture rejects missing resources, invalid selections/transforms and
unexpected canonical fallback, in addition to its existing binding/fault checks.
A negative fault-injection run is expected to fail qualification and retain its
failure record; it is not a passing artwork capture.

## Diagnostics and review scenes

`InstanceRenderDiagnostics.ResourceDeposits` records visible requests, resolved
variants, canonical fallbacks, invalid selections/transforms, missing resources
and retained High LODs. These are part-level counts, with explicit High/Reduced
part counters; the existing visibility/LOD totals continue to count parent roots.
A skipped missing visual is not reported as geometric
culling. Existing total/visible/cull, LOD, draw and binding counters remain available.
`GetResourceBatchDiagnostics()` is an explicit out-of-frame snapshot of actual
mesh/material IDs, instance counts, indices and logical 112-byte instance uploads.
Read it after the color pass; it reports the most recently built batch lists.
Native capture now records it as `resourceBatches`. These counts are not GPU memory
allocation measurements. Use existing submission capacity/upload metrics,
material resident mesh bytes, frame logical texture bytes, native DXGI observations
and CPU/GPU frame distributions alongside them; do not add shared residency per batch.

The old `ResourceDepositSingle`, `ResourceDepositsGallery` (12), `ResourceDeposits100`
and `ResourceDeposits1000` retain their numerical IDs and integrated field-layout,
grounding and state inputs. Cluster selection metadata now binds their surfaces.
Four scenes append after `InfrastructureFordExit`, without replacing old goldens:

| Scene | Authored input | Terrain |
| --- | --- | --- |
| ResourceDepositVariantsGrass | 576: 4 families × 12 forms × 4 surfaces × 3 states | Grass |
| ResourceDepositVariantsRock | Same inventory | Rock |
| ResourceDepositVariantsMud | Same inventory | Mud |
| ResourceDepositVariantLoad1000 | 1,000 copied deposits, repeated inventory | Grass |

The new inventory scenes explicitly clear their basis's ResourceField and select
one known cluster per copied root, so the 576 combinations are not overwritten by
field composition. The original scenes separately exercise the real field producer.
All use actual sampled terrain height, varied fixed yaw and the normal renderer.
Input count is not a claim of simultaneous visibility or complete native coverage
from a single view. Inspect/framing sweeps and close single-family rows remain
necessary. Terrain profiles affect review surfaces, not simulation terrain.

## Field integration

The field producer integrated through master during implementation. Its immutable
cluster contract now additionally carries the approved `VariantKey`/count/surface.
Keep its already-selected layout/size, grounded transform, tint, crust, quota and
depletion behavior. Its independent `resource-field/1` protocol gains only a
`surface` channel per authored anchor ordinal; it does not switch to the root's
`resource-visual/1` protocol or alter any existing seed channel.

Each child is adapted to the common resource-binding branch, retaining the parent
entity/picking identity and ordinal-based debug/LOD identity. The adapter validates
the copied High/Reduced IDs against the approved binding or the explicit canonical
pair. Requested selection metadata survives the producer's canonical metadata
fallback, so the renderer can diagnose an incomplete optional variant pair.
Legacy copied clusters without selection metadata can be translated by
`TrySelectAssetPair`; canonical pairs remain neutral defaults and foreign/mismatched
pairs become diagnosed invalid selections. Field tint is composed exactly once.

Shared submission hunks are reconciled with the integrated producer's aggregate
and child bounds, Prepare/record traversal, root counters and shadow admission.
There is no feature-branch implementation dependency remaining. Multi-cluster
depletion, native field appearance and performance still require manual acceptance.

## Execution and manual acceptance — NOT EXECUTED

Only source inspection, implementation, documentation and Git diff review were
performed. No restore, build, asset compilation, tests, native captures, GPU
measurements or manually dispatched CI ran. Prepared regression cases cover the
allowlist, field pair adapter, bad selection/key/tint inputs, deterministic key
selection, state/LOD/bounds continuity, full scene inventory, hidden/missing assets,
batched surface overrides and complete-pair fallback. They remain **NOT EXECUTED**.
Automatically triggered GitHub PR workflows are separate observations. No historical
green check, image, metric or golden is relabeled as acceptance of this change.

Run the canonical restore/build/asset/source-less/test commands in
[Development](Development.md) manually. Then create fresh native requests, for
example (commands below are **NOT EXECUTED**):

```powershell
$request = @{
    outputRoot = [IO.Path]::GetFullPath('artifacts/resource-rendering-grass')
    runtimeRoot = [IO.Path]::GetFullPath('assets/runtime')
    commit = (git rev-parse HEAD).Trim()
    scenes = @('ResourceDepositVariantsGrass')
    profiles = @('High')
    displays = @(@{ width = 1920; height = 1080 })
    zooms = @('NormalGameplay')
    warmups = 32; samples = 128; repeatCaptures = 8
}
$request | ConvertTo-Json -Depth 8 | Set-Content artifacts/resource-rendering-request.json
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --reference-capture-request artifacts/resource-rendering-request.json
```

Expand into independent fresh output roots: the four original scenes plus all four
new scenes; 1080p/1440p/4K; Low/Medium/High/Ultra; CloseTactical/NormalGameplay/Strategic;
fixed, sweep and traverse camera paths; native raster DPI; sun/shadow, dark/wet
settings and targeted terrain. Capture matching before/after source/runtime hashes,
inputs and raw images. Repeated captures must agree; camera motion/readability,
thermal/performance and human art review remain separate live checks. Inspect every
family, state and surface at both actual LODs, mip shimmer, banding, highlights,
normal orientation, Z-fighting, clipping, hover/inspection, fog/Intel and shadow
parity. Do not update old goldens or loosen budgets to obtain a pass.

Prepare valid sealed omission catalogs for missing optional High/Reduced/material,
canonical Reduced/High and incorrect family-slot cases; test declared corrupt
payloads/dependencies as startup failures. Record actual fallback counters and no
hidden uploads. Compare 100/1000 cull rates, batches/draws, upload/capacity bytes,
resident logical/native bytes and controlled frame-time distributions, including
the integrated multi-cluster fields at full/partial/zero quantity. All native,
package, performance and human acceptance remain pending.
