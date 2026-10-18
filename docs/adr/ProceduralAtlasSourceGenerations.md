# Procedural atlas source generations

Status: accepted for optional authoring; production artwork promotion remains pending.

## Context

The shared sixteen-cell atlas already has curated 4K masters, stable material and
texture identities and measured compiler/runtime budgets. Arbitrary Blender shader
graphs are not a runtime material format. Replacing the atlas in place would affect
many unrelated objects and make experiments difficult to review or recover.

## Decision

Use bounded, versioned recipe JSON and the existing pinned Blender launcher to bake
CPU reference cells into isolated source generations. Keep cell assignments, copied
mesh/LOD/socket/collision definitions and compiler texture semantics authoritative.
Each generation owns source/settings/tool/code hashes and a complete output hash
inventory. Publish a completed directory atomically and select it through an atomic
pointer. Reject corrupt generations and retain failed staging for diagnosis.

Keep native 4K editable masters and packed procedural Blender projects. The compiler
continues to derive bounded BC7 2K/1K/512 profiles with semantic mipmaps; smaller
profiles can reduce allocations without discarding the master. Authoring never adds
an installed-player dependency. Promoting a candidate remains a reviewed source
change, after native resource/performance checks and human artwork approval.

## Consequences

Ordinary source-contract CI needs no Blender. Reproducible baking runs on an opt-in
pinned worker. Clean runs compare PNG and copied source bytes, while `.blend` storage
and inspection renders are not claimed byte-identical. The pilot retains exact
allocation and draw budgets, but file integrity and image repeatability do not
establish subjective quality or sustained hardware performance acceptance.

See [production](../ProceduralPbrProduction.md) and
[qualification](../ProceduralPbrQualification.md) for limits and measured evidence.
