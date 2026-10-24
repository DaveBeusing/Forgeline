# Runtime visual asset compatibility

The native client and generated runtime assets must be updated together. Runtime container integrity proves that a catalog is internally consistent; it does not prove that the catalog contains IDs selected by a newer renderer.

## Conveyor and command-core incident

The reported 0.1.469-dev client (source 42ac542f) ran from D:/Code/Forgeline/src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64. Its repository runtime manifest contained 1,357 entries and omitted material.directorate.building.command_core, material.infrastructure.directorate.conveyor and infrastructure.directorate.conveyor.straight_standard.idle_no_input. The current qualified catalog contains 2,049 entries. The source checkout resolver correctly preferred the repository cache; that generated cache had survived code updates without recompilation.

Missing conveyor geometry falls through to the generic cube. Missing materials independently resolve to the explicit magenta development material. Thus a detailed command core can be magenta while conveyors become magenta blocks. This is not evidence of corrupted Blender imports or incorrect authored model mappings.

The previous session preflight checked five representative assets, all present in the old cache. Preparing all assets declared by that cache could also succeed because the missing renderer-selected IDs were not declared there.

## Prevention and recovery

Use the repository-root development entry point:

~~~powershell
pwsh ./build/Start-DevelopmentClient.ps1
~~~

It builds the client and asset compiler, compiles and qualifies runtime assets in build mode, and launches only after success. An existing running process must be restarted to acquire the rebuilt catalog. Compilation never happens inside the player.

GameplayVisualAssetContract checks the renderer-selected building states, attachments, materials and LODs, together with conveyor modules, link states, support variants and LODs, before GameplayAssetsReady. A missing or wrongly typed ID fails session loading with asset names and recovery instructions; it cannot silently qualify an obsolete catalog. The existing bounded preparation still validates payloads, dependencies and texture bindings.

RuntimeAssetsReady now includes the selected runtime root, asset count and catalog signature in startup diagnostics. If FORGELINE_RUNTIME_ASSETS is set, that explicit installation remains authoritative. Update it as a complete matching package; do not fall through to another installation or copy individual missing files.

## Regression coverage

Tests reject internally consistent catalogs missing command-core or conveyor materials and geometry, reject wrong asset types, honor cancellation, and check every required mapping against authored definitions. Asset count alone is not an acceptance condition.
