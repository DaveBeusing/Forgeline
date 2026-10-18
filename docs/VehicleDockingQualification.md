# Vehicle docking qualification

The change is not merge-ready. The natural strategic progression and terminal-match
gates remain open. Current source-specific results and CI status are maintained in
[PR #233](https://github.com/DaveBeusing/Forgeline/pull/233).
This record distinguishes earlier executed evidence from subsequent corrections;
it does not promote old binaries or images to a new source revision.

## Attributable evidence

| Revision / scope | Executed result |
| --- | --- |
| `940db315f8761fcb2925a40b8da9ea20ad6c9998`, integrated master `cd85373` | Release solution build: zero warnings/errors; 44-project graph and SemVer passed. Full solution: 2,678 tests, 2,659 passed, 16 skipped, three failures. |
| Same revision, runtime content | Compiler 1.12.0; 1,357 assets, 75,122,812 runtime bytes, no qualification issues. |
| Same revision, package | Two publish trees matched. Immutable 0.1.400-dev archive SHA-256: `73f1e3d8e826f446a136028bc0fdc473324375a13ffb6535ef5f0410065267da`. Full source SHA and clean-tree state are in its provenance. |
| Same package, Windows startup | Fresh explicit settings root, validated persisted settings, 1,000-instance client smoke; two menu launches after one warmup with application-icon checks passed. |
| Same revision, native fixtures | Six Full-HD High captures at two zooms plus three opposite-facing captures passed structural/repeat-image checks. Human motion acceptance remains pending. |
| `2ee54587c3cf0e9f5b0b7caffbe5e7a20a8e33db`, integrated master `3c61f47` | Version 0.1.401; Release solution build, project graph and SemVer passed. Game module: 495/496 passed. |
| Same revision, terminal match | Seed 2026, original 80,000 ticks: Active/Running, both opponents Mobilizing/PrepareOffensive. 223 completed deliveries, 22,297 cargo units, zero failed cargo orders or route failures. This is a failed terminal gate. |

The three failures in the earlier complete solution run were the natural
40,000-tick progression deadline, the player reload sequence and an asset-test
cleanup lock. The player sequence now explicitly reloads after field distribution;
its original full quantities and combined 1,200-tick deadline are retained.
Docking/fuel/rescue/player follow-up passed 96/96. A separate complete Assets run
passed 163/163 with serial class scheduling; no timeout or assertion was relaxed.
The formation-ownership correction has three dedicated cases, including a denied
owner and unaffected peer. Its preflight Game run passed 498/499; the same natural
progression test remains open.

[Machine-readable evidence](review/vehicle-docking/qualification.json) binds the
earlier package, runtime manifest, native originals and integrated Game result.
Full raw logs, manifests, save/replay smoke and immutable images remain under
`artifacts/docking/` in the qualification workspace. Later source/build identities
must be qualified separately.

## Concrete remaining gates

At `339cfc80aac6c063a3b3d470f4663f32c0e540ef`, the complete solution had
2,686 tests: 2,668 passed, 16 skipped and two failed (the natural strategic
deadline and `FollowingCleanWriterWaitsUntilBuildQualificationCompletes`).
The writer test passed isolated with its assertions unchanged. Release build,
project graph, SemVer, map, entity smoke and save/load/replay passed. All three
quality workflows passed; main CI was still running at this record.
The reproducible archive SHA-256 is
`97D507EA8F9909719B1A5A70094586D68F6A2CFA848CEF4E5DAA92612DAF718C`.
Fresh settings and two menu launches after one warmup, including native icon
observations, passed. The three source/package phase captures had identical
pixels and fixture fingerprints. All 1,358 common runtime files matched; 63
declared playback sidecars account for different whole-tree runtime fingerprints.
Human temporal acceptance remains pending.

Its 80,000-tick match retained the preceding 240-delivery/24,118-unit nonterminal
endpoint. The exhausted-source correction shared the Supply retry deadline,
which could be renewed between every strategic decision and never become due
to the planner. An offset-timer regression now runs through multiple decision
and retry periods. Source selection uses the existing strategic cadence after
physical departure; the physical Supply retry remains independent. Earlier
captures and binaries are not requalified by that correction.

The later `4924a1bbcf736ff42296907eb89d92067d92fbca` qualification completed
the Release build with zero warnings/errors, the 44-project graph, SemVer,
map compilation, 1,000/10,000-entity smoke and 256-tick save/load/replay.
Its full solution run had 2,682 tests: 2,664 passed, 16 skipped and two failed
(natural strategic progression and a building-state allocation assertion).
The allocation method passed all 13 cases in isolation without changing its
zero-allocation assertion. Three phase-focused Full-HD native images passed
structural/repeat-image checks; human temporal review remains pending.
Its reproducible package SHA-256 is
`F27C49AF73E72130CDE8F5CC8A9428D813C2B38CC1AEEDBDD3E96661B61A7C57`.
Fresh settings smoke passed. Startup icon observation failed in sample zero;
that attempt does not qualify startup. All three quality workflows passed;
the main CI run was still running when this record was written.

The reserved-lane admission deadlock is reproduced by a two-case movement test:
an already overlapping waiting truck must evacuate, while an outside truck must
remain outside. One case failed before the correction; both pass afterward.
The subsequent 80,000-tick match completed 240 deliveries and moved 24,118 cargo
units with no failed cargo orders or routes, but remained Active/Running with
both opponents Mobilizing/PrepareOffensive. It is a failed terminal gate.
The saved endpoint also exposed exhausted-source loading orders that automatic
planning never reconsidered after departure. Four new cases cover retry timing,
partial cargo retention, another stocked source and no available replacement;
one failed before the correction. All 35 supply-policy cases passed afterward.
That correction requires its own complete qualification; earlier evidence is
not promoted to its binaries.

- `SkirmishOpponentTests.BoundedHeadlessSkirmishProgressesThroughStrategicLoop`
  fails within its unchanged 40,000 ticks. The eastern factory is still waiting
  on inputs, with no live scout or tank and only its home hub at the deadline.
  Trace seed 2026 factory `156:1`, its Steel/Fuel requests and physical bay visits
  through the initial production sequence. Baseline master passed all 26 natural
  progression tests; this remains an integration regression.
- The original 80,000-tick terminal match must pass on the final production code.
  Successful delivery quantities do not satisfy victory or lifecycle completion.
- CI at `2ee5458` failed
  [UI audio reliability and industrial source contracts](https://github.com/DaveBeusing/Forgeline/actions/runs/39096058357)
  in `WorldAudioMixerTests.OwnerHeartbeatStopsNativeQueueAfterRenderPublicationStalls`
  (expected zero queued native blocks, observed one). An isolated 24-test audio
  run passed, with one opt-in test not run. This failure also occurred on master
  `cd85373`; the docking change does not modify that test or mixer.
- Earlier CI at `940db31` stopped at the
  [native WARP capture](https://github.com/DaveBeusing/Forgeline/actions/runs/39093889675)
  ten-second GPU-fence timeout. This is separate from the strategic regression.
  Required checks must be inspected at the actual final head.
- Native temporal review must verify aligned bodies, stationary wheels during
  service, entry/exit without lateral jumps and the waiting second truck. Frozen
  images and structural checks do not grant this acceptance. Phase-focused Cargo
  loading/unloading and concurrent Supply loading views are provided by the
  existing reference scenes; the focus target is part of their fingerprint.

The schema-2 seed-831/tick-4 checkpoint was measured through save/load as
`424C217FBDEB400FFAB3FC5A303C67467C9E3C7F68332AAA69110DF7DADC9729`
after restoring the original tactical contracts. The historical schema-1 checksum
and fixtures remain unchanged. No natural-match budget, collision limit, unit
price, image tolerance, tag or release was changed to qualify this work.
