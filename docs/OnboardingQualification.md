# Onboarding qualification

Base master: 3b34a562470921bcf3efa2339b918ab9886a3636.
Branch: ui/collapsible-onboarding. Version: 0.1.386, exactly one PATCH.
Product implementation is complete; qualification is in progress. No merge, release,
tag, pixel golden or human visual acceptance is claimed.

Implemented: shared clipped physical-pixel presentation state, 220 ms reversible
SmoothStep slide, translucent compiled skin, session-only minimization, named tab,
keyboard focus, lifecycle settlement, copied read-model rendering and central input
routing. No simulation, content milestone, command authority or asset input change.

First focused checks: Release solution build passed with zero warnings/errors;
14 Client interaction tests and 126 Presentation guidance/context/nonmodal tests
passed before the subsequent compact-content/native-scenario additions. Original
logs remain under artifacts/onboarding-*. The initial test build had a constructor
error in new focus-loss coverage, corrected to the existing event factory; no
product guard or assertion was weakened.

Next: rebuild and validate final implementation, full affected projects, native
sequence candidates/bright-dark and resource stability, canonical overall gates,
push and inspect current-head hosted checks. Historical notification evidence
records the existing 99,608,050-byte gameplay texture budget failure and the
seed-2026 match remaining Active at 80,000 ticks; these gates remain unchanged.

The matrix plan now contains 68 named states / 3,060 cases. Planned coverage is
not executed evidence. Native candidates and physical input/motion/accessibility
review remain pending until explicitly recorded below.

## Implementation checkpoint

Restore and the 43-project reference graph pass. Release solution build
(onboarding-build-sequence-fixed.log) has zero warnings/errors. Complete
Presentation: 1,148 passed / seven opt-in skips; Client: 320 passed before the last
two lifecycle/focus cases and copied-content validation refinement. Latest focused
Client run: 16 passed. Matrix contracts: 68 × 45 pass. A new native entrypoint
`--onboarding-capture <empty-output> <runtime-root>` samples the actual controller
in both directions at 30/60/144 Hz, normal/reduced motion, exact quarter times and
a slowly traversed permitted frozen fixture. Native execution is next.

Current canonical overall gates, source-less package and current-head hosted
results remain pending. All old failed build logs are retained; the second compile
error was a compact-state variable referenced outside its renderer scope, fixed
locally without changing a test. Native tooling also adopted the existing cached
JSON-options convention after the analyzer rejected its one-shot options instance.

## Native first pass and tab correction

Product source bad397aa21f5bd4b5222982f01473196ba47a79a:
`artifacts/onboarding-native-sequence/qualification.json` records 120 quarter-time
samples, two identical readbacks each, both directions, 30/60/144 Hz inputs,
normal/reduced motion and 720p high-scale/1080p clients. Source identity, native
adapter, text, skin and health are recorded. The 40 original PNGs remain local.
Native execution exits 0; no glyph/skin/submission failures were observed.

Image inspection found the high-scale tab label truncated to HI… because its body
text padding consumed too much width. The tab now publishes a separate padded
label rectangle and readable text size; native centered text uses that rectangle.
Four font-metric fit cases cover the complete HINT label. The subsequent focused
geometry/guidance/text run passes 97 cases, with a warning/error-free build.
These corrected images still need fresh source-bound capture.

The first package attempt in artifacts/onboarding-package failed manifest-tree
reproducibility while source/build work overlapped its two publish passes. It is
invalid qualification evidence and is retained unchanged, not overwritten or
called a successful package. Final packaging must run with frozen source and no
concurrent builds, in a new output directory. The natural validation match runs
from an immutable headless copy, preserving source-output DLL ownership.

Canonical assets: 1,337 records / successful forced rebuild and asset-process
contracts; no inputs changed. Map qualification: 71 reachability checks, two
alternate navigation links and two alternate road links. Headless 1,000/10,000
entity smoke tests pass. Next: commit tab fix, rebuild that source, repeat native
sequences/bright-dark snapshots, run complete solution, then isolated reproducible
package and fresh-settings smoke. Current-head hosted checks and human review
remain open.
