# Terrain residency qualification

Status: **implemented, not yet accepted**. Product source
`d8abff95e1134f9986d2b3f90de0eef846340527`, version 0.1.424.
See [final results, measured limits and individual open gates](Results.md).
The entries below preserve the implementation/verification history.

Initial master: bfc16a953d731a844321a0848a2ebadc71c5875f, version 0.1.423.
The checkout was clean. The remote contains only master, no open terrain work;
previous terrain changes and render foundation are integrated. PRs 263/264 are
integrated instance/asset optimizations, not terrain replacements.
The accepted parallel ownership manifest supplies the required frozen contracts.

Master CI 39445686642 fails in Test: five Game cases and
RenderSubmission.SubmissionSafetyTests.BatchEscapeBandUsesBoundedReusablePayloadLeases
(expected zero allocated bytes, observed 8,486,912). These are pre-change results,
not evidence of a particular cause. Full native and final CI remain required.

Functional write scope is TerrainRenderer.cs and the TerrainResidency,
TerrainResidency test, terrain-residency build/documentation prefixes only.
Project files, layouts, shared contracts, shaders and global settings stay frozen.
The serialized one-PATCH exception is finalized against current master later.

Initial custom-artifact build with --no-restore failed NETSDK1004 because that
output directory had no restored assets. The baseline build restores there
before compilation; the failed invocation is recorded here, not a product failure.


The unchanged Release solution builds with zero warnings/errors (4m07.04s).
Four new reference cases pass before product changes. The initial no-test
selection (exit 8) reflected an earlier project file-list evaluation; rebuilding
the test project discovers all four. The initial texture-capability omissions
in the fake context are corrected without changing product assertions.

The bounded CPU preparation/shared-static-index path builds without warnings
or errors and passes 11 reference/preparation cases. These verify unchanged
positions, UVs, control maps and authoritative height samples, bounded CPU LRU,
GPU eviction reuse, late cancellation, static-buffer fallback and failed GPU
publication invalidating readiness. Vertex normals deliberately sample both
sides of immutable chunk boundaries. Native appearance review is still required.


Bounded readiness steps, retained GPU LRU and optional CPU-only neighbour prefetch
build with zero warnings/errors and pass 19 owned cases. The warmed Prepare test
records exactly zero allocated bytes with a positive 4,096-byte control. Tests
cover equal-aspect viewport changes, interrupted plans, independent geometry and
control budgets, one eviction-candidate visit per eviction, and no optional GPU
publication. Native measurement windows are serialized with other render work;
functional results are not presented as performance measurements.

Boundary-preserving geometry selection builds with zero warnings/errors
(6m35.74s) and passes all 31 owned cases (13.237s). Coverage, 32-bit indices,
winding, continuous height-error bounds, boundary positions/normals/UVs,
independent main/light triangle counts, strict pixel-limit hysteresis, protected
road/coast geometry, cancelled worker disposal, frame abort and repeated whole-map
payload plateau are covered. The three required existing classes, full affected
suites, native hardware evidence and final CI are the next gates.

The complete Release solution builds with zero warnings/errors (4m28.58s).
The three required existing classes pass all 26 tests (5.924s). The two unchanged
native WARP residency tests pass (14.399s), including pixel-identical eager versus
streamed cameras, main/light union, resize and over-cap failure. The 46-project
reference graph has no cycles or architecture violations. WARP establishes
functional behavior only. Full solution tests and hardware qualification remain
open. The standalone probe compiles against unchanged master and its CPU smoke
executes successfully; smoke timings overlap functional work and are not used as
performance evidence.

Final budget review adds a pre-GPU check that includes control pixels and error
arrays even for a two-sample mesh. All 32 owned tests pass (9.142s), with a
zero-warning/error Release solution build (4m25.02s). Development runtime
qualification in an isolated copied cache reports 0 compiled / 1,357 unchanged,
309 meshes, 900 textures, 127 materials, 18 animations, two skins and 75,122,812
container bytes. Canonical runtime inputs are not modified.

Eviction now locates the sorted draw-list slot by binary search as well as
choosing its LRU candidate in O(1); only the list's bounded removal shift remains
linear. An out-of-order multi-resident regression verifies eviction after middle
slot removal and retained touches. All 33 owned cases pass (22.192s); the focused
Release build has zero warnings/errors (1m08.63s).

The complete solution at `19c072cb6dd32d74bf7e8cee7cca9672d172dd10` builds with
zero warnings/errors (3m06.94s). Its full suite completes in 14m04.635s:
3,130 total, 3,107 pass, five known Game failures, 18 explicit opt-in/debug-layer
skips. Assets, Presentation, Client and Graphics pass in full. Portable results
and original log hashes are in [functional-checks.json](evidence/functional-checks.json).
Twenty compiled client assemblies all identify that source, with hashes in
[product-binaries.json](evidence/product-binaries.json).

Before hardware execution, a source review identified unintended normal smoothing:
the earlier candidate used render-mesh spacing for its finite difference instead
of the original heightfield spacing. No hardware performance evidence from that
candidate was accepted. The correction preserves the original interior and outer
boundary sampling exactly and only crosses available immutable neighbours at
shared boundaries. Direct bit-pattern comparisons against the existing world
mesh generator and a mixed-heightfield-spacing seam case cover this regression.
The earlier description of a baseline half derivative was incorrect: the existing
heightfield already implements one-sided outer-edge derivatives. The decision
document is corrected accordingly. Final correction checks and new source-bound
hardware evidence follow below.

The corrected product builds with zero warnings/errors (2m10.46s), passes all
34 owned cases and the complete Presentation/Client/Assets suites. Its full
solution run records 3,131 total / 3,107 pass / 6 fail / 18 skip (16m02.586s): five
known Game failures and one unsettled-surface Graphics capture. The unchanged
Graphics class passes 5/5 in isolation. Hosted CI separately records five Game
failures, the known 8,486,912-byte submission allocation and a frontend entered-wait
timeout; that frontend class also passes 5/5 locally. No frozen test is edited.

Four CPU and four direct hardware-terrain cohorts, plus twelve complete client
matrices (68 cases), finish with exit 0. The combined process monitor rejects the
window for one compiler-server CPU spike. Complete CPU/native protocols precede
that event and have separate bounded quiet observations. Results retain this
distinction rather than rewriting the original guard outcome. Pixel changes,
eight advisory client CPU alerts and large same-binary GPU variance leave the
performance/image gates open. Full interpretation and raw archive links are in
[Results.md](Results.md).

## Reproduction

Use the pinned SDK, Release, and isolated artifact directories. Restore before
the first custom-artifact build. Build/test commands use the existing solution
and Microsoft.Testing.Platform; no project or workflow changes are required.

```powershell
dotnet build ForgeLine.sln -c Release --artifacts-path artifacts/terrain-residency/product-build -m:2 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj -c Release --artifacts-path artifacts/terrain-residency/product-build --no-build -- --filter-namespace ForgeLine.Presentation.Tests.TerrainResidency
dotnet test --solution ForgeLine.sln -c Release --artifacts-path artifacts/terrain-residency/product-build --no-build --max-parallel-test-modules 2
pwsh build/Validate-ProjectReferences.ps1
```

`BuildProbe.ps1` compiles the same `TerrainProbe.cs` against each revision's
untouched Rendering.Benchmarks output, using the pinned compiler and framework
references. `InvokePairs.ps1` executes baseline/candidate/candidate/baseline in
separate processes, checks full source identity and runtime manifest hashes and
records all exits. CPU mode measures fixed Prepare, repeated teleports, main
recording and five map laps with managed/process/logical memory observations.
Native mode records 18 compiled-terrain cases (three sites, three actual zooms,
fixed and moving cameras), separate Prepare/CPU/frame and completed-fence GPU
world/shadow samples, actual draw counts and exact repeated readbacks.

`InvokeClientPairs.ps1` uses a dedicated clean detached checkout to supply the
frozen production capture runner with the exact binary source. It restores that
checkout on success or failure. Each cohort captures eight fixed and eight sweep
views across High/Low, close/strategic, daylight/logistics, plus one minimal
RendererCold case (three warmups/eight samples). Cold samples describe startup
behavior and do not establish stable percentile acceptance by themselves.

Run measurement commands through the existing
`build/rendering/snapshot-publication/InvokeQuietWindow.ps1` after exclusive
CPU/GPU access is confirmed. No build, tests, asset compiler or second native
renderer may overlap. Retain and reject overlapped cohorts. Quiet observation
cannot exclude all OS/user activity or establish thermal equivalence; repeated
reverse-order pairs expose some drift. No changes to GC/tiering, thresholds,
profiles, golden files or automatic approvals are permitted.

`SummarizePairs.py` reports raw statistics and ratios, with unavailable GPU time
remaining null. Use the frozen client performance and visual comparators for
their existing 15%/pixel gates. `RetainEvidence.py` losslessly archives raw probe
cohorts and original readbacks, rejects private path exports and verifies every
uncompressed file hash after reconstruction. It never edits or approves pixels.
