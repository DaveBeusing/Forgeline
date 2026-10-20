# Render qualification checkpoint

Status: **qualification tooling implemented; native product acceptance open**.
The implementation preserves evidence and enforces validation boundaries.
Unavailable product metrics and unexecuted hardware checks remain open. This
checkpoint contains no newly measured GPU speedup, complete-frame timing or
memory-plateau claim.

## Baseline pre-check, 20 October 2026

The checked current master is
`e42ae48346bbb1331b7a548a72fc0905ae6d184a`, product `0.1.424-dev`,
SDK `10.0.401` with `latestPatch`, C# 14 and Microsoft.Testing.Platform.
The older analysis source `874b1652bc272ef648120a0b1de9bc85edf5f70c`
is a historical anchor, not this implementation's baseline measurement.

The foundation is integrated at
`17810ee192fac495173a7e9da44c973595833f2b`; its repair is integrated at
`d4c7b62077c98500d6309cdd6c6dbf5e6bd6490c`. The accepted
[release record](../parallelization/Acceptance.json) is present on current master
with `parallelReleased: true`. PR #241 recorded explicit owner approval of the
six foundation images. That approval covers the foundation comparison and does
not accept later rendering changes or this qualification's native matrix.

At the pre-check there were no open PRs. The remote composition/effects branch
was an independent, disjoint rendering workstream. This qualification starts on
`testing/render-performance-qualification` from the checked master. No existing
qualification implementation was replaced.

## Current mandatory CI failure

[Current master CI run 39488943266](https://github.com/DaveBeusing/Forgeline/actions/runs/39488943266),
[job 118856962021](https://github.com/DaveBeusing/Forgeline/actions/runs/39488943266/job/118856962021),
failed at step 4, `Validate one version increment per PR`. The full fetched job
log reports at 20:07:10 UTC that the policy expected **0.1.425** but received
**0.1.424**. Its explicit version base was
`cdf97fb9a8ef277cf75d451476a21128d398007c`, and its head was the checked
`e42ae48346bbb1331b7a548a72fc0905ae6d184a`.

All subsequent build, complete-test, content, WARP, window-mode, benchmark,
source-less package and full-match validation steps were skipped. Consequently,
this run does not establish a current content-budget or allocation result.
[Movement quality run 39488943365](https://github.com/DaveBeusing/Forgeline/actions/runs/39488943365)
passed for the same master, but does not replace the required main CI.

[Earlier main CI run 39481874522](https://github.com/DaveBeusing/Forgeline/actions/runs/39481874522)
was verified successful at source
`ef83d2d586aa2f0059bc6b860f95a1bc085ef917`. Its job steps include successful
runtime assets, full tests, native WARP, windows and packaging. This is a
different source; it is not substituted for the failed integrated master run.

The historical 8,104-byte allocation discrepancy and 99,608,050 compiled texture
bytes described by the earlier analysis were not reproduced in the current
pre-check. The latter refers to compiled gameplay texture/container content
against a 56-MiB gate, not physical GPU memory. Current master CI stopped before
those checks. Their old values must not be serialized as current observations.

The workstream's isolated PATCH preparation follows the current master-plus-one
policy. A later successful PR run can establish its own source validation; it
cannot rewrite the failed baseline run or manufacture missing baseline samples.

## Available execution and runner evidence

The local execution system provides Python and Git. `dotnet` and `pwsh` were not
on PATH, and no Windows/D3D12 execution surface was available at the pre-check.
The existing `frame-hotpaths.yml` uses `windows-latest` for functional and
allocation checks, not a verified physical performance runner.

The read-only repository runner-inventory request to
`GET /repos/DaveBeusing/Forgeline/actions/runners?per_page=100` was rejected by
the available GitHub connector with `HTTP 400 / INVALID_ARGUMENT` because the
endpoint was not allowed. **Runner availability is unverified.** This response
does not prove that no runner exists. No runner was installed and no physical
workflow was queued based solely on label names.

The optional workflow requires its own successful hosted inventory check before
scheduling native execution. A permissions error, unavailable runner or failed
exact-source CI produces an open admission result. WARP and hosted contracts
cannot stand in for qualified hardware.

## Gate table

| Gate | Current evidence boundary |
| --- | --- |
| Foundation and path ownership | Integrated accepted manifest; dedicated paths validated separately |
| Evidence/statistical contracts | Executable Python and PowerShell regression suites; final run records accompany the implementation commits |
| Focused real-sample regression | Client sample boundary tests plus the existing renderer/extraction/index classes |
| Full source-pinned CI | Pre-existing master version failure; candidate checks must be evaluated at their actual SHA |
| Compiled content | Current baseline run skipped the gate; no invented byte result |
| Native paired timing | Unexecuted for this qualification; physical-runner admission unverified |
| Observed hardware stability | Current collector reports thermal state unavailable; a measured condition provider is required even with a reviewed calibration record |
| Complete live-client frame | Exporter lacks the enclosing host interval; supplemental producer requires serial integration |
| Raw GPU timestamp proof | Derived native timings available in the format; original tick/completion observations require serial export |
| Full GPU memory and allocation budgets | Scoped existing counters are insufficient for the complete independent gates |
| Moving 30-minute lifecycle | Required actual workload and comparable resource plateau remain unexecuted |
| Native pixel and motion review | No new review decision; previous foundation approval is kept separate |

See [integration requirements](IntegrationRequirements.md) for exact missing
records and frozen owner paths. A report with one open or failed required gate is
not accepted. The physical workflow uses `--require-accepted` and retains that
status even if individual capture commands return success.

## Resume and integration

Read the actual branch commits, PR checks and retained attempt files first.
Revalidate current master and the one-PATCH delta before integration. The
remaining sequence is to finish the implementation's hosted validation, obtain
source-specific successful mandatory checks, verify reviewed hardware/capacity,
execute the fixed native matrix and lifecycle workload, bind the remaining
product evidence through serial integration, and record exact human review.
No rerun, updated image, merge, tag or release is authorized by this checkpoint.

This implementation does not repair frozen renderer or central workflow files,
raise content/timing budgets, disable tests or accept new goldens. Required
out-of-scope failures remain visible with their source and reproduction context.
