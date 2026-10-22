# Road, bridge and crossing Blender sources

Current master includes world-prop PR #283 at
`34c69dd97db417d1982fd7bf75cb1baa197082f6` (0.1.438). This branch uses
0.1.439 and reuses its six shared transition definitions and native masters.
Active authoring here covers `roads.blend` and `bridge.blend` with twelve
canonical GLBs; the redundant transition master/exports were removed. The
historical inventory and paired measurements below predate that integration.
See the detailed qualification for source boundaries.

The measured Directorate infrastructure geometry was retained in three editable,
packed-PBR Blender masters: `roads.blend`, `bridge.blend` and `transitions.blend`
under `assets/source/infrastructure/directorate/authoring/`. The three families
cover nine road meshes, three bridge states, and High/Reduced bridge aprons,
roadside drains and loading aprons. The existing shared collision proxy remains
unchanged. Eighteen canonical GLBs are bound through the original stable asset
definitions. Sixteen meshes retain every original source channel and index.
The two curve modules replace disconnected slabs with continuous closed ribbons
inside the exact original bounds and atlas rectangle. The short curve uses
20 triangles/36 vertices (previously 24/48); the long uses 36/60 (previously
36/72). No existing collision proxy changes. The original glTF recipes remain available as reconstruction seeds.

The source audit used master `a524b0a8164390abe1e9cb38170e690f46147574`,
version 0.1.436. No related open PR or live remote branch existed. Integrated
infrastructure predecessors include PRs #49, #111, #181 and #185. Existing
rendering optimization and qualification PRs #258, #260, #263–#268 are integrated,
but their full physical, lifecycle and human acceptance gates remain open.
Source preservation does not establish production performance acceptance.

Final comparison preparation rebased onto master
`8e486734383075c39a914b71adf34d8c3311b7be` (0.1.437), adopting the
integrated terrain sources and compact industrial profile from PR #284.
The infrastructure PR advances exactly one PATCH to 0.1.438. Both final native
comparison sides use that same terrain baseline. Final source integration also
includes vegetation PR #281 at master
`a2a88f06f24b6a4351b7f1f3c7a56819f0527d5d` (still 0.1.437). Existing
vegetation scene IDs and factory routing are preserved, with the fifteen
infrastructure scenes appended. The paired timing evidence remains attached to
its measured terrain baseline; it is not relabelled as vegetation-base timing.

Each state occupies its own named collection at the canonical origin. Show the
desired collection to edit it. Engine coordinates are +Y up/+Z forward, Blender
coordinates +Z up/-Y forward, with meter units. Runtime instance scales remain
separate from the normalized source meshes. Packed preview materials use the
existing Base Color, tangent Normal and ORM maps. No material, texture, atlas,
sampler, compiler or runtime resource budget is increased.

Recreate a master from reviewed canonical sources with:

```powershell
./tools/blender/Invoke-Blender.ps1 -Action Check
$fixture = (Resolve-Path tools/blender/fixtures/infrastructure.json).Path
./tools/blender/Invoke-Blender.ps1 -Action Script -Script tools/blender/infrastructure.py -InputRoot assets/source -OutputRoot artifacts/infrastructure-author/roads -ScriptArguments @('--action','Author','--family','roads','--fixtures',$fixture)
./tools/blender/Test-BlenderInfrastructure.ps1 -OutputRoot artifacts/infrastructure-validation
python -m unittest discover -s tools/blender/tests -p 'test_infrastructure_contract.py' -v
```

Use `bridge` for the other active master; shared transitions use the integrated
`build/world-props` authoring and qualification. Export with `--action Export`
and `--blend-file` pointing to the saved master. Supported vertex edits stay
inside the original bounds and a normalized 0.01 displacement; collision,
topology, normals, UVs, materials, pivots and transforms remain locked. Physical
displacement depends on the documented runtime instance scale. Authoritative
surface or source changes require explicit master regeneration. Checkout CRLF/LF
differences are normalized only for identity comparison, never art changes.

The native qualifier reopens each master twice, compares source and compiled
bytes, validates without sources, rejects invalid edits, and proves a saved
supported edit changes exactly its intended compiled mesh. The opt-in
`Infrastructure Blender masters` workflow requires a provisioned pinned Blender
Windows runner; portable tests do not substitute for this native execution.

Crossing render transforms now follow the current authoritative road endpoints.
The North Bridge deck, two existing apron meshes and its west/east approaches
share the actual travel axis. Approach nodes and ramp endpoints are shared;
17 samples per tile across seven lateral positions lift the drawn floor above
terrain. Existing solid slabs and gravel shoulders extend below the terrain,
with unchanged widths, source bounds and mesh/texture inventories.
South Ford uses the existing road surface and two
ramps. The existing authored road-wear and crack overlays follow the same bridge
rotation and stay inside the rails. The drawn road-wear length fits the 42m
deck; its authoritative 72m map value remains unchanged. The intact opaque deck occludes terrain wear, so this covered ground layer
is omitted; the crack overlay remains on the concrete. Both overlays return
to terrain grounding when the authoritative bridge is nonoperational. Their
map records, materials and source quad remain unchanged.
The 12m lane and paired 2.4m shoulders are retained; 234 static presentation
instances cover the approaches. These identities do not consume simulation
entities. Only drawn cargo/supply poses receive the operational crossing plane;
original ECS transforms, movement, wheel travel, graph keys, navigation,
traversability and infrastructure state rules remain authoritative.

`CrossingVisualLayoutTests` checks edge alignment, unchanged map records,
millimetre joins, physical contact, lane widths and terrain clearance. The native
review driver is `build/Invoke-InfrastructureReview.ps1`: 145 hardware cases cover
Low/Medium/High, 120/420/1000m, 1080p/4K, bright/dark/wet, four junctions, real
moving cargo approach/deck/exit samples on both crossings, 1/100/1000 instances,
30m detail views, camera sweeps and ultrawide. Eight warmups, 32 timing samples
and two repeated images are used per case. The native contract permits 640x360
only for software rendering; the rejected hardware request is not counted.

Native image comparisons, final full-suite/headless parity and installed package
evidence are recorded separately. Repeatable static pixels and sampled real
movement do not establish live motion, sustained FPS, enclosing-frame or human
art acceptance. No merge, tag, release or golden approval is part of this work.

Detailed measurements, retained original images, source identity and integration
checks are recorded in [Infrastructure qualification](qualification/infrastructure/README.md).
