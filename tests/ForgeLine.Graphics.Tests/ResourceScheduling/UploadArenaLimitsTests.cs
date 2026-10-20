using Xunit;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class UploadArenaLimitsTests
{
    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 512UL)]
    [InlineData(511UL, 512UL)]
    [InlineData(512UL, 512UL)]
    [InlineData(513UL, 1024UL)]
    public void PlacementIs512ByteAligned(ulong input, ulong expected) => Assert.Equal(expected, UploadArenaLimits.Align(input));

    [Fact]
    public void CapacityAndTransferCountAreIndependentlyBounded()
    {
        Assert.Equal(UploadArenaLimits.NormalBytes, UploadArenaLimits.Capacity(1));
        Assert.Equal(UploadArenaLimits.MaximumTransferBytes, UploadArenaLimits.Capacity(UploadArenaLimits.MaximumTransferBytes));
        Assert.Throws<ArgumentOutOfRangeException>(() => UploadArenaLimits.Capacity(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UploadArenaLimits.Capacity(UploadArenaLimits.MaximumTransferBytes + 1));
        Assert.Throws<OverflowException>(() => UploadArenaLimits.Align(ulong.MaxValue));
        Assert.True(UploadArenaLimits.CanAppend(512, 1024, 512, 31));
        Assert.False(UploadArenaLimits.CanAppend(513, 1024, 1, 0));
        Assert.False(UploadArenaLimits.CanAppend(0, 1024, 1, 32));
        Assert.False(UploadArenaLimits.CanAppend(1024, 1024, 1, 0));
        Assert.Equal(140UL * 1024 * 1024, UploadArenaLimits.MaximumResidentBytes);
    }

    [Theory]
    [InlineData(0UL, 0UL, true)]
    [InlineData(7UL, 6UL, false)]
    [InlineData(7UL, 7UL, true)]
    [InlineData(7UL, 8UL, true)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, false)]
    public void UnknownOrIncompleteSubmissionCannotReset(ulong submitted, ulong completed, bool reset) =>
        Assert.Equal(reset, UploadArenaLimits.CanReset(submitted, completed));
}
