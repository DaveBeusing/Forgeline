# Blender source round trips

The pinned [Blender launcher](../tools/blender/README.md) now imports and exports
existing source definitions through editable Blender geometry while checking the
current compiler contract. Every run requires a fresh, explicit output subdirectory
under repository `artifacts/`. Existing production sources and authoring projects
are never overwritten. Blender remains an authoring dependency only.

## Commands

```powershell
# Both production fixtures, including declared states/LODs and dependencies:
pwsh ./tools/blender/Invoke-BlenderRoundTrip.ps1 -OutputRoot 'artifacts/source roundtrip/inspection'
# One family, from a PowerShell 7.4+ session (AssetId is a typed array):
& ./tools/blender/Invoke-BlenderRoundTrip.ps1 -AssetId @('unit.directorate.main_battle_tank') -OutputRoot artifacts/tank-roundtrip
# Import an inspectable .blend without exporting a source clone:
pwsh ./tools/blender/Invoke-BlenderRoundTrip.ps1 -Action Import -OutputRoot artifacts/source-project
# Reopen that project, validate current authoritative inputs, export elsewhere:
pwsh ./tools/blender/Invoke-BlenderRoundTrip.ps1 -Action Export -BlendFile artifacts/source-project/authoring.blend -OutputRoot artifacts/source-reexport
```

Defaults are `SourceRoot=assets/source` and the checked-in
`tools/blender/fixtures/production.json` family contract. Root selection must match
when exporting an imported project. `FixtureContract` can select a separately
reviewed family contract; unknown roots are rejected. Source/output roots cannot
overlap. Paths resolve against the repository, so callers can run from another
working directory. `BlenderPath`, timeout and cancellation reuse the existing
launcher. No separate execution/discovery subsystem is added.

RoundTrip produces `authoring.blend`, `baseline-source/` (a scoped copy of original
inputs), `source/` (compiler-ready clone with glTF sources rebound to exported GLBs),
and `roundtrip-report.json`. Export produces the clones/report from a saved project;
Import only produces the project/report. Compiler input remains `*.asset.json` plus
its source files, never `.blend`. Native project bytes are not a deterministic
serialization contract; glTF/GLB, source definitions and compiled payloads are.

## Geometry and coordinate contract

Game source basis is +Y up and +Z forward. Blender uses +Z up and -Y forward:
`B(x,y,z)=(x,-z,y)`; inverse `G(x,y,z)=(x,z,-y)`. This is a rotation with determinant
+1, preserving winding. Scene units are METRIC with scale length 1.0; asset metadata
scale stays 1.0. Geometry is not rescaled to gameplay visual-instance dimensions.
Fixture world-socket tests additionally apply representative nonuniform instance
scale, yaw and translation without changing those gameplay definitions.

Each asset has an identity root; mechanical parts have named frames at their
metadata socket pivots and retain the existing parent hierarchy. Geometry vertices
are local to those frames; export evaluates world positions and converts back to
the game basis. Socket empties retain complete game-coordinate frames through the
same basis rotation. Their positions/orientations and representative instance-world
positions are checked. Original canonical socket metadata is retained after these
scene checks, preserving compiled float bindings.

Stable per-vertex and per-triangle attributes allow the exporter to restore
original index order and contiguous mechanical ranges without adding draw sections.
Cyclic triangle-loop rotation is canonicalized; reversed winding, added/missing
triangles or changed logical identities fail. Do not edit `fl_*` bookkeeping fields.
Object and part names, origins, hierarchy and material slot order remain checked.

UVs use `(u,1-v)` in Blender and inverse conversion on export. Authored float32
normals and tangents are retained in corner attributes. Blender's encoded split
normal preview is stored and validated separately, avoiding loss from its editor
representation. Clearing/editing either the authored normal channels or split-normal
preview fails. Tangent handedness stays exact. When source tangents are absent,
they remain absent in exported sources; the existing compiler generates them.

Local position tolerance is `1e-5`, normal/tangent component tolerance `1e-5`, UV
`1e-6`, socket/frame `1e-6`, representative instance-world socket `2e-5` metres.
Metadata IDs/references, topology, channel presence, material order, range ownership
and handedness compare exactly. The compiled test also checks normals/generated
tangents, sections and mechanical geometry-sharing keys. No resource/draw ceilings
or existing acceptance tests are weakened.

## Supported subset and deliberate rejection

Supported inputs are triangle glTF 2.0/GLB, indexed or implicit triangles, float32
POSITION/NORMAL/TEXCOORD_0/TANGENT and byte/ushort/uint indices, embedded/external
contained buffers and GLB binary buffers. Interleaved accessors are range-checked.
Named meshes and source JSON extras are retained. Geometry-only collision glTF
without scene nodes is accepted because the compiler reads mesh data directly.
Declared scene nodes must instantiate each mesh once with identity transforms.

Animation assets, skins, morph targets, compressed/required extensions, sparse or
normalized accessors, unknown channels, nontriangle primitives, embedded image/
texture graphs, duplicate instances, unbaked node transforms and unused vertices
are rejected with errors. The scene exporter rejects animation, constraints,
modifiers, shape keys, instancing, extra objects/scenes, uncommitted Edit Mode,
missing UV/normal/tangent data, changed materials and missing parts/sockets.
No generic FBX or animation runtime support is introduced.

PBR source material definitions and texture dependencies remain authoritative and
are copied unchanged. Blender materials are labelled scalar previews, not a new
PBR authoring authority; texture graphs are not reconstructed in this preservation
workflow. Unsupported preview edits fail instead of silently changing/dropping game
material bindings. Existing texture settings, stable IDs and LOD material identities
remain intact.

The fixture family list explicitly includes construction/damage/wreck/state assets
because those renderer-selected companion IDs are not all represented as links in
a root mesh definition. Declared LOD/collision/material/texture dependencies are
resolved recursively, checked for cycles, and validated against current source
JSON. Missing state IDs, collisions, sockets, invalid LOD chains and changed source
signatures fail. A saved project whose original sources changed must be reimported.

This entrypoint verifies preservation against the current baseline. Intentional
geometry/topology/material/socket changes need a separately reviewed baseline and
production authoring change; there is no tolerance override or automatic migration.

## Reports and qualification

Reports contain source/export SHA-256, dependency/source signature, asset IDs,
mesh/triangle/bounds statistics, component deviations, socket local/instance-world
errors, material/LOD/collision bindings and explicit errors. Parent launcher reports
retain executable identity and UTF-8 logs even when validation exits nonzero.
Production source file hashes are checked before and after processing.

```powershell
# Standard-library tests, no Blender installation:
python -m unittest discover -s tools/blender/tests -p test_asset_contract.py -v
# Actual Blender, compiled contracts and repeated/reopened project checks:
dotnet build ForgeLine.sln --configuration Release
pwsh ./tools/blender/Test-BlenderRoundTrip.ps1 -OutputRoot 'artifacts/roundtrip qualification' -RuntimeSeedRoot assets/runtime
```

The native qualifier independently round-trips both families, reopens the first
`.blend`, rejects manipulated scenes, checks a two-material authored-tangent fixture,
compiles original/exported source clones, compares repeated source/runtime bytes,
and runs source-less integrity checks. Its focused .NET test reads every compiled
mesh and verifies metadata, triangles, sections, basis data and renderer mechanical
batching against baseline. The test is explicitly opt-in via
`FORGELINE_BLENDER_ROUNDTRIP_ROOT`; normal CI records a skip without native exports.
The qualifier sets that variable and executes the comparison.

An optional qualified runtime seed only accelerates unchanged texture/material
compilation; all copied records are integrity-checked by the compiler. With a full
seed, the .NET comparison also prepares a sealed full-catalog runtime overlay under
the qualification root for an isolated player smoke. Original runtime files remain
untouched. No generated overlay is automatically installed or packaged.

Normal CI runs the standard-library contracts without Blender. The existing manual
Blender workflow now runs native production round-trip qualification on its
provisioned authoring runner. Local results do not imply that workflow has run.
Exact local/CI results and remaining limits belong to the qualification report.

Recorded native, compiled, visual and package results, including the open asset
regression gate, are in [round-trip qualification](BlenderAssetRoundTripQualification.md).
