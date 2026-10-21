# Resource deposit Blender masters

## Scope and audit

Audit base: remote `master` at `a6fe118583be842e00416bcb100d1210f3240a00`,
tracked version `0.1.431-dev`, observed 2026-10-21. The repository default and
integration branch is `master`. A fresh Git fetch confirmed this identity.

The remote branch inventory contained `master` and `art/logistics-blender`.
The only open PR was the unrelated logistics preservation PR #273. Resource
presentation (#47), distinct mineral silhouettes (#112), the ferrous kit revision
(#181), bounded Blender material/LOD tooling (#229), and the source-readiness
pilot (#269) are integrated. Their existing work is reused. Related resource and
rendering PR searches, changed paths and available review comments were inspected;
the resource/round-trip PRs had no review comments.

All four families already resolve distinct High/Reduced geometry, stable
materials, strategic symbols and shared Base Color/Normal/ORM textures.
`WorldPresentationCatalog` already consumes Untouched/Active/Depleted presentation
states. Active increases the existing tint and depleted uses the existing grey
tint. These states derive from deposit data; separate mesh states are unnecessary.
The demonstrated source gap is a missing native editable master for each family.

| Family | High triangles / vertices | Reduced triangles / vertices |
| --- | ---: | ---: |
| Ferrous Ore | 400 / 1,200 | 100 / 300 |
| Silicates | 30 / 80 | 18 / 48 |
| Volatiles | 38 / 112 | 24 / 72 |
| Rare Elements | 40 / 120 | 24 / 72 |

These are parsed canonical source counts; density alone is not an art-acceptance
criterion. Preservation adds no geometry or runtime texture payload.
This preservation change does not claim newly designed geometry or new textures.

The canonical ferrous export is the environment-kit revision, not its older
unused glTF sibling. The fixture resolves metadata through the current catalog
and follows its actual source paths and dependency closure. This also retains
the original source-only entities and all stable IDs.

## Authoring and retained files

`build/resource-deposits/author.py` delegates strict rest import/export to
`tools/blender/roundtrip.py`. Each family gets its own native rest project and
High/Reduced GLB exports. The shared texture masters are packed into a separate
inspection project using the existing bounded PBR preview helper. Inspection
graphs/cameras are deliberately separate from the compiler's accepted rest scene.
Packed inspection is an approximation of the runtime shader, not native D3D12
evidence; its ORM preview uses roughness/metallic and leaves AO to the game shader.

No canonical `assets/source/`, compiler, shared material or renderer file is
changed. The renderer ownership record still holds those source paths, and its
qualification gates remain open. `assets/staged/resource-deposits/<family>/`
contains preservation candidates only. The `lineage.json` records bind each
master/export to the canonical definitions and a portable source signature.

Rest scenes retain engine `(x,y,z)` to Blender `(x,-z,y)` mapping, metric scale 1,
root pivots, indexed topology, ordered material slots, source normal/tangent/UV
channels and High/Reduced metadata. The fixture's translated/rotated unit-scale
frame is an authoring contract probe, not a replacement gameplay deposit envelope.
Terrain placement, extractor radius, quantities, collision, selection and
intelligence remain owned by existing game/presentation code.

## Reproduction

Use a fresh output path in a clean checkout and the pinned .NET SDK and Blender
5.2.2 x64. Build serially to avoid competing build-node pools:

```powershell
dotnet restore ForgeLine.sln --disable-parallel -m:1
dotnet build ForgeLine.sln --configuration Release --no-restore --disable-build-servers -m:1
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/resource-check
pwsh ./build/resource-deposits/Qualify-Masters.ps1 -OutputRoot artifacts/rd1 -RenderPreviews
python build/resource-deposits/retained.py artifacts/rd1
python build/resource-deposits/retained.py
```

Qualification creates two independent masters per family, reopens/re-exports the
first and tests six invalid native edits per family. It separately compiles
baseline, first, second and reopened catalogs, validates all with absent source
roots and compares repeated/reopened source/runtime bytes. The opt-in typed test
compares actual compiled geometry, UVs, normals, tangents, indices, material/LOD
references and unchanged nonmesh container hashes. The original canonical source
tree must remain byte-identical.

Retaining refuses existing destinations. Portable verification checks master and
export hashes, the exact fixture/family roster, current canonical source binding
and actual exported geometry; file presence alone is insufficient.

## Integration decision and remaining gates

Rendering qualification PR #268 is merged, but its recorded final five hosted
workflows passed with physical jobs skipped. Its retained PlanOnly report contains
zero native records and leaves twelve product gates open. A merged milestone is
not performance acceptance. Required paired physical timings, complete-frame and
memory observations, lifecycle coverage and human visual approval remain open.
Historical CI failures retain their original source identities; they are not
attributed to the current base without reproduction. On the audit base, main
[CI 39579830636](https://github.com/DaveBeusing/Forgeline/actions/runs/39579830636)
is still in progress at the recorded checkpoint. Its artillery-retention and
movement workflows passed; these do not replace the complete required CI check.

Production promotion is blocked. The staged masters do not by themselves satisfy
end-to-end game integration. Still required are the accepted rendering baseline,
current-source full CI, native before/after pictures at all required zooms,
profiles/displays and four terrain materials, extractor co-location and real
depletion review, 1/100/1,000-instance culling/LOD/resource qualification, sustained
paired performance measurements, reproducible installed-player verification and
explicit human art approval. No budgets, thresholds or goldens are changed.

## Execution evidence

The initial pinned Blender Check passed in the original clean checkout with SDK
10.0.401. The first restore and authoring attempts were interrupted after resource
contention and discovery of unrelated staged tank work in that checkout. They
are not counted as passing builds or completed authoring runs. Own files were
copied with byte verification into a separate worktree on the audited base, then
removed from the shared checkout; the unrelated staged files were left intact.

The portable authoring suite passed **102 tests** with the installed Blender
Python 3.13 runtime, in 238.229 seconds. Sandbox Python startup attempts failed
before tests at executable-path resolution; the native run above completed.
The initial sandbox restore failed `NU1900` while contacting NuGet vulnerability
data. The unchanged restore completed with network access, using two logical
.NET processors and one MSBuild worker; vulnerability auditing stayed enabled.

The committed one-PATCH gate passed at `3dddf952`: `0.1.431` to `0.1.432`.
`Validate-ProjectReferences.ps1` passed for 44 projects, without cycles or
architecture-boundary violations. Ferrous has completed two independent native
imports/exports and a reopened export; per-family rejection, compiler and typed
checks remain separate.

The final execution results belong in this section after their commands finish.
Until then native master creation, deterministic export, compilation, typed
comparison, full tests, package and native-render qualification are **pending**.
