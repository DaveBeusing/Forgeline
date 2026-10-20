# Fence-safe direct-queue upload and binding state

Status: implemented; native performance acceptance and required checks remain open.

Use a bounded owner-thread arena behind the existing texture and initialized
static-buffer contracts. Copy CPU inputs before return. Keep a direct queue;
batch deferred transfers and submit before any dependent binding. A batch's
allocator, list and mapped bytes cannot reset before its actual signal fence.
Unknown completion is a session fault, never a reusable slot. Shutdown releases
infrastructure only after queue idle or confirmed removal; a live unresolved GPU
keeps the existing process-rooted ownership and prevents a new session.

Three 4 MiB slots and one bounded 128 MiB large slot make staging capacity,
transfer count and bytes independently inspectable. The maximum 140 MiB is a
staging policy, not total VRAM. Registered resource-health counts keep their
existing lifetime-registry meaning; pool infrastructure is reported separately
by slot/resident/high-water diagnostics, just as frame allocators are separate
from application-resource wrappers. Idle need not discard reusable staging.

Default-heap static vertex/index resources reuse the frozen initialized-buffer
contract, while an explicit Upload mode remains available. Dynamic UI, instances
and constants retain their existing upload paths. GPU-local placement is not a
universal performance guarantee, particularly on UMA hardware.

Do not recycle public command contexts: a stale reference must stay closed. Keep
only owner-local scratch for repeated binding state. Every new frame/root and
native pass transition invalidates it. Validate ownership, wrapper lifetime and
slots before cache lookup. Never cache by recycled descriptor number. Do not
elide per-draw constants, buffer addresses/offsets or resource registration.
Particle descriptor setup must run after its corresponding root signature.

DRED is explicit and process-wide before device creation; status collection is
bounded and never dumps memory or private names. Breadcrumb output is capped at sixteen nodes and sixteen operations per node,
with allocation types similarly bounded; automatic device-loss reconstruction
is not delivered. Performance processes
keep DRED/debug validation disabled. Existing ten-second fences, quality/texture
budgets, shader layouts and public interfaces remain unchanged.
