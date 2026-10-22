# Deterministic resource field composition

## Implemented boundary

Resource deposits now carry an optional immutable `ResourceDepositField` on their
existing presentation root. The interactive extractor creates it after normal
viewer admission. Each root remains one inspectable, non-commandable entity;
children have no ECS identities, quantities, navigation, collision or extraction
behavior. Source assets and map resource definitions are unchanged.

Data flow is Central Divide `BattlefieldResourceDepositDefinition` ->
`BattlefieldRuntime.Load` / `ResourceDepositSpawner` -> authoritative
`ResourceDeposit` plus `WorldPresentationIdentity` -> `PresentationExtractor` ->
immutable `PresentationSnapshot` / `RenderWorld` -> shared instance batches.
Field composition reads the copied root and authoritative resource values on the
simulation owner. The render owner consumes only the resulting presentation data.
No headless project dependencies or save/replay fields are added.

## Identity, topology and budgets

Field schema 1 uses SHA-256 over `FORGELINE.resource-field/1` followed by a zero
byte, UInt64 Salt, UInt32 ResourceId, length-prefixed ASCII map/deposit keys,
UInt32 cluster ordinal, then a length-prefixed ASCII channel. All integers and
lengths are little-endian. The first four digest bytes are an unsigned
little-endian value divided by 2^32; conversion to float is capped below 1.
Allowed keys are 1–128 ASCII letters/digits or `._-`.

Canonical deposits use `Battlefield.Metadata.Key` and their authored resource key,
resolved through the existing ResourceEntities/definition correspondence. Their
shape is independent of entity allocation order. The default presentation Salt
is 2026 and consumes no simulation RNG values. Standalone/reference deposits use
`entity` and `entity.<index>.<generation>`; this is stable for the full entity
identity, not a promise to preserve visual layout when a different loader changes
entity identities. All cache entries use the complete EntityId. A new registry
clears the cache, and hidden/removed/recycled parents lose retained field entries.

Channels are `field-count`, `field-vein`, `field-radius`, `field-angle`,
`field-stretch`, `form`, `size`, `rotation` and `tint`. State, remaining amount,
camera, LOD, quality, frame time, viewer and dictionary iteration order do not
participate. Each lower cluster budget retains a spatial prefix of the same
layout. Golden-angle placement with a seeded geological direction alternates
compact cores and irregular outer anchors, with mixed sizes and anisotropy.
Footprints derive from the actual deposit AABB, never from quantity or richness.

| Bound | Policy |
| --- | --- |
| Per deposit | 6–24 proposed clusters, varying with area and seed, before global admission and terrain rejection |
| Per snapshot | At most 4096 cluster parts across viewer-admitted deposits |
| Fair quota | `min(24, floor(4096 / admittedDepositCount))` per eligible parent |
| Cache | At most 4096 active deposit entries; total retained layout parts at most 4096 after each capture |
| Grounding | At most nine height queries per proposed cluster; unchanged cached fields do not resample |
| Overflow | More than 4096 eligible parents use existing canonical family roots, retaining every resource family |

Admission is independent of camera/frustum. A change in viewer-authorized parent
count can change the quota and thus detail count, but never rerolls existing
prefix anchors. No existing renderer, descriptor, residency, instance-upload or
shadow limit is increased. Existing shadow root admission charges all emitted
parts to the original root. Distinct variants may increase mesh batches; no FPS,
GPU residency or draw-count improvement is claimed without measurement.

## Terrain and extraction adaptation

Five initial height probes fit the local horizon. Gentle slopes tilt the cluster
to the fitted plane; banks with fitted normal Y below 0.85 use smaller upright
crust. Four final base-corner probes sink the base just into the terrain, keeping
sampled footpoints below the surface. Missing or invalid samples reject that
cluster instead of fabricating a zero height. A complete lack of valid clusters
retains the previous canonical family presentation. This bounded sampling is not
continuous mesh/heightfield intersection: native review must check sharp terrain
features between sample points and unusually steep banks.

The full rotated normalized source cube, including tilted height, is fitted
within the authoritative X/Z AABB. Field geometry cannot visually enlarge the
extractable footprint. Vertical render bounds may extend beyond gameplay bounds.
Ground anchors and topology are cached independently of LOD and camera motion.
The cache assumes the existing immutable scenario terrain; replacing terrain
requires recreating the extractor/composer.

Remaining fraction adapts a copy of that fixed topology. Untouched fields retain
full bodies. As the amount falls, a deterministic ordinal extraction front lowers
and removes upper bodies. Every fourth anchor retains low mineral crust, including
at zero remaining amount. Height reductions preserve the original ground pivot.
This produces local thinning and worked remnants without adding decals or fluid
effects. State metadata still comes from the existing extractor policy; a newly
active extractor with unchanged amount retains the full topology and existing
Active tint until the authoritative amount actually falls.

This is an explicit additive field protocol, extending the earlier
[visual-language contract](ResourceDepositVisualLanguage.md): `resource-visual/1`
describes topology-preserving single clusters and uniform variation;
`resource-field/1` supplies seeded field topology, anisotropic cluster transforms
and remaining-fraction depletion. It does not silently redefine the old schema.
It reuses its four morphology keys and weights per family, 25/50/25 size weights,
family tint endpoints, material IDs and strategic symbols.

## Assets, culling and picking

Field binding chooses the fixed `mesh.world.resource.<family>.<layout>.<size>`
High/Reduced pair only when both manifest mesh records reference exactly the
correct family material and that material exists. Otherwise each part retains
the canonical family mesh pair. The renderer also falls back to canonical High
if a requested part cannot resolve. An unavailable canonical mesh follows the
controlled missing-3D path, without substituting another resource family.
Manifest reading is metadata-only; CPU/GPU asset preparation remains with its
existing owners. No source file is loaded at runtime. Optional LOD2 stays unused.

The existing screen-space High/Reduced policy and per-entity/ordinal hysteresis
select detail for each cluster. World and shadow passes consume the same copied
field. Materials, mesh buffers and batch keys remain shared. Family-relative tint
is applied before the existing state tint, preserving the common depleted gray.
Visible/LOD diagnostics continue counting roots (using the first visible field
part for its LOD summary); submission/upload metrics count actual emitted parts.

`PresentationBounds` still resolves the original selection/inspection root.
Separate render bounds union the complete field and legacy root envelope; sphere
admission conservatively encloses them for spatial/world/shadow culling. Child
transforms enter rendering only, never snapshot identity correspondence or
selection loops. The snapshot still contains one instance per deposit, so
clicking the field remains one inspection action and no child can receive orders.

The four existing mineral reference scenes now include field metadata, retaining
their family/state/root counts and extractor alignment. Their fingerprint schema
advances to 2 because the copied visual data changed. No golden image or accepted
performance baseline was replaced.

## Prepared checks — NOT EXECUTED

`ResourceDepositFieldCompositionTests` prepares checks for reproducible keys/salt,
prefix budgets, all four families, slope/base contact, full transformed bounds,
depletion, immutable retained snapshots, unchanged quantities/roots, 100/1000
parents, overflow fallback, recycled generations, viewer admission before terrain
sampling, unknown keys/resources, missing terrain and complete LOD/material pairs.
`ResourceDepositFieldRenderingTests` prepares shared-runtime-mesh batching and
unchanged root/inspection counts with a fake graphics device. These are source
assertions awaiting execution, not passing results or native evidence.

After synchronizing and compiling through the canonical commands in
[Development](Development.md), the user can run:

```powershell
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.ResourceDepositFieldCompositionTests ForgeLine.Presentation.Tests.ResourceDepositFieldRenderingTests
```

Also run the existing solution, authoritative-state, resource extraction/inventory,
world inspection, visibility, reference-scene and render submission regressions.
Required PR CI remains `.github/workflows/ci.yml` (`build-test`), with existing
path-filtered mineral/frame/render workflows unchanged. No manual CI dispatch is
part of this work; automatically started PR CI must be reported separately.

Native acceptance remains OPEN on Windows x64/D3D12: compare
`ResourceDepositsGallery`, `ResourceDepositSingle`, `ResourceDeposits100` and
`ResourceDeposits1000` at Close/Normal/Strategic, 1080p/4K, all terrain slots and
quality settings. Review all three states, four family silhouettes in grayscale,
field rims and extractor overlap, gentle/steep terrain grounding, LOD transitions,
one-action inspection, shadow admission and missing-variant fallback. Measure
actual CPU capture time, frame time, FPS, GPU/residency and batch counts against
the same baseline snapshot/settings, especially when the 4096-part quota changes.

**No build, test, manual CI run, native capture or GPU/performance qualification
was executed during implementation.** Visual quality, terrain behavior between
probes, native shadow correctness and performance require the user's manual
verification. Build/test success and human art acceptance are not inferred from
static code inspection.
