# Scalability measurement boundary

Qualification belongs to opt-in benchmark hosts and build tooling. Headless simulation and navigation hosts retain no graphics/presentation dependency. A shared source-only reporting helper supplies bounded raw samples and memory windows without creating another runtime service or project dependency.

Simulation diagnostics gains a separately enabled phase clock. It requires diagnostics to be enabled, allocates fixed phase storage once, and leaves the default tick execution path untimed. A timed tick executes the same commands, phase pipeline and observers in the same order. Clocks never feed simulation decisions or change logical tick cadence. Tests compare seeded authoritative state, RNG, metrics and observer order and verify no per-tick timing allocation.

Native renderer qualification completes each submission before reading GPU timestamps. This permits same-frame CPU/GPU reporting with current public graphics contracts at the cost of serial throughput semantics. It is unsuitable as a production frame-pacing proxy. No fence, descriptor or resource-retirement contract is weakened; reload checks require live resources to return to their pre-scene baseline.

Raw samples remain bounded and owned by one qualification process. Measurement allocations, reset work and observation are explicitly scoped. Hardware-sensitive thresholds stay advisory until a reference machine and noise envelope have been qualified. See [Scalability qualification](../ScalabilityQualification.md) for workload limits, budgets and missing coverage.
