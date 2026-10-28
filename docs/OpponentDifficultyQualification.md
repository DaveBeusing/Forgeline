# Fair opponent qualification

## Source and scope

Current implementation: 69b0c0aba15d1d37e4fde340a27fd148deafa8c7, based on master 08e8cf2f2e2632b3239229dc6840928d957c364c; version 0.1.506 (one PATCH per PR). This records headless Game API policies and developer diagnostics. It does not claim a player-facing difficulty selector, a shared pacing runner extension, or completed balance acceptance.

Pre-check inspected remote master/version, related branches, open and closed unmerged PRs, implementation, architecture/contracts, tests, baseline CI and governance in that order. Existing perception, production, paid research, logistics recovery and raid owners were reused. The concurrent pacing change owns its runner and telemetry. Standard is the existing default configuration.

## Current-source checks

- Canonical Release solution build: passed, zero warnings/errors, 1m43.30s.
- Profile, raid, production and persistence-boundary suites: 122 passed, zero failed/skipped, 3m44.281s, canonical SDK-selected Microsoft Testing Platform runner.
- Compiled scenario startup, save/replay recovery and missing-content rejection: passed for Central Divide and Split Basin. At tick 20, seed 2026, hashes remain 14D58A81A68CC2437CA714001BB323859E04A9F4D759D77BBF16F614A2606E99 and 12347A57B4A8196FCAB21E07D2D296DBF8880C1E2C9B1FF3A22579C7F4FC3AFB respectively.
- Architecture validation: 46 projects, no dependency cycles (before the capacity-only master synchronization).
- Existing headless diagnostic smoke: Gameplay, seed 17, 128 ticks; two Standard records, seven decisions each, last tick 121 / next tick 141. Cause and goal serialize as readable names; projection contains no hostile identity, position or hidden stock.
- Tick-zero authoritative hashes are identical across profiles. At 128 ticks the observed strategic decision counts are Easy 4 / Standard 7 / Hard 13. Save/replay continuation and repeated-seed checks preserve deterministic state; reading retained diagnostics cannot mutate it.
- Unknown targets, insufficient fuel/ammunition, unknown routes, lost contacts and logistics guard/reroute counterplay are covered for all three profiles. Factory recovery keeps the existing high-priority Cargo command within each policy's queue limit.

## Six-case cohort

Raw reports and aggregate are in [qualification/fair-opponents](qualification/fair-opponents/summary.json). Each case uses normal Gameplay stock, seeds 17/831/2026 and both Easy/Hard start assignments. All six reached the 80,000-tick ceiling, totalling 480,000 observed ticks. None became terminal: zero completed matches, zero wins, zero draws, six censored observations; Hard win rate is unavailable, not zero percent.

Every case retained two scratch owners, zero route failures and bounded counters: 10,000 strategic decisions (Hard 8,000 / Easy 2,000), 150,000 nondecision evaluations and 160,000 owned/intelligence/economy/force captures with debug enabled. Completed cargo orders were 202 with Easy West and 218 with Hard West. Outcomes repeat by assignment across seeds; this fixture does not establish independent statistical diversity or superiority in decision quality. Elapsed times range from 495,353.8522 to 673,949.1297 ms; these are observations under different concurrency, not a controlled performance comparison.

The six reports belong to product/qualification source 04eb27ee13ee46a3577863f5d07296aacd90b05f on master 8749198f89ce1f1c90684c0bcb3f8a3398e28f45, version 0.1.505. Subsequent changes corrected an Easy test expectation, rejected cancelled/incomplete qualification, expanded lost-contact coverage and synchronized master's recovery capacity validation. The master delta changes no AI, map, fixed-tick ordering or authoritative schema. Historical reports are not relabelled as final-source runs.

The first seed-831/Hard-West report completed within a sequential cohort invocation; that invocation was then deliberately interrupted. The remaining five ran as individually filtered canonical MTP cases, each passing with exit zero. The interrupted invocation is not represented as a six-case passing test run. Native xUnit was used only for discovery of case IDs, with pre-enumerated theories; execution used the canonical dotnet test entry point. See [MTP filter options](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-cli-options).

## Validation corrections and limitations

An initial broad run passed 187 of 188 cases. The single failure was an added fixture expecting a Tank to remain after urgent Cargo recovery under Easy's one-slot queue. Production correctly reserved that sole slot for Cargo. The test now expects zero/one/one remaining Tanks for Easy/Standard/Hard, still requires High-priority Cargo and checks the queue bound; production policy was unchanged. All 122 current-source cases pass.

An experimental redirected build-output invocation failed generated test entry-point discovery (CS5001) and was abandoned for normal isolated builds. Asking SDK 10 MTP for help through dotnet test caused an IPC help-message failure; no product result was inferred. These infrastructure attempts are not successful acceptance evidence.

No terminal result or Hard win-rate ordering has been demonstrated. The profiles provide observable legal planning and coordination differences, while full balance/decision-quality acceptance remains open. Keep the PR draft until review of this limitation and required CI completion. Existing full-match CI is preserved; no terminal assertion or protection was weakened.

Binary and raw-report SHA-256 bindings are recorded in [source-bindings.json](qualification/fair-opponents/source-bindings.json). Final-source control and required CI status are recorded below. Reproduction commands are in [FairOpponentDifficulty](FairOpponentDifficulty.md).

## Synchronized-source control and CI

The additional seed-831 / Hard-West control passed on current source 69b0c0aba15d1d37e4fde340a27fd148deafa8c7, reaching 80,000 ticks in 496144.6794 ms. Its full authoritative hash exactly matches the historical case: D9EB0AC8F92262BEEC47F437BE5BACC7B6FA1AD0DDD5AE4C345F1652E451F4AF. It is a censored repeat, not a seventh independent balance sample or a victory. The raw [control report](qualification/fair-opponents/post-sync-control.json) is kept separately.

The source-head [CI run](https://github.com/DaveBeusing/Forgeline/actions/runs/40682497509) passed the added fair-opponent gate. Complete required CI was still running at documentation time; source-head movement and conveyor workflows had succeeded. Documentation-head CI is a separate required result and is not inferred from local checks or earlier runs. No complete required-CI pass is claimed.
