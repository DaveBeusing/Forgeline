# Split Basin

Split Basin is a 2,048 × 2,048 m Directorate battlefield loaded through the same compiled scenario registry as Central Divide. The original map remains the default. Select Split Basin with Up/Down in New Game, or use the client/headless option `--scenario split-basin`. The stable composition key is `split-basin.directorate.v1`; the map key is `battlefield.split_basin`.

![Compiled Split Basin geography](SplitBasinBattlefield.svg)

The view is generated from the reloaded artifact, sampled terrain, explicit build areas, blockers, resources, roads and Command Cores. North is at the top. Decoration does not establish cover or traversability.

## Geography and economy

West/east starts at (320, 1024) and (1728, 1024) are reflected about X=1024. Cores stand at (370, 1024) and (1678, 1024). Each 304 × 480 m starting yard has the same three finite reserves: 5,500 Ferrous Ore at 7.5/s, 3,500 Volatiles at 5/s and 4,000 Silicates at 5.5/s. All are within 450 m of their start. Extraction uses the existing deposit, placement, power, ownership and transport systems.

Both sides receive the unchanged normal stock: 2,200 Ore, 900 Volatiles, 900 Silicates, 700 Steel, 900 Fuel, 320 Electronics and 420 Ammunition. The tested industrial opening pays for three Power Plants, three Extractors, Smelter, Electronics Fabricator, Ammunition Plant and Supply Depot from that stock, then pays the conveyor Steel separately. Its building costs total 1,470 Ore, 620 Silicates and 170 Volatiles; 173 power demand fits the three plants' 300 generation before the conveyors. The finite local deposits exceed this bootstrap demand; the map adds no free income or material source. Steel takes 10 Ore, Electronics takes 10 Silicates and Ammunition takes 6 Steel plus 2 Electronics per existing recipe cycle.

Richer paired contested reserves sit outside the yards: 12,000 Ore, 8,000 Volatiles and 10,000 Silicates per side. Six explicit shared sites provide northern overlooks, mining outposts and southern staging areas. Rare Elements are absent and introduce no new bootstrap gate. The existing placement validator still rejects slopes, collisions and invalid dock/footprint positions.

The central Basin Bridge at (1024, 1024) is the short exposed route. The Outer Ford at (1024, 1664) creates a longer southern approach around the divide. Terrain rises around the northern overlooks and between the two passages; real static blockers close the divide outside the authored gaps. The lower central basin and elevated flanks are simulation terrain, using normal slope rules. Current visual sensing uses range, faction intelligence, bounded sampled-terrain sight and explicit visual screens. Decorative vegetation has no automatic occlusion and there is no elevation range bonus. Route geometry changes proximity to observers. A production-sensor probe uses the existing Scout Vehicle's 320 m visual range at the northern bridge approach: the bridge target is identified, while the outer-ford target loses current identification. This is one observer placement, not a claim that the complete outer route is concealed. Only the explicitly authored cover rectangles below grant infantry protection; the outer route has no general safety guarantee. Trees and decorative props have no extra gameplay effects.

Both crossings retain the fixed-tick infrastructure lifecycle (80 bridge ticks, 120 ford ticks), their actual road edges and navigation passages. Either single loss retains a real alternative; losing both disconnects the two halves. Road capacities remain bridge 160/s and ford 90/s. The bridge now supports normal weapon damage and paid player repair as described below.

## Identity and compatibility

Map revision 2, artifact format 4 and terrain recipe version 1 reuse the existing Directorate content revision `directorate.v1`. The current content hash is `205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63`.

Map JSON SHA-256: `635ca23e5fa63285c4bb6b7e4b00b8615804af67cf638ee48e36dda21573a36e`.

Terrain SHA-256 over every little-endian height sample in chunk/Z/X order: `ee7f0c7e872c2e32528729eca98171ebbcbd9d0d4d846a007fb4d5eb0d9562cd`.

The registry pins those bytes plus the mandatory digest sidecar. The original map integration used payload 7 and authoritative schema 8. Current captures use payload 11 and authoritative schema 15 with envelope 1; the exact compiled scenario identity is retained. A mismatching saved map identity fails explicitly. The historical Central Divide legacy mapping remains separate. Setup, new/load/restart and installed-package validation resolve the selected descriptor rather than falling back to the default map.

## Reproduction

From the repository root after the canonical Release build:

```powershell
dotnet run --project tools/ForgeLine.MapCompiler/ForgeLine.MapCompiler.csproj --configuration Release --no-build -- --scenario split-basin --output artifacts/maps/split-basin.flmap.json --qualification-output artifacts/split-basin-geography.json --preview-output docs/SplitBasinBattlefield.svg
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj --configuration Release --no-build -- --filter-class '*SplitBasin*'
pwsh ./build/Test-CompiledScenarioLoading.ps1
dotnet run --project src/ForgeLine.Headless/ForgeLine.Headless.csproj --configuration Release --no-build -- --scenario split-basin --profile validation --ticks 80000 --seed 2026 --require-terminal --diagnostics-output artifacts/split-basin-terminal.json
dotnet run --project src/ForgeLine.Client/ForgeLine.Client.csproj --configuration Release --no-build -- --scenario split-basin --smoke-test --settings-root artifacts/split-basin-settings --visual-qualification-output artifacts/split-basin-native.json
```

The compiler reloads and qualifies the actual serialized terrain/map, records the full height-sample hash and bidirectional route lengths, and can regenerate the SVG. The package includes exactly both registered maps and their sidecars. Installed validation constructs both real scenarios and reconstructs their saves and replays using deployed files only. Package provenance lists each map digest.

## Qualification and limits

The loaded-map compiler passes 49 reachability checks, two alternate-navigation checks and two alternate-road checks, with 39.774094 m sampled elevation range. At 16 m navigation cells / 8-cell sectors, Infantry, Wheeled and Tracked each take 1,408 m between starts. Closing the bridge produces a 2,115.677 m west→east path and 2,115.8376 m east→west path. The 0.161 m refinement difference is reported rather than treated as exact path symmetry. Closing only the ford leaves the 1,408 m path. These are geometric distances, not arrival times or balance acceptance.

The build-policy regression samples every road and its ±16 m shoulder strip with 2 m footprints for both owners, while requiring each full Core footprint to fit its own yard. Local Silicates now sit 144 m from the central road rather than 26 m; contested Volatiles and outer Silicates also clear the actual 72 m mining-zone padding. The base rectangles stop before the road entry. The qualified industrial layout leaves real dock approaches free; normal placement validation remains authoritative.

## Current technology integration

Master advanced to `5605d96c` (technology progression, PR #350) while this branch was being published. This candidate prepares **0.1.471-dev** above master 0.1.470-dev. Both registered maps use current Directorate content `205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63`; Central Divide is pinned to `2f2b21f21697c665a8ab3ac607e7cebc6f82d1b9cf06f2838a02444f179f1f1f`, Split Basin to `f82b732e563b611a9106279e260564ba91a77eb3a61e19dd1e439caf28d8c8b8`. Terrain samples, roads, deposits, sites and base layout are unchanged. Payload 7/envelope 1 follow current master; authoritative schema 8 explicitly rejects pre-technology saves/replays. The frozen legacy identity is preserved.

Post-merge validation passes: full Release solution build with zero warnings/errors (2m57s); **57 focused Game cases** covering Split Basin, compiled maps, persistence/composition, technology effects/paid production/recovery and opponent research; **16 Client scenario/argument cases**; compiler qualification with 49 reachability, two alternate-navigation and two alternate-road checks; both-map CLI new/save/load/replay and independent missing-data rejection. Current seed-2026/tick-20 authoritative hashes are `57C5E18F1D591A34245BEB35C77635AFF378526297ABD1AECDA58206FF293344` for Split Basin and `B994E0FD0195766BB241EF64957B83B7F11352272D6913AA0D575E61DAD516A3` for Central Divide. Logs are `artifacts/split-basin-tech-{build,map,game-tests,client-tests,cli}.log`.

The complete suite, terminal outcomes, native/WARP, benchmark and exported-package evidence below predates the technology merge and remains historical. These gates are not claimed passed on the new integration; consult actual-head CI in [draft PR #355](https://github.com/DaveBeusing/Forgeline/pull/355). Existing #344/#345 and human/reference-performance acceptance remain separate open gates.

## Historical qualification before the technology merge

The following measurements qualify clean runtime `9c170bd8` and its old 0.1.470-dev package/content/schema, before the incoming technology integration. They are retained as prior evidence and do **not** qualify the current 0.1.471-dev binaries, state hashes, endgame outcomes or package. See the current checkpoint above and actual-head CI in PR #355.

Thirty-three focused Game cases pass, including all ten Split Basin geography/runtime cases plus compiled-terrain/navigation parity, scenario persistence and custom composition contracts. They cover all three movement classes, both starts, every resource/site/objective, either crossing lost and both lost, actual logistics rerouting/restoration, normal paid industry/conveyors/Fuel, active save/replay/continuation and wrong/missing identities. Sixteen Client scenario/argument cases pass, including actual registered new/load/restart. Repeated same-seed 2,000-tick comparisons exchange the existing opponent configurations between starts; these bounded comparisons are not completed games.

The actual CLI validates both scenarios through new/save/load/replay and independently rejects missing data for each. Split Basin's seed-2026 validation state at tick 20 is `0A5D62CFDD2ACF7C1FC29281817BFD7AF06A604FBA406C0C94083CD477249572`; Central Divide retains `410F9179FD17B30F9203643F6038CF8FA9319B38FF67E0A74191400572E2D185`.

The refined map finishes the unchanged 80,000-tick/seed-2026 validation command naturally at tick **34,281**, player 2 Victory / CommandCoreDestroyed. It records 50 completed and two failed Cargo orders, zero route failures, 5,832 delivered cargo, 1,017.5014 transferred Fuel and 647 Ammunition. No artillery shot occurs in this outcome. Its terminal save pins the registered map above and authoritative state `31CF750FD8141DFDECD23D8D725EF0A4B8C53E0D3C7A56FF095281030859F09E`. The earlier layout's outcome is superseded. Validation retains the existing accelerated stock and asymmetric opponent configurations; one outcome is not normal-stock balance or win-rate evidence. The paid openings separately use Gameplay/Standard stock.

The unchanged Central Divide control finishes naturally at tick 50,862, player 1 Victory / CommandCoreDestroyed. The 1,000/10,000-entity lightweight smokes and 44-project architecture check pass. Fresh forced asset compilation/qualification succeeds with 2,049 assets, 118,619,195 runtime bytes and no issues. The synchronized full Release build has zero warnings/errors. Master remains 3c261dca / 0.1.469-dev; this PR prepares 0.1.470-dev with one patch increment.

The synchronized build, package, native/WARP, complete solution and benchmark runs use clean runtime source 9c170bd85935539cef65ced456a800f3435c4f46. Earlier focused and terminal evidence uses the same final Game/map sources; the intervening master merge changes runtime rendering, not the Game/Headless/map layer. The final evidence-only documentation commit changes no runtime or map source. Master recheck retains 3c261dca3182d9973cce07ea64f2b7826c11f6c4 / 0.1.469-dev.

The canonical publisher's two complete SHA-256 manifests match. The 65,295,335-byte ZIP has SHA-256 99137072c7aed440c9712c11bfa1d1f420b3a8ee9312fa046cd82c4fb93daf28. Provenance records sourceTreeClean=true, the source commit and both map digests. A separate installation outside the checkout contains exactly four map/sidecar files and passes construction, save reconstruction and replay reconstruction for both descriptors.

That installed client runs the actual Split Basin descriptor and confirms its final map hash, with 1,000 stress instances. D3D12 reports zero texture/material binding failures, 112 presented frames and SessionReady/ApplicationReady. The short 1600×900 Medium/CloseTactical sample on NVIDIA RTX PRO 5000 reports 35.775 fps, 27.952 ms/frame, CPU render 20.353 ms and GPU 4.066 ms; the sampled view has 20/1,279 visible/total instances. The report also records 25 textured runtime-mesh instances and 12 fallback-mesh instances; the zero binding-error counts are not a claim of zero fallback geometry. This functional smoke is not sustained reference-performance acceptance. Two measured menu/icon starts pass after one excluded warmup.

WARP produces two 1280×720 UI captures and one 640×360 existing BattlefieldCombat reference capture, all structural-pass with no image failures. These existing fixture scenes are not Split Basin captures. No matched accepted pixel baseline is supplied; image/visual acceptance remains pending.

The required window-mode guard fails before launch at 68,761,923 gameplay texture bytes against the unchanged 58,720,256-byte budget (#344). The complete solution's resource-field fixture fails at ResourceDepositFieldRenderingTests.cs:42 -> SimpleInstanceRenderer.cs:925 -> IGraphicsCommandContext.cs:43 with the same unsupported texture-binding default documented in #345. Both underlying test/renderer files are unchanged by this feature. The previously reported golden-state failure is not reproduced in the current Presentation module.

The complete canonical Release solution run reports **3,883 total / 3,810 passed / 1 failed / 72 skipped**, 31m04.639s, exit 2. All Game and Client cases pass. The one failure is the resource-field fixture described above; the fixture, shared fake context, renderer and graphics interface are unchanged from current master. Skips include opt-in native lifetime, Blender round-trip and isolated hardware qualification cases. They are not passes.

The initial canonical twelve-case Short benchmark command times out restoring the generated project at its fixed 120-second build limit and produces **zero measured cases**, despite host exit 0. Retrying the same twelve cases with the installed CLI option `--buildTimeout 600` succeeds: twelve measured cases, N=3 each, exit 0, 4m47s total. No measurement iteration setting, benchmark or performance threshold is changed. Mean CPU times below come from the BriefJSON reports; Short-run variability and the absence of an accepted comparison baseline preclude a performance-regression or sustained-FPS acceptance claim.

| Benchmark | Mean (µs) |
| --- | ---: |
| Submit1000NearFieldInstances | 720.475 |
| Submit5000InstancesWithCulling | 4.651 |
| SubmitRepresentativeVerticalSliceTacticalView | 654.726 |
| SubmitRepresentativeVerticalSliceNormalRtsView | 701.570 |
| SubmitRepresentativeVerticalSliceStrategicView | 545.789 |
| SubmitRoadReadabilityTacticalView | 148.991 |
| SubmitRoadReadabilityNormalRtsView | 290.533 |
| SubmitRoadReadabilityStrategicView | 259.797 |
| GenerateTerrainChunkMesh | 85.926 |
| SubmitCloseTacticalTerrain | 2.028 |
| SubmitVisibleTerrainChunks | 3.571 |
| SubmitStrategicHighCoverageTerrain | 14.874 |

Local evidence is under `artifacts/`: `split-basin-final-build.log`, `split-basin-full-tests.log`, `split-basin-revised-checks.log`, `split-basin-package-native.log`, `split-basin-installed-visual.json`, `split-basin-startup/summary.json`, both `split-basin-*-regression-warp` folders and `split-basin-rendering-benchmarks-retry/results/*-brief.json`. The original failed benchmark attempt and window-budget log are retained. Actual final-PR-head CI links/status belong in the PR description; local evidence alone does not establish remote CI success.

The change remains a draft candidate with existing [texture-budget #344](https://github.com/DaveBeusing/Forgeline/issues/344) and [resource-field fixture #345](https://github.com/DaveBeusing/Forgeline/issues/345) blockers. Required gates, assertions and budgets are retained. No merge, release or tag is part of this task.

The preceding qualification predates explicit tactical areas and bridge combat integration. Current ground sensing and crossing behavior are specified below; decorative trees still provide no automatic bonus. Automated native/WARP execution, sustained reference-performance qualification and human visual/gameplay acceptance are separate evidence classes. Existing required gates remain unchanged.

## Bridge operation

The Basin Bridge is a neutral, explicitly targeted Structure with 1,200 health in the normal match runtime. Weapon damage uses the normal ammunition, target-class, intelligence and lifecycle rules. Its destruction closes the existing real road edge and navigation passage. Automatic hostile target acquisition excludes shared bridges. Direct projectiles only hit the shared deck when it was their launch target; artillery area damage retains its ordinary behavior.

Select an owned Combat Engineer and use its Logistics ring to inspect or start Repair / Resume for a currently identified bridge. A new kit costs 120 Steel plus 20 Electronics from an own living completed storage within 64 m of the engineer. The engineer must stand within 32 m horizontally of the crossing passage. The copied row and tooltip report lifecycle, health, progress, material source, available unreserved stock and policy rejection reasons. Up to eight identified bridges and 32 own storage candidates are considered.

Accepted repair closes even a damaged operational bridge. It uses the map's existing 80 fixed simulation ticks; the route remains closed through the final tick. A missing or out-of-range engineer pauses work; the paying player can resume or hand over to another own engineer without paying twice. Further damage discards the paid kit and disables the bridge. Other players cannot take over a paid job. The Outer Ford retains its original host-only 120-tick restore lifecycle and is not a combat bridge.

In-flight cargo retains ordinary orders, reservations and loaded stock. Real topology changes invoke the existing alternate-route / blocked-route behavior; repair creates no bypass or cargo replacement. Normal weapon/player-repair command history supports deterministic recovery. Raw host crossing commands remain unsupported by the external player replay codec.

Manual acceptance of weapon destruction, paid completion, cargo continuity and Save/Replay is outstanding; earlier map qualification is historical evidence only.

## Explicit tactical geography

Revision 2 authors four infantry-cover rectangles near basin/outer approaches and two deliberate grove visual screens. Open Sensors to view green cover and amber screens, then use normal movement commands; single owned unit hover/selection status reports its own current state. Only these gameplay entries provide the rules, regardless of decorative object density.

Cover reduces direct infantry damage by 35%, without stacking or artillery/vehicle/structure protection. Screens restrict ground visual/radar identification outside the 32 m close exception; terrain can still block the close ray. Radar detection remains possible as a coarse cell-centered contact. See [locations, budgets and acceptance limits](CoverAndVisualOcclusion.md).

The new map digest above, matching sidecar and registered descriptor replace revision 1 only for new compatible matches. Existing terrain fingerprints and historical measurements are retained without becoming proof of the new sensing or combat behavior. Product compilation and manual acceptance, including Save/Replay at authoritative schema 15, remain outstanding.
