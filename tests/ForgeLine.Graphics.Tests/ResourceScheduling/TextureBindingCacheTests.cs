using Xunit;

namespace ForgeLine.Graphics.Tests.ResourceScheduling;

public sealed class TextureBindingCacheTests
{
    [Fact]
    public void IdentityAndResetProtectRecycledDescriptorsAndNewRoots()
    {
        var cache = new TextureBindingCache();
        object original = new(), replacement = new();
        cache.Remember(0, original); cache.Remember(15, original);
        Assert.True(cache.Matches(0, original));
        Assert.False(cache.Matches(0, replacement));
        cache.Remember(0, replacement);
        Assert.False(cache.Matches(0, original));
        Assert.True(cache.Matches(15, original));
        cache.Reset();
        Assert.False(cache.Matches(0, replacement));
        Assert.False(cache.Matches(15, original));
    }
}
