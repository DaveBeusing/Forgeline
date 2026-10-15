using System.ComponentModel;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceCapturePowerLeaseTests
{
    [Fact]
    public void RequestRestoresPriorRequirementsExactlyOnce()
    {
        var calls = new List<uint>();
        uint previous = ReferenceCapturePowerLease.Continuous | 1;
        var lease = new ReferenceCapturePowerLease(flags => { calls.Add(flags); return previous; });
        lease.Dispose(); lease.Dispose();
        Assert.Equal(new[] { ReferenceCapturePowerLease.Required, previous }, calls);
        Assert.True(lease.ReleaseSucceeded);
    }

    [Fact]
    public void FailedAcquisitionDoesNotPretendToOwnARequirement()
    {
        int calls = 0;
        Assert.Throws<Win32Exception>(() => new ReferenceCapturePowerLease(_ => { calls++; return 0; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CaptureFailureStillRestoresTheRequest()
    {
        var calls = new List<uint>();
        Assert.Throws<InvalidDataException>((Action)(() =>
        {
            using var lease = new ReferenceCapturePowerLease(flags => { calls.Add(flags); return ReferenceCapturePowerLease.Continuous; });
            throw new InvalidDataException("Capture unavailable.");
        }));
        Assert.Equal(new[] { ReferenceCapturePowerLease.Required, ReferenceCapturePowerLease.Continuous }, calls);
    }

    [Fact]
    public void AnotherThreadCannotClearTheOwnersRequest()
    {
        var calls = new List<uint>(); Exception? failure = null;
        using var lease = new ReferenceCapturePowerLease(flags => { calls.Add(flags); return ReferenceCapturePowerLease.Continuous; });
        var worker = new Thread(() => { try { lease.Dispose(); } catch (Exception error) { failure = error; } });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Single(calls);
        lease.Dispose(); Assert.True(lease.ReleaseSucceeded);
    }
}
