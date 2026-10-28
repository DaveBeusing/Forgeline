# 01 Contracts and baseline

## Outcome

Release four disjoint implementation/qualification packages against the current source. Reuse existing graphics and presentation contracts; implement only shared gaps proven by the audit. This is the serial prerequisite for wave 02.

## Ownership

Own this plan's source checkpoint and exact file allowlists. Coordinate changes to Graphics interfaces, RenderContracts, native frame lifecycle, shared doubles, graphics layouts and the existing ownership manifest. No production contract change is required merely to start this plan.

## Tasks

1. Refresh master and record its full SHA, version and required-check state. Inspect existing instance-submission, resource-scheduling, snapshot-publication and qualification records; distinguish historical results from present acceptance.
2. Map existing capabilities: indexed instancing and input slots, 112-byte instance layout, main/shadow frame buffers, initialized static uploads, upload fallback, last-use retirement, snapshot correspondence, GPU timestamp scopes and reference collection.
3. Freeze matrix packing, instance stride, texture/material bindings, shadow behavior, resource-owner thread, Prepare/Record/Submit/Retire phases and actual frame-slot reuse rules. Preserve simulation/headless isolation.
4. Record exact disjoint files/tests/docs/scripts for each wave-02 owner, compatible with the existing ownership manifest. Reserve shared host wiring, global workflows, solution/project files and TerrainRenderer for wave 03. Check for overlapping active work before release.
5. If a benchmark control, counter or shared API is missing, specify its exact semantics and add it serially, updating all doubles. Prefer existing diagnostic controls; avoid expanding the root signature or introducing a new queue.
6. Capture an unchanged hardware baseline using existing reference scripts and sealed runtime assets. Archive commands, source/build/payload identity, raw data and known limits. Keep source clean for qualified runs.
7. Define the scaling fixture, camera timeline, visibility distributions, repeat counts and required metrics once so all packages compare the same work.

## Acceptance

- Capability inventory names implemented versus missing behavior without claiming the four foundations are absent.
- Exact allowlists are disjoint; shared changes compile all consumers before parallel work begins.
- Existing owner-thread, frame-fence retirement, shadow and snapshot contracts remain intact.
- Reference evidence is hardware-qualified or explicitly marked unavailable. Missing hardware does not become successful performance evidence.
- Focused tests and canonical validation cover any shared source changes; a documentation-only checkpoint needs link/diff validation.

See [render ownership](../parallelization/Contracts.md), [instance submission](../instance-submission/Qualification.md), [resource scheduling](../resource-scheduling/Qualification.md) and [reference performance](../../GraphicsReferencePerformance.md).

Suggested commit: `Define rendering performance contracts and baseline`
