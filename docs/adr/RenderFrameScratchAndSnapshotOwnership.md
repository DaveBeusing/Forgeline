# Render frame scratch and snapshot ownership

## Decision

SimpleInstanceRenderer keeps render-owner scratch: a reusable batch lookup, batch slots with instance lists, and contiguous instance staging. Each frame clears keys, instance counts, offsets and resource references before reuse. Slots follow first encounter order; batching keys, visibility, LOD, attachments and draw order are unchanged. Only the populated staging prefix is uploaded. SetData consumes the span synchronously; it must not retain CPU scratch.

Capacity doubles on demand with checked arithmetic. A frame can exceed the retention budget without losing instances. At the next frame, staging above 65,536 instances is released; batch storage is released if its combined instance capacity exceeds 65,536 or it has more than 256 slots. Thus a stable oversized workload intentionally reallocates rather than retaining unbounded CPU memory. Within budget, staging and batch instance payloads retain at most 14 MiB combined (112 bytes per instance), plus managed collection overhead. Disposal releases all scratch references. Scratch and diagnostic reads belong to the render owner; concurrent Render calls are unsupported.

GPU upload buffers remain indexed by graphics frame index. Replacement first creates a new buffer, then disposes the previous wrapper through existing last-use fence retirement. Creation failure preserves the old buffer. CPU reset never changes GPU fence, descriptor or upload-buffer ownership. Native frame capacity retains its existing high-water policy independently of the CPU retention budget.

PresentationExtractor transfers its freshly allocated, unaliased render-instance array through an internal snapshot constructor. It never mutates that array again. A complete base-instance capture with no VFX uses the captured array directly; filtered or VFX-bearing captures still assemble an exact-length array. Public snapshot construction continues to copy caller input. Snapshots remain GC-owned and can be retained indefinitely across publication, interpolation and subsequent ticks; there is no pooled return or lease expiry.

ClientRenderHost.Publish retains defensive copies of gameplay/debug line and label arrays and selected entity IDs. The caller's array types provide no ownership-transfer promise. Removing those copies would permit mutation while a reader holds an older frame. Other referenced read models retain their existing immutable contracts.

## Evidence and diagnostics

CPU regression tests cover warm zero-allocation fallback submission, growth/shrink/empty frames, retained-memory trimming, failed GPU-buffer growth, retained extracted snapshots, concurrent extraction/publication and caller mutation while rendering is blocked. Existing runtime-asset, visibility and interpolation tests remain applicable.

InstanceSubmissionMetrics is a value snapshot sampled explicitly after successful submission on the render owner. It reports CPU staging and batch capacities, submitted instances, populated upload bytes, pipeline bindings and texture bindings. Bindings are command counts, not unique state changes. No instrumentation clock or allocation sampling runs inside Render. Existing InstanceRenderDiagnostics preserves visibility, LOD and draw accounting.

The rendering benchmark host's opt-in --frame-hotpaths mode separates CPU submission, synthetic extraction and actual asynchronous frame publication. It records p50/p95/p99, producer-thread allocated bytes, process GC deltas and operations/second with 1,024 warmups and 8,192 samples, fixed alpha and 1600x900 viewport. Render workloads use the existing fixed-camera benchmark fixtures and compiled runtime assets. Publication reports publisher latency, including copying, locking and signalling; it does not measure consumer-thread allocation. Extraction advances a 1,000-entity transform/visual-only scene; it is not a complete gameplay tick workload.

Null graphics consumes commands without native uploads, synchronization or presentation. These results cannot establish GPU time, fence stalls, frame pacing, or gameplay main-thread stalls. GC counters are process-wide; shared-machine timing is descriptive rather than a stable-hardware threshold. CI enforces allocation/ownership contracts independently of native qualification and uploads measurements without gating noisy timing percentiles. The complete existing build-test gate remains unchanged.
