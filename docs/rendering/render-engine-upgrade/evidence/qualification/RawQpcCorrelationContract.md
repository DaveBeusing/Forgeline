# Retained Windows raw-QPC analysis contract v1

Rule: `windows-coreclr-10.0.13-traceevent4-retained-parser-raw-qpc-v1`.
This is an analysis correctness rule, not a policy, accepted baseline, successful fixture or wave 2 handoff. The analyzer revision and this file's byte hash must be recorded separately from the original producer and frozen parser. Reanalysis uses fresh outputs; original failed/unavailable results remain immutable.

## Supported source path and exact parser

The narrow supported path is Windows CoreCLR `.NET 10.0.13`, original FastSerialization Trace v4, and the following retained parser execution. Future parser images, dependencies, source builds, runtime versions or trace formats retain the generic conservative uncertainty path until separately reviewed.

| Identity | Exact retained value |
| :--- | :--- |
| Parser source | `ec5db1892799977bea3b7908ce8b8688ce94b49b` |
| Parser informational version | `0.1.539-dev+ec5db1892799977bea3b7908ce8b8688ce94b49b` |
| Parser assembly SHA256 | `63808a724288c49f3c5be45412687f9861d21a9c53c18453af2d7604545f3d17` |
| Parser dependency-file SHA256 | `7ea4a4da11c3e5c561be35d934e5f7654ac14cc381f3773e0b3e02b54f79f157` |
| TraceEvent dependency | `Microsoft.Diagnostics.Tracing.TraceEvent/4.0.0` |

The source chain preserves raw integer QPC timestamps:

- [Stopwatch.Windows.GetTimestamp](https://github.com/dotnet/runtime/blob/v10.0.13/src/libraries/System.Private.CoreLib/src/System/Diagnostics/Stopwatch.Windows.cs#L17) directly calls Windows QueryPerformanceCounter.
- [EventPipe event-instance timestamp](https://github.com/dotnet/runtime/blob/v10.0.13/src/native/eventpipe/ep-event-instance.c#L81) uses `ep_perf_timestamp_get`; [ep.h](https://github.com/dotnet/runtime/blob/v10.0.13/src/native/eventpipe/ep.h#L278) delegates to `ep_rt_perf_counter_query`.
- [CoreCLR EventPipe performance counter](https://github.com/dotnet/runtime/blob/v10.0.13/src/coreclr/vm/eventing/eventpipe/ep-rt-coreclr.h#L951) uses `minipal_hires_ticks`; [Windows minipal time](https://github.com/dotnet/runtime/blob/v10.0.13/src/native/minipal/time.c#L7) returns QueryPerformanceCounter unchanged.
- [Pinned TraceEvent v4 decoding](https://github.com/microsoft/perfview/blob/v4.0.0/src/TraceEvent/EventPipe/EventPipeEventSource.cs#L1102) retains integer timestamp values/deltas; [event metadata](https://github.com/microsoft/perfview/blob/v4.0.0/src/TraceEvent/EventPipe/EventPipeMetadata.cs#L177) passes the decoded timestamp to the event record.
- [ETLX serialization](https://github.com/microsoft/perfview/blob/v4.0.0/src/TraceEvent/TraceLog.cs#L2186) writes that event-record header; its reader retains the timestamp at line3335. [TimeStampQPC](https://github.com/microsoft/perfview/blob/v4.0.0/src/TraceEvent/TraceEvent.cs#L1955) exposes the retained raw event timestamp. The exact pinned ForgeLine parser exports it without shifting or rescaling.

## Proof boundary and ordering

Derivation runs only after original trace/report hashes, clean source, unchanged full tool/parser inventories, pinned tool/version, exact collection/parser commands and raw outputs, dependency bytes, original Windows process/image/creation lifetime, source/runtime/session/native thread, header bytes/format/frequency, UTC anchor compatibility, and event loss/truncation gates have passed. Actual **target-PID event stacks** must supply Windows installed `shared/Microsoft.NETCore.App/10.0.13/System.Private.CoreLib.dll` module paths. Missing modules, conflicting versions/installation roots or unsupported versions cannot produce proof. A preflight from a different PID, caller runtime label or `sameClock` boolean cannot supply it.

Under that proof, an event belongs to a measured interval only when `startTick < timestampQpc < endTick`, using original integers. Exact start/end ties remain ambiguous. The UTC SYSTEMTIME millisecond quantization and owner UTC-read bracket remain the unchanged anchor sanity tolerance; they are not an integer-ordering error for this supported raw-QPC path. Unsupported/unproven paths retain the original conservative boundary band. Invalid or foreign explicit proofs fail, without generic fallback, fitted shifts, rescaling or site/count-specific selection.

The trust model is ordinary installed Windows/CoreCLR module-version trust plus the independently retained process/trace/parser provenance. Historical CoreCLR/CoreLib binary-content hashes and an original clock-domain attestation were not retained. No current-file hashes are substituted retroactively. This rule does not claim cryptographic attestation of historical runtime binaries.

Sampled sites remain sampled: matching does not establish exact allocation bytes, zero allocation, complete site coverage, normal-run timing, fixture/profile success, physical stability, accepted A/A controls or wave 2 readiness.
