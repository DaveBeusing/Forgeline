# Reproducible render qualification

The qualification tooling preserves one declared comparison matrix, immutable
source identities, native manifests, pixel files and command outcomes. It reports
which contracts were checked and which required measurements are unavailable.
Successful report generation is not product acceptance. See the
[qualification checkpoint](Qualification.md) for the actual source and validation
status, and [integration requirements](IntegrationRequirements.md) for metrics the
current product exporters do not provide.

## Ownership and active path

The entry point is
`build/rendering/qualification/Invoke-RenderQualification.ps1`. It calls the
existing `Invoke-ReferencePerformance.ps1`,
`Invoke-RtsReferenceQualification.ps1`, `Compare-ReferencePerformance.ps1` and
`Compare-VisualReferenceEvidence.ps1`; it does not maintain private copies of
their capture or comparison implementations. The existing source-less client
package runs the native reference renderer. The original native JSON remains the
input to `qualification.py`, alongside hashed command records and a frozen plan.

`qualification.py` validates evidence, recomputes native summaries from raw
samples and emits a separate report. `check_readiness.py` admits an optional
physical workflow only after trusted-source, required-CI, reviewed-calibration
and online-runner checks. Hosted contract tests cover failure handling without
claiming physical GPU performance.

The build, test, documentation and workflow paths are the qualification
workstream in the existing [ownership manifest](../parallelization/ParallelRenderOwnership.json).
No renderer, benchmark dispatcher, project reference, graphics layout, asset
compiler, budget or golden file is changed. Other rendering workstreams can use
the same cohort schema without changing their implementation files. The shared
version file has only the separately validated one-PATCH exception.

## Fixed comparison matrix

| Dimension | Recorded cases |
| --- | --- |
| Native scene | IndustrialBase, BattlefieldCombat, CombatSupportRepeated (`Vehicles1000` preset) |
| Quality | Medium, High |
| Display | 1920x1080, 2560x1440, 3840x2160 |
| Zoom | CloseTactical, NormalGameplay, Strategic |
| Warm camera | Fixed, Sweep, Traverse |
| RendererCold camera | Fixed |
| Raster DPI / UI scale | 96 / 1 |
| Repetitions | At least five baseline/candidate pairs |
| Warm collection | At least 512 frames after at least 64 warmup frames |
| RendererCold collection | 128 frames by default, with zero case warmup |

There are 54 scene/display/profile/zoom combinations and four camera/cache
variants: **216 cases per side per pair**. Five paired repetitions require 2,160
case observations before preflight and supplementary lifecycle work. The whole
plan is retained even when a machine cannot complete it. Native display clamping,
missing cases or lower warm sample counts cannot silently shrink the matrix.

The existing `Vehicles1000` preset maps explicitly to native scene
`CombatSupportRepeated`. Its source fixture creates 1,000 units: 250 each of
ScoutVehicle, MobileArtillery, CargoTruck and SupplyTruck, through simulation,
intelligence and presentation extraction. Its fixed cosmetic poses qualify
rendering, not a live 1,000-vehicle logistics or combat simulation. The mapping is
defined in `build/Invoke-ReferencePerformance.ps1` and
`src/ForgeLine.Presentation/CombatSupportReferenceScene.cs`.

The order alternates baseline/candidate and candidate/baseline. With five pairs,
the unavoidable order imbalance is one; additional pairs can balance it exactly.
The candidate does not receive different assets or quality settings in an
`optimization` comparison. A deliberate `visual-candidate` comparison is reported
separately and cannot be promoted to an unchanged-quality timing acceptance.

Battlefield effects and camera traversal exercise existing deterministic fixtures.
They do not constitute a 30-minute live match, repeated sessions or resize soak.
Those requirements remain distinct gates.

## Prepare inputs

Use clean, dedicated source checkouts at full commit SHAs. The current master,
integrated foundation and integrated repair are separate provenance descriptors;
historical measurements never become measurements of a newer source. The trusted
ownership record must show the foundation is accepted and released.

Build the source-less packages with the repository-pinned SDK and existing
`build/Publish-PreAlpha.ps1`. For an optimization comparison, pass the exact same
qualified runtime cache through `-RuntimeAssetRoot` when packaging both sources.
The collector validates package provenance and the bytes of package/runtime
inputs before capture, and checks that they did not change afterwards. A package
directory must not contain authoring sources. Prepare binaries and assets before
the measured run; do not compile inside a capture interval.

For example, after building both dedicated checkouts:

```powershell
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -OutputRoot artifacts/qualification-package
```

In the second checkout, set `-RuntimeAssetRoot` to the first checkout's qualified
`artifacts/qualification-package/compiled-assets` directory for an optimization.
Use the resulting `artifacts/qualification-package/win-x64/publish` directories
as the package inputs. Evaluate the exit code of every native command before
continuing.

## Plan and execute

A plan can be generated on hosted or local systems without running the product:

```powershell
pwsh ./build/rendering/qualification/Invoke-RenderQualification.ps1 `
  -PlanOnly `
  -AnalysisMasterSource <full-master-sha> `
  -BaselineSource <full-baseline-sha> `
  -CandidateSource <full-candidate-sha> `
  -OutputRoot artifacts/qualification-plan
```

The resulting cohort has open measurement gates. It contains no synthetic native
performance observations. Each output directory must be new.

The native entry point requires Windows x64, PowerShell 7.4 or later, Python,
the pinned .NET SDK and an actual qualified D3D12 execution environment:

```powershell
pwsh ./build/rendering/qualification/Invoke-RenderQualification.ps1 `
  -AnalysisMasterSource <full-master-sha> `
  -BaselineSource <full-baseline-sha> `
  -CandidateSource <full-candidate-sha> `
  -BaselineRoot <dedicated-baseline-checkout> `
  -CandidateRoot <dedicated-candidate-checkout> `
  -BaselinePackageRoot <baseline-source-less-package> `
  -CandidatePackageRoot <candidate-source-less-package> `
  -OutputRoot <new-evidence-directory> `
  -Repetitions 5 -WarmSamples 512 -ColdSamples 128 -Warmups 64 `
  -CapacityMinutes 720 `
  -ComparisonKind optimization `
  -HardwareQualificationEvidence <reviewed-hardware-json>
```

Keep evidence outside both source checkouts and both package directories. The
process journal records actual argument arrays, exit codes, timestamps and hashed
stdout/stderr. A failure or timeout is retained and stops collection; later cases
stay missing. The output is not reused for a rerun. The collector holds a named
machine mutex during physical collection and records competition observations.
Unrelated workloads outside that coordination still require a quiet, reviewed
execution environment.

Preflight estimates the declared full-matrix cost from actual native execution.
Insufficient capacity stops the attempt without reducing its plan. The capacity
budget is operational admission evidence, not permission to relax sample counts,
quality, resolution or timing thresholds. WARP can exercise the execution path
with `-Software`; it cannot satisfy a physical hardware gate.

## Statistics and integrity

Reports retain absolute milliseconds and changes for mean, p50, p95 and p99, as
well as minimum, maximum and sample standard deviation. The estimators match the
existing native renderer: p50 is the lower median; p95/p99 use nearest rank.
All captured samples, including outliers, remain in their distributions. Paired
run summaries preserve between-run spread; pooled frames do not create additional
independent repetitions. A zero baseline denominator produces unavailable
percentage change and an open timing gate.

Original CPU frames, GPU frames and native pixel files are verified against their
hashes. The validator rejects duplicate manifest use, conflicting source or
hardware identity, incomplete sample indices, contradictory unavailable values,
changed profiles, unexpected case keys, reordered or repeated GPU fences within
a capture stream, and reported summaries that differ from their raw values.
Fence sequences are scoped to their own device/capture generation. Separate GPU
passes may share a submitted fence and overlap in time; they are never summed
into a frame duration. Unavailable GPU timing remains `null` with a reason.

Hashes detect evidence changes relative to a preserved reviewed record. They do
not authenticate an arbitrary author, a claimed reviewer or a fabricated source
measurement. Review the calibration files, source provenance and actual execution
logs. The baseline record and native goldens remain read-only. No command here
approves changed pictures or replaces a golden reference.

Generate a fresh report from a completed or interrupted cohort:

```powershell
python build/rendering/qualification/qualification.py `
  --cohort <evidence-directory>/cohort.json --output <new-report.json>
```

CLI exit codes are 0 for a valid emitted report, 2 for invalid or tampered input
or a refused overwrite, and 3 when `--require-accepted` is requested and a required
gate is not accepted. A valid report can still have `status: open` or `failed`.
Use `--require-accepted` at an acceptance boundary. A failed prior attempt remains
part of the record; a later successful subset does not erase it.

## Independent evidence imports

The collector accepts `-GateEvidenceRoot <reviewed-evidence-directory>`. Its
`index.json` declares `schemaVersion: 1`, every payload file in a `files` array of
`{path, sha256}`, and `validationEvidence` / `supplemental` arrays of
`{kind, path, sha256}` references. The collector copies only that declared tree
into its fresh `imports/` directory. Missing, changed, unlisted or symlink payloads
are rejected. Each independent gate remains bound to both exact source SHAs and
the relevant runtime/hardware identity; adding an arbitrary pass flag cannot
close a gate.

Supported independent records are `required-ci`, `content-budget`,
`allocation-budget`, `gpu-memory-budget` and `visual-review`. Supported measured
supplements are `frame-envelope` and `lifecycle`. See the
[record contracts](IntegrationRequirements.md#independent-mandatory-acceptance-records)
before importing evidence. Actual live-frame, raw GPU and lifecycle producers
still need the coordinated product instrumentation described there.

Visual review and new supplementary measurements normally follow collection.
Preserve the original `cohort.json`, original manifests and original pixels.
Create a fresh derived cohort alongside it so its relative native paths retain
their meaning, include the original cohort hash in its inventory, and reference
new immutable evidence files. Run the same validator on that new cohort. Do not
edit an earlier failed report into a passed one or replace a native baseline.

## Optional workflow and hardware record

`.github/workflows/render-qualification.yml` runs hosted evidence, ownership,
PowerShell and focused .NET contracts. Existing main CI retains the complete
solution, budget, WARP, source-less-package and full-match gates.

The physical job is off by default. Manual dispatch must use the trusted master
workflow, full baseline/candidate SHAs already integrated into that master, and
an exact reviewed hardware record under `docs/rendering/qualification/hardware/`.
Its SHA-256 is supplied separately. The record contains:

| Field | Meaning |
| --- | --- |
| schemaVersion / status | `1` / `qualified` |
| reviewer / reviewedAtUtc / rationale | Explicit review, UTC time and reason |
| hardwareIdentitySha256 | Digest of the measured environment, runtime, driver, device, debug state and SDK |
| serialGpu / capacityMinutes | Exclusive GPU qualification and measured full-run capacity |
| maximumRegressionPercent | Existing unchanged value `15` |
| powerPlan / powerSource / thermalState | Reviewed power-plan identity, `ac`, `stable` |
| qualificationRuns | Nonempty `{path, sha256}` references to retained calibration files |

The identity digest uses UTF-8 canonical JSON with sorted keys, compact separators
and no ASCII escaping for `environment`, `runtime`, `driver`, `device`,
`debugEnabled` and `sdk`. The native validator recomputes it from the actual
manifest and run SDK; a supplied string or a bare `qualified: true` is insufficient.
Calibration paths are relative to the review record's directory. Byte-preserving
attributes cover `hardware/` and `evidence/` so line-ending conversion cannot
silently invalidate reviewed hashes.

The current collector always reports observed `thermalState: unavailable`;
portable Windows CIM does not establish physical GPU temperature, clock or
throttling stability. It also leaves power source unavailable when no battery
status is enumerated. Consequently, a reviewed hardware JSON alone cannot make
this collector's hardware gate pass. Captures may be retained, but acceptance
still needs the measured hardware-condition provider described in
[integration requirements](IntegrationRequirements.md#observed-hardware-conditions).
Review metadata must not replace those actual per-run observations.

Before scheduling the self-hosted job, a hosted preflight checks the most recent
mandatory `ci.yml` result for each exact source and the trusted workflow source,
following pagination. It then requires an idle online runner with all four labels:
`self-hosted`, `Windows`, `X64`, `forgeline-performance`. An inaccessible inventory,
missing evidence, failed CI or untrusted source leaves admission open and prevents
scheduling. Availability can change after admission, so an admitted job is still
not proof of completed execution.

Native execution uses read-only permissions and checkout without persisted
credentials. PR events never schedule self-hosted execution. The dedicated
`forgeline-performance` concurrency group serializes workflow captures. Final
`--require-accepted` preserves every unresolved mandatory gate in the workflow
outcome, and available attempt files are uploaded even after failure.

The native workflow admits a reviewed integral capture capacity of at most 600
minutes within its 720-minute job. Setup/build must finish within 60 minutes;
another 60 minutes remain for final reporting and artifact upload. Otherwise
collection does not start. These limits stop an attempt that cannot fit its
declared matrix; they never reduce that matrix. Local native execution can use a
separately reviewed capacity within the script's supported limits.

### Runner inventory credential

GitHub's [repository runner-list API](https://docs.github.com/en/rest/actions/self-hosted-runners#list-self-hosted-runners-for-a-repository)
requires repository **Administration: read**. That permission is absent from the
[available `GITHUB_TOKEN` permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions);
`actions: read` cannot authorize this endpoint. Main-CI history requests continue
to use the ordinary workflow token with `actions: read`.

An authorized repository administrator can configure the optional repository
Actions secret **`FORGELINE_RUNNER_INVENTORY_TOKEN`** with a fine-grained personal
access token limited to `DaveBeusing/Forgeline` and repository
**Administration: read**, plus GitHub's mandatory Metadata read access. This
inventory token needs no Contents, Actions or write permission. A separately
managed GitHub App installation token restricted to the same repository and
read permission also works; this workflow does not mint installation tokens.
GitHub documents these options under
[additional workflow-token permissions](https://docs.github.com/en/actions/tutorials/authenticate-with-github_token#granting-additional-permissions).

Only the hosted readiness step for a trusted manual `master` dispatch receives
this secret. `check_readiness.py` uses it only for paginated
`GET /repos/DaveBeusing/Forgeline/actions/runners` requests. It is not passed to
checkout, native collection or the self-hosted job, and its value is neither
printed nor written to reports, artifacts or persisted credentials. An absent
secret or a rejected inventory request leaves admission **OPEN** and prevents
native scheduling. There is no fallback to a broader token or assumed runner.

This change does not create or modify a repository secret, install a GitHub App
or configure a runner. The separately documented blocked connector request
remains the only inventory evidence from the baseline pre-check; no successful
inventory request or credential provisioning is claimed.

## Contract checks

```powershell
python -m unittest discover -s build/rendering/qualification -p 'test_*.py' -v
pwsh ./build/rendering/qualification/Test-RenderQualification.ps1
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class '*RenderQualification*' --parallel none
```

These contracts validate the qualification implementation. They do not supply
the native measurements, full live-client frame envelope, physical memory
plateau or human review described in the separate gate table.
