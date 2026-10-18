# Render ownership and frame contracts

Status: implemented, not yet accepted. Parallel implementation remains blocked until
this foundation is integrated into master, required checks pass and native equality
is reviewed. A feature branch or this document never substitutes for integration.
The machine-readable [ownership manifest](ParallelRenderOwnership.json) contains
exact files, explicit new files, separate test/document/build prefixes and a validated
acyclic dependency graph. Its eight parallel write sets are disjoint.

| Responsibility | Active implementation boundary |
| --- | --- |
| Graphics lifetime/uploads | Existing device/resource implementations, initialized static buffer interface |
| Snapshot publication | Snapshot/extractor types, correspondence index, ClientRenderPublication |
| Instance submission | SimpleInstanceRenderer and InstanceVisibilityIndex |
| Asset residency | RuntimeWorldAssetResources and RenderAssetSession |
| Material quality | InstanceMaterialPipeline, TerrainMaterialPipeline and existing PBR/shadow helpers |
| Terrain residency | TerrainRenderer |
| Scene composition | Existing atmospheric, particle and environment components in their assigned files |
| Qualification | Dedicated qualification tests/docs/build prefix and workflow |

Host orchestration, reference capture, graphics interfaces, global workflows,
benchmark dispatcher, solution/project files, versions and both RenderContracts
prefixes are frozen for parallel implementation. Boundary changes require serial
coordination. Existing authoring prerequisites through procedural texture production
are integrated; shared source/compilation changes belong to the asset-residency
owner, and GPU/skin layout changes belong to the graphics owner. No overlapping
animation or artwork implementation is released concurrently.

## Active product flow

`ClientRenderHost` prepares terrain and then instances before `IGraphicsDevice.RenderFrame`.
The instance stage uses the same `RenderCore` selection/resolution code as recording,
including camera/frustum, alpha, LOD history, state attachments and a conservative
main/shadow coverage union. The preparatory pass never rotates LOD dictionaries,
changes draw diagnostics, builds instance batches or deforms skins. Existing caches
perform actual synchronous reads/decodes and initialized geometry/texture uploads.
Recording repeats selection to preserve current particle replacement and draw order.
A newly needed resource can therefore still resolve synchronously while recording;
this is a compatibility boundary, not an asynchronous streaming claim.

The creating render thread owns `RenderAssetSession`, cache access and disposal.
The explicit CPU frame sequence is Prepare -> Record -> Submit -> Retire. Submit is
called after the existing device has submitted/presented the recorded frame; it does
not submit a second command list. Retire completes CPU frame bookkeeping. Actual
resource release remains governed by the existing device's last-use fences. Main and
shadow frame buffers and constant storage remain distinct and untouched.

Suspension/occlusion may skip the device's callback. The renderer cancels an unused
prepared CPU frame in that case instead of stranding it or pretending it submitted.
Raw session Submit still rejects a frame that never entered Record. Prepare during
recording, double preparation, invalid phase transitions and foreign-owner access
fail. Old constructors and callers that never opt into Prepare keep their original
synchronous record-time resolution and own/dispose the same caches.

Native reference rendering uses the same preparation and completion calls for warm
frames, cold frames, first/repeated readbacks and motion inspection. Analytic material
controls and frontend-only frames do not manufacture an unused mesh submission.

Dynamic instance-buffer uploads, skin deformation/frame buffers, particle updates
and overlays retain their existing synchronous recording paths. These are visible
remaining work, not secretly parallelized operations. Async scheduling belongs to
subsequent owners; no borrowed span or GPU context crosses to another thread here.

## Snapshot and publication lifetime

`RenderSnapshotIndex` owns entity/generation correspondence. Production RenderWorld
opts out of its internal legacy visibility-query adapter and separately owns an
`IInstanceVisibilityIndex`. The adapter preserves old direct internal callers without
building a duplicate production cluster index. Visibility reads only authorized
current/previous snapshots and `IRenderSnapshotCorrespondence.PreviousIndex`.
Initial clusters remain 32 entries, with swept bounds, NaN fail-open behavior and the
same retained-capacity trimming. Session changes clear previous correspondence.
The frozen read interfaces in RenderContracts separate future snapshot and culling
implementations without sharing a mutable implementation file.

`ClientRenderPublication` owns defensive copying, the shared mailbox gate and latest
frame storage. Host fault/stopping/qualification operations still share that gate.
Gameplay/debug arrays and selected IDs are copied exactly as before. Snapshots and
old reader frames remain GC-owned and valid indefinitely; no pooling lease is added.

## Initialized static buffer contract and frozen ABI

`IGraphicsDevice.CreateInitializedStaticBuffer(description, ReadOnlySpan<byte>)`
consumes exactly the declared nonzero size synchronously on the render owner, with
Vertex or Index intent. Its current functional fallback creates an Upload buffer and
calls synchronous SetData. It is wired to fallback object geometry, imported runtime
meshes and terrain. An input span is never retained or accessed later on a worker.
Upload failure disposes the candidate; it never invalidates an older valid buffer.
The existing queue stream can immediately bind the returned resource, and existing
fence retirement controls its eventual native release. A future Default-heap override
must preserve these publication/readiness/lifetime guarantees.

Instance data stays 112 bytes: world matrix at 0, color at 64, material0 at 80,
material1 at 96, per-instance input slot 1. Mesh and terrain vertices stay 48 bytes.
Mesh position/normal/UV/tangent offsets remain 0/12/24/32. Instance root constants stay
35 floats, terrain 51; terrain binds 13 textures, and directional shadows retain t15
and b1. Material slots, sRGB Base Color, linear tangent Normal and AO/Roughness/Metallic
packing remain unchanged. Pipeline target profiles, defines, layouts and exact source
bodies are frozen by [factory hashes](FactorySources.json) and executable contracts.
A later coordinated ABI revision is possible; it cannot be hidden in an owner branch.

## Observations

`RenderAssetFrameMetrics` is a nullable value snapshot with owner-frame sequence,
snapshot tick/session, Prepare/Record CPU time, CPU Submit/Retire bookkeeping,
observed submission fence, device texture-upload count delta, logical resident
texture bytes, optional DXGI local usage and existing failure status. Device texture
deltas cover consecutive supplied observations, including terrain preparation. The
first observation establishes a baseline; missing observations remain unavailable. Submit/Retire times are not native Present/fence-wait time.
Present time, wait time and upload byte counts are currently unavailable/null rather
than synthetic zero. Existing graphics pass/GPU/health diagnostics remain authoritative.
With measurement disabled, the new meter does not query diagnostics or run clocks;
its repeated frame transitions are covered by a zero-allocation regression test.

## Reproduction and handoff

Run from repository root using the pinned SDK/Microsoft.Testing.Platform:

```powershell
python build/rendering/parallelization/ValidateOwnership.py
python -m unittest discover -s build/rendering/parallelization -p 'test_*.py' -v
dotnet build ForgeLine.sln --configuration Release
./build/Validate-ProjectReferences.ps1
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj -c Release --no-build -- --filter-class '*RenderParallelization*' --parallel none
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj -c Release --no-build -- --filter-class '*RenderParallelization*' --parallel none
dotnet test --project tests/ForgeLine.Graphics.Tests/ForgeLine.Graphics.Tests.csproj -c Release --no-build -- --filter-class '*RenderParallelization*' --parallel none
./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --solution ForgeLine.sln -c Release --no-build
```

Every native command's exit code must be checked. Existing renderer/extraction/index,
publication, terrain, shadow, resource-retirement, skin, VFX, allocation and complete
project suites remain applicable. CI runs the new namespace once at the frozen
entrypoints; all future test files are picked up by existing SDK globs and full suites.
Ownership-only validation does not grant runtime, visual or merge acceptance.

See [baseline](Baseline.md), [qualification](Qualification.md) and
[decision](Decision.md) for source identities, actual commands, raw evidence and open gates.


## Submitted diagnostic observations

`SimpleInstanceRenderer.ObserveFrameDiagnostics(GraphicsDiagnostics)` consumes an
existing render-owner snapshot after Submit/Retire. Both the gameplay host and
native runner call it after their timed frame interval using their existing
diagnostic capture. Prepare/Submit never poll adapter/memory diagnostics, including
when metrics are enabled. CPU phase timings still include their actual work.
Missing observations retain null counters/fences/bytes, not invented zeros. Upload
counts are deltas between consecutive supplied observations; the first observation
primes the baseline and disabled intervals invalidate it. Counters that move
backwards produce unavailable deltas. Sampling is observational and performs no
GPU retirement. The frozen value/layout contracts are unchanged.

The software graphics backend admits at most one submitted frame before the next
recording callback. Hardware retains per-back-buffer overlap. This prevents several
individually healthy multi-second WARP frames accumulating beyond the unchanged
ten-second idle fence deadline. Timeout remains fatal, with target/completed/last
frame fences recorded for diagnosis. Descriptor/resource retirement still requires
actual fence completion; completed prior-frame resources collect before callback
entry. No extra queue, quality reduction or hardware frame-global idle is introduced.
