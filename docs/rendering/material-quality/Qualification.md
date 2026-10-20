# Material and shadow qualification

Status: **implemented, not accepted**. Strict timing gates fail; human material/shadow decisions are approved and final CI remains open.

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

The complete unfiltered solution run finished: **3,102 passed, five failed,
19 optional/environment skips, 3,126 total**, 20m17.953s, exit 2. Assets,
Graphics, Client and Presentation all pass. All five failures are the known
Game cases below; their owning source trees are unchanged. Master CI additionally
reported the pre-existing SubmissionSafety allocation failure, which does not
reproduce in the local final Presentation suite. No test, threshold or original
golden is changed to hide these outcomes.

- BattlefieldSupplySystemTests.SupplyTruckFinishesRefuelingBeforeRescuingImmobileUnit
- VehicleDockingIntegrationTests.DockedSupplyKeepsCargoSeparateFromOptionalDriveTopupsButAllowsRequiredRecovery (zero-fuel case)
- TacticalCombatSystemTests.ActiveCargoTransportRefuelsWithoutDroppingDeliveryOrder
- SkirmishSupplyPolicyTests.ExpansionFundingKeepsPartialHubCostsAheadOfOptionalExtractors
- SkirmishOpponentTests.EastOffensiveObjectiveAdvancesIntoOpponentHalfAgainstStationaryBase

## Usual launch path and final checks

The ordinary Release solution output was rebuilt after the full frozen-output
suite: **zero warnings/errors, 1m57.52s**. The ordinary client smoke starts the
real High 2560x1440 match, loads the sealed 1,357-entry player catalog, records
zero fallback meshes/pending retirements in its sampled frames and exits **0**
through normal window/session shutdown. Its short startup FPS observations are
not a throughput acceptance or a claimed performance gain.

The ordinary build source is `0529706c1df6644aace353fb1c245f687ee99c25`.
The complete production, test, tool and asset trees and Directory.Build.props
are unchanged from measured/tested source `57fa863f`; later changes are local
qualification helpers/evidence/documentation. The production-tree equivalence
check is retained. Version **0.1.424** validates exactly one PATCH over current
master `bfc16a95`. Ownership validates 159 lane-owned paths at the first evidence
checkpoint, with only the isolated version commit handled separately; final
counts may include additional evidence records. The existing independent
`.gitignore` modification remains excluded.

[Draft PR #266](https://github.com/DaveBeusing/Forgeline/pull/266) contains the
implementation, reproducible helpers, native images and raw evidence. Required
CI was in progress when the draft was created; no green CI claim is made.
Remaining gates are the strict CPU/GPU timings, baseline gameplay failures,
final CI. The project owner approved material appearance and shadow contact for all six final image pairs; see [approval](evidence/visual-approval.json). No merge, tag or
release is performed. See [reproduction](Reproduction.md) and [review](Review.md).

## Serial CI contract correction and visual approval

The previous [CI run](https://github.com/DaveBeusing/Forgeline/actions/runs/39468545297/job/118786233490)
stopped at the full-factory source hash comparison, before later checks. The
project owner explicitly approved a serial correction to exactly the shared
ownership test and one Contracts paragraph. The [applied correction](SerialContractReview.md)
preserves immutable historical extraction hashes and current C#/shader declarations,
while permitting private HLSL function-body changes. Actual CI unittest discovery
passes **9/9, exit 0**. No hash fixture, image golden, budget threshold or workflow
was changed. The next remote CI result remains a separate gate.

The project owner separately approved material appearance and shadow contact
for all six final image pairs at version 0.1.424. The [approval record](evidence/visual-approval.json)
binds these decisions to the measured production source and image hashes. It
waives neither strict timing failures nor the five baseline Game failures.
The implementation remains draft/not accepted.
