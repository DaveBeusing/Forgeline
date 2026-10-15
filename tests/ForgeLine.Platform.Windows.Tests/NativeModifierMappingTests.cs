using Xunit;

namespace ForgeLine.Platform.Windows.Tests;

public sealed class NativeModifierMappingTests
{
    [Theory]
    [InlineData(0x10, 0x002A0000, PlatformKey.LeftShift)]
    [InlineData(0x10, 0x00360000, PlatformKey.RightShift)]
    [InlineData(0x10, 0xC0360000, PlatformKey.RightShift)]
    [InlineData(0x11, 0x001D0000, PlatformKey.LeftControl)]
    [InlineData(0x11, 0x011D0000, PlatformKey.RightControl)]
    [InlineData(0xA0, 0, PlatformKey.LeftShift)]
    [InlineData(0xA1, 0, PlatformKey.RightShift)]
    [InlineData(0x70, 0, PlatformKey.F1)]
    public void NativeKeyMessageMapsModifierSides(int virtualKey, long keyData, PlatformKey expected)
    {
        Assert.True(WindowsWindow.TryMapKey((nuint)virtualKey, (nint)keyData, out var actual));
        Assert.Equal(expected, actual);
    }
}
