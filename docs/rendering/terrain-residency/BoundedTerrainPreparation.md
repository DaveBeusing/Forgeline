# Bounded terrain preparation and boundary-preserving geometry

Decision: render-owned immutable CPU preparation, independent GPU admission and
conservative geometry selection. This extends the existing terrain residency
contract without changing authoritative heightfields, graphics layouts, shaders,
frame phases, queues or retirement fences.

## Active product path

`TerrainRenderer` constructs one immutable source generation from a copied chunk
table, immutable heightfields, fixed mesh settings and a presentation profile.
The cache key is the chunk index **within that generation**. A new renderer/map,
mesh configuration or profile constructs a different generation and cache;
indices from different sources are never interchangeable. The profile copies its
definitions and regions; surface-mark updates produce a new profile. There is no
mutable world, camera or GPU resource in a worker input.

The default constructor enables the new path, including geometry selection and
optional CPU prefetch. Existing callers retain their signature. Optional trailing
`TerrainPreparationSettings` supplies render-local budgets and a full-geometry
comparison mode. This is not a second terrain shader or quality preset.

`PrepareStep(camera, width, height, shadow, maximumNewChunks)` runs on the creating
render owner, outside recording. It admits at most 1--4 missing chunks per call
(default two), with additional 4 MiB/4 ms **soft** limits checked after an atomic
chunk. A chunk's CPU calculation or static upload can exceed that time: there is
no hard real-time deadline. `false` leaves `IsPrepared` false; both main and shadow
recording reject unpublished coverage. `Prepare` drains those steps synchronously
for existing hosts. Thus a cold load/teleport can still block that call until its
complete main/light union is ready. Incremental host scheduling would require a
separate change to the host's frozen frame contract; it is not claimed here.

Changed camera matrices, shadow matrices or viewport dimensions invalidate the
plan. Even equal-aspect resizes require preparation. Required coverage is computed
into reusable masks and its complete chunk/geometry/control budget checked before
resource mutation. Required over capacity is a controlled failure, never a ready
view with holes. Partial GPU failures invalidate readiness and can be retried.
Unchanged plans reuse matrices and masks. GPU LRU nodes are intrusive; required
chunks move behind retained nonrequired chunks. Each eviction chooses the oldest
candidate once. The sorted draw list still has bounded insertion/removal shifts;
no O(1) claim is made for those operations. Warm Prepare has no transient lists
or sorting.

## CPU work and lifetime

The owner CPU LRU retains complete vertex/control/error payloads, with separate
count and byte limits. Optional prefetch considers eight adjacent chunks around
main coverage and a movement prediction capped to one chunk. It never publishes
GPU coverage or changes the required mask. There is one pending work item, at
most four chunks, on the existing global thread pool. No private work queue or
graphics queue is added. A large camera jump cancels optional work; generation
checks discard late results. Failed optional indices are not retried indefinitely.
Required chunks can always be prepared on the owner without waiting for optional
results. Dispose cancels and observes the CPU work before releasing its source.
Workers cannot call GPU APIs or wait for the render owner.

Completed preparation is immutable. Harvest/admit, LRU mutation, material lookup,
static vertex/index upload, control textures and resource publication are owner
operations. GPU resources use existing Dispose/retirement behavior. Palette keys
contain the ordered four material slots, within a renderer whose asset lease pins
one material generation. Palettes have resident references, are removed at zero,
and are rebound at each new pass. Control slot zero is set for every chunk;
twelve material textures and layer constants are set when the palette changes.

## Geometry and conservative error

One initialized static 32-bit index buffer holds every supported power-of-two
stride dividing the chunk cell count. The base range is the original full grid.
Coarser cells use a center fan. Every **outer** chunk edge retains all original
unit segments at every level. Interior coarse-cell edges match within a chunk;
adjacent chunks therefore share identical boundary topology even at different
levels. No skirts, hidden cracks or neighbour-dependent buffer allocation.
Unsupported strides are omitted. Arbitrary non-power-of-two grids retain the
supported levels, including full geometry. Limits are checked before allocation.
The existing static-buffer upload fallback remains executable.

For each coarse cell, subtract an affine plane from its fine-grid heights.
Both fine and coarse triangles interpolate residuals inside the same min/max
interval, including crossings between their edges. The interval width bounds
vertical displacement everywhere, not only at coarse vertices. Normal and macro
tint residual bounds similarly limit interpolation changes. Normal deviation
includes a conservative lower bound for interpolated vector length before
normalization. Invalid or excessive bounds force full geometry.

Main selection projects this world-vertical bound using the nearest view depth,
maximum view-space extent and viewport focal length; the perspective denominator
includes the height displacement. Near-plane intersections force full geometry.
Light selection uses its own orthographic XY/depth scale and shadow resolution;
unusual projective lights retain full geometry. Histories are separate for main
and light and persist across GPU eviction within the immutable map generation.
Changing main camera objects resets main history. Enter a coarser level at
two-thirds of the configured pixel limit; retain it only up to that limit.
Default maximum is 0.5 pixel, normal deviation 0.02, macro tint deviation 0.002.
Hysteresis never raises the maximum pixel error.

Boundary normals sample adjacent immutable heightfields. Outer map edges use
one-sided derivatives. This corrects the old edge-clamped half derivative and
can visibly change lighting; image review remains required. Bounds remain the
original full heightfield bounds, conservative for all selected triangles.
Positions, world sampling, control pixels, local UV convention and authoritative
terrain/navigation data remain unchanged. Material UVs use world coordinates.

Authored surface marks and significant gravel/concrete/industrial control layers
retain full geometry. Chunks crossing zero elevation also retain full geometry
as conservative candidates for the current terrain's shorelines. This is not a
generic water-level API: maps with other water/bridge interfaces require their
own image qualification. Geometry error bounds alone do not establish a complete
material or gameplay-interface image gate.

## Budgets and metric definitions

Defaults: CPU prepared payload 32 MiB / 256 chunks, per-chunk preparation guard
8 MiB, GPU geometry payload 128 MiB, control payload 8 MiB. Shared index bytes are
charged once to CPU and GPU respectively. Optional in-flight work is separately
bounded by `PrefetchChunksPerStep * MaximumChunkPreparationBytes` (default 16 MiB
worst-case guard), not hidden in retained-cache bytes. Fixed map source tables,
LRU/draw metadata, CLR object headers and allocator overhead are not payload
bytes. A full process-memory plateau must be measured separately. GPU metrics
are logical live payload, not physical DXGI allocation or fence-retirement peaks.

`WorkMetrics` exposes cumulative mesh/splat build counts and CPU durations,
cache hits/evictions, pending/discarded work, index patterns, geometry/control
upload and eviction bytes, current resident/prepared payload, candidate visits,
last Prepare duration and step count. Optional CPU elapsed time is reported
separately (it can overlap owner work). Mesh/splat durations from accepted worker
results are also their component totals: do not add those totals to optional
elapsed time. Material acquisition is outside the upload timer and may invoke
the global asset cache; do not charge it again as terrain generation time.
GPU upload duration is owner wall time, not GPU execution time.

`LastDiagnostics.SubmittedTriangles` and `WorkMetrics.MainTriangles` are actual
main index counts divided by three. Shadow triangles and reduced chunk counts
are separate. Actual last-main texture/palette command counts are in WorkMetrics;
the frozen `TextureBindingsPerDraw` diagnostic continues to describe the shader's
13 required slots. No GPU milliseconds are inferred from CPU recording time.

## Validation

The owned TerrainResidency suite covers topology, unit boundaries, winding,
32-bit index range, sampled continuous interpolation error, matching neighbour
positions/normals/UVs, strict hysteresis, protected surfaces, main/light coverage,
actual draw ranges, bounded CPU/GPU payloads, immutable data, failed publication,
recording/viewport guards, cancellation, LRU reuse and zero-allocation warm
preparation with an allocation-positive control. Native camera sweeps, image
review, hardware timings and process-memory plateau complement these tests;
fake/WARP results do not establish hardware performance acceptance.

See [qualification progress and evidence](Qualification.md). Open mandatory gates
mean **implemented, not yet accepted**, even when the functional suite passes.
