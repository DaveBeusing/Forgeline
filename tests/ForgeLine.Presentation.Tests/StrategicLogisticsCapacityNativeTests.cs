using System.Numerics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class StrategicLogisticsCapacityNativeTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    [Theory]
    [InlineData(1920,1080,96)]
    [InlineData(1280,720,192)]
    public void NativeOverlayIsRepeatableFilteredAndWithinVertexBudgets(int width,int height,int dpi)
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Logistics Capacity",width,height));
        using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new(){ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false});
        using var renderer=new RtsInformationOverlayRenderer(graphics);
        var camera=SameTypeSelectionTests.Camera();
        var rows=new List<StrategicLogisticsCapacityItem>();
        for(int network=0;network<3;network++)
            for(int i=0;i<6;i++)
            {
                var start=new Vector3((i-3)*22,0,(network-1)*45);
                var row=StrategicLogisticsCapacityTests.Row(new((uint)(network*10+i+1),1),start) with
                {Network=(StrategicLogisticsNetwork)network,State=i==0?StrategicLogisticsState.Disconnected:i==1?StrategicLogisticsState.Congested:StrategicLogisticsState.Unknown,
                    OutputPerSecond=network==0?i*4:null,CapacityPerSecond=network<2?100:null,BufferedQuantity=network==0?i*8:null,BufferCapacity=network==0?200:null,
                    ScheduledUtilization=network==1?(i==1?1:i*.2):null,ConveyorReason=network==0&&i==0?ConveyorBlockReason.DisconnectedPort:null};
                rows.Add(row);
                if(i>0)rows.Add(row with{Destination=new((uint)(network*10+i),1),End=start-new Vector3(22,0,0)});
            }
        var results=new List<object>();
        foreach(var mode in new[]{StrategicOverlayMode.None,StrategicOverlayMode.Logistics,StrategicOverlayMode.Conveyors,StrategicOverlayMode.Cargo,StrategicOverlayMode.FieldLogistics})
        {
            var snapshot=StrategicLogisticsCapacityTests.Snapshot(rows.ToArray(),mode);
            var view=RtsInformationLayerView.Empty with{OverlayMode=mode};
            byte[] first=Capture();Assert.Equal(first,Capture());
            if(mode==StrategicOverlayMode.None)Assert.Equal(0,renderer.LastLogisticsCapacityItems);else Assert.InRange(renderer.LastLogisticsCapacityItems,1,StrategicLogisticsCapacityOverlay.MaximumProjected);
            Assert.Equal(0,renderer.TextDiagnostics.MissingGlyphs);Assert.Equal(0,renderer.TextDiagnostics.DroppedGlyphs);
            Assert.InRange(renderer.LastRenderedVertexCount,1,262144);
            string? output=Environment.GetEnvironmentVariable("FORGELINE_LOGISTICS_NATIVE_OUTPUT");
            string key=$"capacity-{width}x{height}-{dpi}dpi-{mode}";
            if(!string.IsNullOrWhiteSpace(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,key+".rgba"),first);}
            results.Add(new{Mode=mode.ToString(),width,height,dpi,renderer.LastLogisticsCapacityItems,renderer.LastLogisticsCapacityOmitted,renderer.LastRenderedVertexCount,renderer.TextDiagnostics});
            byte[] Capture()=>graphics.CaptureFrame(GraphicsColor.ForgeLineClear,c=>renderer.Render(c,camera,snapshot,
                new(new(-150,0,-150),new(150,20,150)),view,CombatGroupOverviewView.Empty,(uint)dpi,1,default,default));
        }
        Assert.Equal(0,graphics.Diagnostics.Health.SubmissionFaultCount);
        string? report=Environment.GetEnvironmentVariable("FORGELINE_LOGISTICS_NATIVE_OUTPUT");
        if(!string.IsNullOrWhiteSpace(report))File.WriteAllText(Path.Combine(report,$"native-{width}x{height}-{dpi}dpi.json"),JsonSerializer.Serialize(results,ReportOptions));
    }
}
