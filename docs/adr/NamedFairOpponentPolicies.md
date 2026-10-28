# Named fair opponent policies

Status: implemented; acceptance follows required CI and recorded qualification.

Keep difficulty in validated immutable presets over existing
SkirmishOpponentConfiguration and SkirmishRaidConfiguration. Stable catalog keys
identify exact values; overrides resolve as custom. Standard preserves existing
Gameplay behavior. No resource/sensor/stat authority, command type, live AI
hierarchy or persistence schema is introduced.

The existing Game settings dictionary selects policies and persists its complete
values. The shared runner/parser and pacing telemetry remain outside this change.
Qualification-only decision causes are copied from completed-tick observations,
bounded and gated by existing debug capture. They introduce no player-facing
enemy information or authoritative state.

Harder policy bounds provide faster planning and larger legitimate candidate/
coordination windows, never knowledge or resource shortcuts. Win-rate ordering
requires evidence across seeds and start assignments; incomplete matches remain
explicitly censored. See [profiles](../FairOpponentDifficulty.md).
