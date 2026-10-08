using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class GraphicsResourceRetirementTests
{
    [Fact]
    public void RecordedDisposalWaitsForTheSubmissionFenceAndReleasesOnce()
    {
        var retirement = new GraphicsResourceRetirement();
        int releases = 0;
        GraphicsResourceRetirement.Resource resource = retirement.Register(() => releases++);
        retirement.Use(resource);
        retirement.Retire(resource, 100);
        retirement.Collect(100);
        Assert.Equal(0, releases);
        Assert.Throws<ObjectDisposedException>(() => retirement.Use(resource));
        retirement.CompleteRecording(101);
        retirement.Collect(100);
        Assert.Equal(0, releases);
        retirement.Collect(101);
        retirement.Retire(resource, 101);
        retirement.ReleaseAll();
        Assert.Equal(1, releases);
        Assert.Equal(0, retirement.LiveCount);
    }

    [Fact]
    public void UploadWriteIsRejectedUntilItsLastUseCompletes()
    {
        var retirement = new GraphicsResourceRetirement();
        GraphicsResourceRetirement.Resource resource = retirement.Register(static () => { });
        resource.ValidateWrite(0);
        retirement.Use(resource);
        Assert.Throws<InvalidOperationException>(() => resource.ValidateWrite(100));
        retirement.CompleteRecording(101);
        Assert.Throws<InvalidOperationException>(() => resource.ValidateWrite(100));
        resource.ValidateWrite(101);
        retirement.Retire(resource, 101);
        Assert.Throws<ObjectDisposedException>(() => resource.ValidateWrite(101));
    }

    [Fact]
    public void CompletedResourcesAreCollectedEvenWhenDisposedOutOfFenceOrder()
    {
        var retirement = new GraphicsResourceRetirement();
        var released = new List<int>();
        GraphicsResourceRetirement.Resource older = retirement.Register(() => released.Add(1));
        GraphicsResourceRetirement.Resource newer = retirement.Register(() => released.Add(2));
        retirement.Use(older);
        retirement.CompleteRecording(1);
        retirement.Use(newer);
        retirement.CompleteRecording(2);
        retirement.Retire(newer, 0);
        retirement.Retire(older, 0);
        retirement.Collect(1);
        Assert.Equal([1], released);
        Assert.Equal(1, retirement.PendingCount);
        retirement.Collect(2);
        Assert.Equal([1, 2], released);
    }

    [Fact]
    public void PendingCapacityIsBoundedAndRejectedRetirementKeepsOwnership()
    {
        var retirement = new GraphicsResourceRetirement();
        for (int index = 0; index < GraphicsResourceRetirement.Capacity; index++)
        {
            GraphicsResourceRetirement.Resource resource = retirement.Register(static () => { });
            retirement.Use(resource);
            retirement.Retire(resource, 0);
        }
        GraphicsResourceRetirement.Resource overflow = retirement.Register(static () => { });
        retirement.Use(overflow);
        GraphicsDeviceException failure = Assert.Throws<GraphicsDeviceException>(() => retirement.Retire(overflow, 0));
        Assert.Equal("retirement-capacity", failure.ReasonCode);
        Assert.False(overflow.IsDisposed);
        retirement.CompleteRecording(1);
        retirement.Collect(1);
        retirement.Retire(overflow, 1);
        Assert.Equal(0, retirement.LiveCount);
    }

    [Fact]
    public void DeviceTeardownInvalidatesLiveAndPendingResources()
    {
        var retirement = new GraphicsResourceRetirement();
        int releases = 0;
        GraphicsResourceRetirement.Resource resource = retirement.Register(() => releases++);
        retirement.Use(resource);
        retirement.ReleaseAll();
        retirement.ReleaseAll();
        retirement.Retire(resource, 0);
        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(resource.ThrowIfDisposed);
        Assert.Equal(0, retirement.PendingCount);
    }
}
