# Scene composition and effect submission qualification

Status: **implemented, not yet accepted**. The default product includes the
correctness and bounded effect-submission changes. The optional shared-linear
shader candidate remains disabled pending native hardware and visual acceptance.

## Source and scope

The branch `performance/composition-effects` starts from current `master`
`cdf97fb9a8ef277cf75d451476a21128d398007c`, product `0.1.424-dev`.
During implementation, PR #266 merged to new master
`e42ae48346bbb1331b7a548a72fc0905ae6d184a`. The branch was reconciled by merge
`e121263ababc9d2f51ee38ac507fc6da34fdeaef`; none of its 15 already committed
owned paths overlapped the master advance. The new master still records
`0.1.424`, so version preparation must use its actual current value.
The integrated render foundation, repair and release record are present;
`parallelReleased=true` is recorded in the existing ownership manifest. Its
accepted images qualify that foundation, not these subsequent changes.

The only other active branch at the initial check was
`rendering/material-shadow-quality`, draft PR #266. Its material/shadow work and
separate serial repairs enter only through the later master reconciliation.
No independent copies or extra fixes were made. No existing composition/effect
continuation was found. Historical PRs #98/#100 and #94 are not reused.

| Functional checkpoint | Commit |
| --- | --- |
| Qualification boundaries | `5d842aeaaf75272fdbe82eed6dc4d530f689a789` |
| Scaled depth-aware AA, robust weights, optional shared neighborhood and native cases | `15ad8ab96e41f510efd727da0bcbd82b5d48e9af` |
| Complete bounded effect admission, cached definitions, current view-depth key sorting and portable cases | `6a29d78fd54a7a1ea19f7bdc2d083287876049ba` |
| Reproducible compiler and CPU qualification | `1c4874718c38bef4063fb8cf13806b91a2c4523c` |
| Nearest-rank qualification percentiles and minimum-scale capture instructions | `68d62dbe8d788bb4e29516a69655e74b2ccdd2f8` |
| Balanced fixed process warmup and measured-assembly fingerprints | `8331e384d1fc7b89c9b40ade7590ce9a5ed6f99a` |

Only assigned source, test, build and documentation paths change. The isolated
version preparation is the existing serial exception, with one PATCH relative to
the current master. Native pass orchestration, queues, frame indices, resource
generations, fences, root constants, vertex layouts, texture slots and project
references retain their existing contracts. See [Decision.md](Decision.md) for
active wiring, bounds and intentional image changes.

## Findings and implemented behavior

Scaled composition previously evaluated four independently shaded samples,
including per-sample AA, and decoded their encoded output before reconstruction.
Those source paths do not establish executed sample counts or a GPU speedup.
Sharing the twelve-position union is valid only for four distinct adjacent
in-bounds centers; borders and one-pixel dimensions retain independent clamped
neighborhoods in the optional candidate.

The default scaled AA now constrains cross-depth neighbors before filtering:
outer reconstruction weights cannot undo color/fog already mixed across a
surface. Weights normalize only finite, nonnegative normal-sized sums, with a
nearest-sample fallback for malformed sums. Ordinary valid bilinear inputs already
retain a nearest tap of at least one quarter; no ordinary zero-sum black-pixel
failure is claimed. Per-sample fog, exposure/white balance, tone mapping, grading,
authored output tags and alpha retain their order and semantics. Native AA and
full-resolution overlay placement are not given new bindings or history.

Particle candidates now cover the full 4,096-effect copied snapshot before
admission. Visibility rejection precedes particle/fallback budget consumption;
event priority and stable identity replace first-entry preference. State remains
bounded to 2,048 emitters and the existing quality particle/coverage caps.
Cached definitions and reusable depth/index scratch reduce repeated CPU work;
initial particle payloads and the existing upload owner remain intact.
Current analytic view depth corrects anchor-distance ordering, with deterministic
ties. Admission and moving/off-axis ordering are intentional visual changes and
are qualified separately from byte-identical CPU sorting fixtures.

## Local validation and retained evidence

The validation host is Linux x64 with the repository SDK `10.0.401`, .NET runtime
`10.0.12`, PowerShell and official Linux DXC. It supplies managed compilation,
selected CPU tests and static shader compilation, not Windows/D3D12 or a physical
performance GPU. Exact commands, exits, timestamps and source-file hashes are in
[evidence/local-validation](evidence/local-validation/index.json). Only absolute
validation-root prefixes in retained logs were normalized; original hashes remain.

| Check | Observed result and limit |
| --- | --- |
| Complete dependency restore | Passed after fetching omitted same-commit tool/benchmark projects into the sparse checkout. The first attempt failed on the incomplete checkout, not on a source repair. |
| Release solution build | Final functional source `8331e384` passed in 103.93 s; after the master reconciliation, `e121263a` passed in 98.81 s. Both exit 0 with zero warnings/errors. Earlier runs and source-file hashes remain retained. |
| Project references | Existing validator passed all 44 projects, without a cycle. |
| Ownership and formatting | All 53 functional/evidence paths pass the current ownership validator; whitespace checks pass. `Directory.Build.props` is validated separately as the isolated version exception. |
| New portable particle classes | 21 regression cases passed. The final measured run added the opt-in qualification case: 22/22 passed, zero failed/skipped. All 21 regressions passed again after the current-master reconciliation. Coverage includes full snapshot admission, saturation, visibility, pause/session reset, deterministic sorting and warm allocations. |
| Required existing three-class Presentation selection | 26 executed: 13 passed and 13 failed. Every failing SimpleInstanceRenderer case stops in the unchanged native DXC call with `error: invalid profile` on this Linux host. This is a failed local run, not a passed Windows gate. |
| New native composition cases | 21 cases compile; Windows/WARP execution remains open. Actual 4K product capture is distinct from virtual 4K coordinate probes. |
| Static DXC matrix | 98/98 compiled with production Release flags. Complete source, HLSL, DXIL, disassembly, compiler hashes and per-case results are retained. |
| CPU ordering qualification | Exact payload parity and allocation windows are retained separately from timing acceptance; see [CPUQualification.md](CPUQualification.md). |

The complete compiler evidence is in
[evidence/compiler/README.md](evidence/compiler/README.md). The tested shader hash
is `b3752472dcc3d1d6bae32ae7c91d9347a99eed924807a6b1f637a351abe3094a`,
matching the committed composition source. The original pre-commit run identity
and dirty-source marker are retained alongside this later verified mapping.

Static scaled runtime-AA totals are **LLVM IR instructions / texture-load call
sites**, covering all compiled branches, not executed operations or device ISA:

| Atmosphere | Baseline | Current default | Shared-linear candidate |
| --- | ---: | ---: | ---: |
| Disabled | 3,039 / 25 | 3,505 / 41 | 5,541 / 61 |
| Enabled, haze off | 4,968 / 45 | 5,426 / 57 | 8,602 / 89 |
| Enabled, haze on | 6,008 / 45 | 6,466 / 57 | 10,266 / 89 |

The default adds depth-continuity work for correctness. Candidate totals include
both shared-interior and border-fallback code and may imply register-pressure
risk, but do not establish a runtime regression or improvement. No candidate
activation follows from these counts. GPU duration is unavailable, not zero.

## CI and acceptance

The initial-master main run [39485465207](https://github.com/DaveBeusing/Forgeline/actions/runs/39485465207)
was cancelled after the newer merge: steps 1-54 passed, rendering benchmark step
55 was cancelled, and full Test step 59 and later product gates were skipped.
This was not a product-test failure or a complete accepted baseline.

New-master run [39488943266](https://github.com/DaveBeusing/Forgeline/actions/runs/39488943266/job/118856962021)
failed at version validation before restore/build/tests. Its raw job log confirms
expected `0.1.425`, received `0.1.424` relative to the previous master. Movement
run [39488943365](https://github.com/DaveBeusing/Forgeline/actions/runs/39488943365)
passed. [Baseline CI evidence](evidence/local-validation/baseline-ci.json) records
the exact refs, steps and diagnostic. The composition PR prepares exactly one
PATCH against the actual new-master version; it does not rewrite merge history.

The earlier PR #266 source had a successful main CI run
[39481874522](https://github.com/DaveBeusing/Forgeline/actions/runs/39481874522).
That result does not convert the cancelled or version-blocked master runs into
full-suite passes. Historical Game/allocation/frontend failures remain prior
observations; their repairs were inherited through the current-master merge.

The draft PR records its exact final head, current CI runs and conclusions. A
running workflow or an earlier green commit is not final-head acceptance. No
complete Windows-suite or physical-GPU success is claimed by these local records.

Remaining mandatory gates:

1. Exact-head Windows CI, including the owned native cases, affected/full suites,
   runtime assets and source-less package checks.
2. Fixed-state and moving native source-less comparisons for thin geometry,
   chromatic edges, sentinels, fog/haze/depth, alpha and full-resolution HUD/text.
3. Particle saturation, changed event admission, fallback visibility, moving
   transparency and sustained resource/memory plateaus.
4. Paired real-hardware composite/particle pass distributions, identical assets,
   settings, camera, adapter/driver and thermal conditions, with raw samples and
   separate upload/overdraw/binding evidence. Missing counters stay unavailable.
5. Explicit human image review and a decision on whether the shared-linear
   candidate merits enabling for any measured profile.

[Reproduction.md](Reproduction.md) contains the exact commands and matrices.
Pass timings can overlap and are not summed. Visual reductions never count as
identical-output speedups. No thresholds are relaxed, goldens automatically
accepted, or tags, releases or master merges performed.

## Resume and serial handoff

Read the actual branch/PR/CI state first. Reconcile any newly merged master and
its one-PATCH requirement before integration. The next external action is native
Windows and hardware qualification using the retained source identities; failed
cohorts must remain available. The shared-linear default stays zero until those
gates approve it.

No new public API or shared workflow change is needed. The serial documentation
owner can link this decision and qualification from the central rendering and
performance documentation after acceptance; preserve the existing timing and
ownership definitions. Open required gates keep the status **implemented, not
yet accepted**.
