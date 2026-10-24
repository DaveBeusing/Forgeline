# Split Basin

Split Basin is a 2,048 × 2,048 m Directorate battlefield loaded through the same compiled scenario registry as Central Divide. The original map remains the default. Select Split Basin with Up/Down in New Game, or use the client/headless option `--scenario split-basin`. The stable composition key is `split-basin.directorate.v1`; the map key is `battlefield.split_basin`.

![Compiled Split Basin geography](SplitBasinBattlefield.svg)

The view is generated from the reloaded artifact, sampled terrain, explicit build areas, blockers, resources, roads and Command Cores. North is at the top. Decoration does not establish cover or traversability.

## Geography and economy

West/east starts at (320, 1024) and (1728, 1024) are reflected about X=1024. Cores stand at (370, 1024) and (1678, 1024). Each 608 × 480 m starting yard has the same three finite reserves: 5,500 Ferrous Ore at 7.5/s, 3,500 Volatiles at 5/s and 4,000 Silicates at 5.5/s. All are within 450 m of their start. Extraction uses the existing deposit, placement, power, ownership and transport systems.

Both sides receive the unchanged normal stock: 2,200 Ore, 900 Volatiles, 900 Silicates, 700 Steel, 900 Fuel, 320 Electronics and 420 Ammunition. The tested industrial opening pays for three Power Plants, three Extractors, Smelter, Electronics Fabricator, Ammunition Plant and Supply Depot from that stock, then pays the conveyor Steel separately. Its building costs total 1,470 Ore, 620 Silicates and 170 Volatiles; 173 power demand fits the three plants' 300 generation before the conveyors. The finite local deposits exceed this bootstrap demand; the map adds no free income or material source. Steel takes 10 Ore, Electronics takes 10 Silicates and Ammunition takes 6 Steel plus 2 Electronics per existing recipe cycle.

Richer paired contested reserves sit outside the yards: 12,000 Ore, 8,000 Volatiles and 10,000 Silicates per side. Six explicit shared sites provide northern overlooks, mining outposts and southern staging areas. Rare Elements are absent and introduce no new bootstrap gate. The existing placement validator still rejects slopes, collisions and invalid dock/footprint positions.

The central Basin Bridge at (1024, 1024) is the short exposed route. The Outer Ford at (1024, 1664) creates a longer southern approach around the divide. Terrain rises around the northern overlooks and between the two passages; real static blockers close the divide outside the authored gaps. The lower central basin and elevated flanks are simulation terrain, using normal slope rules. Current visual sensing uses range and faction intelligence; terrain/vegetation occlusion and an elevation sight bonus are not implemented. Route geometry changes proximity to observers. A production-sensor probe uses the existing Scout Vehicle's 320 m visual range at the northern bridge approach: the bridge target is identified, while the outer-ford target loses current identification. This is one observer placement, not a claim that the complete outer route is concealed. No terrain cover or safety advantage is promised for the outer route. Trees and decorative props have no extra gameplay effects.

Both crossings use the normal disable/restore commands and fixed-tick infrastructure system (80 bridge ticks, 120 ford ticks). Each disables its actual road edge and navigation passage. Either single loss retains a real alternative; losing both disconnects the two halves. Road capacities remain explicit (bridge 160/s, ford 90/s) and use the existing routing implementation. No teleport or route override exists. Crossings currently expose host disable/restore commands, not weapon-damage targets or a player repair action. Those existing host commands are outside the player replay codec; persistence correctly rejects their unrecorded history. The paid active-logistics save/replay proof runs before that separate infrastructure lifecycle check. Combat destruction/repair and recovery after host-triggered crossing loss remain an explicit existing integration limitation.

## Identity and compatibility

Map revision 1, artifact format 3 and terrain recipe version 1 reuse the existing Directorate content revision `directorate.v1`. The content hash remains `192624b7d2011b4ae6d0858d74d2fdfa9dc0d361c029444bf663f785556ffbd9`.

Map JSON SHA-256: `d2e47e1ec4da0fe8e2b498d10d73f8b7a2fee6e0a5f76ab86514510ed6c6e861`.

Terrain SHA-256 over every little-endian height sample in chunk/Z/X order: `ee7f0c7e872c2e32528729eca98171ebbcbd9d0d4d846a007fb4d5eb0d9562cd`.

The registry pins those bytes plus the mandatory digest sidecar. Save/replay payload 7 stores the exact scenario identity; envelope 1 and authoritative schema 7 are unchanged. A mismatching saved map identity fails explicitly. The historical Central Divide legacy mapping remains separate. Setup, new/load/restart and installed-package validation resolve the selected descriptor rather than falling back to the default map.

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

The first loaded-map qualification passes 49 reachability checks, two alternate-navigation checks and two alternate-road checks, with a sampled elevation range of 39.774094 m. Additional tests cover all three movement classes, both starts, every resource/site/objective, either crossing individually unavailable and disconnection with both unavailable. Terrain and resource quantities are reflected west/east.

At 16 m navigation cells / 8-cell sectors, Infantry, Wheeled and Tracked each take 1,408 m between starts with both crossings available. Closing the bridge produces an actual 2,115.677 m west→east path and 2,115.8376 m east→west path; the 0.161 m directional refinement difference is reported rather than treated as exact path symmetry. Closing only the ford leaves the 1,408 m route. The alternative is about 708 m longer. These are geometric route measurements, not unit arrival times or balance acceptance.

Nine focused Game cases pass: loaded geography and symmetry, all three movement classes with either crossing unavailable, the bounded real-scout sight probe, normal paid openings on both starts with exact conveyor Steel payment and physical Fuel, active-logistics save/replay/continuation and wrong/missing identity rejection, separate host crossing loss/restoration, and repeated 2,000-tick opponent comparisons with the existing configurations exchanged between starts. Sixteen Client scenario/argument cases pass, including actual registered new/load/restart. The bounded 2,000-tick comparisons are not completed games.

The actual CLI validates new/save/load/replay for both maps. Split Basin's seed-2026 validation state at tick 20 is `C5D2FF7886A5F0ADE25ED74D0E8CC0A3249C557D633E8C35AAC12C7952599F9B`; Central Divide retains `410F9179FD17B30F9203643F6038CF8FA9319B38FF67E0A74191400572E2D185`.

The separate unchanged 80,000-tick validation command finishes naturally on Split Basin at tick **44,753**, player 2 Victory / CommandCoreDestroyed. It records 114 completed Cargo orders, one failed Cargo order, zero route failures, 12,573.7015 delivered cargo, 2,789.6481 transferred Fuel, 1,253 Ammunition, 29 artillery shots and 27 impacts. Validation uses the existing accelerated profile and asymmetric opponent configurations; this single outcome is not normal-stock balance or win-rate evidence. The paid openings separately use Gameplay/Standard stock.

Release solution restore/build and the 44-project architecture check pass; the build has zero warnings/errors. Version is 0.1.470-dev, one PATCH above the checked 0.1.469-dev master 42ac542f. Fresh asset compilation, complete solution tests, native/WARP, reproducible package, installed validation and final-head CI are pending at this checkpoint. Small deterministic samples do not establish final win-rate balance. Automated native/WARP execution, physical reference-performance qualification and human visual/gameplay acceptance are separate evidence classes. Existing required gates remain unchanged.
