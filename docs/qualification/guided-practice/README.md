# Guided practice qualification

Base: 8749198f89ce1f1c90684c0bcb3f8a3398e28f45. Product version: 0.1.505-dev. The local source projection passes the base revision as SourceRevisionId for build metadata; modified file hashes in the local review manifest identify the actual candidate. This is not a committed-release identity.

## Outcome coverage

PracticeScenarioTests exercises all three full lessons with ordinary simulation orders, actual paid construction, actual input delivery, production, loading and field transfers. Completed save restoration and replay reproduce objective timestamps; early saves also preserve active progress and authoritative hashes. Corrupt/missing metadata, cancelled restoration, owned snapshot scope, detached snapshots, critical unit loss and incompatible registered rules are covered. Restart/menu tests keep normal skirmish independent. Reading, deferring, disabling or blocking guidance cannot complete goals.

[Completed outcomes](completed-outcomes.json) records the seeded test timelines. These include test waits and are not human timing measurements.

## Local checks

- Release solution build: no warnings or errors.
- Module dependency validation: 46 projects, no cycles or forbidden dependencies.
- Game regression suites: practice, progress journal, pacing, persistence, compiled scenarios, scenario persistence, telemetry, paid production flow and conveyor persistence.
- Presentation regression suites: practice guidance, early guidance, lessons and guidance presentation.
- Client regression suites: practice entry/retry, scenario sessions, guidance interaction and main menu version metadata.
- Test-CompiledScenarioLoading.ps1: both ordinary scenarios plus three practice scenarios; startup, save, replay and explicit missing-content rejection.

The exact executed suite counts and candidate file/assembly hashes accompany the local review package.

## Native presentation

The D3D12 capture path --practice-guidance-capture renders the existing game HUD and guide at 1280x720/192 DPI and 1920x1080/96 DPI. It covers each lesson's first objective in active, complete and failed authored states and all resulting body pages. Repeat captures must have identical pixels; missing/dropped glyphs, assets and quads must be zero. [Native summary](native-card-summary.json) retains diagnostics and pixel hashes. Two representative frames were visually checked for text, pagination and the lesson-specific objective count.

These are authored card-state captures on a demonstration backdrop. Actual lesson success is established separately by command tests. There is no claimed full interactive native playthrough, human usability study, exhaustive performance qualification or completed remote CI run.

## Integration validation

The publication candidate starts from master 08e8cf2f2e2632b3239229dc6840928d957c364c and advances PATCH once to 0.1.506. It preserves the integrated recovery-capacity preflight. The Release solution rebuild passes with zero warnings/errors. The earlier outcome and native records above retain their original capture base and version rather than being relabelled as new measurements.
