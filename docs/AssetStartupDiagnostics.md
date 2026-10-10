# Asset Startup Diagnostics

Source-checkout startup incrementally compiles authoring assets before loading the runtime catalog. A missing catalog or changed source/dependency/compiler hash can require costly texture compression. Packaged clients consume compiled assets; the development bootstrap is bypassed when a runtime override is supplied or CI is enabled.

## Live compiler records

The Asset Compiler now emits progress immediately, rather than buffering all messages until the catalog is finished. Each record identifies the stage, state, asset ID, source path, elapsed/stage time, managed heap bytes, working set, private bytes and peak working set. Process memory sampling is throttled to 250 ms; the record reports its sample age. Long stages emit a five-second heartbeat with refreshed memory values.

Stages cover discovery, reference validation, dependency ordering, the previous manifest, source hashing, rebuild decisions, import, PNG/TGA decoding, resizing, mip generation, each BC7 mip and band, payload serialization, runtime/manifest writes and qualification. Rebuild reasons distinguish new/missing manifests, missing payloads, changed source, compiler, dependency and path. Failures include their stage, asset, exception and diagnostics. Unchanged assets are explicitly recorded and do not enter texture compression.

```powershell
dotnet run --project tools/ForgeLine.AssetCompiler --configuration Release -- --source assets/source --runtime assets/runtime --progress-log artifacts/asset-compilation.jsonl
```

The optional JSON-lines file is flushed after each record, so evidence survives a stalled or interrupted process. Records include UTC timestamps, a monotonically increasing sequence, the actual compiler process ID and elapsed time. Keep the log outside a runtime directory when using `--clean`. Console progress is emitted even without a file.

## Development-launch evidence

The client prints the compiler progress path before starting the child:

`artifacts/asset-startup/compile-<client-process-id>.jsonl`

The launcher prints its PID, wait duration, CPU time and memory every five seconds, then its exit code and total wait. These launcher measurements include SDK/build activity; use the compiler PID and JSON-lines records for actual asset-processing measurements. A delay before the progress file appears points to launching/building the compiler rather than an asset import. Cancellation still terminates and joins the complete compiler process tree.

Use `--startup-diagnostics-output artifacts/startup.json` on the client to correlate dependency completion with window, application and session readiness. A compiler heartbeat proves the process is alive, not that it is making useful forward progress: compare completed band rows and stages over time. Retain both the client console log and compiler JSON-lines file when reporting a stall.

## Texture processing fix

BC7 formerly processed full mip images serially. Compression now operates on 128-row bands with at most four workers, reserving CPU capacity for startup presentation. Only one texture/band is scheduled at a time. Block order, compression quality, formats, color/normal handling and mip generation stay unchanged. Completed compressed mips replace their uncompressed buffers instead of retaining both complete chains.

PNG decoding previously materialized full-image decompressed, filtered, scanline and RGBA copies. It now retains three row buffers and the RGBA image, reads exactly the declared scanlines, and rejects truncated or extra inflated data without materializing an unbounded decompression stream. RGB/RGBA and all five supported PNG filters are covered by regressions. The decoded source image still needs memory proportional to its authored dimensions; this is not a constant-memory importer.

The compiler/runtime format versions remain unchanged because complete catalog and per-mip parity checks establish identical output. Existing caches remain valid; no unnecessary full rebuild is introduced by the scheduling/decoder fix.

## Observed qualification

On the NVIDIA RTX PRO 5000 Blackwell Laptop development host, the instrumented serial cold compile of 341 assets took 681.78 seconds with 860.4 MiB peak working set. Banded compression with the old PNG decoder took 284.66 seconds and peaked at 1055.6 MiB, exposing the remaining decode memory cost. With streaming PNG decoding, the observed cold compile took 180.37 seconds and peaked at 548.8 MiB. All 341 payloads and the manifest matched byte-for-byte, and runtime qualification passed.

These are individual diagnostic observations. The runs overlapped different amounts of build/test/native work; they are not a controlled throughput benchmark or a portable speedup claim. A cold development catalog still requires substantial compilation. Subsequent unchanged-asset launches use the incremental cache.

The source-checkout native smoke included the real compiler subprocess, created its persistent progress file, reached application/session Ready with zero dropped startup events, and presented D3D12 frames with zero texture/material binding failures. The six new filter, malformed-length and serial-compression parity cases and nine startup/cancellation cases passed. Sustained cold-start visual/input acceptance remains separate from the native warm-cache smoke and automated blocked-dependency presentation tests.
