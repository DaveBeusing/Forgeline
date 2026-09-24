# Job System

## Purpose

`ForgeLine.Jobs` provides the engine's lightweight persistent-worker scheduler for simulation-oriented CPU work.

The implementation is deliberately narrower than a general task runtime. It exists to split large, predictable workloads such as movement, sensors, logistics, navigation preparation, production updates, and combat calculations into bounded jobs without creating operating-system threads per task.

## Worker Lifecycle

A `JobScheduler` creates a fixed worker pool when constructed.

The default worker policy reserves one logical processor for coordinating work where possible:

```text
max(1, processor count - 1)
```

The worker count is configurable through `JobSchedulerOptions`, including one-worker mode for deterministic tests and constrained environments.

Workers:

- remain alive until scheduler shutdown;
- sleep on a semaphore when no ready work exists;
- receive stable diagnostic thread names;
- never create a new OS thread for an individual job;
- execute only scheduler-owned work items.

The scheduler must be owned and disposed by the composition root that creates it.

## Job Submission

`Schedule` accepts a bounded `JobAction`.

`ParallelFor` splits a contiguous integer range into fixed-size batches and submits one scheduler work item per batch. It does not allocate one managed job object per entity.

Callers should choose batch sizes that provide enough work to amortize scheduling overhead. Smaller batches improve load distribution but increase queueing and synchronization cost.

## Dependencies and Fences

Jobs can declare prerequisite `JobHandle` values.

A dependent job becomes runnable only after all prerequisites have completed successfully. If a prerequisite faults, the failure propagates through dependent work rather than executing that work against incomplete state. Cancellation propagates in the same way.

`CreateFence` creates a completion handle over multiple prerequisites. `WaitAll` is a convenience boundary over the same model.

Dependency handles must belong to the same scheduler.

## Waiting and Barriers

The simulation coordinator or another non-worker coordinating thread may wait for a `JobHandle`.

A scheduler worker is not allowed to synchronously wait on unfinished work from its own scheduler. Blocking a bounded pool from inside its workers can deadlock one-worker mode or exhaust every worker.

Simulation integration normally avoids explicit waits by completing tracked jobs automatically at each registered system boundary.

## Exceptions

Worker exceptions are captured and surfaced when the corresponding handle or a dependent fence is waited.

Exceptions are not silently swallowed.

If an instrumentation observer throws, that failure does not corrupt job execution; it is counted through `InstrumentationFailures` in scheduler metrics.

## Shutdown

Two shutdown modes exist:

- `Drain`: stop accepting new work, finish submitted work, then stop workers;
- `CancelPending`: stop accepting new work, cancel jobs that have not begun, signal cooperative cancellation to running jobs, then stop workers after running work exits.

Shutdown is idempotent after completion.

Running work is cooperative: the scheduler does not abort managed threads. Long-running jobs should observe the provided `CancellationToken` at sensible boundaries.

## Instrumentation

`JobSchedulerMetrics` exposes:

- submitted jobs;
- successful completions;
- faults;
- cancellations;
- pending jobs;
- running jobs;
- worker count;
- peak concurrent running jobs;
- accumulated submission-to-start wait duration;
- accumulated execution duration;
- instrumentation observer failures.

An optional `TimingObserver` receives per-executed-job wait and execution durations together with the worker index and completion status.

Timing data is diagnostic only. It must not feed simulation decisions.

## Simulation Integration

`SimulationCoordinator` accepts an optional scheduler.

When present, `SimulationContext.Jobs` can schedule normal or range jobs. The runtime tracks those handles and establishes an automatic fence after the Input Commands batch and at the end of each registered system invocation.

This means a system may parallelize internal work while existing phase and registration ordering remain explicit.

Systems remain free to stay single-threaded. Parallel execution should only be introduced where representative benchmarks show that scheduling overhead is justified.

## Deterministic-Friendly Rules

Parallel execution changes timing, not the intended logical ordering contract.

Simulation code using jobs must:

- keep tick and phase ownership on the coordinator;
- keep writes partitioned when possible;
- avoid data races;
- avoid unordered shared reductions when result order affects gameplay;
- retain stable merge/reduction order where required;
- avoid wall-clock decisions;
- avoid presentation state;
- avoid concurrent access to shared simulation RNG streams unless deterministic ordering is explicitly designed.

Parallel work that cannot satisfy these constraints should remain single-threaded.

## Validation and Benchmarks

`ForgeLine.Jobs.Tests` covers range completeness, dependencies, fences, failure propagation, one-worker mode, shutdown, stress execution, and instrumentation.

Simulation integration tests verify that tracked parallel work completes before the next registered system and that worker failures surface through the coordinator.

The simulation benchmark host contains representative sequential-versus-parallel range workloads. Benchmarks compile with the solution but are not timing gates in CI.

Performance claims require benchmark evidence from representative workloads and target hardware.
