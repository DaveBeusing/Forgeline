# Release versioning

## Adoption and migration

FORGELINE adopts Unified Versioning Standard 1.0 through this technical change.
The original policy document is retained in [UnifiedVersioningStandard.md](UnifiedVersioningStandard.md).
This adoption supersedes the per-commit PATCH rules from the earlier versioning
implementation. Other architecture, governance, PR and no-squash rules remain.
No historical commit, version, tag or artifact is rewritten.

The migration baseline is master `bc6676894edc824ad746ed21eca660fea40e4734`,
with tracked version `0.1.333`. No existing release tags or GitHub Releases
were observed. Keep this version line as the transitional target; it is not
retroactively declared stable. Select the next release target deliberately from
actual scope, not from commit count or the examples in the standard.

## Product scope and public contracts

The game, ForgeLine Engine libraries and repository tools currently share one
release stream. Tags use `vX.Y.Z`; there are no independently released SDKs.
Externally consumed contracts include supported CLI commands, configuration,
save/replay formats, compiled map/assets and documented game behavior.
Internal classes, refactors, tests, documentation and CI are not automatic
public-contract changes. Schema versions remain independent of product SemVer.
This repository does not currently own a database migration stream.

Release scope chooses MAJOR for incompatible public-contract changes, MINOR
for compatible features and PATCH for compatible fixes. Reset lower components
when advancing MAJOR/MINOR. Before 1.0, conventionally use MINOR for notable
features or intentional contract changes and PATCH for compatible fixes;
0.y.z provides no SemVer stable-API guarantee. Document decisions, migrations,
compatibility and rollback in `docs/releases/X.Y.Z.md` before RC/stable dispatch.

## Build identity and .NET mapping

The three tracked fields in `Directory.Build.props` define the release target.
Change them once for a reviewed release scope. Never increment them per commit,
derive PATCH from Git history or commit CI-generated versions back to a branch.

| Context/property | Mapping |
| --- | --- |
| Local and auxiliary qualification builds | `X.Y.Z-dev+FULLSHA`; non-published |
| PR validation | `X.Y.Z-pr.PR.RUN_ID.ATTEMPT+FULLSHA`; non-published |
| master integration | `X.Y.Z-preview.N+FULLSHA` |
| explicit candidate dispatch | `X.Y.Z-rc.N+FULLSHA` |
| explicit stable dispatch | `X.Y.Z+FULLSHA` from annotated `vX.Y.Z` |
| Version and PackageVersion | Full SemVer without SHA metadata |
| AssemblyVersion | `MAJOR.MINOR.0.0`, stable within the compatibility line |
| FileVersion | `MAJOR.MINOR.PATCH.0`, independent of channel counter |
| InformationalVersion | Version with SDK-appended full SourceRevisionId exactly once |

Numeric version components are limited to 65534 for .NET/Windows fields.
The migration deliberately normalizes historical per-commit assembly identities
to `0.1.0.0`; precompiled external consumers of old library identities must be
rebuilt. No stable SDK binding compatibility is promised by the 0.y.z baseline.
The menu displays version/channel and 12 SHA characters; crash reports record
the full informational version, channel, full SHA and CI run/attempt ID.
PR provenance identifies the tested synthetic merge commit.
Local dirty sources can differ from the SHA; local builds are never official
artifacts, and published builds require a clean tracked/untracked source tree.

## Durable allocation and reruns

`Set-BuildVersion.ps1` reserves annotated tags `vX.Y.Z-preview.N` or
`vX.Y.Z-rc.N` through GitHub's atomic Git-ref creation. It scans the matching
ref registry, picks the next positive number and retries only confirmed
collisions. Counters are separate per target and channel and start at 1.
Do not delete/move reservations: failed or cancelled runs leave permanent gaps.
An orphan annotated tag object after a collision is not a reserved number.

CI run numbers and commit counts are not allocators. Every prerelease rerun
reserves a new number, including attempts of the same commit with different
inputs. Use **Rerun all jobs**: partial release-job reruns are rejected when the
allocation belongs to an earlier attempt. Validation artifact names include
version and attempt, so prior uploads cannot collide with a new run.
Once a stable tag exists, the line is closed to further prereleases.
Stable reruns fail on any existing release, including incomplete drafts.
Release creation/upload never updates, clobbers or deletes existing assets.
Publication also creates the atomic permanent claim
`release-publication/vX.Y.Z[-channel.N]`. Claims prevent concurrent publication
or retry after interruption under the same version, independently of release
draft behavior. Existing releases and drafts are checked with pagination.

PR jobs have read-only repository permissions and never reserve tags or run
publication. The write-permitted allocation job executes only on canonical
master. Auxiliary workflows produce only local development identities.

## Validation and publication

The existing CI build-test job retains its full build, tests, architecture,
map/asset qualification, visual smoke, benchmarks, recovery, reproducibility,
fresh-install and natural terminal-match gates. Version-contract checks are
added. No gate is removed or weakened.

On master, allocate preview identity before building; publish only after all
quality gates pass. RC/stable use the CI workflow's explicit dispatch from
master with channel `rc` or `stable`. Stable additionally requires an existing
annotated tag at the exact current master SHA, supplied as `release_tag`.
For example, after scope approval: `git tag -a vX.Y.Z <fullsha> -m "FORGELINE X.Y.Z"`,
then push that one tag. Never move or recreate a released tag.

The publication job downloads the package from the successful build-test job
in the same run. It never rebuilds. Packaging includes an internal SHA-256 file
manifest and build provenance; the release carries the ZIP, ZIP SHA-256 and
external provenance containing the same full version/SHA, repository, UTC time,
run/attempt, framework, runtime, configuration, SDK and source cleanliness.
Reproducibility compares unpacked file trees with identical inputs/timestamp;
it does not claim that ZIP timestamps are byte-reproducible across runs.

GitHub publication creates a draft, uploads the tested files, then publishes
under native immutable-release enforcement and checks the immutable readback.
Interrupted drafts are retained for investigation and block retries. Choose a
new prerelease version; stable failures require deliberate release-owner review.

Validation packages have 30-day Actions retention. Published GitHub release
assets, checksum, provenance and tag are retained indefinitely under native
immutability; they must not be deleted. Rollback installs a retained release
package rather than rebuilding or replacing it.

## Required repository configuration

These settings are outside source-controlled implementation and remain release
activation blockers until an administrator applies and verifies them:

- Protect master and require the complete `build-test` check, up-to-date PRs,
  no force-push/deletion and the established no-squash convention.
- Protect `v*` tags against update/deletion, including unused reservations.
- Protect `release-publication/*` claims against update/deletion.
- Enable GitHub immutable releases.
- Configure the `release` environment to allow master only and require the
  release owner's review as appropriate for candidate/stable authorization.
- Supply environment secret `RELEASE_ADMIN_READ_TOKEN` with only repository
  Administration:read. It verifies immutability because GITHUB_TOKEN has no
  administration access; uploads use the scoped GITHUB_TOKEN.

Publication fails closed if the setting/token cannot be verified. These
requirements do not imply that the settings have already been applied.
The adoption audit found master unprotected and an empty ruleset collection.

## Validation commands

```powershell
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master
pwsh ./build/Test-Versioning.ps1
pwsh ./build/Test-VersionAllocation.ps1
pwsh ./build/Test-ReleasePublication.ps1
pwsh ./build/Validate-ProjectReferences.ps1
dotnet build ForgeLine.sln --configuration Release
dotnet test --solution ForgeLine.sln --configuration Release --no-build
```

Build metadata regressions run in the Client tests. CI supplies
`FORGELINE_BUILD_VERSION` for its complete build/package path; this environment
value must not be changed between compilation, tests and packaging.
