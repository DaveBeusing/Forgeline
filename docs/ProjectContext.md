# FORGELINE Project Context and Repository Governance

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Status](#status)
- [Verified Project Identity](#verified-project-identity)
- [Project Domain Role](#project-domain-role)
- [Product and Engineering Boundaries](#product-and-engineering-boundaries)
- [Source Precedence](#source-precedence)
- [Semantic Versioning Policy](#semantic-versioning-policy)
- [Git and Integration Policy](#git-and-integration-policy)
- [Required Validation](#required-validation)
- [Intended `master` Protection](#intended-master-protection)
  - [Enforcement Status](#enforcement-status)
- [Development and Validation Source](#development-and-validation-source)
- [Decisions Pending Product Owner Approval](#decisions-pending-product-owner-approval)
- [Approval](#approval)

</details>

## Status

This document is the repository-local project context for FORGELINE. It consolidates verified repository facts, binding engineering and governance rules, the intended repository-protection policy, and decisions that still require explicit Product Owner approval.

Project-specific approval metadata is intentionally not fabricated. Until this document receives explicit Product Owner approval, its approval section remains pending and it does not turn unresolved product or release decisions into approved policy.

## Verified Project Identity

- **Project:** FORGELINE
- **Product / system:** FORGELINE game built on ForgeLine Engine
- **Primary domain:** large-scale real-time strategy with industrial production, logistics, intelligence, and direct battlefield command
- **Repository:** `DaveBeusing/Forgeline`
- **Integration branch:** `master`
- **Language baseline:** C# 14
- **Runtime baseline:** .NET 10 LTS
- **Configured SDK:** .NET SDK 10.0.402 selected by `global.json`
- **Package management:** centrally managed through `Directory.Packages.props`
- **Versioning:** SemVer `MAJOR.MINOR.PATCH`, centrally defined in `Directory.Build.props`; read that file for the current version
- **Interactive host baseline:** Windows x64
- **Interactive graphics backend:** Direct3D 12

Windows x64 and Direct3D 12 describe the current interactive-host baseline. They are not a broader supported-platform promise. Simulation and headless projects must remain independent from graphics, UI, audio, editor, and Windows-windowing dependencies.

The primary target-user definition has not been formalized in an explicitly approved project context.

## Project Domain Role

The project-specific technical role is **RTS Simulation and Engine Architect**.

This role is derived from the product and current architecture. It is responsible for maintaining the technical integrity of the custom RTS engine and game composition, including:

- deterministic-friendly fixed-tick simulation;
- data-oriented ECS ownership and stable entity identity;
- large-scale navigation and movement;
- physical logistics and finite-resource flows;
- combat, intelligence, readiness, and supply authority boundaries;
- headless validation and diagnostics;
- simulation/presentation separation;
- Windows client and Direct3D 12 integration without leaking platform dependencies into simulation;
- performance and reliability decisions backed by tests, diagnostics, and benchmarks.

This role does not replace Product Owner authority over product vision, scope, strategic trade-offs, material licensing/cost decisions, compliance, or materially irreversible product decisions.

## Product and Engineering Boundaries

FORGELINE is an RTS first. Industry, automation, and logistics sustain warfare rather than replacing direct battlefield command.

The project preserves these core engineering constraints:

- finite resources and aggregate authoritative inventories;
- physical transport and material costs;
- logical power networks;
- faction-scoped intelligence;
- no hidden simulation advantages or UI-owned resource truth;
- custom data-oriented ECS and stable entity IDs;
- explicit fixed-tick phases and stable ordering where required;
- simulation-owned seeded randomness;
- hierarchical RTS navigation and shared-route formations;
- the existing job system and acyclic project dependencies;
- validated commands for external gameplay actions;
- copied presentation read models rather than live simulation-state ownership;
- strict separation between simulation/headless execution and graphics/platform concerns.

Performance changes require measured evidence. Controlled failure, cancellation, recovery, ownership, and resource lifetime are first-class behavior.

## Source Precedence

The following precedence applies when technical state, documentation, and governance material differ:

1. The current repository state on `master` is the technical source of truth for implemented code, configuration, tests, and current build behavior.
2. The latest explicitly approved project context is authoritative for governance, engineering rules, quality expectations, and decision boundaries.
3. Repository documentation records the project-specific application of those rules and must remain synchronized with implementation.
4. Placeholder or template approval metadata is not approval evidence.
5. Conflicts must be identified and resolved deliberately; no source is silently treated as correct when a material inconsistency is known.

`master` is exclusively the integration branch. New implementation work starts from current `master` unless a relevant existing branch or pull request is intentionally reused and synchronized.

## Semantic Versioning Policy

FORGELINE increments PATCH once per PR relative to current master.
Individual commits and validation reruns do not consume PATCH. Automatic
tag allocation and release publication are disabled. Historical tags and
versions remain intact. [PR Versioning](ReleaseVersioning.md) defines the current
repository policy and supersedes the previous release-only mapping.

## Git and Integration Policy

The project development model requires:

- a protected `master` integration branch;
- short-lived feature, fix, and technical branches;
- pull requests for integration;
- small, logically bounded commits;
- one PATCH increment per PR; no per-commit increment or automatic tags;
- preserve small technical commits; ordinary merge and rebase integration remain allowed and the established no-squash workflow remains in force;
- successful required CI before merge;
- no routine force pushes or branch deletion on `master`;
- deletion of short-lived branches after successful integration when appropriate;
- no long-lived parallel development branch unless explicitly justified.

No minimum reviewer count, merge queue, or additional organizational policy is established by this document. Such requirements need an explicit project decision or a stronger repository/organization policy.

## Required Validation

The current pull-request CI workflow is `.github/workflows/ci.yml`.

The check context emitted by the workflow is:

- **Context:** `build-test`
- **Provider:** GitHub Actions
- **GitHub App ID:** `15368`

The required check represents the **complete CI job**, including version/build identity, restore, architecture, Release build, compiled maps, runtime assets, focused contracts, native/headless smoke, the full solution suite, reproducible packaging and the natural terminal-match gate. [Development](Development.md#canonical-validation-path) owns the canonical command sequence; `.github/workflows/ci.yml` owns the actual executed ordering and feature-specific selections. Full-solution test modules use the existing CI limit of one at a time. Diagnostic artifacts retain failures as well as successes.

The terminal-match gate must not be removed, weakened, replaced with a forced result, or given free resources merely to obtain a green check. Repeated five-match soak validation remains separate from the ordinary pull-request timing gate unless repository policy is deliberately changed.

## Intended `master` Protection

The minimum intended protection for `master` is:

- require integration through a pull request;
- require the GitHub Actions `build-test` check to succeed before merge;
- require branches to be up to date with `master` before integration;
- preserve individual technical commits; merge and rebase integration are valid and squash integration remains disabled by policy;
- prevent force pushes;
- prevent branch deletion;
- preserve any stronger repository or organization protections already in effect;
- do not broaden bypass access.

Any protection configuration must be verified by GitHub readback after it is applied. Documentation alone does not enforce repository settings.

### Enforcement Status

Restored and read back on **2026-10-28** at master
`201af6aad7ace07df21608c1d058d23b0828b070` after #307 recorded a regression
in the required-check field. Current protection again requires Actions
`build-test` (App `15368`) with strict up-to-date enforcement. Existing PR,
administrator, no-force-push and no-deletion controls were preserved.
An isolated branch with the identical policy rejected a content-identical
child ref update with HTTP 422: required pull-request integration and expected
`build-test`. Its head remained unchanged; only the temporary protection/ref
were removed. Master was never a write-test target. See
[the current evidence](validation/Issue410RecoveryAndCi.md).
This administrative repair does not establish successful gameplay CI.

The following records the earlier setup; its date is historical evidence:

Configured and read back on **2026-10-25**, at `master` commit
`65bb7e704232857c8d550be54d0e1feddaf98c19`:

- [Branch metadata](https://api.github.com/repos/DaveBeusing/Forgeline/branches/master)
  reports `protected: true`.
- [Branch-protection details](https://api.github.com/repos/DaveBeusing/Forgeline/branches/master/protection)
  confirms required pull requests, strict up-to-date validation, and required
  `build-test` from GitHub Actions (App ID `15368`).
- Administrator enforcement is enabled. Force pushes and branch deletion are
  disabled. No bypass actor or reviewer minimum was added; the required approving
  review count is zero.
- Before configuration there was no branch protection and the ruleset collection
  was empty. These controls use branch protection rather than a new ruleset;
  an empty ruleset collection alone no longer indicates absent enforcement.
- An isolated branch at the exact master revision received the same protection
  request. GitHub rejected a direct fast-forward push with `GH006`, explicitly
  citing both required pull-request integration and the cancelled `build-test`
  check. The branch head stayed unchanged. The temporary branch and its protection
  were then removed; master was never the target of the test.

The connected content API does not expose administration writes. The locally
authenticated GitHub CLI supplied administrator access for the configuration and
readback. [Issue #307](https://github.com/DaveBeusing/Forgeline/issues/307) records
the configuration and isolated rejection-test evidence.
Read current protection and ruleset settings before subsequent administration
changes and preserve stronger controls. Successful CI alone does not establish
repository protection.

## Development and Validation Source

Use the SDK and test runner selected by `global.json`; do not substitute a different runner for repository validation.

The canonical commands and their current CI ordering are documented in [Development](Development.md). Architectural responsibilities and dependency boundaries are documented in [Architecture](Architecture.md).

## Decisions Pending Product Owner Approval

The following are intentionally unresolved:

- formal project-context version;
- explicit project-context approval status;
- Product Owner approval identity metadata;
- approval date;
- primary target-user definition;
- supported-platform policy beyond the current Windows x64 interactive-host implementation;
- release maturity or a new release process.

No value should be inferred for these items from repository history, tags, filenames, or placeholder text.

## Approval

- **Context Version:** Pending decision
- **Status:** Pending explicit Product Owner approval
- **Product Owner:** Approval metadata not recorded
- **Approved Date:** Not set

Git history records document revisions but does not by itself constitute Product Owner approval.
