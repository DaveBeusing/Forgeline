# Spatial world audio

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The Windows player mixes the authorized [world event layer](WorldAudioEvents.md)
into 48 kHz stereo PCM16 on a dedicated audio owner. It ships 56 world WAVs and
`audio/playback.json` alongside the existing four interface cues. The separate
studio intro stream and interface owner keep their behavior. The authoring
`library.json` remains source metadata; the compiled graphics asset catalog is
not used to decode or import audio at runtime.

## Listening and controls

Settings > Audio exposes GAME AUDIO, GAME VOLUME, BATTLEFIELD VOLUME, ALERT VOLUME
and AMBIENCE VOLUME. Game audio defaults on, with master 65%, battlefield 80%,
alerts 90% and ambience 35%. Existing interface sound has its own enable and volume.
Settings persist through the existing validated settings store, including the
pause-menu settings flow. Missing fields in older settings use the new defaults.
Zero volume consumes events silently; enabling output does not drain old sounds.

The listener sits at the RTS camera target on the battlefield, using the camera's
ground-right axis. Range scales with zoom (1.5 times camera distance, clamped to
180-900 world units). Distance gain is `(1-distance/range)^2`, reaching zero at the
range boundary. Spatial clips are downmixed to mono before equal-power stereo
panning. Nonspatial ambience retains its original stereo channels; alerts and
results remain centered. This models the commander's view, without occlusion,
HRTF, Doppler or hidden-world queries.

## Bounded mixing

| Resource | Policy |
| --- | --- |
| PCM library | At most 24 MiB decoded PCM, 4 MiB per clip; manifest at most 64 KiB |
| World one-shots | 24 voices; combat can replace decorative activity; weaker voices lose admission |
| Alerts/results | Four reserved voices, independent of world pressure |
| Selected loops | Up to 16 nearest/audible candidates, including protected global ambience beds |
| Total voice storage | 64 fixed entries, including slots for fading old loops |
| Native output | Three reusable 480-frame stereo blocks; at most 30 ms queued PCM |
| Loop fades | 50 ms full-scale gain ramp; retained identity preserves sample position |
| Virtual loops | No physical voice outside selection; re-entry uses the mixer timeline within loop endpoints |
| Event start | Original 250 ms monotonic/tick deadline, sequence deduplication and muted-watermark policy |
| Heartbeat | Missing/incoherent/250 ms stale publication clears voices and resets queued output |
| Driver recovery | At most four attempts with 250 ms / 1 s / 4 s backoff; disable/re-enable permits another bounded attempt series |

World/alert/ambience headroom is 0.18/0.5/0.1 before the bus volumes. A continuous
`0.9*x/(1+abs(x))` limiter keeps output below 90% PCM full scale. Bus changes and
loop entry/removal ramp gain; one-shot admission may steal a lower-priority voice.
The producer also admits combat ahead of retained engine/logistics pressure while
preserving reserved alert capacity. Remaining candidate truncation at extraction
is stable source order; nearest selection covers the published set, not every
entity in a ten-thousand-unit simulation.

A newly authorized terminal cue clears world voices. A result cue that started
fresh may finish beyond its start deadline while the focused result view keeps
publishing frames. A stale result cannot start later or replay after muting.
No synthetic match outcome is introduced.

## Ownership and failure behavior

Rendering hands off one immutable snapshot, listener and settings packet through
a bounded latest-value slot. A reset generation preserves brief mute/focus/pause
transitions even when a newer active packet replaces them before the owner wakes.
The audio owner alone loads/hashes/parses clips, mixes samples and accesses WinMM.
Repeated unchanged scenes skip selection work. No authoritative registry, gameplay
random generator or mutable presentation buffer reaches the mixer.

The loader derives paths from the fixed production catalog, verifies SHA256 and
frame-based loop endpoints, and isolates a bad individual clip. All 56 production
clips are packaged; the existing three unavailable event bindings remain silent.
The worker exposes loaded clips, active voices/loops, dropped shots, limiter
activity, queued buffers and device failures. Source/package identity is checked
by `Validate-WorldAudio.ps1` during publication and CI.

Focus loss, pause/menu, minimize, session/player mismatch, rollback, absent/stale
packets, disable and shutdown stop playback. Cursor state survives device recovery;
recovery discards pending history rather than replaying it. Disposal signals the
owner without blocking rendering on a driver. The owner resets/unprepares/closes
its own output; memory is retained if a failed driver refuses to return buffers,
and another device is not opened while such native resources remain owned.

## Qualification

[Spatial audio qualification](SpatialWorldAudioQualification.md) records the frozen
source, tests, native output, package and remaining gates. Buffer submission does
not qualify subjective listening, physical device removal or end-to-end acoustic
latency. Current texture/window and natural terminal-match gates remain independent.
