# UI motion and interface audio

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

<details>
<summary>On this page</summary>

- [Notification entry and inline detail tracks](#notification-entry-and-inline-detail-tracks)
- [World-anchored radial visual track](#world-anchored-radial-visual-track)
- [Notification and context-hint motion](#notification-and-context-hint-motion)
- [Motion vocabulary](#motion-vocabulary)
- [Intent and results](#intent-and-results)
- [Settings and compatibility](#settings-and-compatibility)
- [Audio ownership and content](#audio-ownership-and-content)
- [Qualification](#qualification)
- [Sliding onboarding exception](#sliding-onboarding-exception)
- [Operations panel skin motion](#operations-panel-skin-motion)

</details>

## Notification entry and inline detail tracks

New cards reveal from the right edge to their settled right-aligned bounds
using 180-ms SmoothStep alpha and bounded horizontal geometry. No simulation
tick drives this animation. The detail area opens below its owning card over
200 ms with the same smooth easing, and closing reverses from the live amount.
Only the current authorized card and its expanded bounds own pointer input;
retired fades display no stale text or targets. Existing 0.30 background
alpha and full-intensity severity cues are retained. Reduced motion, Low
quality, changed presentation geometry, focus loss, a >250-ms time discontinuity
or a reset session settle the track immediately. This feature adds no sound
and does not acknowledge or delete a critical alert.

Own single buildings and units now use [world anchored action rings](WorldAnchoredActionMenus.md). Selection release opens categories; the global mode bar and bound keys open the same ring. Click an item to act immediately. Parameters exposes existing priority, mode, thresholds and queue cancellation before activation; Next item selects the edited item. Back / previous / next page remain fixed controls. The inner hole and gaps allow world input; outside clicks dismiss centrally. The inspector remains informational. Multi-selection uses the same [group action rings](GroupActionMenus.md). Closed/fading rings own no input; alpha transitions last 160 ms and reduced/Low modes settle immediately.

UI feedback follows presentation time. Render owners feed a monotonic clock to the
bounded `UiMotionTimeline`; fixed simulation ticks, gameplay requests, selection
authority and copied intelligence are unchanged. Frontend routing and readiness
remain immediate. Leaving a panel switches to current content immediately, without
waiting for an outgoing animation. Startup, the studio compositor and its skip
policy retain their separate lifecycle.

## World-anchored radial visual track

The shared action-ring backgrounds have an effective settled alpha of 0.30, independent of the foreground/icon opacity and independent of the 160 ms presentation fade. Disabled, hover, pressed and locally pending fills use the established palette with the same background alpha ceiling. A reversed open/close track starts at its currently rendered alpha and does not move its hit region. Logical closure immediately frees input; no fading quad participates in hit testing. Invalid projected geometry is discarded rather than fading stale world content. Reduced motion, Low quality, paused/suspended presentation, resized viewport, changed DPI/UI scale, clock stall or rollback settle the target immediately. Native captures for this revision are not automatically qualified by earlier UI feedback evidence.

## Notification and context-hint motion

Notifications, local command feedback and the separate placement-context hint use bounded 180 ms SmoothStep alpha tracks per displayed lifecycle/content identity. Their container background is independently tinted to a 0.30 opacity ceiling; foreground glyphs, icons, rails, and buttons retain independent contrast. New/revised content fades in on presentation time; a resolved row immediately disappears from the interactive stack, with only an optional short-lived noninteractive background remnant. Critically active alerts never time out merely because their animation completes. Reduced UI motion, Low quality, focus/minimize/pause suppression, session/display changes, stalled or reversed presentation time settle to the logical state immediately. This does not animate or change any input hit geometry.

## Motion vocabulary

The existing 140 ms screen-entry interval is a short accent-line reveal within the
new panel, rather than moving text or controls. Focus rails grow in place over
80 ms. A 220 ms, nonlooping result rail accompanies explicit copied result text.
Pressed/hover/disabled feedback remains immediate and structural. There is no
flashing loop, camera movement, hidden text or animation-duration readiness gate.
These decorations keep stationary hit regions. The sliding onboarding exception
below shares its transformed clip with input. Selected controls retain a visible
rail throughout.

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

`ForgeLine.Audio` defines the optional `IUiAudioOutput` contract and bounded UI PCM reader.
`WaveOutUiAudio` is now an asynchronous mailbox owned by a separate audio thread.
That owner preloads each clip independently, opens WinMM only when enabled, scales
samples and releases its own buffers. File reads, driver calls and native teardown
never execute in an interactive render callback. Muted startup opens no device.
The studio stream keeps its independent owner.

There is at most **one pending cue**. Rejected outranks Accepted, then Intent and
Navigation; equal-priority arrivals replace older pending requests. Navigation and
Intent retain a 60 ms throttle. Results bypass it and can retry a busy voice only
until **150 ms after original creation**. A failed attempt never refreshes expiry.
This deliberately replaces the former global no-queue policy. It is not a promise
that every result is audible: expired, superseded, muted or unavailable requests
remain bounded and are counted. One active nonlooping voice is never overwritten.

Mute, invalid/zero gain, focus loss, minimize, frontend transitions, incoherent
snapshots, session replacement and match completion clear pending output. A request
already inside a driver call can finish before the owner observes cancellation;
no other thread resets or frees its memory. Dispose signals cancellation and returns
without joining a potentially blocked driver. The owner performs final release;
`WaitForStop` is a bounded qualification helper, not a gameplay wait.

Clip errors disable only that clip. Device open/prepare/write errors reset and
release safely, then retry on later fresh requests after 250/1,000/4,000 ms. Four
consecutive failures stop opening until explicitly disabled and enabled again.
No autonomous stale result is kept across recovery. A busy header exceeding clip
duration plus 500 ms triggers recovery on the next offered request. Buffers refused
by Unprepare/Close stay owned and are not reopened or freed underneath the driver.
The diagnostic snapshot records availability, retained native resources, played
requests, drops, failed attempts, per-clip/device failures, pending depth and owner
termination. These internal counters do not yet appear as a Settings status row.

Native gain is applied to PCM samples, leaving system gain untouched. Teardown
follows the [Windows reset](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutreset)
and [buffer lifetime](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-waveoutunprepareheader) contracts.

The existing studio stream retains its playback/fade/skip behavior and now restores
the gain observed when it opened before closing. On the qualified Windows device,
changing one legacy stream's volume also changed a second stream's reported gain.
Restoration prevents the studio fade from leaving later interface cues muted. The
native probe verifies this interoperation without repurposing the studio clip.

The four stereo 48 kHz PCM16 cues now use the supplied industrial library revision
1.0.0: navigation 35 ms, intent 55 ms, accepted 95 ms, rejected 110 ms, total
56,640 PCM bytes. The UI limit remains 38,400 bytes per clip. The supplied procedural
provenance notice ships as `ThirdPartyNotices/UiAudio.txt` and
`ThirdPartyNotices/IndustrialAudio.txt`; no new license grant is invented for these
replacement files. Earlier interface files and their CC0 notice remain in history.

[Industrial audio qualification](IndustrialAudioQualification.md) records the
60-file source import, independent signal checks, exact hashes and remaining world
integration gates. `assets/source/audio/library.json` is authoring metadata, not a
player runtime manifest. Only the four UI files are copied into the player. The
other 56 clips have a separate `WorldPcmClip` mono/stereo/frame-loop contract and
are not yet connected to gameplay. `build/audio/RenderIndustrialAudio.py` (also
reachable through the historical `Generate-UiAudio.py` entry point) is offline only.
Use an explicit empty `--output` directory to reproduce and compare source hashes.
`Validate-UiAudio.ps1` checks the complete source library and exact published UI
copies without requiring Python packages in CI or the player.

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

See [executed package/native qualification and remaining acceptance](UiFeedbackQualification.md).

## Sliding onboarding exception

Only the optional onboarding panel moves: a 220 ms SmoothStep (3p² − 2p³) slide
from the currently painted position to the left clip boundary. Its copied
`OnboardingPresentation` owns panel transform, clip, visible intersection and tab;
painting and hit tests consume that exact revision. Other controls keep stationary
hit regions. One transition reverses immediately without queues or simulation time.
Reduced Motion, Low/fallback quality, a gap over 250 ms, rollback, focus loss,
minimize and viewport changes settle to the current logical target. Geometry changes
cancel stale gestures. No decorative timing gates input or readiness.

## Operations panel skin motion

The single `OPERATIONS` entry stays visible and occupies its existing shared safe-area rectangle; an active rail distinguishes its open and closed states. Both entry and panel container backgrounds use 0.30 effective alpha with native alpha blending, independent of text/icon/button foregrounds. The existing panel's skin uses a reversible 180 ms SmoothStep alpha track driven by monotonic render-owner presentation time. Open/close intent, filters, pages, focus, notification details and existing-controls actions remain logically immediate. A closed panel may leave a short fading background rectangle, but that remnant has no hit region, data content or command authority. A newly opened panel owns its whole unchanged `OperationsLayout.Panel` rectangle during the incoming fade. Motion settles immediately on Reduced Motion, Low quality, focus/pause/minimize suppression, session/viewport/DPI/UI-scale changes and 250 ms clock interruption or rollback. This adds no simulation timer, snapshot data, graphics dependency or authoring asset.
