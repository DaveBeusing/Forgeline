using System.Runtime.CompilerServices;
using Xunit;

namespace ForgeLine.Presentation.Tests;

// Background collections can retire allocation contexts while .NET 10 updates
// per-thread counters: https://github.com/dotnet/runtime/issues/134724.
// Keep precise allocation checks away from concurrent allocating test classes.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FrameAllocationCollection
{
    public const string Name = "Frame allocation counters";

    internal static void CompleteBackgroundCollections()
    {
        // Outside the measured interval; zero-byte assertions remain unchanged.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
    }
}

[Collection(FrameAllocationCollection.Name)]
public sealed class AllocationCounterTests
{
    [Fact]
    public void IdleAndKnownAllocationControlsRetainExactCounters()
    {
        _ = Allocate();
        Thread.SpinWait(1_000);
        FrameAllocationCollection.CompleteBackgroundCollections();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Thread.SpinWait(1_000);
        long idle = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        byte[] value = Allocate();
        long positive = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        Assert.Equal(0, idle);
        Assert.True(positive >= 37);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Allocate() => new byte[37];
}
