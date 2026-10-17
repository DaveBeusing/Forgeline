# Spatial world audio qualification

Version **0.1.391**, one PATCH. Base `0a0a20a4a7b7ba467c86b8f1d6b0be10bbb37f68`.
The [implementation](SpatialWorldAudio.md) adds the mixer, camera-relative stereo,
Windows owner, world-WAV packaging and settings over the merged event layer.

## Source identity

Frozen C# and tests: `a3e2407cf670fb4a4dab3a092463007bb65fe40d`.
Clean development package: `b78c2bf544cddde41224280eb3580ec9ff319c7b`.
The latter adds only the artwork generator's audio-role classification; source,
tests, CI and version inputs are identical to the qualified revision. The generator
passes Python syntax and all seven audio-field role checks. Later evidence commits
change documentation only. Simulation, Game and Headless source files are unchanged.

## Tests and measurements

Release solution build: zero warnings/errors, SDK 10.0.401.
Focused audio/settings/artwork contracts: **80 passed, two explicit device cases
excluded from that run**. All **53 event/extraction contracts passed**.
The complete solution passed in **5m30.022s**: **2,501 total, 2,490 passed, zero
failed, 11 existing or explicit opt-in skips**. The two audio device cases were
also qualified separately on the real default Windows output (see below).

An earlier broad run on `d2823f94` failed three cases: an old two-row audio-menu
expectation, absent artwork bindings for the new fields, and 7,904 bytes in the
mixed timing/allocation probe. Menu assertions now verify all seven controls and
their persisted values. Artwork bindings and their generator cover the new fields.
The DSP hot path uses a bounded enum-range check, and warmup exercises the identical
timed path. The zero-allocation assertion is unchanged and passes in the final
full parallel run. This does not establish or repair the cause of unrelated
historical allocation failures. The original failed log is retained by hash.

The 1,024-source pressure fixture warms 512 blocks, then measures 64 blocks with
24 world shots and four alerts per block, 16 selected loops and 44 active voices:

| Measurement | Result |
| --- | ---: |
| Median source selection + PCM mixing | 0.3029 ms |
| p95 source selection + PCM mixing | 0.3152 ms |
| Managed bytes across measured blocks | 0 |
| Stereo output block | 480 frames / 10 ms |
| Fixed voice storage | 64 entries |

This measures synthetic source selection and mixing, excluding driver/physical
latency, the whole client and GPU performance. Maximum-mix tests keep every output
sample below 90% PCM full scale. Other contracts cover panning/rotation, distance,
nonfinite input, voice priority, loop continuity/removal, stereo ambience, buses,
scope/tick/age rejection, mute recovery, result completion, clip corruption,
heartbeat, unavailable/stalled devices and refused native-buffer return.

A seed-4209, 16-tick real-match fixture again produced identical authoritative
SHA256 `A3E44C716381FFD0BA989CF1D98CE1A5C1279BD520CE8416F7F60FB88C166745`
with and without the presentation observer. It does not qualify natural termination.

## Native output and installed package

The explicit world-device test submitted **50 blocks / 24,000 frames** and released
all native resources. The separate existing interface-device test also passed,
including asynchronous owner shutdown. These are submission/lifetime checks;
subjective listening, acoustic latency and physical unplug are not claimed.

Two clean publishes reproduce the same **55,504,281-byte** development ZIP:
`FORGELINE-0.1.391-dev-win-x64.zip`, SHA256
`7b96afd06741aa3792d38c19d934bea4d59318a17bb21560b37ae33f5034a521`.
Its provenance identifies clean package source `b78c2bf5`, SDK 10.0.401 and asset
compiler 1.10.0. The validated graphics catalog contains 1,337 assets. All **60 audio
WAVs** (56 world, four interface) match source hashes and frame-based loop metadata;
the existing separate branding intro is preserved. The three unavailable event
sources remain explicitly unbound.

The extracted player ran from an initially empty working directory using isolated
settings and adjacent assets. It reached application/session **Ready/Ready**, with
zero dropped startup events, empty stderr and an unchanged empty working directory.
It loaded all 56 world clips, submitted **60 blocks**, reported zero device failures
and retained no native resources at owner shutdown. The gameplay smoke uses a
forced completion fixture and is not a natural-match qualification.

## Gates remaining open

The canonical Windows qualification, using the repository-relative asset report,
fails the unchanged texture precheck: **99,608,050 gameplay texture bytes exceed
56 MiB (58,720,256 bytes)**. A supplemental windowed run submitted/presented frames
but failed **15 instance draws against the canonical LOD-aware budget of 12**.
That supplemental invocation supplied an absolute asset-report argument that the
script did not resolve through its repository-relative path handling, so it skipped
the texture precheck. It is not accepted as a passing canonical qualification.
Neither assertion nor budget was relaxed. Borderless qualification remains open.

The package-source [CI run](https://github.com/DaveBeusing/Forgeline/actions/runs/39044138870)
passed playback-catalog, interface/mixer and world-event contract steps at the
recorded snapshot; the overall run was still in progress. Evidence-only pushes
may supersede it. Final-head CI, graphics/window qualification, natural terminal
matches and physical listening/device-removal review remain open. Keep the PR draft.

Compact results, package provenance and original log hashes are recorded in
[validation.json](evidence/spatial-world-audio/validation.json), beside the original
measurement, determinism and native-device records. Raw logs and the development
ZIP remain under the local artifacts directory.
