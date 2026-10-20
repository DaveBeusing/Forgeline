# Scene composition and effect submission qualification

Status: implementation in progress; native acceptance is open.

## Source and scope

The starting source is `master` commit
`cdf97fb9a8ef277cf75d451476a21128d398007c`, product `0.1.424-dev`.
The integrated render foundation, repair and release record are present. The
current ownership manifest records `parallelReleased=true`; the accepted images
belong to that foundation and do not approve these subsequent changes.

The only other active branch at the initial check was
`rendering/material-shadow-quality`, PR #266. It owns material/shadow work and
separate serial repairs; its changes are not copied into this implementation.
No existing composition/effect continuation was found. Historical PRs #98/#100
and #94 are not reused. The implementation branch is
`performance/composition-effects`.

Changes remain in the assigned composition/effect source, test, build and
documentation paths. Native pass orchestration, queues, frame indices, resource
generations, fence completion, root constants, vertex layouts and texture slots
remain governed by the existing owners. Version preparation is a separate serial
operation with exactly one PATCH relative to the then-current master.

## Baseline observations

- The scaled composite evaluates four independently shaded samples, including
  per-sample spatial AA, then decodes their encoded output before reconstruction.
  This is source structure, not a measured GPU sample count or speedup.
- A shared twelve-position AA neighborhood is valid only for four distinct,
  adjacent, in-bounds centers. Clamping centers before adding their AA offsets
  must remain intact at image edges and for one-pixel dimensions.
- Valid finite bilinear reconstruction already retains a nearest tap with at
  least one quarter of the unfiltered weight. A zero-sum fallback is defensive;
  an ordinary zero-sum black-pixel failure has not been reproduced.
- Outer depth weights cannot remove color already mixed by AA across a different
  depth surface. Scaled AA must restrict that earlier mixing too. Native AA
  without a depth binding cannot acquire one within this change.
- The existing particle renderer already owns fixed frame uploads and one
  indexed-instanced draw. Additional batching or a second upload owner would
  duplicate existing machinery.
- Early emitter authorization can reach its 2,048-entry limit before visibility
  rejection, despite the copied effect snapshot allowing 4,096 entries. The
  original alpha sort uses emitter-anchor distance.

## Validation availability

The local validation host is Linux x64. The exact repository SDK `10.0.401`,
PowerShell and official Linux DXC are available. Managed builds, selected portable
tests, source-contract checks and DXIL compile/disassembly inspection are possible.
This host does not supply Windows/D3D12 execution or hardware GPU measurements.

The initial complete restore failed because the sparse local checkout omitted
the solution's tool/benchmark projects. Those same-commit projects were fetched;
the second complete restore exited zero. This was an incomplete local checkout,
not a source defect or a repaired product failure.

At the initial CI check, current-master main run `39485465207` had passed Release
Build and was still executing. Movement run `39485465168` had passed. Historical
Game, submission-pool and frontend asynchronous-load failures are not claimed as
newly reproduced current-master failures. Current and final CI conclusions must
be recorded separately.

## Acceptance gates

The default product shader retains the independently shaded reconstruction path
until paired native hardware qualification accepts the optional shared-linear
variant. A source-local `COMPOSITION_SHARED_LINEAR` define permits isolated
qualification without a new setting, GPU ABI, target, queue or history buffer.
Compiler instruction counts are not hardware timings and cannot release this gate.

Required remaining evidence includes Release build, owned and existing affected
tests, complete Windows CI, fixed-state source-less native images, moving thin
details, HUD/overlay depth, particle saturation/visibility/order and steady-state
resource plateaus. Hardware cohorts must retain exact source and runtime-asset
identity, camera, resolution, quality, adapter and driver, with warmups and raw
per-pass distributions. Missing GPU timings remain unavailable, never zero.

Visual admission/sort corrections must be evaluated separately from identical-
output CPU optimizations. No automatic golden updates, threshold increases,
release, tag or master merge are part of this change.

## Resume

Last verified checkpoint: current-master identity, released ownership, inspected
production paths and complete dependency restore. Next: finish owned regression
tests, compile the complete variant matrix, build the solution, execute portable
CPU coverage and publish the scoped draft for Windows qualification. Re-read the
actual branch and CI state before resuming; this note is not proof that a pending
gate passed.
