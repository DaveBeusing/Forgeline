using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class UiFontMetricsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredLanguageAndSymbolsHaveMeasuredVisibleGlyphs(bool bold)
    {
        var font = UiFontMetrics.Default;
        foreach (char cp in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789ÄÖÜäöüß%→←↑↓…–—")
        {
            Assert.True(font.Contains(cp, bold), $"Missing {cp}");
            Assert.True(font.Measure(cp.ToString(), 12, bold) > 0);
        }
        Assert.True(font.Glyph(0x1f600, bold, out bool missing).Width > 0); Assert.True(missing);
        Assert.Equal(font.Measure("ÄÖÜ", 18, bold), font.Measure("A\u0308O\u0308U\u0308", 18, bold));
        Assert.True(font.Measure("WWW", 18, bold) > font.Measure("iii", 18, bold) * 2);
        Assert.True(font.Measure("AV", 18, bold) < font.Measure("A", 18, bold) + font.Measure("V", 18, bold));
    }
    [Fact]
    public void FitWrapAndScalePreserveMeasuredBoundsAndScalarBoundaries()
    {
        var font = UiFontMetrics.Default;
        const string value = "Long ÄÖÜ label with a visible arrow → and a very long description";
        string fitted = font.Fit(value, 120, 16);
        Assert.EndsWith("…", fitted); Assert.True(font.Measure(fitted, 16) <= 120);
        Assert.Equal(value, font.Fit(value, 2000, 16));
        foreach (string line in font.Wrap(value, 120, 16)) Assert.True(font.Measure(line, 16) <= 120);
        Assert.Equal(font.Measure(value, 16) * 2, font.Measure(value, 32), 3);
        Assert.Equal(font.Measure("1234567890", 12), font.Measure("0000000000", 12));
        Assert.True(font.Measure("one\ntwo", 12) < font.Measure("onetwo", 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Measure("x", float.NaN));
        Assert.All(font.Wrap("WWW", 1, 16), line => Assert.True(font.Measure(line, 16) <= 1));
        Assert.InRange(font.Wrap(new string('\n', 1000), 120, 16).Length, 1, 256);
    }
    [Fact]
    public void WarmMetricsDoNotAllocate()
    {
        var font = UiFontMetrics.Default; for (int i = 0; i < 64; i++) _ = font.Measure("AV ÄÖÜ 12345 % →", 12);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) _ = font.Measure("AV ÄÖÜ 12345 % →", 12);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
