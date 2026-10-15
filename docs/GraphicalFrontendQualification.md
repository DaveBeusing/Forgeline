# Graphical frontend qualification

Base master `fd1a4c0d5869136a43ff5ae7306c9254a60881cf`, version 0.1.375.
Candidate version 0.1.376-dev, branch `ui/graphical-frontend`, PR #210.
Public contracts and scope: [graphical frontend](GraphicalFrontend.md).

## Checkpoint

Typed view/pointer wiring: `ed0173b6`; production rendering: `99d03586`.
The initial Release solution build passed with zero warnings/errors. All 298
Client tests and three new native production tests pass. These cover existing
settings/input/lifecycle behavior, all normal kinds, repeated sessions/descriptor
return, opaque pause backing and controlled required-skin recovery.

Native base root `artifacts/graphical-frontend-before-all/` contains 13 actual
1080p/DPI96/scale1 states from the current-master base. An earlier shell-array
attempt is preserved separately, contains one combined scenario and is not counted
as the baseline. Candidate root `artifacts/graphical-frontend-candidate/` contains
16 real states; review identified value truncation in slider rows. The subsequent
refinement preserves full live values and exposes existing settings list extent.
Those earlier captures remain immutable and are not final-candidate acceptance.

Supported display/DPI/scale capture, full solution regression, source-less
reproducible packaging, current-head required CI and human acceptance are pending.
No existing assertion, approved golden, native write guard, shutdown/fence gate
or terminal-match gate is relaxed. Keep draft; no merge, tag or release.

Runtime content signature (unchanged assets):
`b6cbe90cbbd4dfc780abb28f5d42f7bc3b469ae0cf4e663f594975a2bfd219d9`.

## Reproduction

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --project tests/ForgeLine.Client.Tests -c Release --no-build
dotnet test --project tests/ForgeLine.Presentation.Tests -c Release --no-build -- --filter-class ForgeLine.Presentation.Tests.FrontendProductionSkinTests
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/graphical-frontend-package
```

Use the existing reference lane with arrays passed to the PowerShell script itself:

```powershell
& ./build/Invoke-RtsReferenceQualification.ps1 -Capture -Scenes BattlefieldCombat -Profiles Medium -Zooms NormalGameplay -Displays 1920x1080 -RasterDpi 96 -UiScenarios @('Menu','MenuFocus','MenuDisabled','MenuHover','MenuPressed','Loading','LoadingError','NewGame','LoadGame','LoadEmpty','LoadBroken','SettingsGeneral','SettingsGraphics','SettingsAudio','SettingsBindings','BindingConflict','DisplayTrial','ControlsLong','Pause','PauseSettings','Credits') -CinematicFrontend -Warmups 3 -Samples 8 -RepeatCaptures 2 -OutputRoot artifacts/graphical-frontend-review
```

Capture roots must be new and the source committed/rebuilt. Repeat across compact,
1440p, ultrawide, 4K, DPI96/144/192 and scales .75/1/2, recording the exact union
of keys and original hashes. Physical display acceptance and calibrated accessibility
review remain human decisions, separate from requested raster-DPI sampling.
