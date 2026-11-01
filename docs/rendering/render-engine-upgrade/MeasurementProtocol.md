# Measurement protocol - diagnostic preparation

[Documentation](../../README.md) - [Complete catalog](../../DocumentationIndex.md)

Status: physical baseline admission OPEN; wave 2 NOT READY. This protocol does not authorize a performance claim or render optimization.

Current admission-contract execution uses `143ce699a3f3505168fc4d82f64780d7cae32c3f`; the implementation and supported producer/validator commands are owned by [Admission.md](Admission.md). [Delivery evidence](evidence/admission/validation.json) preserves the failed source5e6 canonical run and the separate successful nine-stage source05a1 follow-up. Functional validation and a tiny sampled fixture trace do not admit a physical baseline or either wave 2 lane.

## Historical diagnostic preparation

The earlier toolkit selected execution baseline `6b38707e29753bee64a014b7e23ef8bd150e970a` (`remote:origin/master`). The initiative planning baseline remains `f34d695fcaa813616da8747a49d030e418081e04`. Both identities must remain in evidence. Runtime and executable hashes must be frozen before any qualifying run; a source SHA alone does not establish a matched binary.

Run `python build/rendering/render-engine-upgrade/baseline.py plan --execution-source <full-sha> --planning-source <full-sha> --output <new-file>` to record a protocol proposal for the minimum five alternating A/A pairs, 64 warmups, 512 measured warm frames, separate cold samples, unchanged 15% gate, canonical scene/profile/zoom/display/camera matrix at raster DPI 96, and named scaling cases. Existing frozen qualification scripts remain authoritative for native capture and acceptance. This incomplete protocol proposal is a preparation record; it lacks admitted executable/runtime inventories, reviewed policy bounds and exact fixture admissions. Additional real mixed-content, actual gameplay and observer-on/off cohorts must be precisely frozen before use. A plan cannot imply these cases were captured.

Run `baseline.py diagnose --nvidia-smi <absolute-executable> --output <new-directory> [--manifest <native-manifest> --global-json <actual-source-global.json>]` for bounded read-only diagnostics. The supplied provider must be an existing absolute `.exe` path; it is resolved once and that identical path is used for hashes and subprocess argv, preventing command-search substitution. Each acquisition first retains query-only `nvidia-smi --version` output and records the actual provider executable SHA256 before and after the attempt. Missing/failed version queries or a changed/unavailable executable fail and retain the attempt. The literal sensor query schema stays in the acquisition record. Every command preserves literal argv, stdout, stderr, exit status, timestamps and hashes. Timeouts and partial acquisitions retain an attempt record. Samples preserve UUID, full PCI address, provider driver text, temperature, clocks, power, raw event bits and actual Windows AC status. Unknown sensor values fail; unknown event bits remain unavailable. Query latency plus polling interval must preserve at most five-second acquisition gaps. The atomic latest snapshot is explicitly `unbound-physical-gpu-observations`; immutable command files retain its provenance. Do not feed it to the frozen physical-admission collector.

In this earlier toolkit delivery, the renderer exported no adapter LUID, PCI address or GPU UUID; the later additive actual-process bridge is described in Admission.md. The supplied global.json SDK is retained as caller-supplied and unverified against the manifest source. Name matching yields only a diagnostic candidate; identical adapter names are ambiguous. Windows driver text and NVIDIA marketing versions are retained separately without inferred conversion. Caller-selected UUIDs cannot attest the renderer's physical device. The provider refuses to emit the accepted `physical-gpu-observations` kind even when names match. This historical diagnostic route remains unbound; only the later separately reviewed actual process/device contract can attest that binding, and its presence alone does not qualify hardware conditions.

Hardware, allocation and GPU-memory policies must contain real reviewed bounds predating capture; no bounds or approval are supplied here. Review metadata presence alone is not approval verification. `baseline.py admit --output <new-file>` preserves a blocked-admission record and exits nonzero. Required CI, fixture binding, matched binary inventories, complete sensor coverage, controlled foreground/presentation conditions and stable A/A cohorts must all be admitted before baseline ranking. Never reinterpret missing process allocation data, physical memory coverage or GPU completion evidence as zero.

Use `baseline.py analyze-owner --input <owner.json> --historical --output <new-file>` for historical diagnostic analysis. The frozen live-owner validator checks the real schema before computing overlapping phase intervals in milliseconds. The report retains actual input SHA256 and original source SHA; historical input is not a wave 1 capture. The report also preserves raw world-including-shadow/overlay/shadow/particle distributions and logical memory distributions. Missing pass samples have count zero and null percentiles. Allocation stack attribution and physical allocation sizes remain unavailable.

CPU `--frame-hotpaths` and `--scalability` outputs are currently unsupported by this analyzer and must remain raw source-labelled diagnostic evidence. The scalability CLI's third argument is sample count, not entity count. The original planned `100000` argument is NOT RUN and cannot establish 100k-entity coverage; native Scaling100000High/Low remain separate render cases.

Preserve every attempt and all rejected cohorts. No winning-rerun selection, sample/deadline reductions, synchronization changes or policy relaxation are authorized. Pass optional `--pid <actual-client-pid> [--hwnd <actual-hwnd>]` to retain read-only Win32 foreground HWND/owner PID, selected-window owner PID, visibility, minimized state and observation timestamps per sensor sample. Without an explicit HWND exactly one PID-owned top-level window is required. Wrong ownership, missing/ambiguous windows, hidden/minimized state, foreground loss or acquisition gaps fail diagnostic control and retain actual facts. No PID means foreground continuity unavailable. No window activation is performed. Point samples cannot establish visibility between observations. Foreground facts require actual observations; WS_VISIBLE does not prove unobscured or continuous foreground presentation.

Provider semantics: [NVIDIA query documentation](https://docs.nvidia.com/deploy/nvidia-smi/), [documented NVML event-bit subset](https://docs.nvidia.com/deploy/archive/R550/nvml-api/group__nvmlClocksEventReasons.html), and [Windows SYSTEM_POWER_STATUS](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-system_power_status). Any active known reason is conservatively retained; GPU idle is not treated as a stable-hardware pass. Unknown future bits are unavailable.


Recorded execution is summarized in [Baseline.md](Baseline.md): canonical commands are functional validation with explicit compiled/publisher source splits; CPU reports are unqualified single invocations and preserve their original schema/bytes. `analyze-owner` intentionally rejects CPU report schemas. Gzip files preserve their original SHA256 and can be decompressed for independent analysis without relabelling the source. The before/after executable/runtime input inventory is identical, but this alone does not satisfy physical, fixture, policy or repeated-control admissions. Snapshot rewriting/fsync grows with the accumulated series; a three-sample readiness check does not establish long-run observation cost. Both wave 2 lanes retain no-change dispositions until qualifying evidence is accepted.


## Separate capture and owner-trace diagnostics

See [the diagnostic producer and proposal contract](Admission.md#opt-in-capture-diagnostics-and-proposal-preparation) for the opt-in flags and original-byte evidence joins. Stage capture and sampled attachment are separate intrusive diagnostics, not timing cohorts. Freeze their literal requests, source/build/payload/runtime identities, expected capacity and attempt order before execution; preserve every failed attempt and the original zero-tolerance image rule. Do not install tools, restore packages or compile inside a live checkpoint deadline.

An external focus hold, when explicitly declared, permits one operator focus action on the verified PID/HWND within the existing thirty-second checkpoint budget. Release files must be atomically published and include `schemaVersion: 1`, kind `diagnostic-setup-release`, the hold's exact `checkpointSha256`, `runId`, `processId`, `nativeWindowHandle`, action `operator-focus-only` and `completed: true`. Actual Win32 facts are checked independently. No refocus, resize, settings change or inferred continuous foreground state is authorized by the release.

Source S is the immutable producer build/payload; accepted protocol P must later bind S without rebuilding it as P; results review R binds the original attempts. A complete unaccepted proposal and a schema-positive test do not replace committed approval or original-input validation. Both original wave 2 lanes retain no-change dispositions pending accepted baseline evidence. Capacity proposals, finite allocation bounds and stable operating ranges remain unresolved in their owning packages.


## Supported retained raw-QPC correlation

[The versioned raw-QPC contract](evidence/qualification/RawQpcCorrelationContract.md) separates UTC anchor sanity from raw integer ordering for one reviewed Windows CoreCLR/runtime/parser path. Proof is derived only after all existing original evidence gates and actual target-runtime module paths; an arbitrary TraceEvent4 parser build or caller assertion is insufficient. The exact retained parser/dependency hashes and source remain pinned. Unsupported or unproven paths keep generic conservative boundary uncertainty; invalid/foreign explicit proof fails.

For supported proof only, strict integer interior events may match; exact endpoint ties remain ambiguous. UTC/frequency compatibility, loss checks and process/source/runtime/native-thread bindings are unchanged. The analyzer revision and contract byte hash are new analysis provenance, separate from producer/parser source `ec5db189`. No producer rebuild, timestamp fitting, old-result replacement, retroactive runtime hash or physical/baseline/wave 2 acceptance follows from reanalysis.


## Owner-cycle and ordinary control observation v1

The additive selection is `ownerObservationVersion: 1` in an actual capture
request, or `FORGELINE_RENDER_OWNER_OBSERVATION_VERSION=1` for an actual live
session with its existing telemetry output. Default version0 preserves the old
collector. Later protocol/policy acceptance must freeze this version explicitly;
full-cycle budget admission is unavailable until a genuinely reviewed allocation
policy explicitly selects `allocationInterval: owner-cycle-v1`. Legacy payload
maxima retain their interval and cannot authorize new full-cycle limits;
historical observer-overhead measurements do not qualify its cost. The old common
probe excludes condition-edge work. A separate v1 ReadEnd-to-ReadEnd bundle
distribution includes intervening idle cycles and one condition-edge observation
per cycle in both arms; initial priming is outside. These overlapping reports
are not summed and introduce no new numeric acceptance threshold.

One preallocated condition-edge witness precedes captured-loop construction.
Adjacent cycles share the identical stored edge as previous END and next START;
the final sampled edge follows presentation ACK and common-probe tail. A terminal
finally closes incomplete work before final GPU-idle/teardown. First priming,
idle/no-payload work, stop, faults and storage overflow remain explicit. Storage
capacity (default8192, maximum65536) is not a workload or allocation policy.

Full/On record cumulative exact process/thread counters, separately bracketed
because the two reads are not simultaneous. Existing Begin/Complete absolute
values are reused without duplicate reads or changed legacy endpoints. Lead,
payload and tail can be derived where both endpoints exist; their sum is a cycle
and **cycle plus payload must never be added**. Off uses the same common copied
facts/clock path but makes no optional precise/thread allocation-counter calls;
its exact allocation fields remain null. Native reference retains its actual
legacy measured endpoint and explicitly has no ClientRenderHost cycle or exact
process-total collector.

A warm first bundle starts immediately after the source-joined final warmup
cycle, including intervening idle cycles. A cold first bundle starts at its own
full cycle, never the entire menu/startup prefix; its preceding-measured-success anchor is unavailable. Later bundles
include every idle/no-payload cycle since the previous retained success. The
selected v1 allocation-policy input is the exact cumulative bundle delta, not
only its final payload-bearing cycle. Individual cycle/legacy decompositions
remain separate and overlapping. A cold
first edge can precede simulation/audio activation. Missing before-facts remain
unavailable while exact counter data is retained separately; they are not zero
work or whole-session evidence.

Copied workload facts contain source identity/session/native owner, read brackets,
monotonic starts/completions and in-flight ordinals. Concurrent transport has
separate cumulative start/end counts, not a single-owner ordinal. Touching work is
started(after) minus completed(before); nested operations are not summed as
independent byte charges. Platform facts distinguish removed native messages,
WM_QUIT, dispatch, pumps, arrivals/dequeues/discards, initial observed queue depth
and high water. Synchronous native callbacks (including SendMessage) can run
outside Translate/Dispatch; a returned dispatch does not prove callback success
when the existing window callback catches an exception. Those source-ledger
terms remain unresolved; pump/dispatch counts are not a complete Platform
allocator bound. Library/runtime/first-use/error and teardown byte terms also
remain OPEN until the complete source-derived ledger is reviewed. Simulation callbacks and platform providers flow upward as copied
values; no render access to mutable ECS/platform queues is introduced.

Simulation slots0..7 are tick, executed control, drain, submission, pause/resume,
terminal acknowledgement, smoke control and status publication. Transport event
slots0..5 are attempted/admitted/rejected/drained/completion-publication/dequeue;
transport span slots0..2 are whole admission, completion dequeue and completion
publication. Public composition is supported; compiled Developer transports are
explicitly unsupported. Pre-host restore/initial status and lifecycle work are
outside activation. Outer legacy CompletedTicks/State reads are not the same
instant as the coherent workload vector. NotifyProgress and caller-created
delegates remain separate ledger terms; these vectors do not claim every
simulation allocation belongs to a tick.

Audio slots0..2 are initialization, UI play/world iteration, and UI recovery/world
output attempt. UI events0/1 count Update calls and Update enabled changes, not
every generation increment; UI events2/3 count completed play success/failure.
World events0/1/2/4 count blocks, handled failures, accepted Publish calls and
owner-observed reset changes. World iterations are observed independently of
blocks; waits of2/10ms are not rate caps and output attempts may reset. Sampled
latest-published immutable snapshot population/high water covers observations at
render-owner fact reads, not every publication or transient ECS maximum.

Ordinary post-window control is separate from diagnostic trace holds. After the
sealed final cycle (or native legacy endpoint), before readbacks, the producer
publishes actual source/runtime/run/checkpoint/PID/HWND/case/profile bytes and the
final submission/fence/device/resource generation tuple. The launcher snapshots
that ready before starting a **new** query. A query already in flight cannot ACK.
The journal row and constant-size prefix are flushed and fsynced before the
source-owned receipt is returned; ACK verification recomputes the actual journal
prefix and last row and joins original raw CSV/provider UUID+PCI and Win32 facts.

There is one absolute existing five-second coverage cutoff derived from the
selected original endpoint, plus the original command deadline. Ready, query and
ACK never start fresh budgets. The final consumer independently joins the actual
last frame/cycle/count/composition and applies hardware coverage to the selected
full-cycle interval. Default/legacy endpoints retain their old interpretation.
The actual gameplay close guard is nonblocking on the platform owner; it preserves
smoke completion scheduling and the one-minute progress timeout while waiting for
ordinary control completion. Native waiting continues event pumping.

These are observations and control integrity, not approved byte ceilings,
continuous visibility, hardware stability, repeatable pixels or baseline/wave2
admission. The detailed PR449 report remains local by the current session publication choice.
No new physical capture or
trace is implied by CPU regression results.


## Fixed-readback world-input inspector v1

Stage diagnostics opt into this additive inspector on a reviewed new producer.
Construct one inspector on the render owner during setup, then explicitly arm
only each declared fixed readback. Stage-enabled but unarmed motion/helper calls
produce no inspector reads or records. There are no hooks in the existing shared
PrepareTerrain/RecordFrame definitions or the warm/sample loops.

Native repetition0 brackets the existing post-window fixed-pose PrepareTerrain
site and the existing CaptureFrame conditional preparation site; later native
repetitions bracket only that conditional site. Host repetitions bracket their
existing readback prepare call. The call counts and order are unchanged.
BeforeSite records PreparationInvoked as null (not yet evaluated); AfterSite
records the actual boolean, with false meaning prepared state was reused. Phases
are BeforeSite, AfterSite and AfterCaptureAndClientRetire. Checked fixed storage
is5*RepeatCaptures, at most640 rows for128 repeats; unrelated motion never consumes
that capacity. Rejected armed phases, ownership, chronology or capture tuples are
sticky invalid evidence, not correctable into a passing subset.

Each <key>-readback-<NN>-world-inputs.json is written CreateNew before pixel
comparison. Its sibling reference is worldInputDiagnostics={file,sha256,
schemaVersion:1}. The file binds source/runtime/recipe bytes, actual camera,
quality-profile bytes, render-owner IDs and its own original stage metadata
file/hash. profileDomain=effective-quality-profile means RenderQualityProfile;
it is NOT the ordinary post-window ready profileFingerprint domain, which hashes
the full ClientRenderConfiguration. Native failure timedWindow.observedComposition
and host owner.observedComposition preserve that separate full-composition proof.

The final pre-capture AfterSite surface supplies the captured-slot join. The
post-capture surface FrameIndex is the next slot and is never substituted for
GraphicsStageCapture.FrameIndex. CompletedFence is supplied only by that actual
completed stage capture, not a pre/after-Prepare diagnostic or lagging CPU/GPU
metric. Each lane binds its own successful submission/fence/generations and
presentation progression; independent processes need not share those numbers.
Incomplete preparation/capture scopes retain an incomplete prefix even if the
final manifest is absent. Persistence and logging errors must not replace the
original producer failure; an unavailable prefix is not a complete record.

Facts are copied from the existing public typed terrain, instance (including
main/shadow/preparation/reuse/asset/skin/material), particle and graphics
properties. One Graphics.Diagnostics read per phase can harvest completed timing
or debug state and allocate; this is a sequential observation bracket, not an
atomic hardware/input read. DXGI memory is cached250ms. Preserve null/lagged facts.
No extra wait, drain, Prepare, Enable or ObserveFrameDiagnostics call is added.
*Milliseconds leaves are separated as timingFacts. Other values are labelled
stateAndActivityFacts: expected cumulative/per-draw activity is not an invariant.
SkinUploadBytes is resident skin-slot accounting and UploadBytes logical submitted
bytes. Public logical staging/pending bytes are separate from physical memory.
Internal upload-arena state and actual full GPU bindings/contents remain unobserved.

The offline consumer embeds a reviewed static schema derived passively from the
public record metadata, including exact fields, CLR integer widths, booleans,
enums and nullable properties. No target constructor/getter/static method is run
for discovery, and no runtime reflection is required for validation. Nullable
values remain explicit nulls; present record timing paths cannot be omitted.
Every available phase device and surface/raster is joined to its own stage.
Prior submitted state is monotonic; the last pre-capture snapshot names the
immediate predecessor, not the captured/completed frame. Native0 may legitimately
advance during its existing non-Fixed settle frames. Raw optional CPU interval
records remain labelled potentially lagged observations, never equal-cost or
state-invariance proof.

The offline API world_input_diagnostics.validate_series(root,stage_refs,
inspector_refs,required_version=1) requires all eight complete inspector records
and their original stages, hashes, phase sequence, owner, camera and tuple joins.
The stage comparator accepts inspector_references and inspector_version=1 for
that selected mode. Missing selected-v1 records fail; old absent records retain
historical/unavailable status and the old stage-only route stays usable.
compare_inspector_lanes validates each lane independently, compares common
source/runtime/recipe/quality-profile/pose and reports state/activity changes;
physical UUID/PCI and exact pixels remain the separate strict stage comparator's
responsibility. Timing differences are labelled separately. Neither changed nor
equal aggregate facts establish a shader/driver/lifetime cause or identical GPU
contents. A pixel mismatch remains a failure with no tolerance or winning rerun.

This initial inspector does not resolve repeatability or complete package2.
Before any physical diagnostic, separately freeze clean source/build/payload/runtime,
no-frame preflight, storage, literal schedule, controls and original deadlines.
A pixel-only continuation needs external finally-path after inventories for both
commands before classification, preserving original run_capture failure/exit.
Any causal production fix needs a reviewed exact ownership amendment. Numeric
policies, qualification and original wave2 readiness remain open.


### Opaque World binding journal v1 (diagnostic only)

An explicit stage capture now also records a bounded ordered command/binding
journal. Ordinary RenderFrame and legacy CaptureFrame with stages disabled do
not construct or hash a journal. CaptureCore arms immediately before its existing
RenderFrame, seals only against that capture's successful submission, recorded
slot, completed fence and one Present, and clears the active recorder on every
exit. No new Prepare, draw, native state change, wait, diagnostic query, upload
hook or timed-loop callback is added. Legacy CaptureInputFingerprint semantics
and bytes remain unchanged.

The scope is opaque World draws. Directional-shadow and particle draws are
excluded explicitly; IsWorldPass alone includes both subpasses. Armed setter
observations include those subpasses so inherited IA vertex/index views remain
known. Pipeline/root/constants/textures follow actual invalidation, including
cache hits; IA views survive pipeline/pass changes. Shadow-end native viewport
and scissor restoration is mirrored observationally. DrawIndexed delegates to
one recorded DrawIndexedInstanced operation.

The journal retains ordered pipeline description and shader-bytecode hashes,
vertex layout, bound VB slots/identity/range/stride, IB identity/range/format,
complete native draw arguments, exact IEEE constant bytes/hash and root/buffered
mode, pixel slots/descriptors/cache-hit versus emitted state, and implicit shadow
frame/CBV/t15 metadata. Ranges describe bound views; they do not prove fetched
bytes. Shadow-frame constants are copied from the same CPU values written by
the existing implementation, not read back from GPU memory. Implicit native
shadow resources have no Lifetime identity: they use device/resource epoch,
slot, descriptor and kind, never a fabricated resource ID.

Explicit resources use weak existing Lifetime keys and monotonically assigned
local IDs within one device epoch. The registry retains no native ownership.
These IDs and descriptors are not asset identities or cross-process equality
keys. Frame-indexed resources normally rotate across slots. Reports retain
actual local bindings and same-slot comparisons (for example repetition0 versus
3/6); changes are observations, not automatic correctness failures. Cross-lane
semantic comparison uses compatible descriptions and shader/constant bytes while
preserving the independent process/session/fence/epoch/slot identities. Equal
descriptions do not prove equal assets, prearmed buffer/texture contents, actual
GPU fetched contents, precision behavior or pixel cause.

Structural refusal bounds are4096 draws,131072 events,4096 identity definitions
and32MiB actual UTF8 per sidecar. The shared immutable compact camelCase,
string-enum wire serializer is used for accounting and export.31MiB bounds the
accepted journal with a conservative4KiB header reserve;1MiB is reserved for its
envelope. The exporter and strict reader check actual original wire spans and
total bytes. Nested strings/layouts/constants and collection lengths are bounded.
These are diagnostic storage/refusal limits, not measured operation maxima,
allocation budgets or approved capacity policies. The existing1.5x stage storage
guard includes the32MiB journal cap for every declared repetition, separately
from raw attachments, lossless images and the64MiB general-record allowance.

Each stage JSON contains worldBindingJournalVersion=1 and a sibling
worldBindingJournal={file,sha256,bytes,schemaVersion:1}. The sidecar filename is
<key>-readback-<NN>-world-bindings.json. The producer's stage reference hashes
the stage JSON, which hashes its journal: no circular stage/journal hash is used.
The sidecar independently joins source/runtime/recipe, quality-profile bytes,
camera, repetition, device/session, actual slot/submission/completion and Present.
Its profileDomain is effective-quality-profile, distinct from the ordinary
post-window full ClientRenderConfiguration domain.

Journal refusals are sticky. An overflow, missing required state, foreign owner,
metadata redefinition, serialization or capture failure cannot be corrected into
a complete record. A current incomplete prefix retains all accepted events and
definitions, no successful completion tuple, and the original error remains
authoritative. Optional failure context that cannot serialize within its reserved
envelope is explicitly unavailable while the journal prefix remains intact.
CreateNew never overwrites a prior prefix or partial artifact; persistence/logging
failures do not replace the original exception. Failure before arming or inability
to persist a prefix stays unavailable, never borrowed from an earlier capture.
Exception-only retention requires the exact opaque invocation token claimed at
actual recorder arm; reusing an exception cannot reuse its earlier attachment. A
known current completed capture takes precedence when exporting an incomplete
post-capture failure prefix. Tokens are process-local and never hardware proof.

world_binding_journal.validate_series(root,stage_refs,required_version=1) and the
stage comparator's world_journal_version=1 require all eight original complete
journal refs, exact types/bounds/hashes, ordered state reduction and own-lane joins.
Missing/malformed selected-v1 data fails with no fallback. Historical missing data
with no selected version remains unavailable; the historical stage-only API stays
usable. compare_lanes reports semantic-description comparisons separately from
local identity continuity; physical UUID/PCI/control and exact pixels remain
separate checks. Failure prefixes and pixel mismatches cannot grant completion.

This journal is another bounded discriminator, not a causal fix or package2
completion. Existing793/native0-versus-later/host pixel failures remain immutable.
New clean source/build/payload/runtime, preflight, storage, selected journal-v1
schedule, controls and manual readiness must be reviewed before any physical run.
Numeric policies, baseline qualification and original wave2 readiness remain OPEN.

### Native CPU-source provenance v1 (diagnostic only)

`captureBoundInputProvenanceVersion: 1` selects an additive native-only CPU-source
observation for the declared single Warm/Fixed/World/1920×1080 Full case. The shared
parameterless request validator rejects this selector, so host and no-frame preflight
paths cannot claim to exercise it. Stage, shadow-only and trace capture are disabled.
The launcher marks every non-null selector diagnostic; admission independently rejects
such requests even if an attempt incorrectly claims `diagnosticOnly: false`.

The explicit creating-thread scope starts before device/resource construction. The
selected native copy and both legacy/new hashes consume the same private frozen source
bytes, including frozen/revalidated mip dimensions and pitches. Root constants freeze
at most the existing64 words before their original command. The shared128MiB transient
freeze refusal is structural diagnostic storage, not a managed-heap or allocation-policy
budget. No additional native Map, copy, barrier, Signal or GPU wait is introduced.

The latest-source map retains whole successful source hashes and slice offsets, not
invented slice hashes or GPU-fetched bytes. Unknown buffer capacity remains explicitly
unknown; it is not zero and does not imply that a shader fetched that capacity. The
map refuses more than4096 resource identities,32768 total segments or64 segments per
actual resource, with32MiB metadata accounting and no eviction. Shadow-frame and
buffered constants share one actual implicit arena per resource epoch/slot. Its weak
raw-wrapper witness detects replacement; it has no fabricated Lifetime or native address.
Copy transfers retain queued, execute-returned and actual signaled-fence states separately.
An observer refusal makes evidence unavailable. Only native Dispose cleanup quarantines
observer propagation so required native Signal/idle/retirement can finish; normal work
still fails closed and native failures remain authoritative.

Each existing FinalColor-only readback exports separate compact journal and CPU-source
sidecars with original `{file, sha256, bytes, schemaVersion}` references. The producer
reference authenticates the companion, which authenticates the separate journal. Their
actual UTF8 files are each bounded by32MiB, reserving31MiB body and1MiB context. Original
source/runtime/recipe bytes, quality-profile bytes (a different domain from full ordinary
composition), camera, process/device/session, captured slot, submission/fence and Present
facts remain joined per process. Sidecar observations do not establish GPU residency,
fetched contents, root cause or cross-process resource identity equality.

Selected-v1 consumption requires all eight complete original references. Historical
artifacts without the selector retain their original interpretation. Capture-prefix
failures require the exact claimed invocation token or a known current capture and carry
no successful tuple. Supplementary scope-only failure facts distinguish before-arm from
arm-attempted phase without claiming that separately retained capture evidence is absent.
The offline reducer replays destination writes and draw associations, reports later
writes overlapping earlier draw bindings separately (whole-source hash plus destination
and slice ranges, never an invented final digest), preserves nonoverlapping writes and
unknown holes, and keeps process-local IDs/slots/write ordinals
separate from source/description semantics. Byte-identical source intent or all-B pixels
cannot establish a causal repair, physical stability, allocation policy or qualification.
The offline overlap report refuses more than the existing131072-event structural cap
of derived overlap records instead of producing an unbounded report. This is a tool
capacity refusal, not a workload or allocation-policy limit.

### Native post-FinalColor resident-resource snapshot v1

`capturePostFrameResourceSnapshotVersion:1` is a separate native-only, unqualified
late diagnostic. It requires the declared single IndustrialBase/Medium/NormalGameplay
1920x1080 DPI96 World case, Warm/Fixed64/512/8 Full with owner-observation v1.
Stages, shadow capture, CPU-source provenance and trace handoffs remain off. Shared
host/no-frame validation rejects the selector; a common no-frame preflight with null
selection proves only static context. Every non-null selection is automatically
classified diagnostic by the runner and independently rejected by baseline admission.

Ordinary capture first returns CPU RGBA and closes the primary PNG. A newly known
mismatch closes its original first-image duplicate and repetition PNG before any new
receipt, enumeration or native-copy work. The selected route skips later duplicate
PNG writers. An opaque one-use receipt binds the actual recorded slot, successful
capture body AND successful cleanup, original device/session/owner/submission and
presentation/resize facts. Rejected foreign-owner calls cannot consume the receipt.
The next swapchain slot is never substituted for the captured slot. A managed witness
also rejects replacement/creation/removal of the captured-slot constant arena.

Late intent copies only resident terrain chunks, stored main-LOD history, index ranges,
bounds, palette/material/resource references and the final retained main/decoration
scratch. These accessors do not Prepare, Select, Harvest or Render. Culled LOD values
may be old; scratch has no independent final-pass marker or entity/root association.
The declared superset includes resident VB/shared IB/control/material textures, actual
captured-slot instance prefixes and, if present, the actual implicit constant-arena
allocation-cursor prefix. Padding has unknown shader relevance. Targets/depth/shadow,
root constants, particles/HUD/skin caches, inactive private caches, earlier scratch and
main entity/root associations are explicitly excluded. Unsupported declared resources
refuse; they are never silently dropped or represented by fabricated identities.

Pending initializer absence is proven with the existing tracker-independent upload
bookkeeping; the late diagnostic does not flush initializers. Additional native COM
references and one packed READBACK allocation are owned by an active composite lease
before Execute. MAY_HAVE_EXECUTED is set before that native call. Uncertain unsignaled
work retains the entire active lease through the existing device drain/removal path.
Release is idempotent, attempts every part and never throws through ReleaseAll.
The separate late-copy fence has its own domain; its numeric value is never compared
with the original frame-fence value. Immutable textures restore their audited PSR
state. Logical BC7 mip dimensions remain distinct from block-aligned native copy
footprint dimensions; canonical raw encoded rows exclude padding and preserve color
encoding. Upload/default buffer copy states follow the audited completed native path.

Structural refusal guards are4096 total resources,32 mips/texture,131072 mip entries,
4096 terrain/main/decoration entries each,32 index ranges/chunk,131072 total intent
resource references and4096 UTF8 bytes/optional string. A running escaped UTF8 writer
bounds metadata before oversized result allocation:31MiB body plus1MiB envelope,
32MiB actual compact wire. Total padded staging and total canonical raw bytes are each
bounded by128MiB per snapshot. These are implementation capacities, not approved
allocation policies or a guaranteed workload fit. Every canonical mip has an exclusive
ordinal file and original size/hash reference. Actual PNG publication, original/copy
receipts, source/runtime/recipe bytes, quality-profile bytes, full observed composition
(a different domain), camera, resource descriptions and intent remain joined. Failure
prefixes retain available raw references and the original PNG without inventing a
completed snapshot; cleanup/export failures do not replace the primary snapshot error.

The strict offline reader requires all eight selected references and validates original
PNG pixels, raw hashes/layouts, source/owner/device/slot/submission joins and chronology.
Historical absence remains unavailable under its original interpretation. Comparisons
separate semantic resident bytes/intent from process-local roster and captured-slot
facts; rotating slots or local IDs are not automatically correctness failures.
These are bytes observed AFTER image persistence, not evidence of what a shader fetched
earlier. The copies can perturb later repetitions. Equal bytes or all-B pixels do not
resolve the intermittent pixel cause, establish a repair, approve numeric policies,
qualify a physical baseline or authorize the original optimization wave2.
