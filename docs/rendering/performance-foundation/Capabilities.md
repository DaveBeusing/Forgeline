# Capability inventory and frozen contracts

Audit source: `63d63a3d8d2977dfff37a851d30ee4e3428b7843`. The historical [instance](../instance-submission/Qualification.md), [resource](../resource-scheduling/Qualification.md), [snapshot](../snapshot-publication/Qualification.md) and [reference](../../GraphicsReferencePerformance.md) results describe their recorded binaries; none is silently carried forward as current acceptance.

| Capability | Implemented source | Current boundary / remaining work |
| --- | --- | --- |
| Indexed instancing | IGraphicsCommandContext, D3D12GraphicsCommandContext, SimpleInstanceRenderer | Compatible resolved geometry/pipeline/material batches; already indexed-instanced |
| Instance input | InstanceMaterialPipeline, DirectionalShadowCasterPipeline | 112-byte slot-1 stream, per-instance step rate 1; no 80-byte replacement |
| Main/shadow storage | SimpleInstanceRenderer.GetFrameInstanceBuffer | Separate dictionaries/buffers per admitted native frame slot; doubling growth; old wrappers dispose into existing retirement |
| Static initialization | IGraphicsDevice fallback and ResourceScheduling/D3D12UploadArena | Interface fallback is Upload; native override is Default/GpuLocal with staged copies and final vertex/index state |
| Diagnostic fallback | FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK=1 | Process-start device choice, existing control; not ordinary default |
| Upload staging | UploadArenaLimits / D3D12UploadArena | Three 4 MiB slots plus one up-to-128 MiB large slot, 140 MiB retained maximum, 32 transfers/batch, 512-byte placement alignment |
| Last-use release | GraphicsResourceRetirement, device/context UseResource | Wrapper invalidation is immediate; native resource/descriptor release waits for actual last-use fence |
| Snapshot correspondence | RenderSnapshotIndex / RenderWorld / SnapshotIdentityOrder | Prepared previous-index access; rebuilt only on accepted snapshot changes; current by-entity binary search still assumes sorted input |
| Reference history | ReferenceSnapshotHistory / optional fixture predecessor | Shared prerequisite in this checkpoint; old fixtures stay current-only, new fixtures can exercise two populated states |
| GPU collection | ReferencePerformanceSamples / VisualReferenceCapture | Direct-queue timestamps, completed fences, warmup/final drain, duplicate exclusion and actual sample counts already implemented |
| Reference workloads | existing reference scripts | Representative and 1,000 production vehicles exist; 10k/100k same-binding scale fixtures are planned, not present evidence |

## Frozen GPU representation

Matrices use System.Numerics row-vector order: Scale * Rotation * Translation, M11..M44 written in row order, row-major HLSL world/view-projection matrices and `mul(position, world)`. Instance offsets stay world=0, color=64, material0=80, material1=96; stride 112, input slot 1. Vertex stride stays 48: position 0, normal 12, UV 24, tangent 32. Preserve index type/offset validation and actual indexed-instanced instance counts.

Preserve the 35-float instance and 51-float terrain constant declarations, their existing buffered/constant binding choices, material texture slots, sRGB Base Color, linear Normal/ORM packing, terrain's 13 textures, directional-shadow t15/b1, shader profiles/defines and root-signature budgets. Graphics layouts and material/shadow factories are serial scope. Equal mesh handles alone do not establish batch compatibility.

## Owner and frame sequence

The creating render thread owns devices, contexts, caches and resource-session disposal. Prepare -> Record -> Submit -> Retire are existing CPU phases. Submit follows native submission/presentation; it does not submit another queue. Retire is CPU bookkeeping, not GPU completion. An occluded/suspended callback can be skipped and unused preparation must cancel safely.

Frame index comes from the actual admitted swap-chain back buffer, after that slot's prior submission fence completes. Hardware overlap remains per back buffer; software additionally bounds outstanding work. Neither CPU frame-number modulo nor Submit/Retire is proof that a slot is reusable. Resize synchronizes before replacing surface resources. Upload-arena slots are a different resource pool; staging resets only after their copy fence completes. No per-resource idle, asynchronous queue or borrowed span is added.

Snapshots remain defensive, immutable, generation-aware, session-scoped and GC-owned for retained readers. Publication/copy elimination/pooling and simulation threading are outside these four packages. Decoration, ground-decal and VFX interpolation exceptions stay intact; shadow accounting uses expanded submitted parts and the existing remaining limits.

## Known gap assigned to correspondence

For a current array with IDs [3,1,2], prepared per-index correspondence can work while FindCurrent's binary search misses ID 3. The correspondence owner must test and repair the by-entity mismatch inside its exact files, preserving predecessor duplicate policy, generation identity, stale/equal-tick/session handling and immutable published order. This checkpoint does not replace the mapping implementation.

## Diagnostics and interpretation

Existing SubmissionMetrics/WorkMetrics, resource-frame metrics, upload-arena probe and GraphicsDiagnostics are the starting controls. Count roots separately from mechanical parts and main/shadow submissions. `SkinUploadBytes` currently represents retained skin buffers, not observed frame traffic; DXGI usage, logical payload, staging capacity and upload bytes are not additive memory categories. UMA/cache-coherent-UMA are not exposed by the current public device record; record unavailable instead of inferring them from performance. A future shared counter/API gap requires a serial prerequisite with precise scope and all doubles compiled; owner-local instrumentation does not authorize a GPU ABI change.
