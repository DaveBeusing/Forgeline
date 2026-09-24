using ForgeLine.Platform;
using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class WindowConfigurationTests
{
    [Fact]
    public void ConstructorPreservesValidSettings()
    {
        var configuration = new WindowConfiguration(
            "FORGELINE",
            1920,
            1080,
            resizable: false,
            WindowMode.BorderlessFullscreen);

        Assert.Equal("FORGELINE", configuration.Title);
        Assert.Equal(1920, configuration.Width);
        Assert.Equal(1080, configuration.Height);
        Assert.False(configuration.Resizable);
        Assert.Equal(WindowMode.BorderlessFullscreen, configuration.Mode);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(-1, 720)]
    [InlineData(1280, 0)]
    [InlineData(1280, -1)]
    public void ConstructorRejectsInvalidClientDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WindowConfiguration("FORGELINE", width, height));
    }

    [Fact]
    public void ConstructorRejectsBlankTitle()
    {
        Assert.Throws<ArgumentException>(
            () => new WindowConfiguration(" ", 1280, 720));
    }
}
