# Strategic logistics capacity qualification

## Source and result

Implementation/qualification source: b91b5b36864796f8be960e1e09b5550798872db0, based on master 08e8cf2f2e2632b3239229dc6840928d957c364c. Version 0.1.506 follows the current one-PATCH-per-PR rule. Subsequent documentation-only commits do not change this product identity. [Source and binary SHA-256 bindings](qualification/strategic-logistics/source-bindings.json) identify the executed assemblies and relevant source files.

Canonical Release solution build passed with zero warnings/errors. The SDK-selected Microsoft Testing Platform runner passed 240 Presentation cases, including both native cases, with zero failures/skips (14.174s). The adjacent Client interaction/reference suites passed 32 cases (4.522s); Game conveyor diagnosis, endpoint recovery, flow, overpass, recovery and policy-aggregation suites passed 67 cases (6.433s). The reference-graph validator passed all 46 projects with no cycles. Exact local build timing is recorded by the build log; only pass status is an acceptance claim.

Coverage includes network separation, no foreign endpoint publication, real completed-tick conveyor rates, overpass connection identity, removed destinations, copied retention, source-hash preservation during projection, unknown missing flow, 4096 actual owned segments, stale session/tick/player/terminal/mode rejection, all three network filters, retained press/release gestures, changed generations, mode/layout/session/blocked-input cancellation and existing Operations entry. The combined All view also supports marker drilldown. Client integration preserves precedence for the expanded minimap and existing HUD/placement/targeting paths. Existing simulation cases cover destroyed ports/supports, missing stock, full/reserved destinations, splitters, priorities and recovery without routing or transfer changes.

## Measurement scope

The [real extraction fixture](qualification/strategic-logistics/extraction.json) adds 4096 owned visual conveyor segments without flow components. One normal completed simulation tick plus existing world/conveyor extraction took 68.6252 ms in this observation. The projection retained 1024 conveyor samples plus two existing Cargo samples, omitted 3072 authorized conveyor samples and did not reach the scan limit. Missing flow is Unknown with no invented output rate. This is not 4096 actively transferring production lines, isolated extraction time, a latency guarantee or a comparative speed claim.

The [copied projection fixture](qualification/strategic-logistics/projection.json) supplies 4096 representative rows, copies the public snapshot ceiling of 3072, and spatially projects 80 with 2992 omitted. After 32 warmups, 128 builds averaged 0.5260484375 ms and zero allocated bytes per build on the recorded host. This measures CPU projection only, not GPU submission, full game frames or reference-hardware acceptance. Different source capabilities make the old topology-only layer an unsuitable equivalent throughput benchmark.

## Native output and input

Native Direct3D 12 WARP readback passed at 1920x1080/96 DPI and 1280x720/192 DPI for Off, all logistics networks, Conveyors, Cargo and Field Logistics. Each view produced byte-identical repeated frames, zero missing/dropped text glyphs and no submission fault, within the existing 262144-vertex ceiling. [96-DPI report](qualification/strategic-logistics/native-1920x1080-96dpi.json) and [192-DPI report](qualification/strategic-logistics/native-1280x720-192dpi.json) retain counts and text diagnostics.

These are isolated information-layer contract fixtures with manufactured copied states, not a complete battlefield scene or physical-adapter performance capture. Off is the reversible same-source control, not an old-release screenshot. Initial visual inspection found overlapping labels; collision/edge placement was corrected before the recorded final output. Final losslessly encoded readbacks were visually inspected for distinct networks, semantic fault/unknown labels and non-overlapping text. Raw RGBA and local PNG identities are bound in the manifest; the native test generates raw files under its requested output directory. Full populated battlefield/human readability and physical-adapter qualification remain separate acceptance work. No such pass is inferred from WARP, synthetic fixtures or unit-test success.

## Reproduction

Run the canonical restore, Release solution build and project-reference validation from Development first, then:

~~~powershell
$env:FORGELINE_LOGISTICS_MEASUREMENTS=(Join-Path (Get-Location) 'artifacts/logistics-projection.json')
$env:FORGELINE_LOGISTICS_EXTRACTION_MEASUREMENTS=(Join-Path (Get-Location) 'artifacts/logistics-extraction.json')
$env:FORGELINE_LOGISTICS_NATIVE_OUTPUT=(Join-Path (Get-Location) 'artifacts/logistics-native')
New-Item -ItemType Directory -Force artifacts | Out-Null
dotnet test --project tests/ForgeLine.Presentation.Tests/ForgeLine.Presentation.Tests.csproj --configuration Release --no-build -- --filter-class 'ForgeLine.Presentation.Tests.StrategicLogisticsCapacity*' ForgeLine.Presentation.Tests.RtsInformationLayerTests ForgeLine.Presentation.Tests.RtsMinimapInteractionTests ForgeLine.Presentation.Tests.OperationsTests --parallel none
~~~

The early CI step adds this focused gate without removing existing checks. [Product-source CI](https://github.com/DaveBeusing/Forgeline/actions/runs/40688302343) was queued at documentation time. No complete required-CI pass is claimed; final documentation-head checks must be read independently. Keep the PR draft while required CI and the stated battlefield visual acceptance remain outstanding. Integration source master itself had no direct commit workflow result available through the API at inspection; the integrated recovery PR source's full CI run 40667758833 had passed. This is baseline evidence, not a substitute for this PR's checks.

[Controls and measurement semantics](StrategicLogisticsCapacity.md)
