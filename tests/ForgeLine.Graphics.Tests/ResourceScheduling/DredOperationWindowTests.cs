using Vortice.Direct3D12;
using Xunit;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class DredOperationWindowTests
{
    [Theory]
    [InlineData(null, 48)]
    [InlineData(10, 2)]
    [InlineData(-100, 0)]
    [InlineData(10000, 48)]
    public void OperationWindowIsBoundedAroundTheReportedCompletion(int? completed, int expectedStart)
    {
        AutoBreadcrumbOp[] input = Enumerable.Range(0, 64).Select(value => (AutoBreadcrumbOp)value).ToArray();
        var window = DredOperationWindow.Capture(input, completed);
        Assert.Equal(expectedStart, window.Start);
        Assert.Equal(input.AsSpan(expectedStart, 16).ToArray(), window.Operations);
        Array.Clear(input);
        Assert.Equal((AutoBreadcrumbOp)expectedStart, window.Operations[0]);
    }

    [Fact]
    public void EmptyHistoryRemainsAnEmptyAvailableWindow()
    {
        var window = DredOperationWindow.Capture([], null);
        Assert.Equal(0, window.Start); Assert.Empty(window.Operations);
    }
}
