# UI motion and interface audio

UI feedback follows presentation time. Render owners feed a monotonic clock to the
bounded `UiMotionTimeline`; fixed simulation ticks, gameplay requests, selection
authority and copied intelligence are unchanged. Frontend routing and readiness
remain immediate. Leaving a panel switches to current content immediately, without
waiting for an outgoing animation. Startup, the studio compositor and its skip
policy retain their separate lifecycle.

## Motion vocabulary

The existing 140 ms screen-entry interval is a short accent-line reveal within the
new panel, rather than moving text or controls. Focus rails grow in place over
80 ms. A 220 ms, nonlooping result rail accompanies explicit copied result text.
Pressed/hover/disabled feedback remains immediate and structural. There is no
flashing loop, camera movement, hidden text, animation-duration readiness gate or
animated hit region. Selected controls retain a visible rail throughout.

These are initial engineering values, not a claim of completed usability testing.
Human motion/sound acceptance is required before calling the visual feel qualified.
Reduced UI motion, Low quality and the frontend software fallback show the settled
state immediately. A stall over 250 ms, clock rollback, resize, focus loss,
minimize or session replacement cancels decoration; resume starts with current
state. Rapid route/focus changes restart only the affected bounded decoration.
Render callers can leave motion unspecified for deterministic static captures.

## Intent and results

Frontend navigation uses a quiet navigation cue, and an actual pressed actionable
control may use a neutral intent cue. Neither signifies a simulation result.
Gameplay pending feedback requires coherent captured session/tick data. Only a new
copied command result with a valid kind and resolution tick can trigger the result
rail/cue. Accepted uses the acceptance cue; partial and rejected use the warning
cue while their existing explicit text/counts distinguish them. Acceptance of a
request never promises completion of construction, production, delivery or research.

First observation of a session, restored coherent state and inactive/modal intervals
consume result identity silently. Repeated copies cannot replay an old result.
Sound settings do not change the observation/consumption rules, so enabling sound
does not replay historical results. Disabled actions retain the current explanation
and input legality rules.

## Settings and compatibility

Schema 3 gains additive `reducedUiMotion`, `uiSoundEnabled` and `uiSoundVolume` fields.
Missing fields in existing schema 1/2/3 settings retain safe defaults: normal motion,
sound off, stored volume 0.35. Existing migrations and corrupt-settings quarantine
remain unchanged. Volume must be finite in [0,1]; zero is silent. Settings exposes
all three controls through the existing keyboard/pointer, scrolling and Apply path,
without resetting camera, display, bindings or graphics preferences. Saving uses
the existing validated adapter and restart boundary.

## Audio ownership and content

`ForgeLine.Audio` defines the small optional `IUiAudioOutput` contract and bounded
PCM reader. The Windows client owns one render-thread voice and its native buffers;
there is no additional audio framework or simulation dependency. At first requested
cue it loads the four small packaged PCM files once. The player performs no source
generation, heavyweight import or codec compilation. Muted startup opens no device.

One nonlooping voice and a 60 ms burst limit drop excess/busy cues; no sound is queued
for late playback. Mute, zero gain, focus loss, minimize and owner disposal stop
playback. Native gain is applied to PCM samples, leaving the studio stream and
system volume untouched. Missing/corrupt content or an unavailable device disables
this owner's optional audio; text and input remain usable. Owner disposal resets,
unprepares and closes the output before releasing native buffers, following the
[Windows reset](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutreset)
and [buffer lifetime](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutunprepareheader) contracts.

The four stereo 48 kHz PCM16 cues are original mathematical synthesis with no sampled
or third-party recordings: navigation 35 ms, intent 55 ms, accepted 95 ms, rejected
110 ms. Total PCM payload is 56,640 bytes. Their CC0 notice is in
`assets/source/audio/ui/NOTICE.txt` and ships as `ThirdPartyNotices/UiAudio.txt`.
`build/Generate-UiAudio.py` is offline authoring only. `Validate-UiAudio.ps1` verifies
format, exact lengths, notice and byte-identical packaged copies. Runtime catalog
formats and the independent studio intro are unchanged.

## Qualification

`--ui-feedback-capture <empty-output> <runtime-root>` samples fixed presentation
times at 30/60/144 Hz, repeats native images, verifies the final static state, and
records short native frame-submit/GPU cohorts. Authored copied command states are
never dispatched; the battlefield fixture retains its intelligence and geometry.
These rates are injected sampling inputs, not measured whole-client FPS.
Missing/dropped glyphs and submission faults fail the qualification. A short native
audio probe records actual device availability and busy-drop behavior separately
from subjective listening approval. See the accompanying qualification record for
executed checks and unresolved acceptance; stable captures do not grant pixel-golden
approval or physical-monitor certification.
