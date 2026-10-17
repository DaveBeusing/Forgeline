using Xunit.Sdk;
using Xunit.v3;

// Native device/window failure-path fixtures require one test owner at a time.
// Explicit resource concurrency exercised inside individual tests is unchanged.
[assembly: Parallelization(Mode = ParallelMode.None)]
