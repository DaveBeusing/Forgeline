# FORGELINE Project Context and Repository Governance

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
- **Configured SDK:** .NET SDK 10.0.401 selected by `global.json`
- **Package management:** centrally managed through `Directory.Packages.props`
- **Versioning:** SemVer `MAJOR.MINOR.PATCH`, centrally defined in `Directory.Build.props`; baseline `0.1.0`
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

FORGELINE uses Semantic Versioning in `MAJOR.MINOR.PATCH` form. The authoritative version components are `ForgeLineVersionMajor`, `ForgeLineVersionMinor`, and `ForgeLineVersionPatch` in the repository-root `Directory.Build.props`. The initial governed baseline is `0.1.0`.

The versioning rules are binding:

- every repository commit after the versioning bootstrap increments `ForgeLineVersionPatch` by exactly one;
- `MAJOR` and `MINOR` may only change through an explicit product/release decision and must never decrease;
- changing `MAJOR` or `MINOR` does not reset the patch counter; the same commit still increments `PATCH` by exactly one;
- merge commits are not permitted because they introduce an additional commit without a corresponding patch increment;
- pull requests must therefore be rebased onto current `master` before integration and integrated with rebase/linear-history semantics rather than merge-commit or squash semantics;
- CI validates the complete commit range and rejects missing, repeated, skipped, or decreasing patch versions;
- `build/Increment-PatchVersion.ps1` is the canonical helper for preparing the version change before each commit.

The bootstrap commit that first establishes `0.1.0` is the only commit without a prior governed version to increment from.

## Git and Integration Policy

The project development model requires:

- a protected `master` integration branch;
- short-lived feature, fix, and technical branches;
- pull requests for integration;
- small, logically bounded commits;
- one semantic patch increment per commit after the versioning bootstrap;
- linear integration using rebased commits; merge commits and squash merges are not compatible with the version invariant;
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

The required check represents the complete CI job, not only compilation or the unit-test subset. Its current validation path includes:

1. semantic-version progression and linear-history validation;
2. restore;
3. project-reference validation;
4. Release build;
5. Windows graphics client smoke validation;
6. headless diagnostics smoke validation;
7. lightweight entity stress validation;
8. the Microsoft.Testing.Platform solution test run;
9. the natural vertical-slice terminal validation using seed 2026, an 80,000-tick limit, and `--require-terminal`;
10. diagnostic artifact upload on success or failure.

The terminal-match gate must not be removed, weakened, replaced with a forced result, or given free resources merely to obtain a green check. Repeated five-match soak validation remains separate from the ordinary pull-request timing gate unless repository policy is deliberately changed.

## Intended `master` Protection

The minimum intended protection for `master` is:

- require integration through a pull request;
- require the GitHub Actions `build-test` check to succeed before merge;
- require branches to be up to date with `master` before integration;
- require linear history and rebase-style integration; merge commits and squash merges must be disabled for governed integration;
- prevent force pushes;
- prevent branch deletion;
- preserve any stronger repository or organization protections already in effect;
- do not broaden bypass access.

Any protection configuration must be verified by GitHub readback after it is applied. Documentation alone does not enforce repository settings.

### Enforcement Status

Observed on 2026-09-29, repository metadata reports `master` as unprotected and the repository ruleset collection is empty. The current integration available for this change does not provide an administration write action for branch protection or rulesets, so the intended configuration cannot be applied through this repository session.

Repository protection therefore remains **pending administrator action and readback verification**. This status must be updated after protection is successfully applied and confirmed.

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
