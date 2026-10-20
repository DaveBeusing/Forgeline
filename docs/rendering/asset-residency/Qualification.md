# Asset residency qualification

Status: implemented, not accepted. Functional build/asset/scene/package checks pass; warm performance gates, existing gameplay baselines and final CI remain open; human image equality is approved.

Baseline master is 47f4230f487e37f699b6572354e89f800476c513, version 0.1.420.
The accepted render foundation and initialized static-buffer implementation are integrated.
No existing asset-residency branch or open PR is present; source/compiler ownership is reserved
for this change. The unrelated local ignore-file modification remains excluded.

Master CI 39421043011 fails only the version gate (expected 0.1.421, found 0.1.420);
later checks are skipped. This is not evidence of a runtime failure. The baseline solution
build passes with zero warnings/errors (20.79s), runtime validation passes, and the 26
specified renderer/extraction/index tests pass (6.040s). Baseline binaries are isolated
before source changes. The final version delta will be exactly one PATCH over current master.

The inventory distinguishes exact FLAS container bytes, payload section bytes, format-specific
logical mip bytes, effective profile mip upload bytes and a separately named RGBA8 equivalent.
Native Default-heap allocation, staging and DXGI budgets remain null until observed; container
or mip byte counts do not pretend to measure heap allocation. The gameplay gate retains the
same texture-container metric and studio-ID exclusion as window qualification, capped at 56 MiB.
No source master, profile, runtime format, shader, graphics backend or gameplay data is changed.


The current content gate genuinely passes: 57,665,206 gameplay texture-container bytes
against 58,720,256; studio splash containers are 1,786,417 bytes. All 1,357 containers
sum to 75,122,812 bytes; payload sections sum to 73,991,270 and logical texture mips to
58,735,312. These equal the existing baseline qualification totals in their respective
metrics. The historical 99,608,050-byte defect does not reproduce; no content/profile
or 4K source changes are justified by this current gate.

Seven inventory contracts pass (3.170s), including full-container positive/negative
56-MiB fixtures, BC7 block-aligned profile selection, separate RGBA8 equivalence and
studio-prefix exclusion. The first run had one invalid test fixture (DisplayImage
incorrectly declared sRGB); the fixture now follows the existing linear rule and
both logs remain retained. Inventory compilation has zero warnings/errors.


The real RuntimeWorldAssetResources facade now leases one render-owner device/catalog/profile
store shared by terrain and instances. A closed mesh/material dependency plan prepares typed
payloads before first-frame publication, in bounded startup steps; the session constructor
publishes all required GPU resources using the existing initialized-buffer API. Normal and
reference starts already construct this same session, so no frozen host/renderer ABI changes
are needed. Scope details and limits are recorded in Decision.md. There is no background GPU
work or unbounded streaming/LRU cache.

The first residency build passes with zero warnings/errors. Existing boundary/material contracts
pass all fourteen tests; expanded residency plus original boundary contracts pass all 21 cases
(2.706s). The tests delete prepared container files, then verify 1,024 warm lookups add no managed
allocations, file reads, buffers or textures; shared wrappers survive first-owner disposal, profile
and device wrappers stay distinct, pinned generations reject excess admission, missing/hash-damaged
main containers fail, cancellation never publishes partial readiness, and a retirement exception
still releases remaining wrappers without double-decrementing the scope. These are functional
contracts, not native timing or physical heap acceptance.


The full Presentation suite on 15a0c56e passes 1,424 tests, zero failures and twelve
optional skips / 1,436 total (1m10.254s). The complete solution builds with zero
warnings/errors (18.02s). The next narrow refinement validates texture usage/color
space and mesh material/LOD types before any required GPU publication. Thirteen
focused residency tests pass (6.091s), including incompatible normal/BaseColor
bindings and a small explicit resident budget that cannot evict an active texture.
The final full suite is rerun on the committed refinement before native qualification.


## Complete solution and baseline gameplay blockers

Release build on e2931f79 succeeds with zero warnings/errors (21.43s). The complete
serial solution run finishes with 3,019 passed, six failed and eighteen optional
skips / 3,043 total (8m59.982s). All six failures are independently reproduced on
the original master 47f4230f binaries: SupplyTruckFinishesRefuelingBeforeRescuingImmobileUnit,
DockedSupplyKeepsCargoSeparateFromOptionalDriveTopupsButAllowsRequiredRecovery (zero fuel),
ActiveCargoTransportRefuelsWithoutDroppingDeliveryOrder,
ExpansionFundingKeepsPartialHubCostsAheadOfOptionalExtractors,
DirectCombatTargetsRequireCurrentIdentifiedIntelligence, and
EastOffensiveObjectiveAdvancesIntoOpponentHalfAgainstStationaryBase. The original-binary
processes execute seven cases combined, six failed and one passed (the other docking
Theory input). Game, Logistics, Simulation, Economy sources and Game tests are byte-identical
to master. Their failures are retained baseline blockers, not repaired or waived here.

The complete standalone Assets suite on the earlier 15a0c56e source passes all 170 cases
(7m46.274s); the full solution subsequently covers the committed semantic refinement.
Native measurements wait for an explicitly coordinated exclusive CPU/GPU window.


## First native cohort and approved host integration

Source f04cbaf4e8b3ea00777370fe2c583fc958e2fef5 builds with zero warnings/errors
(1m15.78s, fresh source metadata). Presentation passes 1,432 cases with twelve optional skips
/ 1,444 total (52.741s); Client passes 450 with two optional skips / 452 total (16.670s).
The runtime-only player package passes asset, UI skin, icon and audio validation. Its first
invocation used an unsupported absolute output argument and was rejected before publication;
the normal relative-output invocation succeeds. Both outputs remain evidence.

The declared native B-A-A-B sequence records four complete High/1920x1080 six-case matrices.
All twelve matched images have zero channel delta. The unchanged 15-percent warm gate fails:
nine metric/statistic observations in pair one and eighteen in pair two. In particular,
MainBattleTankProduction/CloseTactical CPU p50 rises 1.2682 to 1.9311 ms and 1.3193 to 1.9732 ms.
These are failed acceptance observations, not an accepted FPS improvement. Per-frame managed
allocations for that fixture remain 152 bytes on both sources; resource counts are stable.
Setup rises from 1,058.7271/1,294.9939 ms to 2,014.5371/1,818.3938 ms for the first case;
its setup allocations rise from about 212.7 MB to 294.5 MB. The closed superset pins more mesh
buffers and retains more preparation data, while sharing reduces native texture count from
78 to 76. This is a measured startup/residency tradeoff and remains visible.

The original master's RendererCold fails before its first sample: initial Prepare leaves a
prepared asset frame, then VisualReferenceCapture observes diagnostics before any submission.
The original sequence stops there; it is not a successful cold pair. Failure metadata and all
four warm matrices are retained. No scene, quality, warmup count or threshold is reduced.

Serial scope approval explicitly includes ClientRenderHost.cs and VisualReferenceCapture.cs
for real stop/window-close preparation cancellation, and the reference's zero-warmup diagnostic
correction. Frozen renderer/graphics ABI, phases and fence semantics remain unchanged. The
subsequent build has zero warnings/errors (18.23s); thirty focused residency/boundary cases pass
(2.853s), and all 450 Client cases pass with two optional skips (17.314s). The runtime material
texture publisher now uses the existing same-queue deferred upload contract instead of issuing
one startup fence wait per texture. The initial failed warm measurements remain the first cohort;
final cold/upload/plateau evidence and human image approval are still pending.


On the integrated host source 16071418255c560335b17cf02a2bb681965dd1be, the complete solution
build again has zero warnings/errors (1m48.38s), and the full Presentation suite passes 1,433
cases with twelve optional skips / 1,445 total (49.639s). Existing Client contracts passed on
identical code immediately before the source-metadata commit. Ownership validation accepts
eighteen owned paths after listing the one serialized version path and two explicitly approved
host paths separately. Project references and the exact 0.1.420 -> 0.1.421 version rule pass.

The first native window and subsequent functional work ended at 13:42:39 UTC; no local build,
test, compiler or graphics workload is run during the next independently reserved measurement
window. Remaining native cases are explicitly separate follow-up observations, not replacement
samples for the failed first B-A-A-B gates. The comparison host retains the normal Prepared /
Record / Submit / Retire order; DXGI observations after retirement wait outside timed work for
the existing 250-ms sample cache to expire.


## Integrated native functional evidence

The corrected 16071418 RendererCold records all six cases, zero warmups, exit zero. Their
native pixels match the original master's warm-reference images exactly, with identical
runtime fingerprint, camera and fixture. The original master's cold-sampling failure is still
retained; fresh renderer setup costs can be compared because setup precedes warmups in both
protocols. Current six-case setup observations are 1,552.4417, 1,460.2454, 970.2814, 988.1071,
995.3475 and 993.4704 ms in matrix order. They do not erase either failed warm performance gate.

Three original-master and three integrated load/warm/unload cycles complete successfully.
The native facade identity is shared only on the integrated source. The current store reads
484 required containers (38,676,681 encoded payload bytes), pins 11,230,152 mesh bytes plus
23,564,584 texture-mip bytes (34,794,736 logical bytes), and retains two owners, then one after
instance disposal. Warm container reads, texture uploads, static-buffer transfers and upload
backpressure waits do not increase. Baseline has no prepared-read counter, so it remains null.
The canonical probe observes no same-ID texture overlap between the old independent terrain
and instance caches; sharing is not presented as a measured same-ID duplication win there.
The explicit same-ID multi-owner contract is tested separately, and the native facades prove
that their resource store is identical. Common fallback textures are now owned once per scope.

All final owner releases return registered native resources/descriptors/pending retirements to
their initial zero workload and clear prepared CPU payloads. The original three/six-cycle
observations sometimes admit the existing upload arena's fourth bounded slot late. A separate
unchanged-binary twelve-cycle observation preserves those results and shows nine consecutive
identical retired observations: cached staging capacity 20,971,520 bytes, DXGI local usage
17,477,632 and nonlocal usage 57,745,408 bytes. These are device-level caches/process readings,
not per-asset Default-heap allocation. Exact Default-heap committed bytes and a measured warm
shader-compilation counter remain unavailable; neither is reported as zero.

The current runtime-only archive has 1,532 entries, no authoring/compiler files, and passes its
own packaged-player smoke with new settings and a child PATH containing only system/.NET
locations (exit zero, packaged runtime root confirmed). The system Blender installation is
not removed. A real owned reference-window close at 1,718.988 ms triggers the preparation scope's
OperationCanceledException and exits as expected (reference cancellation exit one) in 2,833.015 ms,
without a hang. Both cancelled and successful complete captures remain retained.

Source, exit codes, raw samples, images, original/retained hashes and explicit path redaction
are recorded under evidence/. The first expanded plateau adapter build exposed a parameter-name
analyzer error before execution; the corrected adapter builds cleanly. This failure is retained.

A subsequent remote-master advance to a1610d1a51d4107c4b8202e234ed33edd3cfc9c9 integrates
logistics/alert/CI changes and version 0.1.421. Historical measurements above remain tied to their
actual sources. The branch must integrate that master, move its isolated delta to 0.1.422 and
repeat integrated build/tests before any final acceptance claim.


## Current-master integration

Master a1610d1a51d4107c4b8202e234ed33edd3cfc9c9 is merged without rewriting history. The isolated
version delta is now exactly 0.1.421 -> 0.1.422. Product source 1edf00e20cdc3887eebeab57da2cc0863ea60106
builds with zero warnings/errors (2m08.90s). Its complete unfiltered solution run finishes in
7m16.338s: 3,047 passed, five failed, eighteen optional skips / 3,070 total, exit two. All modules
except Game pass, including the entire Assets, Graphics, Presentation and Client suites. The five
remaining failures are the previously documented supply, docking, tactical refueling, expansion
funding and EastOffensiveObjective cases. DirectCombatTargetsRequireCurrentIdentifiedIntelligence
now passes following the master update. Game, Game tests, Logistics, Simulation and Economy have
no branch delta from current master. An independently isolated current-master baseline run
likewise reports five failures/two passes across those seven original cases.

The final integrated image/function matrix compares the unchanged current-master a1610d1a client
with product 1edf00e2, same owned runtime root, High/1920x1080, three zooms and two scenes,
128 warmups/64 samples/two repeat readbacks. Both native processes exit zero. All six rows have
identical runtime/device context and MaximumChannelDelta zero. This deliberately shorter final
image verification is not a replacement warm performance cohort and grants no timing acceptance.
The original failed B-A-A-B reports remain the relevant unresolved timing observations.

The final 0.1.422 runtime-only archive passes asset/UI/icon/audio validation and actual player
startup, exit zero, own packaged runtime confirmed, 1,532 archive entries/no authoring files.
The complete package and images in this observation use source 1edf00e2; subsequent integration
is recorded separately and is not inferred from these earlier images. The accepted native plateau observation remains source 16071418; residency,
render-host and reference-capture implementation files are unchanged by the master integration.
Its nine steady retired observations therefore remain a source-identified functional memory
result, while the final master-derived image and complete solution checks cover integration.

No frame gate, quality level, source texture, gameplay assertion or public renderer/graphics
contract is weakened. The PR remains draft until performance qualification, the visible baseline
blockers and required final CI/image acceptance are resolved. Main CI on the earlier product
source completed frame, movement and scalability checks; its long main job and the final
synchronization checks are reported by their actual status, never inferred from local success.


## Submission integration in current master

Master advances again to cd62153e by integrating the independently qualified instance-submission
implementation. This changes SimpleInstanceRenderer/visibility/submission code, so the combined
residency/submission product needs a new integrated build and complete suite. Existing records
above retain their exact source identities and unresolved gates. The next isolated version delta
is 0.1.422 -> 0.1.423. No existing timing result is relabelled as the new product or current master.


## Final combined residency/submission result

Current master cd62153ef77b6a5dda754381a0aa4b7346b32b29 is integrated without conflicts. Exactly one
PATCH remains: 0.1.422 -> 0.1.423. Final production source is
21650bad0584bdf390b33117b86e0833f0caee8c. Release build succeeds with zero warnings/errors
(1m39.27s). The complete unfiltered solution finishes in 5m51.222s: 3,074 passed, the same five
existing Game failures, eighteen optional skips / 3,097 total, exit two. All other modules pass.
There is still no Game/Logistics/Simulation/Economy branch delta from current master.

Because submission code genuinely changed during integration, a new fixed B-A-A-B warm cohort
uses the actual b69e5f72a1444bbffce149c6a71b7551a27044a4 baseline build. Its source and version
are verified identical to current master under src/, tools/ and Directory.Build.props; its actual
build identity is retained rather than relabelled as cd62153e. Parameters remain High, 1920x1080,
two scenes/three zooms, 128 warmups/512 samples/two repeat captures, unchanged 15-percent limit.
The known before-source RendererCold sampling defect is explicitly excluded by WarmOnly; it is
not a passed cold gate. Current corrected cold operation is captured separately. The earlier
failed cohorts remain untouched. Both new warm pairs are structurally comparable but fail with
five and fourteen metric/statistic threshold observations. All twelve image rows have zero
channel delta. This is still **implemented, not accepted**; no timing improvement is inferred.

Final corrected RendererCold passes all six cases on source 21650bad, exit zero. The 0.1.423
archive passes all packaging checks and its runtime-only child startup, exit zero, 1,532 entries,
no authoring/compiler files, fresh child settings. Archive SHA256 is
89a9790b957d6bbf7a3217411a735077d4fc6931f318f01ecc539896abd58115.
Its own packaged runtime is confirmed. All local builds/tests/native processes are finished.

The twelve-cycle resource plateau and controlled native window cancellation retain their
original source identities; residency implementation and its host bindings are unchanged by
submission integration. The new complete suite, two fixed warm pairs, six cold cases and
source-less player startup verify the actual combined renderer path. Exact Default-heap
allocation and shader-compilation call counters remain unavailable through the frozen contracts.

Remaining acceptance items: the failed unchanged warm time gates, visible pre-existing Game
failures, required human image review and final remote CI. The image review covers equality only
and cannot waive timing or gameplay failures. The PR stays draft; no merge/tag/release is performed.


## Human image decision

The project owner explicitly approves equality of the six final source-21650bad reference
images. The exact decision timestamp, source/runtime/manifests and six image hashes are recorded
in evidence/visual-approval.json. Both fixed pairs remain technically pixel-identical. This closes
the human visual-equality item only; failed warm time gates and baseline Game failures stay open.
