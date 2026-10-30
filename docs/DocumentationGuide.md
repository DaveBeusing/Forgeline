# Documentation conventions

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Keep current instructions easy to find, technical claims tied to implementation, and historical evidence attributable to its original source. The [documentation hub](README.md) is the reader’s entry point; the [complete catalog](DocumentationIndex.md) covers the whole repository.

## One owner for each kind of guidance

| Content | Canonical owner |
| :--- | :--- |
| **Product overview and quick start** | [Repository README](../README.md) |
| **Reader paths and discovery** | [Documentation hub](README.md) and [complete catalog](DocumentationIndex.md) |
| **Build and complete validation sequence** | [Development](Development.md) and the actual [CI workflow](../.github/workflows/ci.yml) |
| **Version and artifact policy** | [PR Versioning](ReleaseVersioning.md), `Directory.Build.props` and the version scripts |
| **Governance, precedence and approval boundaries** | [Project Context](ProjectContext.md) |
| **Dependency and authority boundaries** | [Architecture](Architecture.md) and the owning system guide |
| **Feature behavior / reproduction** | The feature’s guide; link to shared setup instead of copying it |
| **Measurements and qualification** | A source-bound record with retained commands, raw data, failures, skips and limitations |

A short reminder near the relevant feature is useful. Repeated full policies, long test inventories and copied acceptance narratives tend to diverge; retain them in the owning guide and link there.

## Markdown structure

1. Use one `#` title that names the subject.
2. Start with the purpose, behavior or status a reader needs.
3. Use `##` sections and `###` subsections without skipping levels.
4. Put blank lines around headings, lists, tables and code fences.
5. Use parallel bullets for related facts, numbered lists for actual steps and tables for comparisons.
6. Use inline code for identifiers, paths, options and exact values. Label fenced blocks with `powershell`, `json`, `csharp`, `xml`, `text` or the actual language.
7. Add a small navigation row to the hub and catalog. Longer current guides can use a collapsed **On this page** list.
8. Keep stable anchors. When consolidating sections, preserve old deep links with explicit HTML anchor aliases or update every incoming link.

Use GitHub-flavored Markdown for tables, checked lists, concise callouts and collapsible detail. Mermaid is appropriate for a small architectural flow; measurements still need raw data and correctly scoped tables. Decorative formatting should not replace meaningful headings.

## Commands and tool versions

- Use the SDK and runner selected by [global.json](../global.json); verify `dotnet --version` in the checkout.
- Current full-solution examples use `--max-parallel-test-modules 1`, matching CI. Focused test selections do not replace the full suite.
- Keep the complete canonical command sequence in [Development](Development.md). Include feature-specific arguments only where they are relevant.
- Explain prerequisites such as compiled runtime assets, Windows, a physical GPU, a debug layer or Blender.
- Show fresh output locations under ignored `artifacts/` and retain exact source/payload identities for qualification.
- A historical SDK, command, failure or skip remains historical; do not rewrite a measured record to look current.

## Historical evidence and open gates

Preserve source-pinned qualification documents, native manifests, archives, image bytes and checksum indexes. Put current navigation and status interpretation in the hub/catalog or the current owning guide. A proposed policy is not adopted simply because its Markdown is present.

Report correctness, structural/native image checks, performance, physical memory, human review and required CI separately. An unavailable measurement is not zero; a merged PR is not a new acceptance run. Preserve regressions and failed attempts rather than replacing them with later summaries.

## Maintenance checklist

- [ ] Check the latest master and the files that actually changed.
- [ ] Reconcile the README, hub and owning guides with code/configuration.
- [ ] Keep the complete catalog current, including source/tool READMEs.
- [ ] Check relative file links and heading anchors after restructuring.
- [ ] Preserve quoted code, measured values, evidence hashes and approval status.
- [ ] Review the rendered Markdown, including tables, collapsible sections and diagrams.
- [ ] Follow the existing one-PATCH-per-PR policy and required CI.
