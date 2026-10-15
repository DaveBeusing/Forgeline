# Directorate icon and portrait source qualification

Base master: `859d6f6bf04d5f32b759a50b93337e57c4169f8d`, product 0.1.373.
Candidate: 0.1.374-dev, one PATCH increment. The prerequisite skin change is
integrated; no original board is changed or imported a second time.

## Checkpoint

Native fitted portrait tool source: `f89423b` (full identity recorded in the
source catalog). Immutable output root: `artifacts/ui-roster-portraits-fitted/`.
All 20 roster families have five independently rendered sizes, 100 total
captures, repeated pixel equality and no fallback mesh/material binding failures.
An earlier three-size root is preserved separately and is not counted twice.
The original native camera/lighting/identity record is included verbatim in
`assets/source/ui/directorate/artwork/artwork.catalog.json`.

## Validation

| Check | Result |
| --- | --- |
| Native portrait export | Passed: hardware renderer, fixed pose, 100 repeatable size cases |
| Source production | 143 vector roles, 20 roster identities, 815 PNGs, 799 texture definitions |
| Exclusions | Patrol (5 PNGs) intentionally unavailable; 16-pixel cursor inspection (11 PNGs) not registered; railway/aviation reference-only |
| Restore | Passed |
| Release solution build | In progress; constant-array analyzer findings in new tests corrected |
| Pixel/hash/hotspot/size source tests | Pending |
| Actual enum/resource/technology/roster mapping tests | Pending |
| Project boundaries / canonical runtime compile | Pending |
| Complete solution tests | Pending |
| Package boundary and reproducible source-less player | Pending |
| Current-head required build-test CI | Pending |
| Human art, physical display and calibrated color-vision review | Review pending |
| Live UI integration / installed cursor behavior / integrated GPU cost | Not in this source-art scope |

```powershell
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Assets.Tests.DirectorateUiArtworkSourceTests
dotnet test --project tests/ForgeLine.Client.Tests/ForgeLine.Client.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Client.Tests.UiArtworkCoverageTests
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
pwsh ./build/Test-UiSkinPackage.ps1
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/ui-artwork-package
```

No existing assertions, approved goldens, native lifetime or terminal-match gates
are weakened. The previous shutdown finding is historical context; current-head
results must establish their own outcomes. Keep the PR draft while required CI
or human acceptance is pending. No merge, automatic tag or release is performed.
