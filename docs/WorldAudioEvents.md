# Authorized world audio events

The presentation event bridge translates completed-tick, permitted information
into immutable audio facts. It is the prerequisite for the world mixer. It adds
**no world playback, devices, buses in Settings, PCM loading or new packaged WAVs**.
The four existing UI cues and their independent owner keep their current behavior.

## Ownership and information boundary

`PresentationExtractor` owns one `WorldAudioEventBridge` only when constructed with
a real `PresentationExtractionContext`. Unscoped diagnostic/reference extraction
cannot create world audio. The live simulation is read only by extraction; the
payload and cursor retain no simulation registry or mutable source buffer.

Owned vehicle, industry, construction, logistics, supply and repair observations
are captured after the render-extraction authorization gate. Foreign mutable
production, inventory, health and supply facts never enter the bridge, even when
a foreign entity is visible. This intentionally conservative policy currently
omits visible-enemy engine and industry loops.

Combat is observed at the existing permitted-event gate, before the VFX pool can
refuse a visual slot. A shot uses a known owned/currently visible source position;
otherwise only an already-visible event position can be used, with no source ID.
Impacts require visible terrain and discard unknown target identity/classification.
New visible wrecks publish destruction cues independently of visual pool capacity;
the initial baseline and wreck history prevent playing old wrecks on discovery.
Retained spatial events are rechecked against current visibility before publication.
Their positions are frozen historical facts, never extrapolated hidden movement.

Every snapshot is scoped to a specified session, player and completed tick.
`WorldAudioSnapshot.Resolve` rejects mismatched root/experience/audio identities,
future or inconsistent ticks and older packets. The writer rejects rollback and
retired lower session identities; changing viewer in a session requires a new
writer. A repeated tick returns the same payload and cannot authorize new cues.

## Events, loops and bounded state

One-shot identity is `(Session, Player, Sequence)`. Sequence ordering is local to
presentation and variants rotate through the three supplied takes without using
simulation RNG. Readers retain their own cursor across device recovery. A limited
output span does not consume events that did not fit. Re-reading a packet does
not replay its events.

Requests remain eligible for at most **250 ms of monotonic wall time**, and at
most `ceil(250 ms / tick duration)` ticks, capped at 32 ticks. Timestamps are copied
from the extraction owner. Both the producer journal and reader check age, so a
frozen tick or resumed simulation cannot replay a minutes-old shot. The expiry is
a **start deadline**, not an instruction to truncate a long clip after 250 ms.
`SuppressThrough` advances the cursor silently while output is disabled or being
recreated; enabling output must not drain muted history. Timestamp injection in
unit tests uses the same Stopwatch units as production.

Loops are a **complete replacement set**, keyed by full generational EntityId and
cue; global ambience also uses the cue as identity. Disappearance, ownership loss,
nonoperational industry, wreck state and match completion remove loop sources.
Missing/incoherent payload, focus loss, pause/menu transition and stale publication
must clear/stop mixer voices, rather than retain the last loop set indefinitely.
The next mixer phase must use `CapturedAtTimestamp` for its heartbeat policy and
fade changes between idle/move sources without resetting playback each snapshot.

| Resource | Bound / policy |
| --- | --- |
| Retained events | 256; 16 slots reserved against decorative/world pressure; a warning can displace an older world event |
| Current loop sources | 1,024, including three reserved ambience slots; source eligibility, not physical voice count |
| Transition history | 2,048 fixed slots; full identity/type/tick checks; replacements counted, uncertain entry transitions suppressed |
| Repeated supply/repair bursts | Rising activity edge plus at least twice the retention-tick interval, normally 500 ms at 20 Hz |
| Publication | Two copied bounded arrays and a snapshot; proposed local gate at most 64 KiB per warm publication |
| Overflow | Explicit cumulative drop/replacement/retraction metrics; no unbounded queue or dictionary |

Current ground velocity determines motion when available, so history pressure does
not turn moving real units into idle sources. Adjacent transform deltas are a
conservative fallback for sources without a movement read model. The current
stable extraction order is the source truncation order. Listener-aware selection,
virtual loops, attenuation, voice stealing and physical voice limits belong to
the next phase; this source limit does not promise all ten-thousand-unit events.

First capture, reacquisition, entity-generation/type change and history collision
do not invent old transitions. Construction placement remains nonspatial when
its accepted-feedback read model supplies no position. Notifications describe
completed-tick facts; accepted feedback does not mean delivery or research finished.
A terminal outcome removes all loops and old world history, emits only the actual
Victory/Defeat transition once, and cannot resume from a stray Active packet.

## Binding coverage

[Complete 56-clip ledger](WorldAudioBindings.json) matches the production manifest
and C# catalog in tests: **53 explicit bindings, three unavailable sources**.
The latter are rocket launch (no current rocket weapon event), incoming artillery
(no authorized telegraph read model), and research completion (no completion event
or counter). They stay unavailable rather than borrowing an unrelated trigger.

All 15 loop definitions are represented. Existing low-power/critical-supply/core
alerts are condition edges; core damage is not a per-hit alarm. Unit-ready feedback
comes from the owned production counter, not discovery of an unknown unit.
Wind is an active-match bed, industrial ambience requires owned operating sources,
and battlefield ambience requires retained authorized combat after retraction.

The library manifest remains authoring metadata with runtime support disabled.
No `RuntimeAssetCatalog.Get(audio...)` lookup is introduced. The world files remain
source assets until the mixer phase deliberately packages, loads and mixes them.

## Qualification and next step

Focused contracts cover catalog/ledger parity, all shot families and variation,
impact/destruction mapping, every loop family, immutable retention, duplicate and
skipped frames, session/player/tick rejection, visibility retraction, frozen/resumed
wall time, muted backlog suppression, full generational identity, transition edges,
terminal cleanup and source pressure. Real-match fixtures exercise hidden enemy
shots versus visible anonymous impacts, movement/destruction and unchanged
authoritative state with/without the observer. Measurement captures distinguish
source publication from playback, acoustic latency, GPU frames and whole-client
memory. Frozen-source outcomes are recorded in [world audio qualification](WorldAudioEventQualification.md).

The next step is the bounded world/alert/ambience mixer and owner lifecycle,
including deliberate world-WAV packaging, PCM/headroom policy, source selection,
listener positioning, virtual voices, fades, output settings and native/device
qualification. Required graphics/texture, whole-suite allocation and natural
terminal-match gates remain separate and must not be relaxed for this feature.
