# Asset readiness qualification checkpoint

Status: **source inventory and isolated pilot implemented; production integration
not accepted**. Remote base remains `2b4b61ece625d1311c9ee5564cf513dadf2d1123`
(`0.1.425-dev`). Local branch `tools/asset-readiness` advances exactly one PATCH
to `0.1.426-dev`. Native captures, final Release build and reproduced package use
`e822ec063c91cb103d3dace66b458fd9ec418b59`. Subsequent changes retain reports only.

## Implemented scope

- [Contract and ownership boundaries](Contract.md).
- [Per-ID source inventory](Inventory.json): 397 definitions, 273 meshes,
  56 materials, 48 textures, 18 clips and two skins. All expected 13 building,
  seven unit, eight terrain-material and four deposit roots exist. No missing
  source or unresolved metadata reference was observed. Static geometry passes
  the existing bounded reader; rigs remain on their separate contract.
- Exact source file/domain ownership, authoring-file hashes, source maturity,
  materials, geometry channels/bounds, LODs, sockets, collision and observed
  runtime-container records. Runtime presence is deliberately not labelled art
  or compiler acceptance.
- Three-family fixture: Command Core, Main Battle Tank and world crate, with
  declared companion states/LODs and dependencies. The exported catalog has
  32 assets, 21 meshes, 15,696 triangles and 11 mechanical parts.
- Reused the existing pinned authoring path and compiled comparison. Existing
  two-family validation expectations and tolerances remain unchanged.

Source ledger signature:
`b229ae36a69af6c6e431f58e5ac15b165a49904075415587abda98a4a13c9a6c`.
The source-owned geometry and simulation/renderer product code were not changed.
Saved staged `.blend` files and original logs remain under
`artifacts/readiness/pilot/`; they are not promoted as production asset masters.

## Executed checks

| Command / check | Observed result |
| --- | --- |
| `dotnet restore ForgeLine.sln` | Passed with SDK 10.0.401 |
| `dotnet build ForgeLine.sln --configuration Release --no-restore` | Final source passed, zero warnings/errors, 52.48 s |
| `./build/Validate-ProjectReferences.ps1` | 44 projects, no cycles |
| `python -m unittest discover -s tools/blender/tests -p 'test_*contract.py' -q` | 88 passed, 57.870 s |
| Native staged pilot | Blender 5.2.2 LTS, build `d13f752e3b9c`; two independent saved masters and reopened export passed |
| Existing native negative contracts | 19 invalid scene edits rejected; two-slot/tangent positive passed |
| Baseline / first / second / reopened compilation | Four isolated catalogs passed canonical compiler and absent-source validation; repeated/reopened source and runtime bytes equal |
| Opt-in compiled pilot comparison | One passed, three unrelated opt-ins skipped; metadata, geometry, materials, UVs/normals/tangents and batching preserved across all three variants |
| Full catalog compilation | 1,357 compiled, zero unchanged; 75,122,812 runtime bytes; compiler 1.12.0 |
| Full absent-source catalog validation | Passed all 1,357 assets |
| Native development before/after | Three fixed 1080p High/NormalGameplay pairs, all pixel-exact |
| Reproducible local player ZIP | Passed; both publish trees identical; source tree clean at package creation |
| Fresh ZIP extraction | All 1,481 manifest file hashes verified; zero Blender/source-model/script/compiler files |
| Installed player native capture | Three cases passed using the extracted player and its own runtime; all pixels equal to development overlay |
| One-PATCH validation | Passed, 0.1.425 to 0.1.426 |

Maximum compiled position difference is `1.4901161e-08`; maximum tangent-vector
difference is `7.275674e-07`. Existing tolerances were not changed. The full
baseline runtime content signature is
`edcbaeaf79e9a93e907b9e4f62bbb3065f7c60b928b71d742e3f94c47a4f8136`.
The compiled pilot overlay and player retain their own distinct signed manifest.

The typed comparison was executed explicitly after adding its opt-in test:

```powershell
$env:FORGELINE_BLENDER_READINESS_ROOT = (Resolve-Path artifacts/readiness/pilot).Path
$env:FORGELINE_BLENDER_RUNTIME_SEED = (Resolve-Path artifacts/readiness/catalog).Path
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Presentation.Tests.BlenderRoundTripQualificationTests
```

The final wrapper now includes that same typed test. Its earlier native/compile
stages and the added typed step were verified separately; the final complete
wrapper was not redundantly rerun in a second fresh root.

The first sandboxed Python run failed three cases because its temporary directory
denied writes. Setting TMP/TEMP to `artifacts/test-temp` produced the successful
unchanged 82-test baseline, followed by the final 88-test contract selection.
The first readiness-wrapper invocation stopped at a PowerShell interpolation
syntax error before launching Blender; that error was corrected. Failed logs
were retained locally. These attempts are not product-regression evidence.

## Native image scope and limitations

Hardware: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, D3D feature level
12.1, hardware rendering (`software=false`), debug layer unavailable. Scenes:
CommandCoreProduction, MainBattleTankProduction and IndustrialBase. Each capture
uses three warmups, eight samples and two repeat images. Both development cohorts
and the extracted player report zero material binding failures and zero fallback
mesh instances. Observed instance draws are 34 / 28 / 37 respectively.

All before/after and installed image comparisons have zero changed pixels at
zero channel tolerance. This is a short functional comparison, not the planned
multi-pair performance qualification, a direct per-ID drawn-object trace, full
animation/culling/stress review or human art acceptance. Raw CPU/GPU observations
are retained without claiming a speedup or passing the unchanged performance gate.
No golden, budget, timeout or gameplay assertion was relaxed.

[Native evidence projection and original manifest hashes](evidence/NativeEvidence.json)
retain the selected raw counters and distributions. Complete original manifests
remain locally in the corresponding `artifacts/readiness/native-*` roots; the
repository projection omits local absolute execution paths. Unmodified native
PNGs, compiler reports, negative cases, typed comparisons and a local evidence
hash ledger are retained in [evidence](evidence/LocalEvidenceLedger.json).

Archive SHA-256:
`769b84297f998c1b58a2d442dda735ebd7f01701edac8fa661566bf4e1202134`.
The archive remains local under
`artifacts/readiness/package/FORGELINE-0.1.426-dev-win-x64.zip`.
No release was published.

## Remaining gates

The final repository readback still reports renderer qualification PR #268 open,
head `1a0117802db3079fec315426d1b5486d7a680c34`, based on the same current master.
Current master `build-test` is still in progress in the final observed check
snapshot; no current-source green full CI is claimed. Historical merged PR check
results in [precheck](Precheck.json) are historical, not a replacement for this
gate. Renderer optimization integration/acceptance is not inferred from merge
names or the accepted older foundation record.

Still required before production promotion:

- Integrated and accepted optimized-renderer baseline, successful exact-source CI
  and applicable complete solution/headless/simulation/replay gates.
- Full close/normal/strategic, bright/dark/wet, 1080p/4K and Low/Medium/High review,
  animation/LOD/culling assessment, direct per-ID drawn-object proof and relevant
  1/100/1,000-instance cases.
- Paired sustained physical-GPU/CPU distributions, workload/source signatures,
  residency/descriptor/resource-churn observations and unchanged acceptance limits.
- Explicit human silhouette, motion, physics-compatibility and image approval.
- Family-specific editable production masters wherever future artwork changes;
  the staged preservation masters are not final content promotion.

No requested gameplay identities, ECS, navigation, combat, economy or collision
authority were changed. No merge, tag, release or accepted golden is authorized
by these results.

## Local commit boundaries

1. `26e386e`: canonical inventory, audit snapshot and ownership/review contract.
2. `cfd9279`: three-family staged fixture and typed runtime comparison.
3. `e822ec0`: isolated one-PATCH increment.
4. Subsequent evidence-only checkpoint: observed compiler/native/package results
   and the updated runtime-observation inventory.
