# Strategic map audit qualification

## Source and scope

Base master: 8749198f89ce1f1c90684c0bcb3f8a3398e28f45, including the integrated
gameplay recovery changes. Product/qualification source:
63d567794863a09b158544d5b5fc686cbbdc036d. Later commits add documentation and
textual evidence only. PATCH advances once from 0.1.504 to 0.1.505.

The original inspection started at 201af6aad7ace07df21608c1d058d23b0828b070.
All 34 non-version files introduced by the intervening master merge were copied
into the isolated qualification tree and verified against their exact Git blob
identities. The final branch comparison is ahead with zero commits behind.
No unrelated gameplay changes are introduced by this PR.

The local tree has no independent Git checkout metadata. Source is bound by the
published product commit, exact incoming Git identities and retained binary/report
hashes rather than by inventing a local source-revision attribute. SDK: 10.0.402.
The full Release solution builds with zero warnings and errors.

## Executed results

- 43 Game cases pass with zero failures/skips: all nine new audit cases, existing
  PrototypeBattlefield, both SplitBasin suites, CompiledTerrain,
  CompiledNavigationParity, CompiledScenario and ScenarioPersistence.
- The complete Navigation suite passes 53 cases with zero failures/skips.
  Total: 96 distinct selected cases. Repeated cases are not counted twice.
- Project reference validation passes: 46 projects, no cycles or boundary changes.
- Test-StrategicMapAudit passes on final source. Both complete registered-map JSON
  and Markdown reports repeat byte-for-byte. Explicit rationale is retained;
  stale/duplicate exceptions, invalid/missing arguments and a file-blocked output
  directory fail as required. The script returns explicit success after its
  expected negative CLI cases.
- Both canonical map compiler commands pass, retain their exact registered map
  digests and append the new audit. Central Divide retains 71 reachability,
  two alternate-navigation and two alternate-road checks; Split Basin retains
  49, two and two respectively.
- Test-CompiledScenarioLoading passes actual new/start/save/replay execution for
  both deployed maps and independent missing-content rejection. Seed 2026,
  tick 20: Central Divide state
  14D58A81A68CC2437CA714001BB323859E04A9F4D759D77BBF16F614A2606E99;
  Split Basin state
  12347A57B4A8196FCAB21E07D2D296DBF8880C1E2C9B1FF3A22579C7F4FC3AFB.
- Both optional offline SVG previews generate and parse as valid SVG. The
  Central Divide baseline warning is located at the actual contested deposit,
  (1536, 1310), with a labelled problem symbol. No D3D12 window is created.
  Preview images remain local; the generator and complete textual evidence
  are reviewable in the PR.

## Observations

| Map | Route observations | Hard errors | Unaccepted warnings | Yard sample area per side |
|---|---:|---:|---:|---:|
| Central Divide | 624 | 0 | 11 | 238080 m² |
| Split Basin | 456 | 0 | 8 | 145920 m² |

Both maps remain ReviewRequired. Each has six movement/start disconnection
warnings and two road-cut warnings when both crossings are lost. Central Divide
also has three movement-class warnings for nearest Rare Elements expansion
access: player 1 measures 1143.8802 m, player 2 measures 1902.1635 m. Both have
two reachable deposits; the nearest is center.rare_elements. This exceeds the
1.25 ratio criterion and remains visible without automatic exception acceptance.

Normal local Ore/Volatiles/Silicates reserves remain reachable within 450 m.
Recorded quantities and extraction rates match between starts. All movement
classes measure equal reachable permitted yard samples per map. Each single
crossing loss retains a real start-to-start route; the combined loss severs it.
Widest road paths are limited to 120/s by the bridge approach edges, 90/s with
the bridge lost and zero with both crossings lost. Bridge-edge capacity alone
is 160/s. None of these observations establishes a match win rate, physical
vehicle throughput or complete map fairness.

The before-state compiler already qualified reachability, elevation and crossing
alternatives, but did not emit these comparable opening, expansion, sampled
yard and end-to-end capacity observations. No map or gameplay parameter was
changed as part of the audit.

[Complete JSON](qualification/strategic-map/audit.json) and
[generated human-readable report](qualification/strategic-map/audit.md) retain
all states, movement classes, starts, target measurements and diagnostics.

## Reproduction hashes

| Artifact | SHA-256 |
|---|---|
| Complete JSON | ab08fbbef22ed26acbd9c562cc4b708295adecb12d630cc47cd800d3ab169909 |
| Markdown summary | d5ebbb5bd57e1b2faf61730069d9b39b93a9325b8b97919d6ed5acb19d964f8c |
| ForgeLine.Game.dll | e32d4b5f82ec6554a76034b7e36b9ab3a21c5f8c6ff51cbf51cd480c27d89887 |
| ForgeLine.MapCompiler.dll | 467dc4ea042b6f778470d163381b7bfde6e08ac012edfed623cec961bb78627b |

Reports also include exact map, content and full terrain-sample hashes. They
consume no match seed or RNG. Log filenames in the isolated qualification tree:
map-build.log, map-game-tests.log, map-navigation-tests.log, map-architecture.log,
map-cli-tests.log, map-canonical-compiler.log, map-split-compiler.log and
map-compiled-recovery.log.

## Corrected iteration findings and remaining gates

Initial compilation required invariant Markdown formatting and the repository's
xUnit cancellation token. An early test incorrectly expected the bridge's
160/s rating as the complete road capacity; actual 120/s approach limits were
confirmed in the authored edges and the regression corrected accordingly.
An overlapping build hit Windows DLL-copy locks while scenario tests were still
running. It was rerun after the test process exited; the final build and final
43-case Game run both pass. No runtime route rule, test gate or capacity value
was weakened.

At product source, required CI 40670440600 and four qualification workflows had
started without failed steps; required build-test was still checking out the
repository. The baseline master's CI 40667689410 was also still in progress;
its logistics-navigation, movement and conveyor workflows passed. Earlier-master
failures are not attributed to this synchronized source.

Final documentation-head required CI must be read independently before merge.
The complete hosted solution/native/asset/package/natural-terminal gates are
not replaced by the local selected suites. No unrelated asset or renderer
qualification is claimed. The Rare Elements asymmetry and deliberate disruption
cuts need map-design review or explicit documented rationale; no automatic
warning waiver or terrain/resource rebalance is included. Keep draft pending
required CI and final review. No merge, release or deployment.
