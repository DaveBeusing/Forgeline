# Precompiled runtime asset qualification

## Host and scope

Measured on 2026-10-12 with .NET SDK 10.0.401 / runtime 10.0.12, Intel Core Ultra
9 285HX (24 logical processors), NVIDIA RTX PRO 5000 Blackwell Generation Laptop
GPU, driver 32.0.15.9653. Compiler processes used `DOTNET_PROCESSOR_COUNT=4`,
resulting in two BC7 band workers. Inputs were the same 348 assets from master
`b67d142b1587d6dca8b5ef9b3da793a57c179825`. Baseline used compiler 1.8.0 from
that revision; verified output used 1.9.0. No importer, renderer or payload-format
algorithm changed.

Full compilation means an empty runtime-output cache (`--clean`), not an evicted
OS file cache. No-op means a subsequent launch with unchanged source/output.
The table uses isolated compiler runs; an earlier baseline that overlapped an
asset-test run is excluded. Native startup samples ran separately from compiler
and test workloads. OS-cold startup after reboot/cache eviction remains a manual
acceptance check; no OS-cold result is claimed here.

## Offline compilation and qualification

| Measurement | Baseline | Verified output | Observed delta |
| --- | ---: | ---: | ---: |
| Full compilation, 348 rebuilt | 318.731 s | 263.521 s | -55.210 s |
| Full runtime qualification | 257.124 ms | 367.270 ms | +110.146 ms |
| No-op compilation, 0 rebuilt / 348 reused | 2.405 s | 1.908 s | -0.497 s |
| No-op full qualification | 554.644 ms | 443.503 ms | -111.141 ms |
| Full-run peak working set | 533.25 MiB | 530.91 MiB | -2.34 MiB |
| No-op peak working set | 114.92 MiB | 119.87 MiB | +4.95 MiB |

These are single wall-clock samples, not a causal speedup or memory guarantee.
Compression is unchanged, and verification adds SHA-256 reads; the observed
lower compile times must not be attributed to integrity checks. Host scheduling,
file-cache state and sustained CPU behavior remain sources of variance. Full
texture compression remains the offline cost; the player executes none of it.

Every stable ID, type, runtime path and dependency list matched the baseline.
All 348 payload byte sequences matched exactly. Container metadata gained the
integrity fields, so container hashes intentionally differ. Runtime containers
occupy 57,491,011 bytes (165 meshes, 56 textures, 127 materials); full-container
hashes include header and metadata as well as payload.

## Installed player and native startup

Both cohorts use a Release package, skip the optional studio intro and measure
three warm launches after one excluded warmup. Native small/large window icons
were observed in every launch. Values below are medians; milestones are measured
from managed process entry, not OS process creation.

| Measurement | Baseline | Verified player | Observed delta |
| --- | ---: | ---: | ---: |
| Native first feedback milestone | 309.955 ms | 181.527 ms | -128.428 ms |
| Manifest-loading phase duration | 140.485 ms | 144.388 ms | +3.903 ms |
| Boot-renderer phase duration | 1,695.173 ms | 985.610 ms | -709.563 ms |
| First presented frame milestone | 2,687.205 ms | 1,601.561 ms | -1,085.644 ms |
| Interactive menu milestone | 2,721.732 ms | 1,637.294 ms | -1,084.438 ms |

Baseline menu samples ranged 1,471.688–3,107.662 ms; verified samples ranged
1,567.540–1,722.911 ms. The renderer was not changed. This variation and sequential
sampling do not establish a renderer/startup speedup. The added manifest check
remains small relative to graphics/JIT initialization, which is still the menu
startup bottleneck. Gameplay data is not eagerly hashed before menu readiness.

The verified player was copied outside the checkout with no source directory,
project or AssetCompiler executable. Its separate 1,000-instance native gameplay
smoke reached application/session Ready with zero dropped events. Required
asset validation completed at 1,383.650 ms, after session-load request at
1,301.725 ms. Gameplay renderer construction took 3,256.709 ms; the first
gameplay frame and session readiness occurred at 8,110.675 ms. These phases
include reconstruction/resource creation and are not isolated GPU-upload timers.
The GPU smoke reported 179 texture uploads, 52,707,564 resident texture bytes,
zero texture/material binding failures and a reported debug-layer error count of
zero. This Release device had the debug layer disabled; that counter does not
prove debug-layer validation.

Five installed-player negative runs covered missing manifest, unsupported
manifest format, damaged manifest signature, damaged required command-core mesh
and missing required command-core mesh. All exited 1 with startup diagnostics;
manifest faults had no requested session, payload faults marked the session
Failed. None invoked source compilation or claimed a ready match.

## Reproduction and validation

Use the commands in [Asset Pipeline](AssetPipeline.md#build-modes-and-deployment-integrity)
and [Startup timing](StartupDiagnostics.md). Preserve compiler JSON-lines records
outside the runtime cache. For comparison, run compiler full/no-op samples and
native startup separately from other compiler, test and GPU workloads; compare
payload bytes separately from container metadata.

The automated process contracts additionally verify separate writers blocked by
an independently held kernel lock, recovery after killing a live lock owner,
source-less validation and refusal to repair damaged deployment output. Asset
regressions cover transitive invalidation, unchanged inputs, compiler/settings
changes, missing integrity, incompatible versions, duplicate IDs, invalid
dependencies, damaged/truncated containers and interrupted-generation recovery.

Release publish was verified with two identical SHA-256 publish trees. Its
package has exactly the manifest-listed containers, records their content
signature/compiler version in provenance, retains runtime notices and excludes
authoring sources, compiler/encoder, locks and temporary files. A separate fresh
Git checkout started without bin/obj, runtime assets or prior restore to verify
the default build-before-publish path. Complete solution/gate outcomes are
recorded in the pull request; these timings do not claim optional hardware or
manual cold-start acceptance checks that were not executed.
Validation before synchronization completed 1,756 solution tests: 1,747 passed, nine optional
hardware/performance cases skipped, zero failed. Test modules were serialized
with --max-parallel-test-modules 1. An earlier parallel run under concurrent
clean-publish load timed out in the existing native session-handoff test; all
three handoff cases passed in isolation and the complete serialized rerun passed.
The final Release solution build had zero warnings/errors. Separate-process
contracts and all 25 focused pipeline regression cases also passed, including
qualification protected from a following clean writer.

## Synchronization with updated production assets

The final branch incorporates master `fad0f7a6` and uses product version
`0.1.348`. The production tank update expands the catalog to 351 assets
(168 meshes, 56 textures, 127 materials), occupying 58,408,475 container bytes.
The incremental update rebuilt seven affected assets and reused 344; the next
unchanged development run rebuilt zero and reused all 351. Full qualification,
separate-process contracts and the Release solution build passed again.

All 351 payload byte sequences, stable IDs, types, runtime paths and dependency
lists match compiler 1.8.0 using these updated source inputs. The verified
manifest signature is
`d9c386116edef0b96fba4327c533af4393e1969bc034020ec804137ddd69ab1f`.
The timing tables above retain the earlier, identical 348-asset cohorts; they
must not be treated as measurements of the larger updated catalog. Complete
synchronized regression and package outcomes are recorded in the pull request.

## Optional menu image deployment

The title still adds one compiled `branding.forgeline.menu.directorate` texture (493 canonical
assets total). It passes the same manifest/hash/deployment verification and is copied by the
normal reproducible publisher with the runtime notices. Its complete BC7 mip chain is about
3 MiB, with a strict 4 MiB loader bound; the existing 56 MiB gameplay-catalog and 64 MiB scene
budgets are not raised. Runtime fallback does not compile, download or repair artwork.
