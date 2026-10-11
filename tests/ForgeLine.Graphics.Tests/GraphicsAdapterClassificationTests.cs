using Vortice.DXGI;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class GraphicsAdapterClassificationTests
{
    [Theory]
    [InlineData(AdapterFlags.Software, 0x10deu, 1u, true)]
    [InlineData(AdapterFlags.None, 0x1414u, 0x8cu, true)]
    [InlineData(AdapterFlags.Remote, 0x1414u, 0x8cu, true)]
    [InlineData(AdapterFlags.None, 0x1414u, 0x8eu, false)]
    [InlineData(AdapterFlags.None, 0x8086u, 0x8cu, false)]
    [InlineData(AdapterFlags.None, 0x10deu, 1u, false)]
    public void BasicRenderDriverCannotMasqueradeAsHardware(AdapterFlags flags, uint vendor, uint device, bool software)
    {
        var description = new AdapterDescription1 { Flags = flags, VendorId = vendor, DeviceId = device };
        Assert.Equal(software, D3D12GraphicsDevice.IsSoftwareAdapter(description));
    }
}
