using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12DescriptorAllocatorTests
{
    [Fact]
    public void ReleasedDescriptorIsReusedWithoutLeakingCapacity()
    {
        var allocator = new D3D12DescriptorAllocator(2);

        int first = allocator.Allocate();
        int second = allocator.Allocate();

        Assert.Equal(2, allocator.UsedCount);
        Assert.Throws<GraphicsDeviceException>(() => allocator.Allocate());

        allocator.Release(first);

        int recycled = allocator.Allocate();

        Assert.Equal(first, recycled);
        Assert.Equal(2, allocator.UsedCount);

        allocator.Release(second);
        allocator.Release(recycled);
        Assert.Equal(0, allocator.UsedCount);
    }

    [Fact]
    public void DoubleReleaseFailsClearly()
    {
        var allocator = new D3D12DescriptorAllocator(1);

        int descriptor = allocator.Allocate();
        allocator.Release(descriptor);

        Assert.Throws<InvalidOperationException>(
            () => allocator.Release(descriptor));
    }
}
