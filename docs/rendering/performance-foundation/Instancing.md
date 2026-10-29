# Instanced submission package

Technical owner: `InstancedSubmissionQualification`. Depends on the integrated foundation. [Ownership.json](Ownership.json) is the exact write allowlist, including current renderer partials, RenderSubmission helpers, its dedicated tests/CPU benchmark and a new qualification document. Everything else is read-only; shared metrics, shaders/layouts, Graphics, terrain, RenderWorld, assets and reference scripts stay outside this owner.

Use [Protocol.json](Protocol.json). Audit batches by resolved mesh/LOD/index geometry, pipeline, complete material/texture binding and pass. Verify zero main-visible objects produce zero object color draws and compatible visible objects use the expected indexed batch, while shadow visibility/budgets remain independent. Production mixed assets are a separate workload from the synthetic one-binding control.

Profile lookup, preparation, pose reuse, bounds, packing/binding and allocation costs before changing them. Preserve 112-byte data, sorting/transparency, current material/animation fields, conservative swept rotated/nonuniform/mechanical bounds, authorization filtering and main/shadow frame-slot storage. Count root and expanded-part upload/retained capacity separately. Do not release or overwrite in-flight buffers or weaken the recently corrected shadow-part allowance.

Qualify capacity growth, delayed slot reuse, zero/incompatible batches, near-frustum motion and skin/mechanical attachments. Reuse existing SubmissionReferenceTests/SubmissionSafetyTests and lane-local SubmissionGraphics; add the two named tests and CPU benchmark only in the planned files. Current production may already have optimal draw counts: evidence/tests are a valid result without a rewrite. Shared APIs/counters go back to the serial owner; missed static callers go to integration.

Deliver the new ScalingQualification document with exact source/build/workload identities, raw preparation/recording, GPU, draws, traffic, allocations and variance. A structural draw reduction alone is not a frame-time improvement. Keep existing historical qualifications intact.
