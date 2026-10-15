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
| Release solution build | Passed; zero warnings/errors |
| Pixel/hash/hotspot/size source tests | Passed: 6/6 artwork plus skin tests after canonical-LF and source-isolation repair |
| Actual enum/resource/technology/roster mapping tests | Passed: 3/3, all current domains and identities |
| Project boundaries / canonical runtime compile | Passed: 43 projects; 1,329 assets, 799 new textures |
| Complete solution tests | Initial run: 2,006 passed, 3 failed, 9 skipped; source-boundary fixes prepared, final rerun pending |
| Package boundary and reproducible source-less player | 9 package guard contracts passed; reproducible player packaging pending |
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

## Source boundary corrections

The initial full run found three concrete source-test regressions. The skin
round trip recursively included the new artwork child directory; it now stages
only its 37 own PNG/definition pairs, preserving the exact 37-texture assertion.
Physical material checks selected UI action filenames containing production;
they now exclude DisplayImage usage while retaining all 24 native 4096-pixel
masters, runtime cap requirements and the 51,364,204-byte physical texture budget.
No assertion or approved golden is relaxed.

A separate Git-byte audit found editable SVG hashes depended on Windows CRLF
conversion, including the preceding skin family. Both authoring scripts now emit
canonical LF and scoped .gitattributes preserve it. Editable hashes are corrected
for the same vector content; all production PNG bytes remain unchanged. Source
checks explicitly reject CRLF masters. This repairs reproducible source identity,
not the visual content or runtime texture format.

## Retained inspection evidence

[Evidence ledger](qualification/directorate-ui-artwork/evidence.json),
[semantic coverage](qualification/directorate-ui-artwork/semantic-coverage.csv),
[resources](qualification/directorate-ui-artwork/resource.png),
[commands](qualification/directorate-ui-artwork/command.png),
[markers](qualification/directorate-ui-artwork/marker.png),
[cursors](qualification/directorate-ui-artwork/cursor.png) and four portrait pages
retain actual-size source inspection output. Original source PNGs and vector
relationships are in the catalog. Labels belong to contact sheets only.
The ledger remains human-review-pending; these are not accepted native UI goldens.

Registered artwork storage: 12,481,024 decoded RGBA bytes and 13,110,630 compiled
container bytes. All 815 source PNGs total 2,010,426 bytes, including excluded
inspection/unavailable variants. This is storage accounting, not integrated GPU
residency or sustained frame-time performance. No live draw path changes.

Canonical runtime content signature:
`e62d83a106f8a2e88c3a2a1f5acb7794ed8bc7fb683e40adc91c5cd404753aa5`.
Native model capture used the preceding sealed production runtime signature
`c7fe53eb349cb4b93e36e4c2bec1f324bc39f3a59bcf96aa617984357eb10df9`;
adding UI textures does not change the captured model/material identities.
