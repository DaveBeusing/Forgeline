# Resource deposit Blender masters

## Change and integration status

Four editable Blender 5.2.2 masters and separate packed-PBR inspection projects
preserve existing mineral artwork. Eight verified High/Reduced GLB exports now
bind existing resource mesh IDs in the regular source catalog. Shared materials,
texture atlases, LOD thresholds, symbols and source-only entities are retained.
This branch is a review candidate; it is not production acceptance.

Native review exposed a second gap: map deposits have an X/Z scale of 50.4 meters
but Y scale 8, making clusters appear almost flat. PresentationExtractor now
applies minimum Y scales: Ferrous 16, Silicates 28, Volatiles 8, Rare Elements 32.
It adjusts the copied render center by half the added height to preserve the
ground anchor, retaining X/Z, rotation and taller authored transforms. Logical
WorldTransform, bounds, extraction radius, quantities, economy, selection and
intelligence authority remain unchanged. Source-only identities do not gain a
ResourceDeposit component.

## Audit and synchronization

Initial ordered remote audit: master a6fe118583be842e00416bcb100d1210f3240a00,
version 0.1.431-dev. Default/integration branch: master. Related resource PRs #47,
#112, #181, Blender tooling #229, readiness #269 and rendering #268 were inspected
along with branches, changed paths, CI and available review comments. Existing
family silhouettes, PBR atlases and Untouched/Active/Depleted presentation are
reused. No new depletion mechanism or texture state is invented.

During execution, logistics PR #273 merged. This branch was rebased onto master
a391a5bb0c0e25f079fa394613a5f84dfe7e9728; its logistics enum IDs are preserved and
resource kinds appended after them. The single PR PATCH delta is now 0.1.432 to
0.1.433. Evidence retains actual historical identities rather than being relabeled.

| Family | High triangles / vertices | Reduced triangles / vertices |
| --- | ---: | ---: |
| Ferrous Ore | 400 / 1,200 | 100 / 300 |
| Silicates | 30 / 80 | 18 / 48 |
| Volatiles | 38 / 112 | 24 / 72 |
| Rare Elements | 40 / 120 | 24 / 72 |

These are actual source counts, not an art-acceptance score. Ferrous uses the
current environment-kit revision rather than its older unused sibling. There is
no extra texture payload or geometry density increase.

## Source ownership

assets/staged/resource-deposits/<family>/ retains each native rest master, packed
inspection project, two exports and lineage. Regular definitions under
assets/source/world/meshes/resources/ reference byte-identical GLBs under
sources/blender/<family>/. Lineage retains original binding/source signatures and
the current bound portable signature. Status review-bound-not-accepted separates
functional binding from approval. Original glTF files remain available.

build/resource-deposits/author.py delegates to the bounded shared round-trip.
Rest scenes retain metric scale, root pivots, engine-to-Blender (x,y,z) to
(x,-z,y), topology, material order, normals, UVs and High/Reduced metadata.
Inspection uses the existing surface helper with packed shared Base Color,
Normal and ORM. Its roughness/metallic preview approximates the runtime shader;
AO remains the game shader's responsibility. Inspection scenes are separate
from compiler-accepted rest scenes. Blender does not enter the player package.

The translated/rotated unit-scale fixture tests export contracts; it does not
redefine logical envelopes. Six actual native mutations per family are rejected:
unbaked transform, changed envelope, modifier, animation, material slots and
unexpected object. Unchanged reopen/export is accepted.

## Reproduction

Use the pinned SDK and Blender 5.2.2 x64, a clean checkout and fresh output paths:

```powershell
dotnet restore ForgeLine.sln --disable-parallel -m:1
dotnet build ForgeLine.sln -c Release --no-restore --disable-build-servers -m:1
pwsh ./tools/blender/Invoke-Blender.ps1 -Action Check -OutputRoot artifacts/resource-check
pwsh ./build/resource-deposits/Qualify-Masters.ps1 -OutputRoot artifacts/rb -RenderPreviews -NativeBatch
python build/resource-deposits/retained.py
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master
```

Qualification creates independent first/second projects and reopens the first,
then compiles baseline/first/second/reopened for each family. It checks current
source hashes, absent-source integrity, repeated/reopened output bytes and typed
compiled positions, normals, UVs, tangents, indices, material/LOD, socket and
collision references. Sources must remain unchanged during the run. The retained
verifier checks hashes, exact roster, bound IDs, geometry and portable signatures.
Retention with a positional output root is initial-only and refuses existing files.

For a qualified full catalog, opt-in ResourceDepositMaterialCacheTests creates
fresh four-record family seeds from verified material/texture containers. Set
FORGELINE_RESOURCE_RUNTIME_SEED and a fresh FORGELINE_RESOURCE_MATERIAL_CACHE.
Pass that destination to Qualify-Masters through -MaterialCacheRoot. Every variant
still imports its two meshes through the real compiler. -ReuseAuthored requires
native rejection/source-hash evidence and fresh runtime destinations.

## Native game review

Four immutable fixtures cover a single deposit/extractor pair, all four families
by three real presentation states with twelve colocated extractors, and 100/1,000
deposits. They use map definitions and sampled canonical ground height. No economic
simulation runs in these art fixtures. Tests check identities, states, counts,
deterministic fingerprints, grounding and co-location.

Capture-Review.ps1 uses the real D3D12 player and complete catalogs: three zooms,
1080p/4K, Low/Medium/High, 1/100/1,000 instances, dark and wet. Its 3 warmups,
8 samples and 2 repeats provide functional evidence, not sustained performance
acceptance. Historical flat-height and intermediate-height runs each completed
fourteen paired cases. Current-base results are recorded separately when complete.

## Observed evidence

Pinned Blender check passed: 5.2.2 build d13f752e3b9c, SDK 10.0.401.
Portable authoring: 102 tests passed with Blender Python 3.13.
Two earlier full Release builds passed with zero warnings/errors. Architecture
validation passed for 44 projects without cycles. Fixed seed 2026 ended naturally
at tick 55,210 with CommandCoreDestroyed/Victory, winner 1.

All four first/second/reopened native projects and 24 mutation rejections passed.
Sixteen real compiler catalogs passed, followed by sixteen absent-source
validations and deterministic source/runtime comparisons. Typed family/full
overlay tests passed nine cases. The overlay replaced only eight meshes in a
sealed 1,363-record catalog. Canonical container bytes were 74,844,434; overlay
74,844,604 (+170 mesh metadata bytes). Texture containers stayed 59,453,925 bytes;
after the existing splash exclusion gameplay textures are 57,667,508, below the
unchanged 58,720,256 gate. File lengths are not GPU residency. These runs used
verified unchanged texture caches and are not claimed as force-rebuilds.

The initial cold repeated texture run was interrupted, not passed. A copied
runtime folder correctly tripped the fresh-output guard. Native-only evidence
was copied with hashes from artifacts/rd2 into rd4, checked against actual sources
and completed with material-only cache seeds. Native origin is rd2; rd4 is resumed
compiler qualification. The raw evidence index hashes retained bytes. Sandbox
Python/path and test-pipe failures executed no tests; later native runs passed.
A grounding assertion exposed float rounding at a decimal boundary; it now uses
an absolute 0.00001-meter bound.

## Acceptance boundaries

Rendering PR #268 is merged, but its physical/performance/human gates were not all
accepted. Neither that merge nor deterministic pictures implies qualification.
No budget, golden, threshold, gameplay rule or renderer ownership is relaxed.
No merge, release or golden approval is performed.

Separate gates remain: current-source complete CI, four terrain-material review,
real extraction/depletion play review, moving-camera LOD/lifecycle coverage,
controlled sustained paired CPU/GPU timings and resource churn, reproducible
installed-player qualification and explicit human art acceptance.

Native hardware: NVIDIA RTX PRO 5000 Blackwell Laptop, 25,309,478,912 bytes reported
VRAM, feature level 12_1, hardware rendering, debug layer off. Offscreen native
1080p/4K backbuffers do not certify physical display performance. Production
acceptance remains open until required gates pass.

## Bound-master qualification update

After the regular catalog switched to GLB, the old frozen source signature correctly
refused export. The masters were regenerated against the bound sources. The mineral
adapter now normalizes JSON/glTF line endings in its frozen contract signature while
retaining raw source hashes for unchanged-input checks. Material/content/path changes
still invalidate the contract. A regression test verifies portable line endings and
rejection of changed material content. Shared exporter modules are unchanged.

The final real-Blender run at ad6b88e06d8c0f99ae561a00429f694ef44c7470 used fresh
factory scenes for every independent import in one pinned process (-NativeBatch).
All four first/second/reopened runs, 24 actual native mutation rejections, 16 real
compiler catalogs, 16 source-less validations and repeated source/runtime byte
comparisons passed. The typed run passed 9 cases with the overlay helper skipped.
All eight exported GLBs match the previously bound files byte-for-byte.

For a qualified binding/signature migration only, retained.py --refresh-native
artifacts/rb checks the complete qualification and unchanged exports before replacing
the native projects. It refuses undeclared edits to existing masters. Previous
master hashes remain in lineage and their original files remain in Git history.
The current frozen-contract signature and authoring commit are recorded explicitly.

On current master, the complete baseline contains 74,844,665 container bytes and
the bound candidate 74,844,887 (+222 mesh metadata bytes), both 1,363 assets with
zero issues. Only the eight mineral mesh payloads differ; every nonmesh payload
is identical. Current-catalog.json retains those counts.

The full Release solution build passed with zero warnings/errors (5m55.88s after
a compiler file-lock retry). The complete solution test run executed 3,285 cases:
3,246 passed, 37 optional skips, two native session-handoff timeouts during parallel
cold production compilation. The isolated unchanged handoff class subsequently
passed all 3 cases in 23.899s with the original deadlines. The asset module passed
including its clean production compilation. No timeout, threshold or test filter
was weakened. The initial missing conventional runtime prerequisite was resolved
by restoring and validating the qualified catalog at assets/runtime.
