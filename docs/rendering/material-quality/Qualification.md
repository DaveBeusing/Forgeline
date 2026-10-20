# Material and shadow qualification

Status: implementation in progress; no quality or performance acceptance claimed.

## Baseline

Current master: `bfc16a953d731a844321a0848a2ebadc71c5875f`, version 0.1.423.
The accepted render foundation and its reviewed ownership release are integrated.
No existing material/shadow implementation branch or open PR was found.
The independent local `.gitignore` modification is excluded.

Master CI [39445686642](https://github.com/DaveBeusing/Forgeline/actions/runs/39445686642)
failed in the full test step: five existing Game failures and
`RenderSubmission.SubmissionSafetyTests.BatchEscapeBandUsesBoundedReusablePayloadLeases`
(expected zero allocation; observed 8,486,912 bytes on the hosted runner).
Later CI gates were skipped. Movement qualification passed. These failures
are baseline observations, not a waiver or a request to change other subsystems.

## Scope and contracts

Only material/shadow shader and projection ownership, local tests, measurement
helpers and this documentation may change. Root constants, shader resource
slots, vertex layouts, formats, resource lifetime, frame phases and simulation
remain fixed. The version increment is finalized once against current master.

The current material shader already implements GGX, cofactor normal transforms,
normal derivative specular filtering, hemisphere ambient, four weighted shadow
taps and snapped shadow projection. New evidence must distinguish existing
capabilities from the remaining numerical and quality delta.

## Next verification

Freeze an unchanged Release baseline, run the existing focused classes, create
controlled material/normal/shadow sequences, and compile all production variants.
Then qualify the minimal numerical and visual changes using unchanged assets,
lighting, camera sequences and quality profiles. Retain original references.
Human material and shadow-contact approval is separate from automated tests.

## Rejected filtering experiments

The source `9d5cf3968112c0a9aa812d9f56ec21771fc63a2a` squared-slope kernel
and source `44efde21` conservative sum-of-axes kernel each completed all twelve
hardware cohorts. Repeated images were exact within each implementation.
Neither consistently improved bare-steel temporal response. Original normal-variance
filtering is restored; no success claim is derived from a selected panel or metric.
The baseline Release build passed with zero warnings/errors (1m38.71s), and
53 existing focused tests passed. The new numerical contracts and existing
material/shadow integration passed 50 tests before the filtering experiments.

## Final production source

`57fa863f540f283ffbed742923de3e9fa6b3f80c`, version **0.1.424**.
The complete Release solution builds with zero warnings/errors (43.09 seconds).
The isolated version commit validates exactly one PATCH against current master.

The first native attempt stopped before recording because an active build was
detected. A subsequent request exposed a helper-only scalar/array serialization
error and exited before graphics setup; the request helper was corrected.
The next sequence completed its first baseline fixed/sweep cohorts and candidate
fixed cohort, but a concurrent terrain build overlapped the candidate sweep.
The process monitor detected the overlap and rejected that sequence. None of
those partial cohorts establish performance acceptance. The complete predefined
protocol is repeated in a newly isolated window, retaining every earlier record.

Separate world/shadow GPU series use the public production renderers and the same
compiled game scenes, 1920x1080, all four profiles at normal RTS distance, fixed
mechanical pose and a deterministic target pan. Full-client references separately
cover all three zooms. CPU/Present and whole-frame GPU times are not substituted
for these completed-fence component measurements.

## Final native outcome

The isolated B-A-A-B client sequence completed all **56 cases** (6 fixed High
references and 8 moving four-profile references per member), with exact repeated
readbacks, unchanged source/runtime/hardware configuration and no detected
competing named workload. Cross-cohort fixed images are exact within each source.
The candidate changes 974–6,719 pixels per fixed 1920x1080 image; maximum channel
differences are 11–26. These are local material/shadow differences, not a broad
artwork replacement. See [the source-qualified review](Review.md).

The unchanged 15% full-client gates have **13 CPU metric/statistic exceedances**:
2 fixed + 4 sweep in pair one, 2 fixed + 5 sweep in pair two. All total-GPU
comparisons pass that gate; this does not waive the CPU failures.

The separate production-renderer probe completed **32 native component cases**
(8 cases × 4 members), producing 16 pair comparisons. Its 15% checks retain
**25 exceedances: 10 world GPU, 5 shadow GPU, 7 total GPU and 3 CPU**, with no
missing/unqualified series. Neither hardware path establishes complete
performance acceptance. Unchanged caster code also exhibits timing variation;
no attribution of every observed difference to receiver arithmetic is claimed.

Each component case returns to zero asset textures, zero pending retirements
and the same one device-owned registered resource after renderer disposal.
Warm texture/upload counts remain stable. This is a registered-resource plateau,
not a strict physical-VRAM bound. World/shadow intervals use distinct completed
fences, and shadow samples match the corresponding frame fence. The focused
probe omits dedicated atmosphere/particle passes and is not whole-game timing.

An output-stream mixing error prevented the client helper from writing its
final summary after all captures/comparisons completed. The summary was rebuilt
from the four existing comparison reports without recapturing, altering a metric
or dropping an observation. The helper now keeps warning text out of its result
objects. All earlier incomplete/error attempts remain retained.

Architecture validation passes for **47 projects**, with no cycle or new
reference. Development asset validation passes with **1,357 unchanged assets**,
zero newly compiled assets and zero issues. Gameplay texture containers remain
**57,665,206 / 58,720,256 bytes**. No source texture, mip policy or asset identity
is changed. The first two analytic filtering candidates include the common
numerical changes; their observed GPU differences are not isolated filter costs.

The complete Presentation suite passes on final production source. The full
solution run is still completing its long Assets suite; the Game project reports
the same five known failures. Master CI additionally reported the pre-existing
SubmissionSafety allocation failure, which does not reproduce in this local
Presentation suite. No test, threshold or original golden is changed to hide
these outcomes.
