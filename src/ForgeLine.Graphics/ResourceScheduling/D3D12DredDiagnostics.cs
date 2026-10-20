using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private bool _dredEnabled;
    private bool _dredCaptured;
    internal bool DredEnabled => _dredEnabled;

    private void ConfigureDred()
    {
        if (Environment.GetEnvironmentVariable("FORGELINE_DRED") != "1") return;
        if (D3D12.D3D12GetDebugInterface(out ID3D12DeviceRemovedExtendedDataSettings? settings).Failure || settings is null)
        {
            Console.Error.WriteLine("[graphics:dred] requested=true supported=false");
            return;
        }
        using (settings)
        {
            settings.SetAutoBreadcrumbsEnablement(DredEnablement.ForcedOn);
            settings.SetPageFaultEnablement(DredEnablement.ForcedOn);
            _dredEnabled = true;
        }
        Console.Error.WriteLine("[graphics:dred] requested=true configured=true; process-wide diagnostics enabled before device creation");
    }

    private void CaptureDredFailure()
    {
        if (!_dredEnabled || _dredCaptured) return;
        _dredCaptured = true;
        try
        {
            using ID3D12DeviceRemovedExtendedData? data = _device.QueryInterfaceOrNull<ID3D12DeviceRemovedExtendedData>();
            if (data is null) { Console.Error.WriteLine("[graphics:dred] available=false"); return; }
            var breadcrumbs = data.GetAutoBreadcrumbsOutput(out _);
            var pageFault = data.GetPageFaultAllocationOutput(out _);
            // Bounded status telemetry only: never dump GPU addresses, memory or object names.
            Console.Error.WriteLine($"[graphics:dred] breadcrumbStatus=0x{breadcrumbs.Code:X8} pageFaultStatus=0x{pageFault.Code:X8}");
        }
        catch (SharpGen.Runtime.SharpGenException exception)
        {
            Console.Error.WriteLine($"[graphics:dred] available=false status=0x{exception.ResultCode.Code:X8}");
        }
    }
}
