# World prop Blender masters

## Scope and current review status

Starting integration base: master a524b0a8164390abe1e9cb38170e690f46147574,
version 0.1.436. The branch prepares exactly one PATCH to 0.1.437.
The ordered source audit is retained in world-props/Precheck.json.

All 22 existing world prop families are covered: rock, slab, spire, barrier,
modular barrier, concrete block, crate, drum, pallet, pipe section, elbow, utility
box, fence, service fence, industrial light/signage, rubble, scorched barrier,
burst pipe, loading gantry, loading apron, road edge and bridge apron. These last
three are already catalogued as props; infrastructure road/bridge sources remain
separately owned. Vegetation, mineral deposits, terrain and decals are excluded.

The current catalog already contains functional silhouettes, hollow pipes,
flanges, supported gantry bays, atlas UVs, explicit normals and High/Reduced LODs.
This work preserves that artwork and supplies the missing retained editable
Blender sources. It does not infer a new throughput, lamp, damage or cargo state
from decoration. The existing crate is one atlas-authored family; timber/metal
regions retain their existing material and UV identity rather than new gameplay
variants or duplicate stable IDs.

## Ownership and adoption

Native source belongs under assets/staged/world-props/<family>/. Each family
retains a strictly exportable rest master and a separate packed-PBR inspection
project. Reviewed GLBs bind the original 44 High/Reduced IDs under
assets/source/world/meshes/sources/blender/props/. Existing glTF recipes and
inputs remain available. Every definition field except its source path must
match the audited original-bindings.json.

The adapter under build/world-props uses the existing bounded static importer,
exporter, pinned Blender launcher and production PBR preview. Common Blender
modules, compiler and renderer contracts are unchanged. No authoring content
belongs in the runtime/player package. Source signatures normalize JSON/glTF
line endings while raw hashes still detect changes during processing.

Engine +Y up/+Z forward maps to Blender +Z up/-Y forward as (x,-z,y), in metres
at unit scale. Pivots, bounds, winding, vertices, triangle/index order, UV0,
normals, tangents, material slots, LOD distances and tooling collision references
retain the existing contracts. Original ForgeLine provenance and shared
Base Color (sRGB), Normal and ORM (linear) atlases are retained. No new texture,
material, transparent billboard, mesh section or shader feature is introduced.

## Gameplay and review fixtures

CentralDivideDressing, CentralDivideKitDressing, map definitions, world keys and
original transforms are untouched. PresentationOnly scenery remains public,
noninteractive and nonblocking; compiler collision proxies are tooling metadata
and never become navigation, construction, visibility or cover authority. No
new scenery ECS entities, construction footprint, resource clearance or road
placement logic is introduced.

EnvironmentKitYard and EnvironmentKitBridge retain actual authored placements.
IndustrialBase retains its existing production fixture. WorldPropsGallery adds
22 copied presentation instances, while WorldPropSingle, WorldProps100 and
WorldProps1000 provide bounded synthetic loads without allocating ECS entities.
They use existing map scales when available and the existing half-height ground
anchor. They are inspection scenes, not new map placements or gameplay presets.

The pre-existing bridge visual/logistics orientation mismatch described in
ModularEnvironmentKit.md remains an infrastructure review item. A static ground
anchor does not prove every corner contacts uneven terrain or that a moving
camera has no LOD popping; these require native review.

## Reproduction

Build Release first. Use fresh artifacts directories, the pinned Blender 5.2.2
launcher and a material-only cache containing the existing four shared records.

```powershell
python build/world-props/prepare-materials.py artifacts/prop-material-source
./build/Build-RuntimeAssets.ps1 -Mode build -NoBuild -SourceRoot artifacts/prop-material-source -RuntimeRoot artifacts/prop-material-runtime
./build/world-props/Qualify-Masters.ps1 -OutputRoot artifacts/prop-native -MaterialCacheRoot artifacts/prop-material-runtime -NativeBatch -RenderPreviews
python build/world-props/test_contract.py
python build/world-props/retained.py
```

The qualifier imports independently twice, reopens the first saved master, runs
six actual Blender mutation rejections against the visual root, compiles four
source variants per family and validates every catalog with an absent source
root. Repeated/reopened source and runtime bytes must match. Typed .NET tests
compare compiled geometry, generated tangents, material channels, sections and
metadata to the original baseline, including unchanged shared collision proxies.

World-prop CI verifies retained source hashes and the portable signature
regression on hosted Windows. Manual native qualification requires the existing
provisioned Windows/X64/blender runner; a skipped native job is not a pass.

Capture-Review.ps1 uses the real client executable, two full qualified runtimes
and a fresh output. Its 48 paired functional cases cover actual yard/bridge/base
views, all prop families and repeat loads, close/normal/strategic zooms,
Low/Medium/High, 1080p/4K/ultrawide and dark/wet settings. Three warmups, eight
samples and two repeated readbacks establish short functional observations only.
They cannot certify sustained FPS or replace controlled physical AB/BA runs.

## Acceptance boundaries

Render qualification PR #268 is integrated, but its physical paired timings,
complete live-frame/memory/timestamp observations, moving lifecycle and explicit
human visual approval remain separate open gates. No budget, threshold or
golden is relaxed. No production promotion, merge, tag or release is authorized
by a saved master, passing schema or repeatable still image.

Current-source build/test/compiler/native/package results are recorded as they
complete; unexecuted work remains pending.
