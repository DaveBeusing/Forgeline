# Battlefield decal bakes

## Integration audit

Audited remote integration/default branch `master` at
`a524b0a8164390abe1e9cb38170e690f46147574`, version 0.1.436. No related open PR
or unfinished decal/render/surface/bake remote branch was found. Existing
production art (#139), metallic/roughness lighting (#188), bounded live decals
(#191) and render qualification (#268) are integrated and reused. Their changed
files and issue/review/inline comments were inspected; no comments/reviews were
present on #139/#191/#268. #191's final historical main CI failed, while its
focused checks passed. Current exact-master main CI, navigation and retained
Scout checks pass; optional native Scout is skipped. The historical #191 failure
is not relabelled as success.

The optimization tooling is integrated, but #268's actual paired physical,
complete-frame, GPU/memory/thermal/lifecycle and human review gates remain open.
This branch is an isolated review candidate. No merge, release, tag or golden
approval is performed. Initial branch `art/battlefield-decal-bakes` starts directly
from audited master; version 0.1.437 is one PATCH above that base.

The source/renderer/compiler, focused lifecycle/disclosure/material tests,
existing EffectsBarrage native fixture, Blender launcher and provisioned-runner
workflows were inspected, together with Architecture, Project Context,
Development, Asset Pipeline, Release Versioning, World and Terrain, World Asset
Presentation, World Environment Rendering, Combat/Industrial Rendering and the
bounded decal decision. Simulation/headless remain independent of authoring and
graphics. No authority, visibility, pool, shader, sampling or global budget is
changed.

## Demonstrated source gap and repair

The existing eight materials share an integrated 1254-square color/alpha atlas,
one materialless quad and fixed cell UVs. No saved native impression masters or
matching normal/ORM maps exist. Prelit color contains directional shadows and
specular highlights; the flat material response cannot react consistently to
lighting/wetness. The shared quad lacks a tangent channel for directed relief.
Existing lifetime, terrain alignment, copied event and visibility ownership are
already implemented and are reused.

Eight genuine editable Blender 5.2.2 masters now preserve original geometric
impressions and point material attributes. CPU Cycles selected-to-active bakes
produce a shared 1024-square color/straight-alpha, tangent Normal and ORM atlas.
Color is unlit and encoded once to sRGB. Numeric maps stay linear; oil is smooth,
dry tracks/debris/soot rough, and every family remains nonmetallic. Blender's
NEG_Y bake is checked against the exported engine TBN at 25 geometric points per
family; the native preview graph performs its own green conversion.

The shared carrier keeps its stable ID, original four vertices/two triangles,
normals, normalized UVs, bounds and materialless section. Only a derived tangent
channel is added. Its source now points to the saved master export. All eight
material IDs, cell transforms and map/event placements are retained. All maps
share the existing four-mip limit; base remains capped at 512 and the two new
numeric maps at 256. Expected lossless resident texels total 2,088,960 bytes;
actual compiled/global/resource results are recorded below when executed.

The production pool remains 512 active / 16 per 256m anchor cell / 128 published,
strength at most .65 and final-fifth fade. Ground visibility still requires
center/corners; hidden/dead/reused receivers retire their marks. Terrain height,
navigation, cover, collision, combat and saved simulation data are unchanged.
Single planes can still intersect curved terrain; receiver marks retain proxy
faces rather than projected-volume geometry.

## Authoring and diagnostic checks

Fresh pinned-version Check passes. The current-master Release build passes with
zero warnings/errors. Its complete source-less-capable baseline compile has
1,363 assets, 74,844,887 container bytes, 59,453,925 texture-container bytes and
1,786,417 studio-splash bytes. Initial focused decal/reference tests pass: 23,
zero failed/skipped. These working-tree checkpoints are not clean-source native
runtime acceptance.

Original failed development attempts are retained: a literal array passed through
the native PowerShell CLI was rejected before baking; the first saved color
buffer lost coverage during a colorspace switch; the corrected pilot exposed
linear color being stored directly as sRGB bytes. Floating bake buffers and one
explicit transfer correct that. An initial atlas contract rejects equal blast
and shell alpha footprints; blast coverage is then made broader and distinct.
No failed attempt is counted as final visual qualification.

Four pure diagnostic terrain substrates use actual industrial/asphalt-like,
concrete, dirt and gravel material IDs through the existing terrain renderer.
The fixture region selects one material with strength one while leaving terrain
geometry unchanged. Authored diagnostic copies cover 1/100/1,000 quads and
bounded overlap/opacity, using stable world/decal materials and sampled terrain
normals. These stress copies are independent of the unchanged live pool cap;
input count and actual admitted/visible counts are reported separately. Existing
pool/extraction tests retain live expiry, receiver identity and disclosure checks.

The adopted source passes the current Release solution build (zero warnings /
errors), four portable decal contracts, two actual scoped compiler/texture/
source-less tests and 24 presentation/lifetime/disclosure/reference tests, all
with zero skipped. The reference tests include a real terrain-slope review
point, terrain-normal alignment through quarter-turn rotations, four pure
substrates and preservation of the live publication cap. The dedicated native
workflow is opt-in on provisioned Blender runners; hosted contracts are distinct
from its actual authoring execution.

## Qualification status

Final native reopen/rebake/source-less gates, compiled catalog parity, current
source tests, paired D3D12 workload/image/timing/resource observations, reproducible
freshly installed player package and current-head CI are pending. Human visual
approval and the broader physical renderer qualification remain separate open
gates. Authoring or repeatable pixels alone do not qualify art, animation or
sustained frame rate. The technical PR remains draft until acceptance is assessed.
