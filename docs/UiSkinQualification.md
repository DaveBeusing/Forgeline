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
| Original import in current remote master | Passed: all five fetched master blobs equal the original files and recorded identities |
| Authoring | 37 PNG/SVG/texture-definition sets and state catalog generated |
| PNG decoding, alpha, dimensions, SHA-256 and Git blob identity | Passed: four source tests |
| Slice bounds, minimum sizes, unique IDs and state resolution | Passed: four source tests |
| Native-size contact sheets over dark/light backgrounds | Source pixel inspection performed, including combined states and 28 x 28 slices; human review pending |
| Release restore | Passed |
| Release build | Passed: zero warnings/errors after narrowly documented Git SHA-1 analyzer compatibility exception |
| Targeted source tests | Passed: 4/4; compiler decodes all 37 skin sources and five originals |
| Package guard contracts | Passed: 7/7, including corrupt/missing/duplicate payloads, source leakage and escaping paths |
| Project-reference boundary validation | Passed: 42 projects; no cycles or boundary violations |
| Canonical runtime compilation | Passed: 530 assets, including all 37 skin textures; no references |
| Source-less reproducible package | Passed: two publish trees identical; package guard passes; isolated packaged application and session both Ready |
| Full solution tests | Passed: 2,004 passed, 9 existing opt-in skipped, 0 failed; 2,013 total, 6m23s |
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
pwsh ./build/Test-UiSkinPackage.ps1
pwsh ./build/Publish-PreAlpha.ps1 -Configuration Release -VerifyReproducible -RuntimeAssetRoot assets/runtime -OutputRoot artifacts/directorate-ui-skin-package
```

No test assertions or required workflow gates are disabled. The prior planning
snapshot's fence/shutdown failure is historical context, not a result for this
candidate. Current checks must establish their own outcomes. Full pipeline and
human acceptance are separate from successful source decoding.

## Retained source inspection evidence

[Components](qualification/directorate-ui-skin/components.png),
[combined states](qualification/directorate-ui-skin/states.png),
[original revalidation](qualification/directorate-ui-skin/reference-verification.json)
and their [hash ledger](qualification/directorate-ui-skin/evidence.json) are retained
in the repository. These are authoring contact sheets, not native game captures
or approved goldens. Labels appear only in inspection output, never in skin PNGs.

PNG bytes total 11,234. Decoded single-level RGBA totals 944,384 bytes. Compiled
skin containers total 972,486 bytes. This is storage accounting, not a GPU residency
or frame-time measurement. No live draw paths change; integrated GPU costs and
physical-display/motion acceptance belong to the subsequent renderer/integration
qualification. There is no sustained FPS or final AAA acceptance claim.

The runtime content signature is
`c7fe53eb349cb4b93e36e4c2bec1f324bc39f3a59bcf96aa617984357eb10df9`.
The source-art compiler and full-test build is `649e7d68b6968bed99490e2d94b06df08ca5d9ad`.
The later changes add inspection tooling, package guards, provenance and evidence;
production PNGs and C# source/test contracts remain byte-identical.

Nine existing opt-in skips cover two GPU-lifetime cases and seven isolated
Presentation hardware/performance cases. They are not new tests skipped to clear
this change. The current local complete suite passed; the historical allocation
and shutdown findings are not reported as failures of this run.

## Source-less player verification

Package source revision: `c98652741c8a2786373729a194794b33c10e5e76`, clean tree, SDK 10.0.401.
Two publish trees passed the existing reproducibility comparison. The package
validator confirmed all 37 runtime skin records and payload hashes and excluded
reference boards, source PNG/SVG/TTF files, asset definitions and the compiler.

Archive: `FORGELINE-0.1.373-dev-win-x64.zip`; 36,198,055 bytes.
SHA-256: `015e80a749522f488f52eb1b16328a8204452386223a22e15929232f347bdea1`.
[Durable package verification](qualification/directorate-ui-skin/package-verification.json)
records identity/readiness; full local outputs remain in the ignored immutable
`artifacts/directorate-ui-skin-package/` root.

The packaged player was launched from its publish directory using
`--smoke-test --skip-splash --settings-root <isolated-directory> --startup-diagnostics-output <report.json>`.
ApplicationReadiness and SessionReadiness are both Ready. No existing user
settings were deleted. This smoke proves startup/session operation with compiled
assets; the new skin has no live consumers yet and receives no native integration
or human acceptance claim from that launch.

Current-head required build-test CI and human art review remain pending. Keep
PR draft. No merge, auto-merge, tag or release is performed.
