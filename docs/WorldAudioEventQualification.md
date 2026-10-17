# World audio event qualification

Qualified code and tests: `56758c3bac7191742fcfb46eec9d6b4f3ad829a5`.
Base: `09d431c1ed5361442e19b1e7f94d6803e3e5640b`. Version **0.1.390**,
one PATCH for this pull request. Subsequent qualification commits change only
documentation and evidence. This implements the [presentation event layer](WorldAudioEvents.md).

## Results

Release solution build passed with zero warnings/errors using SDK 10.0.401.
All **52 focused cases** passed. The full solution completed in **6m30.383s**:
**2,476 total, 2,466 passed, zero failed, 10 existing opt-in skips**. There are no
new skipped world-audio tests. This successful run does not establish the cause
of earlier intermittent allocation failures or claim to repair them.

The source library retains 60 files, 15 loops and 20,852,160 PCM bytes. Format,
hash and provenance checks pass. The 56 world clips have **53 explicit bindings**;
rocket launch, incoming-artillery telegraph and research completion lack current
authorized event sources. All 15 loop definitions are represented. World WAVs
remain source-only; the player packages the existing four interface audio WAVs
and its existing separate branding intro WAV.

The same seed-4209, 16-tick real-match fixture produced identical authoritative
SHA256 `A3E44C716381FFD0BA989CF1D98CE1A5C1279BD520CE8416F7F60FB88C166745`
with and without the observer. This short fixture does not qualify natural match
termination. Simulation, Game and Headless source files are unchanged.

## Publication measurements

Each fixture uses 64 warmup ticks, 64 measured publications, 16 synthetic shots
per tick and alternating moving/idle vehicle facts. The source count below means
owned vehicles; the zero-vehicle fixture still emits shots. These are extraction
and immutable-publication measurements, excluding playback, acoustic latency,
GPU frames and whole-client memory.

| Owned vehicles | Median ms | p95 ms | Maximum managed bytes/publication |
| ---: | ---: | ---: | ---: |
| 0 | 0.0154 | 0.0199 | 5,328 |
| 100 | 0.0910 | 0.1036 | 18,496 |
| 1,000 | 0.3355 | 0.3524 | 43,544 |

Every measured warm publication passed the 65,536-byte gate. Event retention is
bounded at 256 entries with 16 reserved alert entries, loop candidates at 1,024,
and transition history at 2,048 slots. Events expire within 250 ms of monotonic
elapsed time and their simulation-tick limit. Rollback clears abandoned history.

## Package and native smoke

Two clean publishes produced the same **40,078,057-byte** development ZIP:
`FORGELINE-0.1.390-dev-win-x64.zip`, SHA256
`649de095a8bdd19ea147a9d8a16bd306ca12d2456c5293219fda3f5333e6fe6a`.
Provenance identifies the qualified clean source, SDK 10.0.401 and compiler 1.10.0.
The installed manifest contains 1,337 runtime assets. All four interface WAVs
match their source hashes; no world WAV is packaged.

Both the source client and extracted player reached application/session
**Ready/Ready**, with zero dropped startup events. The extracted player exited
successfully from an initially empty working directory, leaving it empty and
writing zero stderr bytes. Both used isolated settings. The installed fixture
enabled existing interface audio; no physical-device listening result is claimed.

## Required gates remain open

The code-source CI run [39040569584](https://github.com/DaveBeusing/Forgeline/actions/runs/39040569584)
passed the new world-audio contracts and existing interface-audio contracts, but
failed the unchanged Windows qualification precheck: **99,608,050 compiled gameplay
texture bytes exceed the 56 MiB budget (58,720,256 bytes)**. The same original gate
failed locally before launching window qualification. Component smokes do not
clear that required gate. No budget, assertion, game rule or timeout was weakened.
Natural terminal-match qualification remains open. Keep the pull request draft.

The next step is the mixer, spatial playback, source selection and device lifecycle.
Physical sound, mixed headroom, audible world events, output devices and listening
latency have not been qualified by this event layer. Alerts have reserved capacity,
but world events currently share FIFO pressure: retained decorative events can
suppress later combat until expiry. Loop truncation uses stable source order.
Combat priority and listener-aware voice selection must be refined before audible
rollout.

Compact machine-readable results, provenance and raw-log hashes are in
[validation.json](evidence/world-audio-events/validation.json), with the adjacent
original determinism and measurement records. Raw logs and the development ZIP
remain in the local artifacts directory.
