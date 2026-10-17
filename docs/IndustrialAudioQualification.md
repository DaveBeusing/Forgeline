# Industrial audio qualification and remaining integration

The current change hardens optional interface output and imports the supplied
industrial source library. It does **not** complete a gameplay mixer, certify a
release, or turn the production manifest into a player manifest.

## Source and integration boundary

Base: `08dc69cf83be37bab31ecc69f37c180cb09a160b`, current master when work began.
No overlapping open audio PR was found. Product version advances once from
0.1.388 to 0.1.389. The independent studio stream and simulation rules are retained.

`assets/source/audio/library.json` records 60 original procedural WAV files with
unique IDs and SHA-256 values. There are 15 full-file loops, 157.165 seconds of
unique content, 20,852,160 PCM bytes and 20,854,800 WAV bytes. No recordings or tools
are bundled from external sound libraries. The supplied provenance notice is
retained; it does not grant new CC0 terms. The previous four interface recordings
and their notice remain in Git history.

Only Navigation, Intent, Accepted and Rejected are copied by the existing client
content pipeline and used in gameplay/menu output. All are stereo 48 kHz PCM16
with unchanged 35/55/95/110 ms lengths and the unchanged 200 ms UI parser ceiling.
The other 56 files remain source assets. `WorldPcmClip` adds a separate bounded
4 MiB mono/stereo PCM reader with inclusive start and exclusive end sampleframe
loop metadata. It does not create voices or decide which events are audible.

[Interface ownership and policy](UiMotionAndAudio.md#audio-ownership-and-content)
describes the audio worker, one-slot priority mailbox, 150 ms expiry, separate
60 ms decorative throttle, cancellation and bounded device recovery.

Startup checkpoints now compare event count **and readiness/session state** and
only acknowledge a successful atomic report replacement. A failed report write
can be retried without another event. No per-checkpoint event-array copy is needed.

## Executed source qualification

- Source-library PowerShell gate: all 60 hashes, canonical RIFF/PCM structure,
  sampleframes, loop endpoints and fixed totals pass. The UI gate still verifies
  exact short-clip lengths, provenance and byte-identical published copies.
- Independent signal validator: **60/60 pass**, including four-times polyphase
  peak estimates below -3 dBFS, DC below -60 dBFS, no clipped samples, unique audio,
  silent one-shot endpoints and all 15 loop seams within the numerical criterion.
  These are signal checks, not listening or multivoice mix approval.
- Offline reconstruction using Python 3.12, NumPy 2.3.5 and SciPy 1.17.0:
  **60/60 regenerated WAV hashes match the supplied manifest exactly**, including
  on Windows. Recipe keys and emitted paths use portable forward slashes.
- Initial focused reliability run: **19 passed**. Coverage includes 49/50/59/60 ms,
  95/110/150 ms busy-result ages, expiry at 151 ms, priority under 1,000 decorative
  offers, old-attempt replacement, cancellation, partial clip failure, four bounded
  device-open failures, prepare/write recovery, stalled playback and retained
  buffers when Unprepare or Close refuses release.
- The first broad Client run: **392 passed, 1 failed, 1 explicit native test skipped**.
  The failing existing startup checkpoint test read Pending instead of Failed.
  The original failure is retained. Two deterministic checkpoint regression cases
  cover readiness changes without an event and retry after a failed replacement;
  the complete post-fix result is recorded below after execution.

Commands and full raw logs are retained under `artifacts/audio-*`. Reproduction:

```powershell
pwsh build/Validate-UiAudio.ps1
dotnet build ForgeLine.sln --configuration Release --no-restore -m:4
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build
# Install the pinned offline authoring requirements into a local tool environment first.
python build/audio/ValidateIndustrialAudio.py --root . --output artifacts/audio-signal-validation.json
python build/audio/RenderIndustrialAudio.py --output artifacts/audio-regenerated
```

## Required qualification remains red

The master CI [run 39035035692](https://github.com/DaveBeusing/Forgeline/actions/runs/39035035692)
failed at `Windows window mode and visual qualification smoke` with
**99,608,050 compiled gameplay texture bytes > 58,720,256 bytes (56 MiB)**.
Its original failure log confirms the same condition as the local unchanged
qualification script and existing runtime asset report. This occurs in the catalog
precheck before launching the window, not as evidence of a new audio/driver crash.
Downstream skipped checks are not passes. No texture budget, assertion, scene
content or timeout is relaxed by this change.

The historical GPU comparisons in earlier qualification reports are not new
measurements of this branch. No GPU speedup, stable 60 FPS or reduced whole-client
memory usage is claimed. The natural terminal-match gate also remains open in
prior evidence; it has not been substituted with a forced result here.

## Ordered remaining milestones

| Priority / milestone | Concrete work | Acceptance |
| --- | --- | --- |
| P1 / Windows qualification | Attribute catalog bytes by texture family; reduce actual production content/residency with reviewed artwork and matched native captures. Rerun the unchanged catalog/window gate and all formerly skipped CI stages. | At most 56 MiB catalog and 64 MiB measured scene, both native window modes pass, debug layer clean; complete required CI and natural terminal-match validation pass. |
| P1 / Interface physical devices | Exercise no device, unplug/return, output switching during playback and blocked teardown on target devices. Expose the internal audio-state snapshot in Settings. | No crash, no freed driver-owned buffer, four-failure backoff and explicit re-enable behavior visible; Settings accurately reports unavailable/recovering output. |
| P1 / Presentation event bridge | Map combat, vehicles, industry, logistics, construction, alerts, ambience and terminal events from coherent copied presentation data. Define event identity/session/tick and deduplicate one-shots. | All 56 world clips have an explicit binding or a documented unavailable source event; future/stale/cross-session inputs produce no sound. Hidden enemy positions/activities never create audio without an explicit permitted-information rule. Simulation RNG and save/replay hashes remain unchanged. |
| P1 / Bounded world mixer | Own world/alert/ambience buses on an audio owner; implement mono positioning, attenuation, virtual loops, variant rotation, attack/release and loop crossfades. Keep short UI and studio output independent. | Proposed starting limits: 32 world, 2 alert and 2 ambience voices; hard caps, bounded requests, PCM/mix headroom, no clipping/NaN, no allocation or file/decoder work in the callback. Review these proposed limits against target hardware before adoption. |
| P1 / World lifecycle | Bind loops to session and stable entity identity; stop/virtualize on pause, destruction, visibility loss, menu return and session replacement. | Repeated menu/match/menu and 30-minute stress leave no active orphan voice or growing native resource count. Numerical loop checks and subjective crossfade/mix review both pass. |
| P2 / Measured latency and mix | Record cold load, first/warm cue submission and observed output latency separately; stress simultaneous combat, industry and alerts. | Publish source/payload/device/buffer identities, p50/p95/p99 and overruns. A buffer submission is never described as measured acoustic latency or a listening pass. |
| P2 / GPU and mailbox measurements | Capture matched GPU pass times, CPU submit/present wait, resident/upload bytes and overlay publication allocations. Optimize only the measured bottleneck. | Frozen payload/map/camera/quality and warmup; repeated before/after distributions; leases only returned after the final consumer, no native lifetime regression. |
| P2 / Render shutdown | Add progress/timeout diagnostics and a defined escalation state for the render owner; keep resources alive while native work can use them. | Normal/repeated dispose, blocked renderer, queued frame and owner exception tests; no blind timeout followed by resource destruction. |

The source library is a foundation for these milestones. Numerical signal checks,
UI buffer submission and a passing unit subset do not close the world-mix,
physical-device, visual or full-release gates. Keep the pull request draft while
required qualification is unresolved.
