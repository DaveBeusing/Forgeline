# 02 GPU instancing

## Outcome and dependency

Qualify and improve the existing indexed-instancing path. Depends on 01; runs alongside geometry, interpolation and benchmark work. Current master already batches production geometry with a 112-byte instance stream. Preserve that layout and production materials.

## Ownership

Own the files assigned by 01 within SimpleInstanceRenderer and its partials, InstanceVisibilityIndex, RenderSubmission helpers/tests and instance-submission documentation. Do not edit Graphics, TerrainRenderer, RenderWorld, reference scripts or shared contracts. Reconcile exact existing ownership before starting.

## Tasks

1. Inventory batches by resolved geometry, pipeline, textures/material and main/shadow pass. Measure useful instances per draw and reasons for splitting; equal mesh handles alone do not establish compatibility.
2. Use a synthetic same-mesh/material cohort to verify one indexed-instanced draw per compatible visible batch. Measure representative mixed assets separately; do not expect one draw for an entire production scene.
3. Profile preparation, material/mesh lookup, skin deformation/reuse, instance writes and bindings. Optimize only measured redundant work while preserving ordering and transparency semantics.
4. Audit frame-slot buffer reuse, growth and main/shadow separation against completed fences. Account for all instance upload bytes and capacity changes; do not release or overwrite in-flight resources.
5. Validate conservative cluster/object bounds for rotated non-uniform scales, interpolated motion, mechanical attachments, LOD and shadow coverage. Preserve authorization/visibility filtering and current animation/material fields.
6. Add missing counters through 01 before parallel implementation. Let the benchmark package consume diagnostics without changing renderer ownership.

## Acceptance

- Zero visible instances produce zero object draws; N compatible visible instances use the expected instanced batch count and exact instance count.
- Cover incompatible materials, dynamic capacity growth, frame reuse, main/shadow passes and near-frustum rotated/animated bounds.
- Compare approved native images and counters against the fixed baseline, including hidden-entity filtering.
- Report preparation/recording/GPU duration, draws, batches, upload bytes and allocation distributions at 1,000 / 10,000 / 100,000 roots. Root count and expanded mechanical instance count are separate.
- No new performance claim is based only on draw reduction. If batching is already optimal, deliver evidence and tests rather than a redundant rewrite.

Suggested commit: `Qualify and optimize instanced object submission`
