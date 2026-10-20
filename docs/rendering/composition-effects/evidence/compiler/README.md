# Composite shader compiler evidence

This is static compiler evidence for **98 successful compilations**: two vertex shaders and 96 pixel variants. It does not qualify native images or GPU performance. `gpuMilliseconds` remains `null`; instruction totals and texture-load call sites describe DXIL IR across compiled branches, not executed operations, device ISA, bandwidth, overdraw or timings.

## Retained files

| File | Bytes | SHA256 |
| --- | ---: | --- |
| `manifest.json` | 261142 | `80cadb6bf214bce177675c3e34961a30c66dc495845143de1af48920e2085b13` |
| `scene-composite-dxc-evidence.tar.gz` | 4217307 | `7a4b94085a3c0a62c815c33a9efbd61ed703fdc4032d39490b7e9d8a96a5c87b` |

The archive contains all **297 original evidence files**: 98 HLSL inputs, 98 DXIL binaries, 98 disassemblies, two C# source snapshots and the manifest. Its manifest is identical to the separately retained copy. The other 296 files retain their original bytes. Archive members are sorted by name; timestamps, owner IDs and owner names are fixed, and the gzip header omits an original filename and timestamp.

## Path normalization

Original manifest SHA256 before normalization:

`9fde0c3619f369ba69fba873d95684909c9533dbd352dfab9251745ee9491e53`

The absolute directory prefix of the validation environment was replaced with `<validation-root>` in **393 occurrences**, including compiler and argument paths. Only these path-prefix bytes changed. Compiler results, exit codes, source hashes, output hashes, argument ordering and all statistics are otherwise untouched. The original manifest hash is retained above for audit. No other artifact contained that absolute prefix.

## Source and compiler identity

- Baseline commit: `cdf97fb9a8ef277cf75d451476a21128d398007c`.
- Current shader snapshot SHA256: `b3752472dcc3d1d6bae32ae7c91d9347a99eed924807a6b1f637a351abe3094a`.
- The exact current shader snapshot is present in source commit `15ad8ab96e41f510efd727da0bcbd82b5d48e9af`.
- The run occurred before that source commit: the manifest truthfully retains its original `currentCommit` and `currentSourcesDirty` values. They have not been relabeled.
- Compiler: official [Linux DXC v1.9.2609](https://github.com/microsoft/DirectXShaderCompiler/releases/tag/v1.9.2609), reporting `1.9.2609.5`. Executable/library hashes, exact arguments and per-case outputs are recorded in the manifest.

## Reproduction

Use the recorded shader source state, a verified matching DXC installation and a fresh output directory:

```text
python build/rendering/composition-effects/Compile-SceneComposite.py --base-ref cdf97fb9a8ef277cf75d451476a21128d398007c --dxc "<dxc-executable>" --output "<fresh-output-directory>"
```

The production default stays disabled for the shared-linear candidate. Its override applies only to extracted compiler inputs. Runtime AA is retained alongside separate constant-AA costing fixtures. Local haze with atmosphere disabled is labeled as a preprocessor case that the production lifecycle does not select. Hardware and image acceptance remain open.
