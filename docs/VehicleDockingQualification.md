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