using Xunit;

// Native device/window failure-path fixtures require one test owner at a time.
// Explicit resource concurrency exercised inside individual tests is unchanged.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
