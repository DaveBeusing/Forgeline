# Gameplay Telemetry and Match Analysis

## Purpose

FORGELINE gameplay telemetry provides reproducible evidence for pacing, economy, logistics, combat, progression, and match-result analysis.

It is deliberately observational. Telemetry does not submit simulation commands, own authoritative gameplay state, alter priorities, grant resources, move units, select targets, or affect match results.

## Architecture Boundary

Gameplay telemetry is separate from engine and renderer performance diagnostics.

- GameplayTelemetryCollector reads completed simulation state and existing subsystem metrics.
- The collector is advanced by the headless host after each completed simulation tick.
- GameplayTelemetrySnapshot is a read-only structured result for one match.
- GameplayTelemetryAnalysis aggregates multiple fresh-match snapshots and compares compatible aggregate series.
- GameplayTelemetryBatchReport owns JSON serialization in the headless host.
- --diagnostics-output remains the engine/runtime diagnostics path.
- --telemetry-output is the gameplay/balance evidence path.

The simulation remains independent from the headless report format. No telemetry type participates in gameplay authority or command processing. Hot-path collection reuses scratch storage where practical, and enabling gameplay telemetry does not enable the heavier existing debug-capture paths.

## Reproducibility

Every telemetry match records its deterministic simulation seed.

For a headless batch, match seeds are derived from the configured starting seed:

    match 1 = starting seed
    match 2 = starting seed + 1
    match 3 = starting seed + 2
    ...

Each match is created as a fresh vertical-slice runtime. State, scratch storage, diagnostics, and telemetry do not carry between matches.

Reproducible evidence requires the same repository revision, scenario profile, starting seed, match count, tick budget, and gameplay configuration.

Runtime and machine metadata are retained in the batch report for context, but gameplay metric values are based on logical simulation state rather than wall-clock timing.

## Stable Metric Contract

Telemetry schema version 1 uses stable metric names together with an explicit owner, dimension, numeric value, and unit.

Owner values use:

- match for match-wide measurements;
- player:<id> for participant-owned measurements;
- network:<id> only when a power network cannot be associated with a configured participant.

Dimensions use stable resource or unit keys where a metric has a content dimension. Empty dimension means the metric is already fully identified by its name and owner.

### Economy and Production

| Metric | Meaning | Unit |
|---|---|---|
| economy.resource_income.quantity | Extracted raw-resource quantity observed from deposit depletion | quantity |
| economy.processing.output.quantity | Cumulative processing output | quantity |
| economy.processing.throughput_per_second | Processing output divided by logical observed seconds | quantity_per_second |
| economy.production.utilization | Running processing-facility ticks divided by observed facility ticks | ratio |
| storage.utilization.average | Average aggregate owned storage utilization | ratio |
| storage.utilization.peak | Peak aggregate owned storage utilization | ratio |

Resource-income dimensions use stable resource keys such as resource.ferrous_ore.

### Power

| Metric | Meaning | Unit |
|---|---|---|
| power.generation.average | Average generation over observed ticks | power |
| power.demand.average | Average demand over observed ticks | power |
| power.shortage.duration_seconds | Logical duration with power deficit | seconds |

Power shortage is observed from real network deficit. It does not infer shortage from later production state.

### Logistics and Supply

| Metric | Meaning | Unit |
|---|---|---|
| logistics.cargo.delivered.quantity | Physical Cargo Transport delivered quantity | quantity |
| logistics.cargo.completed_orders | Completed cargo orders | count |
| logistics.cargo.route_failures | Cargo route failures | count |
| logistics.cargo.failed_transports | Failed cargo transports in the final observed state | count |
| logistics.cargo.travel_time.average_ticks | Average completed cargo-order elapsed ticks | ticks |
| logistics.cargo.travel_time.maximum_ticks | Maximum completed cargo-order elapsed ticks | ticks |
| logistics.distribution.completed_requests | Completed automated distribution requests | count |
| logistics.distribution.failed_requests | Failed automated distribution requests | count |
| supply.fuel.transferred.quantity | Physical Fuel transferred through battlefield supply | quantity |
| supply.ammunition.transferred.quantity | Physical Ammunition transferred through battlefield supply | quantity |
| supply.shortage.duration_seconds | Match time with at least one Low/Critical/Unsupplied unit | seconds |

Cargo travel starts at the authoritative order submission tick and completes when the order leaves the transport after normal completion.

### Units, Combat, Expansion, and Match Result

| Metric | Meaning | Unit |
|---|---|---|
| units.production.completed | New owned unit entities observed after telemetry start | count |
| units.losses | Previously observed owned units no longer present | count |
| combat.damage.applied | Cumulative authoritative combat damage | health |
| combat.destructions | Cumulative authoritative combat destructions | count |
| expansion.completed_buildings | Buildings completed after telemetry start | count |
| match.duration.ticks | Authoritative completed tick or current tick | ticks |
| match.duration.seconds | Logical match duration | seconds |
| match.terminal | 1 when the lifecycle is completed, otherwise 0 | boolean |
| match.winner | Winning player ID, or 0 when no winner exists | player_id |

Unit dimensions use stable unit keys such as unit.directorate.main_battle_tank.

In addition to numeric match metrics, every snapshot contains a structured match-result block with public match status, lifecycle phase, outcome, termination reason, winner, defeated participant, start/completion/finalization ticks, and lifecycle transition count.

## Progression Milestones

Schema version 1 records the first observed tick for:

- progression.first_extractor_expansion;
- progression.first_supply_depot_expansion;
- progression.first_scout_produced;
- progression.first_main_battle_tank_produced;
- progression.first_artillery_produced;
- progression.first_offensive_commitment.

Milestones are participant-owned and recorded once per match.

## Debug Summaries

Each telemetry snapshot includes compact end-of-match summaries for:

- supply state and transferred Fuel/Ammunition;
- processing and unit-production activity;
- each participant's surviving combat-unit count and centroid as a coarse front indicator;
- each computer-controlled participant's current strategic state and active objective.

These summaries are diagnostic observations only. They do not create hidden gameplay knowledge or alter opponent decisions.

## Headless Batch Execution

Create a five-match deterministic validation batch:

    dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --matches 5 --require-terminal --telemetry-output artifacts/balance-telemetry.json

Diagnostics and gameplay telemetry can be emitted together:

    dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --matches 5 --require-terminal --diagnostics-output artifacts/match-soak.json --telemetry-output artifacts/match-soak.telemetry.json

The repository helper does this by default:

    pwsh ./build/Run-MatchSoak.ps1 -Profile validation -Matches 5 -TicksPerMatch 80000 -Seed 2026

## Aggregation

The batch report aggregates every compatible metric series by stable name, owner, dimension, and unit.

For each series it records sample count, minimum, maximum, mean, and population standard deviation.

Progression milestones are aggregated separately with sample count, minimum tick, maximum tick, and mean tick.

A missing milestone is not converted into tick zero. The milestone sample count therefore communicates how many matches actually reached the event.

## Revision Comparison

Compare a new batch against a previously retained telemetry report:

    dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release -- --scenario central-divide --profile validation --ticks 80000 --seed 2026 --matches 5 --require-terminal --telemetry-output artifacts/current.json --telemetry-baseline artifacts/baseline.json

Comparison is performed only for metric series present in both reports with the same name, owner, dimension, and unit.

Each comparison records baseline mean, current mean, absolute delta, and relative delta when the baseline mean is non-zero.

A comparison is evidence, not an automatic balance decision. Interpretation still requires gameplay context and controlled changes.

## Validation

Gameplay telemetry coverage includes:

- observational-only state invariants;
- resource-income cross-check against authoritative extraction totals;
- deterministic-seed reproducibility;
- JSON serialization round-trip;
- batch aggregation and comparison;
- long-run finite/non-negative metric validation.

The canonical CI terminal match emits both engine diagnostics and gameplay telemetry. The manually dispatched Match Soak workflow emits both reports for multi-match analysis.
