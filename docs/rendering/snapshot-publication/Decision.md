# Immutable snapshot correspondence and publication

## Decision

Published instances retain their original order and indefinite GC-owned lifetime.
Render-owner permutations normalize full unsigned Index/Generation identity only for
correspondence and entity lookup. Monotone data needs no permutation. Two ascending
runs use a stable merge; arbitrary order uses eight stable radix passes. Spatial
clusters, visibility interfaces, draw orchestration, GPU ABI and fences are untouched.
Duplicate predecessor keys preserve the earlier binary-search tie as compatibility;
they are not the unique production identity contract.

Completed-tick combat capture still queries every eligible owned entity and copies
every operational field. Only complete record equality, count/order and matching
session permit sharing private immutable member/eligible/lookup payloads. A fresh
snapshot envelope carries the current tick. Spawn/despawn, ownership filtering,
generation, health, supply, readiness, formation, order/status and unit eligibility
are revalidated every capture. UI selection does not scope this owner-wide model.
Extraction owns reusable capture-list scratch; published arrays never reference it.
Public construction still filters/deduplicates/copies untrusted input.

ClientRenderPublication always validates full caller values and never retains caller
arrays. A producer-thread cache reuses only earlier private published copies whose
contents still equal the current input. Passing a published array back as caller
input forces a fresh copy. Copies are immutable by the existing internal render-frame
reader contract; engine code never modifies or returns them to a pool. Concurrent
producers have separate thread caches; mailbox gates, stop/fault checks and signalling
are unchanged. Each channel caches at most 1,024 elements; label channels additionally
cap retained text at 16,384 UTF-16 characters. Oversized data still publishes normally
but is not cached. Cache lifetime is the producer thread, not the host: it contains
bounded value/string data and no device/host/snapshot references.

## Boundaries

No new queue, library, project reference, pooled lease, public format or frozen render
contract is introduced. Internal helpers and capture reuse are active in the existing
host/extractor path. The GC-owned arrays required for changed ticks/frames are valid
allocations; reduced duplicate work does not mean a zero-allocation complete game.

Intelligence, player actions/experience, placement, diagnostics, guidance, Operations,
alerts, audio and docking overlays retain fresh capture. Their dependencies include
completed tick, session/player, interaction selection/hover/debug/overlay settings,
command feedback, visibility/intelligence, mutable inventories and simulation state.
There are no complete revision keys for safely skipping those captures. Static dressing
is already captured once by the existing extractor; no additional cache is invented.

## Qualification obligations

Compare producer and consumer separately with identical complete gameplay workloads,
and track retained scratch independently from allowed new immutable arrays. Exercise
all identities, interpolation, old readers, mutations, concurrent publishers and host
shutdown/fault paths. A positive allocating control must validate thread-local allocation
measurement. Historical allocation reports are not a reproduced failure or a fix.
