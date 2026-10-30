# Resource deposit visual language

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Status and ownership](#status-and-ownership)
- [Family grammar](#family-grammar)
  - [Morphologies and occurrence](#morphologies-and-occurrence)
  - [Fragment and field composition](#fragment-and-field-composition)
- [Independent variation layers](#independent-variation-layers)
  - [State and depletion](#state-and-depletion)
- [Interchange contract](#interchange-contract)
  - [Persistent identity and binding](#persistent-identity-and-binding)
  - [Copied presentation selection record](#copied-presentation-selection-record)
  - [Deterministic seed derivation](#deterministic-seed-derivation)
  - [Bounds, LOD and controlled fallback](#bounds-lod-and-controlled-fallback)
- [Manual acceptance matrix — NOT EXECUTED](#manual-acceptance-matrix--not-executed)
  - [Negative cases and prepared assertions](#negative-cases-and-prepared-assertions)
  - [Evidence and integration gates](#evidence-and-integration-gates)

</details>

## Status and ownership

Normative art and interchange specification, schema `resource-visual/1`.
Implementation baseline: `master` at
`b726ff9c00b7dc74cf285219fa58440acf663e16`, product version 0.1.454.
This document defines requirements for subsequent asset, field-composition and
rendering integration. It does not claim that those requirements are implemented
or visually accepted. Human art acceptance and all execution remain pending.

The integrated baseline has four stable family bindings, High/Reduced selection,
copied Untouched/Active/Depleted states and tint changes. PR #303 adds grounded
cluster geometry and optional variant assets; runtime still selects canonical
family bindings. The detailed grammar below is the art acceptance target, not a
claim that every new mesh has passed it. No simulation, spawn, renderer, production
mesh, texture, material or save/replay format changes belong to this document.

Reuse the layout keys authored in `art/resource-cluster-families` (geometry
contract commit `8d8e3526`), integrated through PR #303 during this work. See
[cluster geometry](ResourceDepositClusterGeometry.md). This documentation branch
contains that current master; it contributes no additional assets. Native
masters, 3 size classes and optional LOD2 still require separate qualification
and dispatch. Never silently rename their keys. Existing runtime IDs remain
authoritative when optional bindings are unavailable. The seed and probability
contract here extends that work.

Asset work owns editable meshes, shared PBR surfaces and fixed variant bindings.
Terrain work owns bounded visual layouts within already authorized deposit
footprints. Presentation/rendering work owns copied-data selection, runtime
resolution, LOD, conservative render bounds and batching. Economy/World retain
quantity, extraction, placement, navigation, collision and intelligence authority.

## Family grammar

Every cluster has a grounded host mass, several medium fragments and small
fracture debris. Large quantities read as geological fields of multiple bounded
clusters rather than an enlarged single object. Cosmetic density is never a
quantity gauge. An occupied extractor may obscure a core but must leave a readable
family rim. Shapes and surfaces are original FORGELINE work; use only general RTS
principles of rapid recognition, material plausibility and spatial variation.

| Family | Required silhouette and fracture | Density and surface direction | Depletion cue retained in silhouette |
| --- | --- | --- | --- |
| Ferrous Ore | Heavy horizontal beds, offset seams, angular blocky lumps; stepped shear and compression fractures | Dense interlocking host, dark iron faces and muted oxide breaks; edge polishing surrounded by dusty rough recesses | Stepped host and blunt fractured rim; no crater or new obstacle |
| Silicates | Broad basal plates, oblique cleavage planes and fans of inclined facets; two dominant cleavage directions | Medium dense slabs with separated readable faces; pale mineral faces against dusty darker seams; occasional glasslike face, no transparent spike forest | Broad split base and surviving inclined planes |
| Volatiles | Low closed nodules, capped lenses and laminated pockets; rounded faceted outer shell with short angular breaks | Dense sealed host with shallow seams; slate/teal mineral crust, chalky aging and subdued satin breaks | Closed dry lens/rind, no puddle, hose, gas plume or implied free transfer |
| Rare Elements | Asymmetric rock matrix with sparse oblique inclusions, split lobes and irregular short shards | Open gaps between host lobes, inclusions embedded in substantial matrix; dark neutral host and restrained mineral glints | Asymmetric matrix and embedded inclusion stubs; no neon tower or floating crystal |

Use large structural fractures before fine noise. Weathering follows exposed
edges and sheltered seams, not a uniform dirt overlay. Rare inclusions occupy
10–25% of visible mineral area; host remains the majority. Silicates always have
a basal span at least as wide as their highest individual fragment. Volatiles
must remain sealed mineral deposits even beside real industrial fluid equipment.
None of these visual cues establishes new extraction or transport behavior.

### Morphologies and occurrence

Rows are ordered, immutable schema-1 categories. Weights are integer percentages
per eligible cluster within that family, independent of deposit richness and
current state. A cumulative bucket in `[0,99]` selects a row; these are statistical
targets, not exact quotas or a promise of no repeats in a small field.

| Family | Layout key | Weight | Composition and negative space |
| --- | --- | ---: | --- |
| Ferrous Ore | `layered_shelf` | 40 | Three offset bed steps with a broken low apron |
| Ferrous Ore | `faulted_ridge` | 25 | Interrupted blunt ridge with two unequal shoulders |
| Ferrous Ore | `breccia_fan` | 20 | Angular fragments spread from one heavy host into a low fan |
| Ferrous Ore | `offset_seam` | 15 | Two displaced seam lobes with a narrow diagonal gap |
| Silicates | `cleaved_plate` | 35 | Broad split plate with subordinate inclined facets |
| Silicates | `crystal_fan` | 30 | Grounded fan of unequal leaning faces, no radial spike crown |
| Silicates | `stepped_bed` | 20 | Terraced cleavage slabs with short transverse breaks |
| Silicates | `split_rosette` | 15 | Flattened asymmetric rosette with an open off-center cleft |
| Volatiles | `sealed_nodule` | 40 | Unequal closed nodules touching a shared low bed |
| Volatiles | `fractured_lens` | 25 | Capped elongated lens with shallow interrupted crust seams |
| Volatiles | `laminated_pocket` | 20 | Low nested layers around a sealed central pocket |
| Volatiles | `weathered_saddle` | 15 | Two capped shoulders linked by a worn lower saddle |
| Rare Elements | `inclusion_reef` | 35 | Irregular matrix ridge crossed by sparse oblique inclusions |
| Rare Elements | `split_matrix` | 30 | Unequal host lobes exposing short embedded mineral faces |
| Rare Elements | `radial_inclusions` | 20 | Off-center host with differently inclined short inclusions |
| Rare Elements | `faulted_pocket` | 15 | Broken matrix pocket with one dominant and several minor inclusions |

Each of the 16 morphologies has `small`, `medium`, `large` composition variants:
48 fixed High meshes and 48 paired Reduced meshes. The geometry branch also
authors 48 optional LOD2 meshes. Those are asset availability, not an extension
of the integrated `WorldAssetLod` enum. Do not introduce an implicit third runtime
LOD or additional resource states through naming conventions.

### Fragment and field composition

Fragment classes describe parts inside one cluster, independently of the whole
cluster's size key. Let D be that cluster's occupied horizontal diameter.

| Fragment class | Individual horizontal span | Small cluster target count | Medium target count | Large target count |
| --- | --- | ---: | ---: | ---: |
| Host / large | 0.35–0.60 D | 2 | 2–3 | 3–4 |
| Medium | 0.15–0.30 D | 3–4 | 4–6 | 6–8 |
| Small | 0.05–0.12 D | 3–5 | 5–8 | 8–12 |

Counts are authoring targets, not independent runtime entities or required draw
calls. Distinct joined fracture faces may represent multiple fragments at Reduced.
Geometry already authored in the integrated cluster set must be reviewed against these
targets; its existence is not evidence that every target is met. Do not exceed
existing geometry or GPU budgets to meet counts; preserve primary masses first.
Silhouette review may justify a documented per-layout count exception while
retaining multiple scales and the family grammar.

Plan-view occupied mineral coverage inside a cluster's convex footprint targets
70–85% for Ferrous, 55–75% for Silicates, 70–90% for Volatiles and 45–65% for Rare.
These are art-review targets, not collision fill or terrain coverage. Cluster
size probabilities are 25/50/25% in the ordered small/medium/large categories.
Size means different composition, never a multiplication by authoritative amount.

Future field composition uses a stable finite list of cluster anchors wholly
inside an existing visual placement envelope. Anchor order and count are authored
presentation topology; this contract does not invent a spawn-count algorithm.
Small fragments may overlap their host; separate clusters retain irregular gaps.
Avoid a grid, equal-radius rings, evenly spaced peaks and identical aligned seams.
The Terrain package must publish anchor keys/transforms and preserve the existing
admission/instance limits before multi-cluster fields can be enabled. If those
limits cannot be met, retain the existing single family cluster, not a megamesh.

## Independent variation layers

| Layer | Frozen selection / allowed variation | Must not affect |
| --- | --- | --- |
| Form | Family + layout + size selects one fixed asset binding | ResourceId, quantity or placement |
| Cluster layout | Stable authored anchor list and geological field orientation | Navigation and extraction reach |
| Rotation / scale | Per-anchor yaw and bounded positive uniform scale | Authoritative transform or picking bounds |
| Tint | One bounded family-relative RGB multiplier per anchor | Family identity, alpha, state or visibility |
| UV orientation | Authored alternatives only, same atlas rectangle and tangent basis | Runtime batch key or atlas region |
| State | Copied authoritative Untouched/Active/Depleted | Seed, form, anchors or simulation |
| Topology / LOD | Paired High/Reduced for the same form and anchor | Seed, state, center, ground or family |

Yaw is `2*pi*u(rotation)`. Uniform scale is `0.90 + 0.10*u(scale)` of the admitted
visual envelope; negative scale, mirroring, pitch/roll and animated jitter are
excluded. Terrain grounding belongs to the anchor transform before this scale;
rotate/scale about its ground pivot and convert to the centered mesh pivot once.
Do not apply these transforms on top of a separate renderer randomization pass.

Tint uses one scalar u per anchor and linear interpolation of each RGB component
between the following endpoints. Components are multipliers in linear color;
alpha stays 1. These are future instance tint ranges, not new material assets.

| Family | RGB multiplier minimum | RGB multiplier maximum | Surface roughness art target: host / fresh face |
| --- | --- | --- | --- |
| Ferrous Ore | (0.94, 0.92, 0.90) | (1.04, 1.02, 1.00) | 0.72–0.90 / 0.42–0.60 |
| Silicates | (0.94, 0.95, 0.94) | (1.03, 1.04, 1.02) | 0.65–0.85 / 0.30–0.50 |
| Volatiles | (0.93, 0.96, 0.96) | (1.01, 1.03, 1.04) | 0.70–0.90 / 0.48–0.65 |
| Rare Elements | (0.95, 0.93, 0.96) | (1.03, 1.01, 1.04) | 0.70–0.88 / 0.28–0.48 |

Roughness targets guide future shared-surface authoring and are not claims about
current texture values. Retain the current atlas/normal reuse and ORM packing.
No emission, liquid transparency or saturated palette substitution. Any UV turn
must be baked within the permitted mineral atlas rectangle; schema 1 exposes
only `authored` UV mode, so runtime does not select UV offsets or shader variants.

### State and depletion

Use `PresentationExtractor`'s existing authoritative mapping: untouched initial
amount, worked/active deposit, zero remaining amount. `None` is metadata absence,
not an extra deposit lifecycle state. Render code never infers a state from color,
elapsed time, worker count or camera distance.

Schema 1 keeps topology across states. Untouched has intact weathered host faces;
Active reinforces existing fracture readability through the existing state tint;
Depleted retains a muted, identifiable mineral rim. No fragments disappear, no
new crater or traversal hole appears, and no renewed amount is implied. More
elaborate exhaustion topology requires a separately qualified additive contract.

Tint composition is family material/fallback factor, then bounded instance tint,
then the existing state policy. Active uses the current RGB x1.18 capped at 1;
Depleted replaces RGB with (0.30, 0.31, 0.32), alpha 1. Therefore depleted family
recognition must survive identical tint. This document changes no current code.

## Interchange contract

### Persistent identity and binding

Resource numbers come from `ResourceIds`, not the order in an art table.

| ResourceId | Economy key | WorldVisualId | Family suffix |
| ---: | --- | --- | --- |
| 1 | `resource.ferrous_ore` | `ResourceFerrousOre` | `ferrous_ore` |
| 3 | `resource.silicates` | `ResourceSilicates` | `silicates` |
| 2 | `resource.volatiles` | `ResourceVolatiles` | `volatiles` |
| 8 | `resource.rare_elements` | `ResourceRareElements` | `rare_elements` |

For suffix F retain `mesh.world.resource.F`, `mesh.world.resource.F_lod1`,
`material.world.resource.F` and `material.world.symbol.resource.F` exactly.
All sizes/layouts of F bind that same family material and strategic symbol.
Variant identity is the tuple `(schemaVersion=1, F, layout, size)` with canonical
key `resource-visual/1/F/layout/size`. State, LOD and seed are separate fields.

The optional asset set uses `mesh.world.resource.F.layout.size` and that ID plus
`_lod1` (or reserved `_lod2`). Non-rendered geometry proxies append `.collision`
to the unsuffixed mesh ID; they never register simulation obstacles. The default
alias is the first morphology in the family table, size `medium`. Registry
bindings must be explicit, unique, ordinal/case-sensitive and compiled offline;
runtime must not assemble arbitrary paths or load authoring sources. Missing
variants never change the persisted ResourceId or original aliases.

### Copied presentation selection record

The future integration record contains these semantic fields; no current public
API or serialization schema is introduced by this documentation:

| Field | Type and rule |
| --- | --- |
| `schemaVersion` | UInt32, exactly 1; unknown version uses family fallback |
| `resourceId` | Existing UInt32 ResourceId; allowlisted four-family mapping |
| `mapKey`, `depositKey` | Immutable canonical authored ASCII identifiers, case-sensitive, 1–128 bytes each |
| `visualSeed` | UInt64 authored presentation seed, separate from the simulation RNG stream |
| `anchorOrdinal` | UInt32 index in immutable authored anchor list |
| `layout`, `size` | Selected keys from the tables, not free-form file names |
| `state` | Existing copied resource presentation state |
| `anchorTransform` | Finite admitted ground anchor; no writes to simulation |
| `binding`, `renderBounds` | Resolved compiled binding and conservative bounds for all admitted LODs |

Authored keys use `[A-Za-z0-9._-]` only and must be unique in their scope. A field
with no stable map/deposit key stays on the canonical family fallback until its
owner supplies one. Never substitute transient entity index, process hash, array
iteration order, position rounding or simulation RNG output. Runtime entity
generation/session still governs snapshot lifetime and stale-data rejection;
it is not a persistent geological identity. Save/load and replay reconstruct
these optional visuals from unchanged authored metadata. No new save field is
required here; legacy recordings without keys retain canonical family rendering.

### Deterministic seed derivation

Use SHA-256, with exact byte encoding below. This is a presentation-only protocol,
not a new random number source for simulation. Derive independent channels so
adding future cosmetic channels cannot shift existing selections.

```text
prefix = ASCII("FORGELINE.resource-visual/1") + byte(0)
S = prefix
  + LE64(visualSeed)
  + LE32(resourceId)
  + LE32(byteLength(mapKey)) + ASCII(mapKey)
  + LE32(byteLength(depositKey)) + ASCII(depositKey)
  + LE32(anchorOrdinal)
H(channel) = SHA256(S + LE32(byteLength(channel)) + ASCII(channel))
x(channel) = unsigned little-endian UInt32 from H(channel)[0..3]
u(channel) = x(channel) / 4294967296.0  // binary64, [0,1)
bucket(channel) = floor(x(channel) * 100 / 4294967296)
```

Calculate the bucket numerator with UInt64 to avoid overflow. Channels are exactly
`form`, `size`, `rotation`, `scale`, `tint`. Form uses cumulative row weights;
size uses cut points 25 and 75. Yaw, scale and tint use the formulae above. No
state, LOD, remaining amount, current tick, viewer, quality or screen resolution
is hashed. Never reroll for adjacency: deterministic shared anchor topology plus
independent choices provide variation without order-dependent neighbor queries.
Identical neighbors can occur; prominent repetition is an art/layout review
failure to fix in authored topology or form design, not by reseeding at runtime.

### Bounds, LOD and controlled fallback

The integrated cluster convention is engine Y-up, centered source pivot (0,0,0),
ground Y=-0.5 and normalized source envelope [-0.5,0.5] on every axis. Small,
medium and large occupied spans are at most 0.68, 0.86 and 1.00. All LODs share
one normalization; do not normalize each mesh independently. Render bounds must
union every LOD's actual extents, then transform all eight corners with the final
anchor transform and ground-pivot conversion. Include any existing visual-height
adjustment before world/shadow culling; never use the logical deposit height as
the mineral silhouette's culling height.

`ResourceDepositVisualTransform` currently enforces minimum heights 16/28/8/32
for Ferrous/Silicates/Volatiles/Rare. Those values are retained. Review grounded
plates and Rare matrix under these actual transforms; a later adjustment belongs
to presentation integration and requires its own culling/shadow qualification.
The source cube is a conservative authoring envelope, not new gameplay bounds.
Picking remains on the existing inspectable deposit identity and bounds; fragments
never become commandable entities or widen extraction range.

Keep the current projected-size High/Reduced policy (65 reference pixels, 12%
hysteresis) and its reference-resolution normalization. The catalog's 260m helper
is compatibility metadata, not a replacement for the production screen policy.
Required silhouette landmarks survive both LODs: Ferrous bed steps, Silicates
basal plate and inclined cleavage, Volatiles closed low caps, Rare asymmetric
matrix and at least two unequal inclusion directions. Preserve ground contact,
dominant gaps, material and centroid at transition. Optional LOD2 must pass the
same review before selection support is enabled; a symbol is not a mesh LOD.

Resolution order is deterministic: complete requested variant High/Reduced pair
with matching material; otherwise the canonical family pair. Do not mix a variant
High with a canonical Reduced. If only canonical High is present, retain High at
Reduced distance under existing budgets. If that cannot be admitted or the family
mesh is missing, omit the 3D visual using the existing controlled-missing path,
retain authorized inspection/symbol feedback, and report the missing binding.
Use existing family tint only where the current material fallback supports it;
never substitute another family's material or introduce a generic box. Invalid
keys, nonfinite transforms, unknown versions and unresolved bindings take the
controlled fallback before resource allocation. Unknown ResourceIds follow the
existing unsupported-resource policy; do not coerce them into a known family.

Only already viewer-authorized copied instances participate. World and shadow
submission use the same resolved transform, LOD and bounds. Share compiled meshes
and materials by stable binding, put transforms/tints in existing instance data,
and retain current resource, descriptor, instance and residency limits. Multi-form
fields may add distinct mesh batches; measure that cost instead of claiming
unchanged draw counts. No per-deposit material, generated megamesh, GPU readback
or hidden-contact asset resolution is permitted by this contract.

## Manual acceptance matrix — NOT EXECUTED

Every tuple in the following Cartesian product needs an explicit result. Scene
captures may cover multiple tuples, but unexercised combinations remain OPEN.

| Dimension | Required values |
| --- | --- |
| Family | Ferrous Ore, Silicates, Volatiles, Rare Elements |
| State | Untouched, Active, Depleted |
| Terrain | All eight stable terrain slots: grass_ground, dirt, mud, rock, gravel, industrial_ground, concrete, scorched |
| Zoom | Close, Normal tactical, Strategic, using existing camera presets |
| Resolution | 1920x1080 and 3840x2160 native captures; no rescaled substitutes |
| LOD | Forced High, forced Reduced, natural transition in both directions |
| Form | Every family layout × small/medium/large |

The base family/state/terrain/zoom/resolution/LOD matrix has 1,728 cells; expanded
form coverage has 20,736 cells. Each includes representative light/dark exposure,
dry/wet material appearance where supported, and extractor overlap. Wet is an
appearance check, not a new terrain slot or material system. Quality Low/Medium/High
is an additional sweep for Gallery and 1000-instance scenes at Normal/Strategic;
optional LOD2 remains an additional blocked row until dispatch is implemented.

At Close, judge plausible fractures, contact, seams, atlas continuity, normal
orientation, and weathering without requiring microscopic noise. At Normal,
all four families must be distinguishable in grayscale and in uniform neutral
tint without relying on labels or UI symbols. At 4K keep the same framing and
reference-pixel LOD policy; inspect native pixels for shimmer and texture scale.
At Strategic, distinguish admitted mesh silhouettes while they remain readable;
once too small, use existing authorized family symbols and inspection. Do not
force tiny geometry to remain visible, raise LOD range or expose hidden deposits.

For Untouched/Active pairs require stable layout and a readable existing state
cue. For Depleted require a muted recognizable family with no renewed-richness
appearance. Review LOD switches while panning in both directions: no identity swap,
floating base, moved seam, changed state, shadow loss or repeat-pattern popping.

Use existing `ResourceDepositsGallery` (12 family/state examples),
`ResourceDepositSingle` (one default Ferrous example), `ResourceDeposits100` and
`ResourceDeposits1000`. Single does not cover the other families automatically.
Current scenes exercise canonical bindings and do not select every optional
variant, terrain, or forced LOD. Expanded coverage requires a later copied-data
review fixture owned by presentation integration; until then mark those cells
BLOCKED, never inferred PASS. Compare the same reference snapshot/camera/settings
before and after asset integration; keep original captures and fingerprints.

### Negative cases and prepared assertions

These are assertion designs, not executed tests. The companion
[contract fixture](fixtures/resource-deposit-visual-language.v1.json) records
weights, mappings and negative-case expectations as inert reference data.
No runtime loader or automatic CI consumer is claimed.

| Case | Required assertion / review result |
| --- | --- |
| Persistent mapping | IDs 1/3/2/8, economy keys, four original mesh/material/symbol bindings unchanged; numeric 2 is Volatiles |
| Variant schema | Exactly four ordered layouts per family, weights sum to 100, sizes 25/50/25; keys unique, ordinal and allowlisted |
| Bucket edges | For form weights 40/25/20/15: 0–39/40–64/65–84/85–99; size 0–24/25–74/75–99 |
| Seed stability | Same bytes produce identical selection across runs, enumeration order, thread count, save/load and replay |
| Independent channels | Changing state, LOD, tick, quality or viewer leaves form/size/yaw/scale/tint unchanged; introducing a new channel leaves old hashes unchanged |
| Seed identity | Different keys/seeds/anchor ordinals are independently hashed, without assuming collision-free output or a different selected form every time |
| Unsupported input | Missing key, version 2, layout typo, wrong case, unknown resource, invalid size, nonfinite transform follow the documented controlled fallback |
| Binding failure | Missing variant Reduced rejects the whole variant pair; missing material never borrows a different family's material; missing canonical mesh stays controlled |
| State validity | Exactly three deposit states; None is absence; zero quantity uses authoritative Depleted; render seed never selects state |
| Family silhouette | Distinguish all four with identical gray tint and reduced LOD; reject shared spike forests or identical rotated masses |
| Exhaustion | Zero amount does not alter navigation, collision, extraction reach, inspector identity or field topology |
| Simulation invariance | Equivalent headless commands/seeds yield the same resource amounts, inventories, navigation and replay outcomes with visuals enabled/disabled |
| Bounds | All High/Reduced vertices after maximum scale/yaw/height adjustment fit render/shadow bounds; logical picking bounds stay unchanged |
| Visibility and reuse | Hidden contacts produce no extra field parts/shadows/symbols; recycled entity index cannot inherit previous cached metadata |
| Repetition and limits | 100/1000 scenes retain shared resources and existing limits; record actual batches/residency/timing, do not claim performance from mesh counts |
| UV and material | UVs stay within family atlas region; normal/ORM channels and tangent handedness survive; tint does not imply emission or fluid transfer |

Existing regression owners are Presentation's `ResourceDepositReferenceSceneTests`,
`ResourceDepositBlenderMasterTests`, `ResourceDepositMaterialCacheTests`,
`PresentationExtractionTests`, `WorldPresentationTests` and `SimpleInstanceRendererTests`,
plus Simulation extraction/inventory tests and Game visibility/map tests.
Use actual current test names when adding executable fixtures. No new headless
dependency on Presentation, Assets or Graphics is allowed.

### Evidence and integration gates

For each reviewed tuple record baseline/candidate commit, package/asset manifest
identity, snapshot fingerprint, seed/keys, selected form, terrain, state, zoom,
LOD, resolution, quality, capture path, reviewer and PASS/FAIL/OPEN/BLOCKED with
reason. Record real hardware and CPU/GPU/resource observations separately from
art approval. Do not replace visual goldens simply to clear a mismatch.

Manual build/test commands and native reference-capture procedures remain in
[Development](Development.md), [World asset presentation](WorldAssetPresentation.md)
and [Resource deposit masters](ResourceDepositBlenderMasters.md). After dependent
integration, the user runs restore, project-reference validation, Release build,
runtime asset build/pipeline checks and solution tests there. This document does
not run or report those commands.

Required PR CI is `.github/workflows/ci.yml` (`build-test`). The path-filtered
`resource-deposit-masters.yml` and `render-qualification.yml` provide their own
asset/evidence checks and optional native dispatches; documentation alone does
not activate their full matrices. Do not dispatch workflows during this work.
GitHub may automatically start PR CI; report its actual observed status separately
from local execution. CI passing never constitutes native or human art approval.

**NOT EXECUTED:** builds, tests, contract-fixture automation, manual CI dispatch,
Blender reopen/export qualification, native captures and GPU/performance review.
PR #303 has integrated the geometry sources. Remaining gates are asset art review,
stable field-anchor metadata,
presentation variant dispatch, expanded review fixtures, full native matrix,
human art acceptance and existing performance/resource acceptance. Documentation
can integrate independently without representing these gates as complete.
