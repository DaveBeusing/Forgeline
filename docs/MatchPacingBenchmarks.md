# Match pacing and decision benchmarks

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

The existing headless telemetry runner emits an additional PacingBenchmarks schema-1 block and a sibling .pacing.md analysis when --telemetry-output is used. Gameplay telemetry remains schema 2 with additive optional decision observations. Existing client telemetry does not enable them. No opponent policy, costs, resources, sensing, combat or transport rule changes.

## Run and compare

Build the solution normally, then use:

    pwsh ./build/Run-PacingBenchmarks.ps1 -Mode Quick -NoBuild -OutputRoot artifacts/pacing-baseline
    pwsh ./build/Run-PacingBenchmarks.ps1 -Mode Quick -NoBuild -OutputRoot artifacts/pacing-current -BaselineRoot artifacts/pacing-baseline

Each output directory must be fresh. Quick runs 600 logical ticks per match on Central Divide and Split Basin with Gameplay seeds 17/18, plus Validation seed 2026 as a separate control. The suite retains the existing default opponent configurations. Quick is an execution/schema baseline covering 30 logical seconds, not a measured match-duration qualification.

Reference uses the same manifest and existing opponents, with 108,000 ticks for the standard Gameplay fixture and 48,000 for the smaller scenario fixture. The 45–90 and 25–40 minute windows are aspirational calibration policies. Split Basin selects the shorter policy; the fixture does not resize the map or demonstrate shorter actual matches. No --require-terminal is added and missing victory never becomes a zero-duration win. Long horizons may remain censored and can be expensive; this package does not rebalance them.

A single existing headless command also supports:

    dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario central-divide --profile gameplay --ticks 12000 --seed 17 --matches 2 --telemetry-output artifacts/current.json --telemetry-baseline artifacts/baseline.json --pacing-thresholds build/pacing/standard-thresholds.json

Threshold JSON accepts the seven documented fields in the checked-in policies. Unknown fields, nonfinite/nonpositive time limits, invalid ratios and reversed victory ranges are rejected. Input is limited to 64 KiB; baseline reports to 32 MiB. Telemetry benchmark batches admit at most 128 matches. Warnings are advisory, without a balance-change or success guarantee.

## Measurement meanings

| Measurement | Evidence |
|---|---|
| scout.first_produced | Existing post-start owned scout milestone; starting units are excluded |
| contact.first_authorized | Existing participant-authorized contact observation; no unknown enemy identity/stock/target |
| combat.first_damage | Match-wide authoritative applied damage; not per-player reconnaissance |
| expansion.first_strategic | Completed building outside the owner's starting build area; initial structures are excluded |
| research.first_completed | Existing completed paid-research progress evidence |
| technology.first_unlock | Actual owned TechnologyCapabilityUnlock with source technology and completion tick |
| factory.productive_uptime | Running unit-production facility ticks / observed unit-production facility ticks |
| resources.shortfall | NoInput processing/unit-production facility seconds; simultaneous facilities contribute separately |
| supply.first_chain_disruption / recovery | Existing bounded causal progress observations |
| supply.first_pressure / longest_pressure | Real owned Low/Critical/Unsupplied unit states; not proof of zero delivery throughput |
| front.centroid_travel | Owned non-logistics unit centroid travel across identical full-generation membership; not territorial control |
| activity.longest_no_progress | Logical time without new milestone, processing output, damage or sampled front motion; intent is unknown |
| victory | Actual completed victory tick; an active/censored or non-victory result is never converted to zero |

Every numeric row carries availability and evidence. Reached has a measured value, including legitimate zero when a sampled population is stationary. Unreached is an available event not observed before the horizon and has null value. Unavailable means absent population/feature or incomplete observation and also has null value. Distributions count these separately and exclude missing values from min/mean/median/P90/max. Quantiles use linear interpolation. Comparisons always retain reached and unavailable counts beside conditional means.

A missing factory is unavailable, rather than zero productive uptime. A missing front population is unavailable, rather than zero movement. Population changes do not count as centroid motion. Starting structures do not establish completed expansion, and research completion alone does not establish a capability unlock.

## Clock, scope and budgets

Decision observation is enabled explicitly by the headless host after completed ticks. At most 4,096 dense controllable/unlock entries are visited per tick across both queries, plus bounded exhaustion probes, with at most 16 participants. Dense enumeration avoids unbounded scans over sparse entity slots. Retained counters, first timestamps and current/previous membership sets are bounded; no event history grows. Existing general telemetry work is unchanged and has its own pre-existing costs.

Repeated reads at the same tick are no-ops. Missing ticks, a late start or truncation mark the benchmark observation unavailable; neither quiet time nor supply streaks bridge those gaps. Host rendering rate, pause duration and wall-clock speed do not enter logical-time metrics. This separates measured progress gaps from pause/slow-host time without pretending to infer human intent.

All new decision summaries are offline diagnostics. They may include multiple participants and must never be reused directly as player-facing intelligence. No new data is sent to player UI or persisted in authoritative save/replay state.

## Reproducibility and failures

Pacing reports include the scenario/profile, ordered seeds, tick budget/rates, actual configuration fingerprint, game assembly SHA-256, active thresholds, stop reasons and distributions. Baseline comparison requires compatible schema and identical cohorts/configuration, and refuses partial/cancelled or developer-preset cohorts. Missing prior schema is explicit. Incompatible contexts produce no numeric deltas, including in the parent metric comparison.

ExecutionStatus Complete means the requested cohort finished running; each run separately records Terminal, TickBudget or Cancelled. It does not mean a match was won. Ctrl+C preserves partial observations, marks Cancelled and retains the existing headless cancellation return path. In PowerShell-hosted terminals the wrapper's interrupt exit can differ from the program return code; inspect report state as well.

Run ./build/Test-PacingBenchmarks.ps1 after the normal build for actual CLI repeatability, zero deltas, incompatible seeds, observed warning paths, malformed/unknown/missing threshold configuration and output-denied failures. The ordinary Game suite also covers no mutation, missing data, actual supply states, capability timestamps, observation gaps/budget exhaustion and deterministic seeded observations.

Source-bound local evidence and limits are recorded under qualification/match-pacing. Full duration calibration and all required hosted CI remain separate acceptance results. Historical failures are scoped to their recorded source revisions; measurement code does not repair gameplay.

## Simulation faults

A managed simulation fault writes a separate <telemetry-output>.failure.json with status SimulationFault and the last completed bounded observation, then returns a nonzero process result. It never captures the failed tick as a normal match snapshot or treats the failure as a successful baseline. The qualification report distinguishes historical starting-master failures from results after upstream gameplay repairs.
