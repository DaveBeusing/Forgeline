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

    private static (DredAllocationType[] Types, bool Truncated) AllocationTypes(DredAllocationNode? node)
    {
        var types = new List<DredAllocationType>(16);
        while (node is not null && types.Count < 16) { types.Add(node.AllocationType); node = node.Next; }
        return (types.ToArray(), node is not null);
    }

    private void CaptureDredFailure()
    {
        if (!_dredEnabled || _dredCaptured) return;
        _dredCaptured = true;
        try
        {
            using ID3D12DeviceRemovedExtendedData? data = _device.QueryInterfaceOrNull<ID3D12DeviceRemovedExtendedData>();
            if (data is null) { Console.Error.WriteLine("[graphics:dred] available=false"); return; }
            var breadcrumbs = data.GetAutoBreadcrumbsOutput(out DredAutoBreadcrumbsOutput breadcrumbData);
            var pageFault = data.GetPageFaultAllocationOutput(out DredPageFaultOutput faultData);
            var nodes = new List<DredBreadcrumbSummary>(16);
            AutoBreadcrumbNode? node = breadcrumbs.Success ? breadcrumbData.HeadAutoBreadcrumbNode : null;
            while (node is not null && nodes.Count < 16)
            {
                var window = DredOperationWindow.Capture(node.CommandHistory ?? [], node.LastBreadcrumbValue);
                nodes.Add(new(node.BreadcrumbCount, node.LastBreadcrumbValue, window.Start, window.Operations));
                node = node.Next;
            }
            var existing = AllocationTypes(pageFault.Success ? faultData.HeadExistingAllocationNode : null);
            var freed = AllocationTypes(pageFault.Success ? faultData.HeadRecentFreedAllocationNode : null);
            // Only bounded numeric metadata; never serialize native objects, names or addresses.
            Console.Error.WriteLine("[graphics:dred] " + System.Text.Json.JsonSerializer.Serialize(new
            {
                breadcrumbStatus = breadcrumbs.Code, pageFaultStatus = pageFault.Code,
                pageFaultReported = pageFault.Success && faultData.PageFaultVA != 0,
                breadcrumbs = nodes, breadcrumbsTruncated = node is not null,
                existingAllocationTypes = existing.Types, existingAllocationsTruncated = existing.Truncated,
                freedAllocationTypes = freed.Types, freedAllocationsTruncated = freed.Truncated
            }));
        }
        catch (SharpGen.Runtime.SharpGenException exception)
        {
            Console.Error.WriteLine($"[graphics:dred] available=false status=0x{exception.ResultCode.Code:X8}");
        }
    }
}

internal readonly record struct DredBreadcrumbSummary(int Recorded, int? Completed, int HistoryWindowStart, AutoBreadcrumbOp[] Operations);

internal static class DredOperationWindow
{
    internal static (int Start, AutoBreadcrumbOp[] Operations) Capture(ReadOnlySpan<AutoBreadcrumbOp> history, int? completed)
    {
        int point = Math.Clamp(completed ?? history.Length, 0, history.Length);
        int start = Math.Clamp(point - 8, 0, Math.Max(0, history.Length - 16));
        return (start, history.Slice(start, Math.Min(16, history.Length - start)).ToArray());
    }
}
