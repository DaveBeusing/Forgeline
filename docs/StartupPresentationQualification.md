# Startup presentation qualification

## Implementation boundary

The startup surface uses built-in branded glyph geometry before runtime catalog completion. Exactly two CPU/I/O jobs prepare the validated runtime catalog and read-only save entries. Window/input pumping stays on the platform owner; graphics initialization, presentation, resize and disposal stay on the render owner. Gameplay scheduler construction remains deferred until session selection. Intro completion/skip and successful dependency completion jointly gate the frontend handoff. No elapsed-time percentage or new minimum delay is used.

## Local checks, 2026-10-08

- Fifteen startup coordinator/loop/native cases passed, including both completion orders, blocked I/O, skip without duplicate loads, invalid/missing manifests, artwork fallback, cancellation, compiler termination and restart isolation.
- All 161 client tests passed. After the wider run, the native startup test was extended and rerun successfully to check actual menu presentation and interactive readiness after handoff.
- The native test holds the asset worker until the bootstrap brand has actually presented and ten further event-pump iterations complete. It verifies bootstrap presentation precedes asset readiness, and no menu milestone exists before handoff. It then presents a menu on the render owner and observes interactive application readiness on the platform owner. This is a focused integration test, not complete normal-client visual qualification.
- Release solution tests: 1,048 total, 1,030 passed, 16 failed, two opt-in GPU cases skipped. The same previously recorded failures remain in Presentation (9), Assets (5) and Game (2); the asset authoring tests expect 299 entries while the compiler produces 307. A separate complete clean-master test run was not performed.
- Release solution build passed with zero warnings/errors using the local `ApplicationIcon=` override. A forced canonical client compile failed with CS7065 while embedding the unchanged invalid application icon. An incremental canonical build alone is insufficient evidence because it can reuse the workaround-built executable.
- Project reference validation passed for 42 projects without cycles or boundary violations. Per-commit patch progression and whitespace validation passed.

## Repeated startup observations

Baseline: master `553ed46a16776e9091b3eab0df9cb5af04be3f58`, version 0.1.190. Updated executable: regression revision `8d9f628802ec8e1274fc78df3a12751365fe0292`, version 0.1.194; subsequent documentation/version metadata does not change startup behavior. Both were built with .NET SDK 10.0.401, Release Windows x64 and the local icon override.

Five observations per revision followed one excluded warm-up per executable. Runs alternated baseline then updated executable using the same precompiled 307-entry catalog selected through `FORGELINE_RUNTIME_ASSETS`, the same persisted default settings, 1600x900 windowed presentation, 144 DPI and VSync. Smoke mode bypasses both intro and menu; explicit `--skip-splash` was additionally supplied on alternate pairs. All twelve runs exited 0 with ApplicationReadiness and SessionReadiness Ready and zero dropped events.

Machine: Intel Core Ultra 9 285HX, 68,137,205,760 bytes reported physical RAM, NVIDIA RTX PRO 5000 Blackwell Generation Laptop GPU, driver 32.0.15.9653. Storage and OS cache state were not controlled. Builds and tests had completed before sampling, but other machine activity was not eliminated. These are repeated warm observations, not a controlled performance benchmark.

| Milliseconds since managed entry | Baseline median (range), n=5 | Updated median (range), n=5 |
| --- | ---: | ---: |
| First presented frame | 1135.83 (1116.64–1369.86) | 1081.15 (1009.44–1542.72) |
| Runtime assets ready | 1256.22 (1233.95–1528.25) | 408.29 (325.96–532.52) |
| Application ready, smoke gameplay | 4941.58 (4518.73–5253.39) | 4620.95 (4314.77–7137.32) |

First-frame ranges overlap, and the updated gameplay range includes a slower outlier. No overall speedup is established. Earlier asset readiness is consistent with overlapping CPU/I/O work and graphics initialization, but does not itself qualify a presented menu. Cold measurements, complete normal launch/menu timing and subjective first-frame appearance remain unmeasured.

## Remaining qualification

Resolve the canonical icon blocker and existing broad-suite failures before claiming a fully validated release. Check PR CI on the published head. Follow `StartupDiagnostics.md` for controlled cold/warm normal launches, splash-disabled settings, interactive skip, valid-save loading, settings-driven restart and close-before-ready. Inspect branding at 1920x1080, 2560x1440, 3840x2160 and 3440x1440, including real malformed texture payloads and window minimize/restore. The native presentation and deterministic loop tests do not substitute for these visual and full-client lifecycle checks.
