# Compiled UI skin qualification

Base master: `810011c8471a4cc5e1291c96411e47a7008fe9b2` (0.1.374).
Branch: `ui/compiled-skin-rendering`, candidate 0.1.375-dev, one PATCH.
Contract and public API: [compiled UI atlases](UiAtlasRendering.md).

## Current checkpoint

- Contract/compiler/source commit: `f89e769e9c557bf79e2b5116b5b4142a212a3e3f`.
- Renderer/preparation/native fixture commit: `e1b46f447b8a811d818b8b3202a1bcfaa00ec5f8`.
- Native alpha test commit: `6a0026245f2bbc17a0dca760915b0643a62ff358`.
- 836 sprites, seven pages, 29,360,128 page bytes; no production pixels or reference boards replaced.
- Required artwork availability exclusions retained; no additional cursor activated.
- Release solution build passed with zero warnings/errors before the native alpha test addition.
- Ten asset/compiler tests and six batch/ownership tests passed, including native WARP alpha readback.
- 43-project boundary validation passed.
- Canonical runtime compilation, complete solution run, native hardware captures and reproducible packaging pending.
- A later concurrent build encountered a compiler DLL file lock during runtime compilation. That attempt is not a passing build; final build will run sequentially.
- Required current-head CI and human visual/physical-display acceptance pending. Keep draft.

No existing assertion, approved golden, shutdown/fence or terminal-match gate is
removed or relaxed. The existing five whole-catalog count assertions increase
from 1,329 to 1,337 for seven page textures and one metadata container. Physical
world texture budgets remain unchanged. Separate frontend and HUD goldens do not
automatically acquire the newly prepared skin; migration needs explicit preparation.

## Reproduction

```powershell
python build/Author-UiAtlas.py
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --project tests/ForgeLine.Assets.Tests -c Release --no-build -- --filter-class ForgeLine.Assets.Tests.UiSkinPipelineTests
dotnet test --project tests/ForgeLine.Presentation.Tests -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.UiSkinBatchTests
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Test-UiSkinPackage.ps1
src/ForgeLine.Client/bin/Release/net10.0-windows/win-x64/ForgeLine.Client.exe --ui-skin-capture artifacts/ui-skin-native assets/runtime
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/ui-atlas-package
```

Run compilation/build/packaging sequentially where they share compiler binaries;
capture and package output roots must be empty. Preserve failed attempts rather
than overwriting accepted evidence. Native structural results, local regression,
package integrity, remote required CI and human approval are distinct gates.
