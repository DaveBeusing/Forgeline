# Match pacing qualification

Producer: 9815bafba2a90cbdac63fcacd80c67fff3fd8a5a, based on master 8749198f89ce1f1c90684c0bcb3f8a3398e28f45; version 0.1.505; .NET SDK 10.0.402, Windows x64. Source and executed assembly hashes are in source-bindings.json. The following data files belong to that producer unless explicitly marked historical.

## Validation

- Canonical Release solution build: zero warnings and errors.
- Focused MatchPacingBenchmarks, GameplayPacingProgress, GameplayProgressJournal and GameplayTelemetry tests: 22 passed, zero failures or skips. These include an injected simulation fault and unchanged authoritative hashes with observation enabled/disabled.
- Nine actual CLI contracts passed; cli-contracts.json records expected/actual exit codes.
- Architecture validation: 46 projects, no cycles, boundaries preserved.
- Quick matrix: Central Divide and Split Basin Gameplay seeds 17/18 plus Central Divide Validation seed 2026, 600 ticks each. Repeated on the same producer: all run measurements identical, 27 comparison series per fixture, all numeric deltas zero. Missing values remain null. comparison-summary.json records the checks. The three baseline-*.json files are full runner reports usable as future baseline inputs.
- Longer Central Divide Gameplay seed 17: 12,000 ticks / 600 logical seconds completed, exit 0, stop TickBudget. Full report: longer-central-gameplay.json. This is a censored match, not a victory qualification.
- Actual Ctrl+C: stopped at completed tick 8,502. cancelled-central-gameplay.json has ExecutionStatus/StopReason Cancelled; it is refused as a comparison baseline. The PowerShell terminal wrapper returned 1; the program retains its cancellation return path.

## Findings and limits

The 30-second Gameplay fixtures observe first completed expansion at 20.05 seconds and actual research/unlock at 13 seconds. Scout/contact/combat/victory remain unreached; absent factory populations remain unavailable. Neither becomes a zero event time or zero factory uptime. A stationary sampled military population can legitimately report zero front movement; a test separately verifies that an absent population is unavailable.

The 600-second run produces real late-contact, sustained-supply-pressure and low-factory-uptime warnings for both participants. Those warnings invite inspection; they do not prove player intent or prescribe balance changes. All timings use completed logical ticks and exclude paused host time. Factory and shortfall figures are population-conditioned diagnostics.

The 45–90 minute standard and 25–40 minute smaller-scenario windows are aspirational threshold policies. No full-duration statistical calibration or victory guarantee is claimed. The shorter Split Basin policy does not resize its map. No native renderer qualification applies to this headless-only addition. Hosted CI remains a separate required result.

## Historical starting-master failure, resolved upstream

Before synchronization, an unmodified 201af6aad7ace07df21608c1d058d23b0828b070 control without telemetry failed with MovementOrder missing in GroundMovementSystem.BeginOwnedQueueApproaches. historical-control.json binds the control assemblies and raw log hash. simulation-fault.json belongs to the earlier e81bb1d0caca42b69a4778dad2d4aefe8dd9eb11 instrumented producer (0.1.504), not the current baseline: it retains only completed observation through tick 5,115 and exits nonzero.

Upstream repair PR #416 merged as 8749198f89ce1f1c90684c0bcb3f8a3398e28f45 during qualification. The package was synchronized and rebuilt, and the same scenario/profile/seed now completes 12,000 ticks. The historical failure is not an outstanding blocker for this producer. No movement or opponent repair is included in this package.
