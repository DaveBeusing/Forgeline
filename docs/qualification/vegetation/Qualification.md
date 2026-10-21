# Native vegetation candidate qualification

Capture source: 0b4f6fb0b9340730ce65e022122af33f65bd3261, product 0.1.437.
Base master: a524b0a8164390abe1e9cb38170e690f46147574 (0.1.436).
Branch: art/vegetation-blender; draft PR #281.

## Functional and installed results

Real pinned Blender 5.2.2 x64 saved/reopened four independent rest masters,
anatomical selections and a shared packed PBR inspection project. Four saved
edits compiled; 24 actual unsupported edits were rejected. Committed-master
replay reproduced all eight GLBs byte-for-byte. Five scoped compiler generations
passed source-less integrity, with deterministic repeated source/runtime output.

Release build: zero warnings/errors. Full solution: 3,274 passed, 47 optional
skips, zero failures. The explicitly enabled typed native comparison and six
portable geometry/PBR/lineage tests passed separately. Project-reference and
runtime writer/process checks passed.

Both complete catalogs contain 1,363 assets. Exactly eight scoped mesh containers
differ; total bytes increase from 74,844,887 to 74,844,989 through source-path
metadata. Texture-container bytes stay 59,453,925. The candidate package was
reproducible. ZIP SHA-256:
f6cf80d075f3b9ad844569320436ab6e4b3e634dde48b2b39b2d9da6c3071b7a.
Fresh extraction verified 1,487 files, no authoring/compiler files and successful
startup from an empty directory with only Windows/.NET on PATH. The generic
smoke is separate from the actual plant workloads below.

## Physical D3D12 evidence

Selected adapter: NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU,
25,309,478,912 dedicated bytes, feature level 12_1, software=false.
Driver: 32.0.15.9653 (NVIDIA telemetry 596.53).

All 91 pairs completed: 182 main images and 32 adjacent-camera-pose images.
Every case has eight identical repeated readbacks, 32 warmups and 128 CPU
samples. Actual available GPU samples range from 74 to 126; missing observations
are not synthesized. The matrix covers 1/40/100/1,000 plants, Low/Medium/High,
1080p/4K, 120/420/1,000 metres, density 0.5/1, dark/dawn and wet High settings,
Traverse-v2 and four 40-metre conifer view directions.

All pairs have equal instance, LOD, draw, material, submission and tracked GPU
resource counters. There are zero mesh fallbacks, material/texture binding
failures or submission faults. 69 pairs show intended art changes; 22 are exact,
including the conifer-only views where its stem is occluded. The timber UV
correction is verified in source/runtime data; these views do not demonstrate a
visible bark improvement.

| Workload | Visible / authored | High / Reduced | Instance draws |
| --- | ---: | ---: | ---: |
| Grove, 1080p High, 420 m | 40 / 40 | 20 / 20 | 4 |
| 1,000 plants, 1080p High, 120 m | 193 / 1,000 | 193 / 0 | 4 |
| 1,000 plants, 4K High, 1,000 m | 1,000 / 1,000 | 0 / 1,000 | 4 |
| Traverse full density, final pose | 902 / 1,000 | 386 / 516 | 5 |

Tracked texture residency is 22,469,216–44,915,036 bytes and SRV use 53–111,
with exact per-case A/B equality. DXGI whole-process local usage spans
192,581,632–741,621,760 bytes. This is neither plant-only nor complete GPU
allocation accounting.

## Timing limits and open acceptance

| Example | CPU p95 before / after, ms | GPU p95 before / after, ms |
| --- | ---: | ---: |
| 1,000 plants, 1080p High, 120 m | 48.2069 / 97.4902 | 13.129568 / 15.0072 |
| 1,000 plants, 4K High, 1,000 m | 74.7622 / 58.2871 | 30.419872 / 30.372576 |
| Grove, 1080p High, 420 m | 43.4313 / 36.7954 | 11.025504 / 10.559840 |

The first example has an observed 102.2% CPU-p95 increase. Other cases vary in
both directions, GPU windows are incomplete, and host load/clocks were not
controlled in alternating independent pairs. Passive telemetry covers only the
later segment. No causal speedup, no-regression, thermal stability, sustained FPS
or complete-frame acceptance is claimed. No limit was raised.

Traverse and adjacent poses exercise LOD/culling; stalled still readbacks do not
establish continuous popping/judder or temporal wind approval. Existing tests
cover wind-time matrices and root bounds. Controlled performance pairs, complete
memory/lifecycle coverage, moving 30-minute operation, render-product acceptance
and explicit human art/motion review remain open. Production promotion and merge
remain blocked.

## Evidence and source boundaries

images.json maps original full-resolution PNGs to capture keys and SHA-256.
reports/ preserves compressed original manifests and pixel comparisons;
native-summary.json retains all paired metrics and raw distributions/references.
authoring-runtime-evidence.zip and its index preserve failed and successful
journals, package identity, compiler outputs and source signatures.

Earlier corrected assumptions concerned split-normal encoding after edits,
selecting the unmaterialed collision mesh in a material-slot negative, bundled
module lookup and repository-relative arguments. The initial tangent assertion
also included that tooling collision mesh; all eight visual meshes still require
valid UVs/tangents. No source/runtime budget or acceptance limit was relaxed.

Five executed hosted checks passed on the capture source, including full main CI;
optional native runner jobs were skipped. Local real Blender/D3D12 results are
separate evidence. The final report commit changes documentation/evidence only;
capture binaries and runtime-facing sources remain bound to the SHA above.
No new golden or human approval is recorded.

## Image evidence storage

All 214 original PNG captures are retained locally byte-for-byte under `artifacts/vg-original-evidence-images`, with 37 lossless ZIP parts under `artifacts/vg-image-archives`. These full image archives are not included in Git. Identical SHA-256 captures share an archive member. The committed `images.json` maps every capture to its retained local member and original SHA-256, and records archive sizes and SHA-256 values. Every member was read back and verified. Git includes six full-resolution previews under `images/` and all metric reports; no PNG was rescaled or re-encoded.

Direct full-resolution previews:

- [VegetationGrove-1920x1080-High-NormalGameplay (after)](images/029-after.png)
- [VegetationGrove-1920x1080-High-NormalGameplay (before)](images/029-before.png)
- [VegetationGrove-1920x1080-High-NormalGameplay (after)](images/082-after.png)
- [VegetationGrove-1920x1080-High-NormalGameplay (before)](images/082-before.png)
- [VegetationGrove-1920x1080-High-NormalGameplay (after)](images/084-after.png)
- [VegetationGrove-1920x1080-High-NormalGameplay (before)](images/084-before.png)
