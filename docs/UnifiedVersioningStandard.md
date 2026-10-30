# Unified Versioning Standard

> [!NOTE]
> **Historical cross-project proposal.** FORGELINE currently follows [PR Versioning](ReleaseVersioning.md): one PATCH increment per PR, no automatic tag or release publication. The proposal below retains its original adoption-dependent scope.

**Status:** Proposed project-wide policy\
**Applies to:** .NET-based software projects, including Depot, DepotManager, rtaime, FORGELINE / ForgeLine Engine, Singularity, Sentum, and related tools where adopted\
**Version of this document:** 1.0\
**Effective date:** Upon project-specific adoption

## 1. Purpose

Provide a consistent, auditable and automatable approach to product releases, development builds and source-code traceability across independently versioned projects.

**Core rule:** A Git commit is not a product release. **Do not increment SemVer PATCH on every commit.**

## 2. Version identity

Three distinct identifiers serve different purposes:

| Identifier | Example | Purpose |
|---|---|---|
| Product version (SemVer) | `0.16.0` | Product/API compatibility and release progression |
| Pre-release identifier | `0.16.0-preview.42` | Ordered builds of the upcoming release |
| Source revision | `g7f3a91c` | Exact Git commit used to build an artifact |

An informational version may combine them:

```text
0.16.0-preview.42+g7f3a91c
```

Build metadata after `+` does **not** affect SemVer precedence. Do not rely on the hash for chronological sorting. For the strongest traceability, record the **full** commit SHA in release manifests and build provenance; abbreviated hashes are for display only.

## 3. Semantic Versioning rules

Use `MAJOR.MINOR.PATCH` and the SemVer 2.0.0 precedence rules.

| Change | Release version action |
|---|---|
| Incompatible change to a defined public contract | Increment MAJOR, reset MINOR and PATCH |
| Backwards-compatible feature within a defined public contract | Increment MINOR, reset PATCH |
| Backwards-compatible bug fix | Increment PATCH |
| Internal refactoring, tests, documentation, CI-only changes | No automatic product-version increment |

**Version `0.y.z`:** SemVer treats `0.y.z` as initial development; compatibility is not guaranteed. Projects should still *conventionally* use MINOR for notable features or intentional contract changes and PATCH for compatible fixes, but must not imply a SemVer-guaranteed stable API until `1.0.0`.

The public contract must be specified per project (for example public SDK/API, persisted data formats, configuration, CLI, externally consumed behavior). A change to a purely internal implementation is not necessarily a breaking public change.

## 4. Release channels

Use the following pre-release identifiers:

- `preview.N`: integrated development builds before release stabilization.
- `rc.N`: release candidates that meet the project's release-candidate gate.
- no suffix: officially released version.

`N` is a positive, monotonically increasing integer **within its particular base version and channel**. Each new product version resets the sequence to `.1`.

Example progression:

```text
0.16.0-preview.1
0.16.0-preview.2
0.16.0-preview.3
0.16.0-rc.1
0.16.0
0.16.1
0.17.0-preview.1
```

**Important:** `0.16.0-rc.1` has lower SemVer precedence than `0.16.0`; `preview` and `rc` are release stages. Do not substitute a generic CI run number for `N` without ensuring uniqueness and the intended ordering policy.

## 5. Branch, PR and CI policy

### 5.1 Source control

- The protected default branch is `master` where already established; individual repositories may retain their existing default branch. This standard does not require a rename.
- Develop through short-lived branches and pull requests where repository governance requires them.
- **Do not modify a tracked PATCH number as part of every commit.**
- Do not commit CI-generated version changes back to the source branch.
- A merge is not automatically a product release.

### 5.2 Build types

| Context | Version behavior | Distribution |
|---|---|---|
| Local build | Deterministic development label, e.g. `0.16.0-dev.<shortsha>` where supported, or a non-published local version | Not an official release |
| PR validation | Temporary PR-specific identifier, e.g. `0.16.0-pr.123.4+g<sha>` | Validation artifacts only; never published into the release feed |
| Successful integration build on default branch | Allocated unique `preview.N` | Internal/nightly/pre-release feed as configured |
| Release candidate pipeline | Explicit `rc.N` | Candidate feed |
| Tagged release | Exact `MAJOR.MINOR.PATCH` | Official release channel |

Only publish build versions to a shared package/feed if the full version is unique within that feed. Reserve version numbers atomically or use a durable allocator, and prevent reruns from publishing different artifacts under the same version. The same commit rebuilt with different inputs is not assumed byte-identical.

### 5.3 Preview numbering

`preview.N` must be allocated centrally per repository, target release and channel. Recommended: a CI-maintained monotonic sequence or an explicitly persisted version counter/registry. GitHub Actions `run_number` is repository/workflow-specific and may have gaps; it is acceptable only when its uniqueness and ordering characteristics fit the chosen feed and versioning scope. **Do not use commit counts alone as a concurrency-safe version allocator.**

Release pipelines must fail on version/tag collisions rather than silently overwrite an existing artifact.

## 6. Git tags and release ownership

- Create annotated release tags in the format `vMAJOR.MINOR.PATCH` (example: `v0.16.0`).
- For an RC that is intentionally published, tag `vMAJOR.MINOR.PATCH-rc.N` if the repository's release process uses tagged candidates.
- A stable product version is immutable: do not move tags or replace published release binaries.
- Bug fixes after release result in a new PATCH release, not an overwrite.
- Each project/versioned deliverable has its own version stream; no cross-repository synchronized PATCH counter.
- In a monorepo, decide explicitly whether products share one release version or have separate tags (e.g. `component/vX.Y.Z`). Document the decision before publishing.

## 7. .NET version metadata

Keep assembly identity, Windows file versions and product/informational versions separate.

| MSBuild property | Policy |
|---|---|
| `Version` | Full product or pre-release version, e.g. `0.16.0-preview.42` |
| `VersionPrefix` / `VersionSuffix` | Optional mechanism to assemble `Version`; avoid conflicting declarations |
| `PackageVersion` | Usually mirrors `Version` for published NuGet packages |
| `InformationalVersion` | Include full SemVer and exact/recoverable revision information; avoid duplicate commit-hash suffixes generated by the SDK |
| `AssemblyVersion` | Stable assembly-compatibility identity as required; do not increment for every build |
| `FileVersion` | Numeric four-component Windows resource version, managed separately and within field limits |

Example central project settings (illustrative; CI passes the build-specific values):

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>0.16.0</VersionPrefix>
    <AssemblyVersion>0.16.0.0</AssemblyVersion>
    <FileVersion>0.16.0.0</FileVersion>
  </PropertyGroup>
</Project>
```

For a preview build, CI can pass `-p:Version=0.16.0-preview.42` and set or validate the informational version. Recent .NET SDKs may automatically append the Source Revision ID to `InformationalVersion`; inspect actual build output before adding a hash manually. `AssemblyVersion` and `FileVersion` do not use hyphenated SemVer suffixes.

**Do not auto-update tracked `Directory.Build.props` or source files per commit.** If multiple components have independent release versions, use component-specific configuration rather than forcing one global `VersionPrefix`.

## 8. Build provenance and reproducibility

Every published artifact must retain, at minimum:

- Product and full build version.
- Full Git commit SHA and repository identity.
- Build date/time in UTC and CI run identifier.
- Target framework/runtime identifier, configuration, SDK/toolchain version.
- Release channel and artifact checksum (SHA-256 recommended).
- Source tree cleanliness/approved inputs where relevant.

Prefer deterministic compilation, locked dependencies and pinned SDK/toolchains. A version number identifies the release, but **does not by itself prove** two compiled binaries are identical.

The application's About/Diagnostics view should display product version, channel, shortened commit SHA and optionally CI build ID. Logs and support bundles should record the same identity.

## 9. Database schema versions

**Database schema versioning is independent of product SemVer.**

- Increment schema version only for an actual schema migration according to the project's migration policy.
- Store a durable migration identifier and applied migration history, not just a product PATCH number.
- Define supported application↔schema compatibility and migration/rollback behavior per project.
- Depot's existing rule requiring a database schema version increment for schema changes remains in force.

A UI-only patch need not modify the schema version; a schema migration can occur during preview development before any stable release.

## 10. Release process

1. Define the public contract and select the next target product version from the actual release scope.
2. Set the target version once in source-controlled release configuration, not at each commit.
3. Validate PRs using ephemeral build identifiers.
4. After integration to the default branch, allocate and publish `preview.N` only for eligible builds.
5. Freeze/review the release scope; publish `rc.N` if a candidate phase is needed.
6. Execute the repository's test, security, packaging and release quality gates.
7. Produce the stable artifact from an explicitly identified commit and create immutable tag `vX.Y.Z`.
8. Publish checksums, source revision, release notes and compatibility/migration notes.
9. Begin the next cycle with a newly selected target version.

## 11. Migration of existing projects

### Depot

Previous policy: increment `DepotVersionPatch` for every commit. **Replace this only upon explicit adoption of this standard.**

Suggested transition:

1. Retain historical versions and tags; never rewrite released history.
2. Stop commit-by-commit PATCH increments once the replacement policy is adopted.
3. Keep the current version line intact until an intentional version boundary.
4. For the **next planned minor development cycle**, `0.16.0-preview.1` is an example only, contingent on release planning and the version actually in `master` at migration time. Do not automatically assume `0.15.169-preview` is still the latest version.
5. Implement a repository-specific CI preview allocator and expose version + SHA in diagnostics.
6. Keep Depot database schema versioning independent and unchanged in principle.

### rtaime / FORGELINE / other repositories

Adopt the policy without changing the established protected-branch, PR, commit-message or product governance rules. Select each product's target version from its own actual release state. Existing release artifacts and tags remain authoritative.

## 12. Mandatory acceptance checks

A repository conforms to this standard when:

- [ ] No per-commit tracked SemVer PATCH increment remains.
- [ ] SemVer changes are driven by release scope and public-contract impact.
- [ ] Preview versions are unique and allocated safely under parallel CI runs.
- [ ] PR builds cannot overwrite official package/release versions.
- [ ] Each released artifact maps to one immutable tag and full Git SHA.
- [ ] About/log output exposes sufficient identity to trace a defect.
- [ ] `AssemblyVersion`, `FileVersion`, `InformationalVersion` and package versions have documented mappings.
- [ ] DB/schema migration versions are separately governed.
- [ ] Release artifacts and checksums are retained according to project policy.

## 13. Reference

- Semantic Versioning 2.0.0: <https://semver.org/spec/v2.0.0.html>
- .NET assembly versioning guidance: <https://learn.microsoft.com/dotnet/standard/assembly/versioning>
- .NET SDK project properties: <https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props>

---

**Decision summary:** SemVer is for product/release compatibility; `preview.N` and `rc.N` identify ordered prereleases; the Git SHA identifies source. Never conflate the commit counter with PATCH.
