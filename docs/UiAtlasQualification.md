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
- Canonical runtime qualification passed: 1,337 assets, including seven page textures and one skin payload.
- Final sequential Release solution build passed with zero warnings/errors at 935e3fcc.
- Native hardware captures passed: 12 repeated-pixel cases and a real menu image.
- Source-less packaged captures passed, byte-identical across all 13 native PNGs.
- Reproducible packaging passed from a clean source tree; archive payload hashes and source exclusions verified.
- Packaged application/session Ready, zero dropped events, isolated settings.
- Complete sequential solution run passed: 2,025 passed / 0 failed / 9 existing opt-in skips (2,034 total), 6m 44.463s; failed initial attempt remains recorded below.
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

## Qualification findings repaired

The first hardware fixture correctly rejected writing a second flush into a
buffer already recorded for that frame. Native whole-resource write guards are
preserved: each configured flush now owns a distinct preallocated frame buffer.
The default single-flush batch reserves 3 MiB; the two-flush core fixture reserves
6 MiB. Capacity is configured during preparation (maximum eight), with explicit
failure on excess flushes, no new frame allocations and no per-control GPU wait.
The native straight-alpha test now exercises two flushes on the same frame.

The first broad run completed 2,023 passed / 2 failed / 9 existing opt-in skips
(2,034 total). It overlapped canonical compilation; the menu test read the
previous manifest before the new skin generation was published. An unchanged
world-renderer allocation assertion observed 480 bytes. These failed results are
retained; the final sequential qualification passed with no errors.

## Retained native and package evidence

Validated code, build, native and package source:
`935e3fccd7d52b3e3b9fa535249e837f512887d9`.
Subsequent qualification changes only retain evidence, update documents and add
an explicit targeted CI step; they do not alter compiled production behavior.
The complete required build-test job and terminal-match gate remain unchanged.

[Native ledger](qualification/directorate-ui-atlas/evidence.json) retains all
12 unchanged hardware fixture images plus the real menu control. The original
native manifest records the actual adapter and source/runtime identity. These
are structural/readability inspection images, not approved goldens. The debug
layer was unavailable on this host, so no debug-layer certification is claimed.

[Dark fixture](qualification/directorate-ui-atlas/fixture-0-1280x720-dark.png),
[bright fixture](qualification/directorate-ui-atlas/fixture-0-1280x720-bright.png),
[real menu control](qualification/directorate-ui-atlas/menu-control.png).

The three sessions returned to zero loaded UI textures and zero used texture
descriptors after each disposal. Peak descriptors were eight, with 36 MiB of
texture residency including the existing 8 MiB font. All 26 uploads were released;
there were no texture binding failures or graphics submission faults. Fixture
counts are core 11 draws / 1,506 vertices / 48,192 requested upload bytes, icons
1 / 108 / 3,456, portraits 2 / 48 / 1,536. These are counts and storage, not isolated
GPU time or sustained frame-rate acceptance. The two-flush core uses eight bounded
frame/flush buffer slots; one-flush groups use four each.

[Package ledger](qualification/directorate-ui-atlas/package-verification.json)
records the archive, all-payload verification, readiness and packaged native
manifest identities. The source-less packaged fixture matches all 13 development
PNG bytes. Archive `FORGELINE-0.1.375-dev-win-x64.zip` is 39,962,628 bytes, SHA-256
`6886818cc84218cb637b7ee821b2852bc4ac52209a2fccedaf4edcc95963e47b`.
Provenance records sourceTreeClean=true. Runtime content signature:
`b6cbe90cbbd4dfc780abb28f5d42f7bc3b469ae0cf4e663f594975a2bfd219d9`.
Skin metadata container `ui.skin.directorate.compiled` is 99,652 bytes, SHA-256
`4aad78d8202de12ec53a45f1bdd2a40c85fc524e13fe9830624bd434211501fc`.

Human art, physical-display/DPI and calibrated accessibility review remain pending.
The native fixture covers resize through two output dimensions, clipping, state
layers, tiled and partial-fill geometry and text interleaving. It is not evidence
of a fully migrated HUD or a sustained gameplay soak.

## Final regression result

The complete sequential suite on the validated production source passed:
2,034 total / 2,025 passed / 0 failed / 9 existing opt-in skipped,
6m 44.463s. This includes the previously failing menu lifecycle and world
allocation assertions unchanged. The initial broad failure is historical attempt
evidence, not the final candidate's result. No tests or native write guards were
weakened to obtain this outcome.
