# Asset Startup Diagnostics

[Documentation](README.md) · [Complete catalog](DocumentationIndex.md)

Asset compilation is an explicit authoring/build step. The client consumes compiled runtime assets and never starts the compiler. A missing or invalid manifest produces a visible startup failure. Compile assets using the command below before launching a source checkout; packaged clients require no source files or compiler.

## Live compiler records

The Asset Compiler now emits progress immediately, rather than buffering all messages until the catalog is finished. Each record identifies the stage, state, asset ID, source path, elapsed/stage time, managed heap bytes, working set, private bytes and peak working set. Process memory sampling is throttled to 250 ms; the record reports its sample age. Long stages emit a five-second heartbeat with refreshed memory values.

Stages cover cache-lock acquisition, discovery, reference validation, dependency ordering, the previous manifest, source hashing, rebuild decisions, import, PNG/TGA decoding, resizing, mip generation, each BC7 mip and band, payload serialization, runtime/manifest writes and qualification. Rebuild reasons distinguish new/missing manifests, missing payloads, changed source, compiler, dependency and path. Failures include their stage, asset, exception and diagnostics. Unchanged assets are explicitly recorded and do not enter texture compression. Compiler 1.9.0 verifies full-container size/SHA-256 before cache reuse; damaged output has rebuild reason `invalid-payload-or-path`. See [build modes and integrity](AssetPipeline.md#build-modes-and-deployment-integrity).

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler --configuration Release -- --source assets/source --runtime assets/runtime --progress-log artifacts/asset-compilation.jsonl
```

The optional JSON-lines file is flushed after each record, so evidence survives a stalled or interrupted process. Records include UTC timestamps, a monotonically increasing sequence, the actual compiler process ID and elapsed time. Keep the log outside a runtime directory when using `--clean`. Console progress is emitted even without a file.

## Development-launch evidence

Keep compiler and client measurements separate. Use `--progress-log artifacts/asset-compilation.jsonl` for the compiler and `--startup-diagnostics-output artifacts/startup.json` for the client. A compiler heartbeat identifies a live compiler process; completed rows and stage changes establish forward progress. Player-startup reports must use precompiled assets and must not include source compilation.

## Texture processing fix

BC7 formerly processed full mip images serially. Compression now operates on 128-row bands with at most four workers, reserving CPU capacity for startup presentation. A process-wide texture slot is acquired before decoding, so concurrent compilation requests cannot multiply the worker count or retain several large decoded source images while waiting. The slot is released on success or failure, and `texture-queue` records identify a wait for it. Only one texture/band is scheduled at a time within a compiler process. Separate compiler processes have separate budgets. Block order, compression quality, formats, color/normal handling and mip generation stay unchanged. Completed compressed mips replace their uncompressed buffers instead of retaining both complete chains.

PNG decoding previously materialized full-image decompressed, filtered, scanline and RGBA copies. It now retains three row buffers and the RGBA image, reads exactly the declared scanlines, and rejects truncated or extra inflated data without materializing an unbounded decompression stream. RGB/RGBA and all five supported PNG filters are covered by regressions. The decoded source image still needs memory proportional to its authored dimensions; this is not a constant-memory importer.

The earlier scheduling/decoder fix retained its compiler/runtime format versions because complete catalog and per-mip parity checks established identical output. That fix did not invalidate caches. The current integrity upgrade separately advances the compiler to 1.9.0 and rebuilds old unsealed caches once.

## Historical compiler qualification

On the NVIDIA RTX PRO 5000 Blackwell Laptop development host, the instrumented serial cold compile of 341 assets took 681.78 seconds with 860.4 MiB peak working set. Banded compression with the old PNG decoder took 284.66 seconds and peaked at 1055.6 MiB, exposing the remaining decode memory cost. With streaming PNG decoding, the observed cold compile took 180.37 seconds and peaked at 548.8 MiB. All 341 payloads and the manifest matched byte-for-byte, and runtime qualification passed.

These are individual diagnostic observations. The runs overlapped different amounts of build/test/native work; they are not a controlled throughput benchmark or a portable speedup claim. A cold development catalog still requires substantial compilation. Subsequent unchanged-asset launches use the incremental cache.

The source-checkout native smoke included the real compiler subprocess, created its persistent progress file, reached application/session Ready with zero dropped startup events, and presented D3D12 frames with zero texture/material binding failures. Targeted validation covers serial-compression parity, RGB/RGBA/all-filter decoding, malformed inflated lengths, concurrent compiler queuing and recovery after failed decode. Startup/cancellation and native session handoff checks also pass in isolation. Complete solution and hosted gate status is recorded in the pull request. Sustained cold-start visual/input acceptance remains separate from the native warm-cache smoke and automated blocked-dependency presentation tests.
