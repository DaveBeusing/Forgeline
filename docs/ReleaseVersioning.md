# PR Versioning

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

FORGELINE increments the tracked PATCH exactly once per pull request.
This repository policy supersedes the automatic preview/RC allocation and
publication mapping introduced with Unified Versioning Standard 1.0.
The standard remains a historical reference; its release-only product-version
rule does not govern the current PR increment policy.

## Preparing a PR

Fetch current master, then run:

```powershell
pwsh ./build/Increment-PrVersion.ps1 -BaseRef origin/master
```

Commit the resulting Directory.Build.props change with the PR. Repeating the
command against the same base leaves the version unchanged. Additional commits,
CI attempts and master builds do not consume PATCH. CI requires the PR head to
contain exactly base PATCH + 1, with unchanged MAJOR/MINOR. Rebase concurrent
PRs onto current master and prepare their version again before integration.
MAJOR/MINOR boundaries require a separate policy change; PATCH never wraps
beyond the .NET numeric metadata limit.

This policy change itself advances 0.1.337 to 0.1.338.

## Sequential integration and reconciliation

When two PRs both branch from PATCH 417, both may initially prepare PATCH 418.
The first merged PR consumes 418; the second must synchronize with the new
`master` and prepare 419 before merging. A successful Git merge does not
guarantee that the version contract was preserved. Verify the PR base is still
current and rerun the SemVer and CI gates immediately before integration.

On 20 October 2026, the radial HUD correction (PR #254) and cargo docking
recovery (PR #255) were merged with the same 0.1.418 version. The resulting
`master` commit `f2be6532d759ab0840f2a4674c28d00446fe75ea` still
carried 418, while its version validation expected 419. The reconciliation
advances only PATCH to 419 from that exact master base; no historic merge or
release tag is rewritten. This is an integration-history correction, not a new
MAJOR/MINOR allocation or an automatic build-time version change.

## CI and metadata

CI has contents: read and performs no remote writes. The version-assignment
script only sets a local build environment variable. There is no automatic tag
creation, preview/RC reservation, publication claim or GitHub release job.
PR builds use X.Y.Z-pr.PR.RUN.ATTEMPT; master and manual validation builds use
X.Y.Z-dev. Run and attempt identify validation artifacts without consuming a
product version or claiming a published release.

Version and PackageVersion retain the full validation identity. AssemblyVersion
remains MAJOR.MINOR.0.0; FileVersion remains MAJOR.MINOR.PATCH.0.
InformationalVersion retains the full source SHA through the SDK. Package
checksums and source provenance remain in place. Existing tags and history are
preserved. CI artifacts remain available through the workflow's retention policy.

Release tags and GitHub releases, if wanted, require an explicit separate manual
release procedure. No release is published by a PR, master push or CI rerun.

## Validation

Version contracts cover missing/double increments, .NET limits, idempotent
preparation, explicit Git refs, PR/master identities and tag-free reruns.
The isolated repository test exercises the actual scripts and rejects any GitHub
transport call. Run:

```powershell
pwsh ./build/Test-Versioning.ps1
pwsh ./build/Test-VersionAllocation.ps1
pwsh ./build/Validate-SemVer.ps1 -BaseRef origin/master
```

The existing build, test, architecture, asset, smoke and match gates remain in CI.
This change does not modify simulation, rendering, native startup or game logic.
