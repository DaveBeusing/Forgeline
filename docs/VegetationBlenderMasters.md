# Rooted vegetation Blender sources

Status: isolated authoring and runtime-review candidate; production promotion and
human visual approval remain separate gates.

The audited master is a524b0a8164390abe1e9cb38170e690f46147574, product 0.1.436.
Its [main CI](https://github.com/DaveBeusing/Forgeline/actions/runs/39625414800)
completed successfully. There were no related unfinished remote branches or open
PRs. Existing volumetric vegetation, broadleaf and the render foundation were
already integrated. PRs 171, 181, 190, 192, 227–229 and 268 were inspected for
implementation and qualification scope; historical results are source-specific.

## Existing art and demonstrated gap

The four requested families already have High/Reduced geometry. Broadleaf has a
trunk, branches and five opaque closed crown lobes. Conifer has a rooted stem
and three crown layers. No additional variants are declared. Saved native
Blender masters were missing for these four families.

The initial real Blender PBR previews demonstrated two additional quality gaps:
the old scrub is two cones and reads as another small conifer; grass has
full-clump-width triangular bases, up to seven metres against a one-metre
physical height. The candidate replaces scrub with five/three overlapping
closed shrub lobes and grass with three narrow leaves. High grass inserts a
bend into each blade; Reduced retains the same roots and tips with one front
triangle per blade. Both levels include the opposite face without alpha.
Existing exact per-LOD bounds and triangle/vertex budgets are fitted and retained.

The conifer stem samples the same foliage atlas cell 12 as its crown: original
UV range approximately (0.018, 0.768)–(0.232, 0.982). The candidate maps only the
existing 60/36 stem vertices to the kit timber cell 10, inset 0.018 on each edge.
Conifer positions, normals, topology and foliage UVs remain unchanged.
Broadleaf is preserved exactly. Scrub/grass positions and normals change only
within their original per-LOD bounds; materials, UV cells, source vertex and
triangle counts, and gameplay metadata stay fixed.

| Family | High / Reduced triangles | Representative metres |
| --- | ---: | --- |
| Conifer | 80 / 48 | 7 x 17 x 7 |
| Broadleaf | 624 / 152 | 7 x 12 x 7 |
| Scrub | 40 / 24 | 6 x 2.5 x 5 |
| Grass clump | 12 / 6 | 7 x 1 x 6 |

Reference fixtures obtain physical scales from actual presentation-only map
objects. Source geometry stays at Y=0..1 and X/Z within ±0.5; production
placements and terrain grounding are unchanged.

## Native source contract

The family-scoped build/vegetation scripts use pinned Blender 5.2.2 and the
existing strict static adapter. Four independent rest projects contain named
High/Reduced collections and selectable trunk/branch/crown, shrub-cluster and
blade groups derived from actual source partitions. These editorial selections
do not introduce runtime mechanical parts. Broadleaf consumes the unchanged shared rooted tooling
collision proxy; this candidate does not own or promote that shared mesh.
A separate inspection project packs the existing three shared PBR maps once.
Base Color is sRGB; tangent Normal and ORM are linear. The existing BC7 caps
are 2048/1024/512 pixels and 5/4/3 mip levels: 7,323,648 shared texel bytes.
ORM remains R AO, G roughness, B metallic. No new texture, material, sampler, alpha path, shader
or runtime texture budget is introduced.

The axis mapping remains engine +Y up/+Z forward to Blender +Z up/-Y forward,
(x,y,z) to (x,-z,y), at metre scale. Rest projects contain no wind actions.
Native readback is checked before source floats are serialized; the maximum
permitted editor storage difference is 5e-8. Each family is imported twice and
exported again from its reopened saved master. Unsupported transform, envelope,
modifier, animation, material-slot and unexpected-object edits must fail.
A separate qualification-only saved vertex edit moves one upper vertex by
0.001 along X, keeps root Y and explicitly reapplies native split normals before
reopen/export. That edit is compiled and excluded from the retained candidate.

Canonical assets/source is under the rendering ownership hold. The retained
candidate therefore belongs to assets/staged/vegetation. Runtime review uses a
complete isolated source clone with exactly the eight visual bindings replaced,
compiled by the ordinary compiler. Runtime files are never edited directly.
Blender and authoring content are excluded from installed packages.

Four appended review scenes contain 1, 40, 100 and 1,000 actual plants. Mixed
scenes cover all four families, four orientations and existing density filters;
there are no gameplay entities. Existing enum values and fixture fingerprints
remain unchanged. Close/normal/strategic distances stay 120/420/1,000 metres.
Production wind stays at eight seconds, maximum 0.025 radians and 18–72
reference-pixel fade; projected LOD stays at 65 pixels with 12% hysteresis.
Gameplay concealment, navigation, cover, fog/minimap, object keys, placement
clearance, density/distance rules and save/replay authority remain unchanged.

## Reproduction and acceptance

    ./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/vg-check
    ./build/vegetation/Qualify-Masters.ps1 -OutputRoot artifacts/vg-native -RenderPreviews
    python build/vegetation/retained.py retain artifacts/vg-native
    python -m unittest discover -s build/vegetation -p 'test_*.py' -v

The dedicated workflow runs hosted retained-source checks; real bpy validation
requires explicit dispatch on a provisioned Windows Blender runner.

Current render-product records still leave controlled paired physical timing,
full live-frame/GPU memory accounting, observed thermal stability, moving
30-minute lifecycle and explicit visual/motion review open. Foundation image
approval covers its own historical scope. Short captures and deterministic
stills do not establish sustained FPS or absence of moving LOD popping.
These independent gates prevent production promotion; no merge, tag, release
or golden approval is part of this candidate.

## Functional checkpoint

Local Release build: zero warnings/errors. The complete solution run completed
with 3,274 passed, 47 optional tests skipped, zero failures (3,321 total).
The explicitly enabled typed native-source comparison passed separately, and
all six portable retained-source/geometry tests passed. Each of the four native
families passed its saved-edit proof and six actual Blender rejection cases.
Five scoped compiler generations (baseline, two imports, reopened master,
qualification edit) passed source-less integrity. Repeated source/runtime
outputs are deterministic. The full canonical catalog was freshly validated:
1,363 assets, 74,844,887 compiled-container bytes. Runtime writer/process tests
and project-reference validation passed.

These are local functional results before the source commit; authoring lineage
records the checked canonical base, actual producer hashes and the dirty
working-tree context explicitly. Source-controlled master replay and installed
native captures bind their own subsequent committed source identities.

## Installed native checkpoint

The complete source-bound [qualification and original image index](qualification/vegetation/Qualification.md) records 91 native pairs, exact per-case resource counters and unresolved timing/art gates. The installed candidate demonstrates in-game adoption; canonical promotion remains blocked.
