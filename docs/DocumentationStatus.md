# Master documentation audit — 30 October 2026

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

This records the source examined for the documentation consolidation. It is a documentation audit, not a new gameplay, hardware-performance or human visual qualification.

## Master update

| Item | Verified state |
| :--- | :--- |
| **Master revision** | `6b38707e29753bee64a014b7e23ef8bd150e970a` |
| **Master product version** | `0.1.536` |
| **Incoming updates** | [PR #444](https://github.com/DaveBeusing/Forgeline/pull/444), integrated rendering qualification, followed by [PR #445](https://github.com/DaveBeusing/Forgeline/pull/445), bounded render-owner telemetry and raw GPU evidence. |
| **Initial comparison** | Master `f34d695fcaa813616da8747a49d030e418081e04` had the same tree as integration head `1f0d6773dbf92cfb67253c64743bca1a5d5afec7`. |
| **Latest delta** | Seven commits through #445 change 26 files, including opt-in telemetry, strict qualification/ownership contracts, tests and one new telemetry guide. The documentation work was refreshed onto that complete master. |
| **Reviewed scope** | All 403 Markdown documents present on the refreshed master (402 initially, plus the new telemetry guide), including source/tool READMEs, current guides, decisions, plans and evidence. |

The documentation branch starts from this verified master. Its required PATCH increment is bookkeeping for this PR; it is not a new renderer optimization or measurement source.

## Reconciled guidance

| Area | Before | Consolidated guidance |
| :--- | :--- | :--- |
| **SDK** | README and several active authoring/runbook instructions still named 10.0.401. | Current setup follows exact SDK **10.0.402** with roll-forward disabled in `global.json`. Historical measurements retain their recorded versions. |
| **Solution tests** | Some current examples omitted CI’s serial module option. | Current full-suite instructions use **`--max-parallel-test-modules 1`**. Recorded past invocations remain historical. |
| **Test and benchmark inventory** | Central development prose omitted Assets/Client tests and the Logistics benchmark host. | **14 test projects** and **five benchmark hosts** are represented. |
| **Battlefields** | Split Basin was appended after the README footer; generated selection was absent from the overview. | Authored and generated paths now appear together, with validation, seed, cache and save-identity limitations. |
| **Feature validation** | Long repeated narratives accumulated in Development and several system guides. | Development owns the complete validation path; feature guides own their specific details and reproduction. |
| **Version governance** | A generic release-only proposal could be mistaken for active repository policy. | PR Versioning is explicitly canonical; the generic standard is marked historical without rewriting its proposal. |
| **Navigation and markup** | Documents had inconsistent entry links and long uncatalogued sections. | A reader hub, complete topic catalog, stable deep links, current-guide breadcrumbs and collapsed contents lists. |

## Rendering evidence remains scoped

The merged [serial integration report](rendering/performance-integration/Qualification.md) contains **80 cases**, **40,960 CPU frames**, **40,742 distinct completed-GPU observations** and **15 unchanged pixel comparisons**. Its complete CI-equivalent local suite passed **4,801 tests**, with **81 explicit skips** and no failures, at its named measured source. Those results are not rerun or relabelled as measurements of this documentation revision.

The repeated 10k High Sweep CPU-p95 increase (**+32.1% / +81.8%**) remains unresolved. Broader live-client, complete-memory, thermal, debug-layer and human visual requirements remain separately open. Source-pinned measurements, original raw archives, image parts and checksums are preserved.

## New telemetry foundation

The [telemetry guide](rendering/render-engine-upgrade/Telemetry.md) now documents the bounded live-owner window, original completed timestamp observations, disjoint logical resource-byte accounting, exact source/package SDK binding and physical-observation ingestion seam. It preserves the default diagnostics layout and rejects invalid/incomplete windows instead of reducing sampling or deadlines. The seam does not implement a hardware sensor provider.

Its full canonical run belongs to `dead0c5ad23d141b76f10718f75342d62afbe72c`: 4,819 tests passed, 81 skipped, no failures (4,900 total). Later corrections retain their separate focused validation. Actual live attempt 05 belongs to `02dc24f9549d90748ad3771af54f54214db9b1ec`, with 64 warmups, 512 CPU and 512 completed GPU samples and no overflow; earlier failed attempts remain retained. That is structural diagnostic evidence with uncontrolled environment sensitivity, not a resolved driver cause, a matched native fixture or performance acceptance. These source-specific results are not relabelled as tests of this documentation revision.

## Consolidation boundaries

- Current guides receive readable navigation, heading/list/table spacing and labelled examples.
- Duplicated explanatory paragraphs point to a shared owner rather than maintaining divergent copies.
- Existing document filenames and prior Development/README deep links remain available.
- The catalog includes historical records, architectural decisions, source instructions and plans; their presence does not establish acceptance or approval.
- No runtime, simulation, rendering, asset data, CI workflow, test assertion or repository-protection setting is changed.

## Validation of this documentation change

Check relative file links, generated contents/anchor targets, complete catalog coverage, preserved historical/evidence bytes, unchanged code-example content apart from documented current-command corrections, whitespace and the one-PATCH version contract. Review representative rendered Markdown. Remote CI still belongs to the final PR source.

### Executed documentation checks

| Check | Result |
| :--- | :--- |
| Complete catalog | All **407** Markdown documents represented exactly once. |
| Relative file links | **2,586** checked; no missing files. |
| Section links | **1,615** checked; no missing anchors. |
| Structure | One top-level title per document; no heading-level jumps or unclosed fenced blocks. |
| Retained reference documents | **210** unchanged documents retain their exact original Git blob hashes, including the newly merged telemetry guide. |
| Existing examples in edited guides | **224** compared with exact master text; only the documented current-command corrections are allowed. The rewritten README/Development examples were separately reconciled with current configuration and source. |
| Local rendered preview | Five representative pages rendered offline with GitHub-flavored Markdown and an isolated headless browser; tables/details and the 1,440-pixel layout pass. |
| Architecture | 46 projects; no cycles or boundary violations. |
| Scope | Markdown documentation plus the required single PATCH increment; runtime/source assets, tests and workflows stay unchanged relative to the refreshed master. |

Rendered previews used local files with HTTP/HTTPS blocked. The documented runtime qualification results remain bound to their original measured sources.
