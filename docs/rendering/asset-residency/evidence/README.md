# Retained asset residency evidence

Final integrated image source: `1edf00e20cdc3887eebeab57da2cc0863ea60106` against current master `a1610d1a51d4107c4b8202e234ed33edd3cfc9c9`, product 0.1.422. Earlier cold/warm/plateau observations retain their own sources in the qualification report.

[index.json](index.json) maps every original artifact path to original, retained and stored hashes. Text objects are gzip-compressed after explicit private-path redaction. PNG bytes are unchanged. Failed tests, the original RendererCold failure and both failed warm gates remain retained.

## Native image review

High, 1920×1080; these final integrated images match the current master pixels exactly. Technical equality does not replace human approval. Performance acceptance is independently open.

| Scene and zoom | Native image |
|---|---|
| MainBattleTankProduction-1920x1080-High-CloseTactical | [PNG](objects/c091b5b91419dd6808ba1358fc5a58068681fb64451311f79ba5618a99497320.png) |
| MainBattleTankProduction-1920x1080-High-NormalGameplay | [PNG](objects/f156028d99a9d5ca869306b7ab30b6aae4ef4a22a84639560a09d252d54d3ae0.png) |
| MainBattleTankProduction-1920x1080-High-Strategic | [PNG](objects/573ecfead47910b9aa0a13202505fdcc3f5338d02fa91008e304563613ce036a.png) |
| IndustrialBase-1920x1080-High-CloseTactical | [PNG](objects/555f1271216befa60296f390dcc123e3993117b8cfb4f8a6c92864a9a0cdb69f.png) |
| IndustrialBase-1920x1080-High-NormalGameplay | [PNG](objects/54930583f7e8adfb1e233249234e589e82ffece34b25b6be80ed2d6d639a262c.png) |
| IndustrialBase-1920x1080-High-Strategic | [PNG](objects/0488ee1d61fc565f9a2d83c8376fd6a3fe716a602078c24cffa0b975cbc47264.png) |

## Principal raw records

| Original artifact | Retained object |
|---|---|
| `asset-residency-followup-summary.json` | [data](objects/cd8bd9fb68dec296dc52e9ce00cbbee924b82e7f8d994fe3151924471f1a72b1.gz) |
| `asset-residency-inventory.json` | [data](objects/8fb0b8412ae6503c000d456c317a92d16fd07036a3d66aa3d5d6686cb990567d.gz) |
| `asset-residency-solution-tests.log` | [data](objects/b9a4f8b4756354f213fc043320074d57490b785053589b33d2d4339481ee042c.gz) |
| `asset-residency-integrated-presentation.log` | [data](objects/3e5cacccb2b970ed07f95ac4425279a85e7621eb93e568ed84cd7ae3eecfd87e.gz) |
| `asset-residency-probe-current-plateau-verified/result12.json` | [data](objects/cb9ca9694ba699f53500366f495ba098aacc2e2d43fbd4bd0306ee72f1f22ce4.gz) |
| `asset-residency-package-smoke/status.json` | [data](objects/1e8db63fcfd7f712897349111ac2dda1f9cf91fafcfc7d1b5fea58ed6c19e7e3.gz) |
| `asset-residency-native-cancel.status.json` | [data](objects/df4ecee040fd35658e44bac175fe2c83ea70c99344b4e062fd58d500c9b7160f.gz) |
| `asset-residency-native-final/warm-pair1-performance.json` | [data](objects/7b690236080e113f33868b7277f2c199df269dd75db20dcee607939a3cf3a3d6.gz) |
| `asset-residency-native-final/warm-pair2-performance.json` | [data](objects/a2d37949f64c2cee07ea544518fd1986bf4ee8163de45dbade0c726fcea7ec20.gz) |
| `asset-residency-native-final/cold-before/failure.json` | [data](objects/c100b176802fecbf72675a54de19f1f07a26f334c7fef527d44a6d6e5a87cfc8.gz) |

The twelve-cycle probe shows nine identical retired DXGI/staging observations after the bounded fourth slot admission. Exact per-asset Default-heap committed bytes and a warm shader-compilation call counter are unavailable, not zero.

## Reproduction

Run from the repository root with the source-bound clients and request files in the index. Build: `dotnet build ForgeLine.sln -c Release --artifacts-path artifacts/asset-residency-final -m:2 -nodeReuse:false -p:UseSharedCompilation=false`. Run the matching project/solution tests with the same artifacts path and `--no-build`.

`Measure-Residency.ps1` requires both frozen client executables, full baseline/current source SHAs, runtime root and a fresh artifacts output root. It writes the protocol before capturing. Failed warm gates cannot be replaced by RendererCold. The original before-source RendererCold fails; the corrected current cold run is separately indexed.

Build the observation host with `Prepare-ResidencyProbe.ps1 -ClientDirectory <frozen-client-directory> -OutputRoot <fresh-artifacts-directory>`, then run `dotnet <output>/bin/ResidencyProbe.dll <fresh-result.json> <runtime-root> <presentation-source-sha> [3..12]`. The optional fourth argument changes only observation count. `Test-PlayerPackage.ps1` validates a source-bound archive and starts its runtime-only player using fresh child settings.
