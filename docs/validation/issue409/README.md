# Policy-template selection measurements (#409)

Actual PolicyTemplateSelectionCapture, completed tick 1, seed 410, no AI,
256 completed owned processing facilities, equal global policy/request counts.
Each case uses 16 warmup captures and 32 measured captures; raw samples,
runtime, OS, process memory, and assembly SHA256 identities are in before.json
and after.json. No renderer, Present, GPU wait or GPU cost is measured.

| Selected | Policies / requests each | Before p50 ms | After p50 ms | Before bytes/op | After bytes/op |
|---:|---:|---:|---:|---:|---:|
| 1 | 256 | 0.3684 | 0.4126 | 15072 | 11880 |
| 8 | 256 | 2.3734 | 0.8096 | 115256 | 86096 |
| 32 | 256 | 7.8030 | 1.7383 | 458368 | 336480 |
| 1 | 2048 | 1.0200 | 0.9288 | 15744 | 12160 |
| 8 | 2048 | 8.0536 | 0.7343 | 120632 | 88336 |
| 32 | 2048 | 9.7104 | 0.8976 | 479872 | 345440 |
| 1 | 8192 | 1.0313 | 1.0589 | 18048 | 13120 |
| 8 | 8192 | 8.2283 | 1.2570 | 139064 | 96016 |
| 32 | 8192 | 33.0445 | 1.5828 | 553600 | 376160 |

All nine before/after serialized fact SHA256 values match exactly.
The original source is PolicyTemplateSelectionCapture.cs at master
201af6aad7ace07df21608c1d058d23b0828b070; the final source is in this PR.
The measurement harness is identical in both runs.

The change replaces repeated global queries per selected facility with one
stable pass per component type, retaining at most 32 policies and requests
per selected full EntityId. The first duplicate, policy-overflow marker,
32-target ordering and omitted count remain unchanged. No persistent cache
or entity-index high-water allocation is introduced. Single-target filtering
uses direct full-identity comparison. Per-resource temporary filtered arrays
are removed while accumulation order remains stable.

These are advisory local CPU measurements, not a qualified hardware baseline
or CI timing threshold. Scheduling, JIT and shared host load affect timings.
Reproduce with the Release Logistics benchmark executable:

    --policy-templates report.json 32
