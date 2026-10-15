# Directorate skin source qualification

## Source identity and boundaries

Base: master `27d80f8b04d77404b5287e66d533dcfea3788250`, product 0.1.372.
Candidate product: 0.1.373-dev; exactly one PATCH increment for this PR.
Source hashes, dimensions, editable SVG relationships and slice contracts are in
`assets/source/ui/directorate/skin.catalog.json`. The five unchanged originals
retain their original hashes and Git blob identities in the reference catalog.

This qualification covers artwork sources and the existing compiler path.
No frontend/HUD consumer has been migrated and no new runtime loader is added.
The inventory's pending migration states are intentional scope boundaries.

## Validation record

| Check | Status |
| --- | --- |
| Original import in current remote master | Existing completed import reused; local revalidation in progress |
| Authoring | 37 PNG/SVG/texture-definition sets and state catalog generated |
| PNG decoding, alpha, dimensions, SHA-256 and Git blob identity | Pending source tests |
| Slice bounds, minimum sizes, unique IDs and state resolution | Pending source tests |
| Native-size contact sheets over dark/light backgrounds | Initial source inspection performed; final composite inspection pending |
| Release restore | Passed |
| Release build | In progress |
| Targeted source and integration tests | Not run yet |
| Project-reference boundary validation | Not run yet |
| Canonical runtime compilation | Not run yet |
| Source-less reproducible package | Not run yet |
| Full solution tests | Not run yet |
| Required current-head build-test CI | Pending |
| Human art/accessibility/physical-monitor acceptance | Review pending; no reviewer decision recorded |

## Reproduction

```powershell
python build/Author-DirectorateUiSkin.py --preview artifacts/directorate-ui-skin/contact-sheet.png
dotnet restore ForgeLine.sln
dotnet build ForgeLine.sln --configuration Release --no-restore
dotnet test --project tests/ForgeLine.Assets.Tests/ForgeLine.Assets.Tests.csproj --configuration Release --no-build -- --filter-class ForgeLine.Assets.Tests.DirectorateUiSkinSourceTests
pwsh ./build/Validate-ProjectReferences.ps1
pwsh ./build/Build-RuntimeAssets.ps1 -Mode development -NoBuild
dotnet test --solution ForgeLine.sln --configuration Release --no-build
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible
```

No test assertions or required workflow gates are disabled. The prior planning
snapshot's fence/shutdown failure is historical context, not a result for this
candidate. Current checks must establish their own outcomes. Full pipeline and
human acceptance are separate from successful source decoding.
