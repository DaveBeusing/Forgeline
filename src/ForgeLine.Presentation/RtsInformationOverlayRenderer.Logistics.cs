using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public sealed partial class RtsInformationOverlayRenderer
{
    private readonly StrategicLogisticsCapacityOverlay _logisticsCapacity = new();
    private readonly HudRect[] _logisticsLabels = new HudRect[12];
    public int LastLogisticsCapacityItems { get; private set; }
    public int LastLogisticsCapacityOmitted { get; private set; }
    private void EmitLogisticsCapacity(RtsCamera camera, PresentationSnapshot snapshot, in RtsInformationLayerView view,
        in GameplayHudLayout layout, int width, int height)
    {
        _logisticsCapacity.Build(view.IsDragSelecting ? null : snapshot, camera, layout, view.OverlayMode);
        LastLogisticsCapacityItems = _logisticsCapacity.Items.Length;
        LastLogisticsCapacityOmitted = _logisticsCapacity.Omitted;
        float scale = layout.Scale;
        int labels = 0;
        Span<char> buffer = stackalloc char[180];
        foreach (var row in _logisticsCapacity.Items)
        {
            var item = row.Item;
            var color = item.Priority == 0 ? new Vector4(1,.35f,.2f,.95f) : item.Priority == 1 ? new(1,.75f,.25f,.95f) :
                item.State == StrategicLogisticsState.Unknown ? new(.7f,.75f,.8f,.85f) : new(.35f,.85f,.75f,.85f);
            if (item.IsLink)
            {
                int parts = item.Network == StrategicLogisticsNetwork.Conveyor ? 1 : 12;
                for (int i = 0; i < parts; i++)
                    EmitPixelLine(Vector2.Lerp(row.Start, row.End, (float)i / parts),
                        Vector2.Lerp(row.Start, row.End, (i + (parts == 1 ? 1 : item.Network == StrategicLogisticsNetwork.Cargo ? .65f : .25f)) / parts),
                        2 * scale, color, width, height);
            }
            var center = row.Marker;
            float radius = 5 * scale;
            if (item.Network == StrategicLogisticsNetwork.Conveyor)
            {
                EmitPixelLine(center + new Vector2(-radius, radius), center + new Vector2(0,-radius), 2 * scale, color, width, height);
                EmitPixelLine(center + new Vector2(0,-radius), center + new Vector2(radius,radius), 2 * scale, color, width, height);
                EmitPixelLine(center + new Vector2(-radius,radius), center + new Vector2(radius,radius), 2 * scale, color, width, height);
            }
            else if (item.Network == StrategicLogisticsNetwork.Cargo)
                EmitRectangleOutline(center-new Vector2(radius),center+new Vector2(radius),1.5f*scale,color,width,height);
            else
            {
                EmitPixelLine(center + new Vector2(0,-radius), center + new Vector2(radius,0), 2*scale,color,width,height);
                EmitPixelLine(center + new Vector2(radius,0), center + new Vector2(0,radius), 2*scale,color,width,height);
                EmitPixelLine(center + new Vector2(0,radius), center + new Vector2(-radius,0), 2*scale,color,width,height);
                EmitPixelLine(center + new Vector2(-radius,0), center + new Vector2(0,-radius), 2*scale,color,width,height);
            }
            if (item.Priority == 0)
            {
                EmitPixelLine(center-new Vector2(radius),center+new Vector2(radius),2*scale,color,width,height);
                EmitPixelLine(center+new Vector2(-radius,radius),center+new Vector2(radius,-radius),2*scale,color,width,height);
            }
            bool selected = view.SelectedEntities is { } selectedEntities && Array.IndexOf(selectedEntities, item.Entity) >= 0;
            if (labels >= 12 || item.IsLink && !selected || labels >= 8 && !selected) continue;
            HudRect label = default;
            float labelWidth = 235*scale, labelHeight=34*scale;
            for (int slot=0;slot<4;slot++)
            {
                float x = slot%2==0 ? center.X+10*scale : center.X-labelWidth-10*scale;
                float y = slot<2 ? center.Y-labelHeight-10*scale : center.Y+10*scale;
                var candidate = new HudRect(x,y,labelWidth,labelHeight);
                if(candidate.X<layout.SafeArea.X || candidate.Right>layout.SafeArea.Right || candidate.Y<layout.SafeArea.Y || candidate.Bottom>layout.SafeArea.Bottom ||
                    view.MinimapEnabled && Intersects(candidate,layout.Minimap))continue;
                bool overlap=false;for(int i=0;i<labels;i++)if(Intersects(candidate,_logisticsLabels[i])){overlap=true;break;}
                if(!overlap){label=candidate;break;}
            }
            if(label.IsEmpty)continue;
            _logisticsLabels[labels]=label;
            EmitQuad(label.X,label.Y,label.Width,label.Height,new(.025f,.035f,.04f,.88f),width,height);
            var text = new HudTextBuilder(buffer);
            text.Append(StrategicLogisticsCapacityFilters.NetworkLabel(item.Network)); text.Append(" "); text.Append(CapacityState(item));text.Append(" / OPS");
            EmitText(text.Written,label.X+3*scale,label.Y+2*scale,label.Right-3*scale,color,scale*.8f,width,height);
            text = new HudTextBuilder(buffer);
            if (item.Network == StrategicLogisticsNetwork.Conveyor)
            {
                text.Append("OUT "); if(item.OutputPerSecond is { } rate) text.Append(rate); else text.Append("N/A");
                text.Append("/s BUF "); if(item.BufferedQuantity is { } stock)text.Append(stock);else text.Append("N/A");
                text.Append("/"); if(item.BufferCapacity is { } cap)text.Append(cap);else text.Append("N/A");
            }
            else if (item.Network == StrategicLogisticsNetwork.Cargo)
            {
                text.Append("RESV "); if(item.ScheduledUtilization is { } load){text.Append(load*100);text.Append("%");}else text.Append("N/A");
                text.Append(" CAP ");if(item.CapacityPerSecond is { } cap)text.Append(cap);else text.Append("N/A");text.Append("/s Q N/A");
            }
            else text.Append("RATE/QUEUE N/A");
            text.Append(" DOWN N/A");
            EmitText(text.Written,label.X+3*scale,label.Y+17*scale,label.Right-3*scale,color,scale*.7f,width,height);
            labels++;
        }
    }
    private static int CountLogisticsSamples(StrategicLogisticsCapacitySnapshot? data,StrategicOverlayMode mode)
    {
        int count=0;if(data is not null)foreach(var item in data.Items)if(StrategicLogisticsCapacityFilters.Includes(mode,item.Network))count++;return count;
    }
    private static bool Intersects(HudRect a,HudRect b) => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;
    private static string CapacityState(in StrategicLogisticsCapacityItem item) => item.Network == StrategicLogisticsNetwork.Cargo && item.State == StrategicLogisticsState.Congested ? "! RESV SAT" : item.ConveyorReason switch
    {
        ConveyorBlockReason.DisconnectedPort => "X PORT", ConveyorBlockReason.DestinationFull => "! FULL",
        ConveyorBlockReason.ReservedStock => "RESERVED", ConveyorBlockReason.DestinationTargetCovered => "TARGET COVERED",
        ConveyorBlockReason.DestroyedSupport => "X SUPPORT", ConveyorBlockReason.NoPower => "X POWER",
        _ => item.State switch
        {
            StrategicLogisticsState.Unknown => "? UNKNOWN", StrategicLogisticsState.Connected => "CONNECTED",
            StrategicLogisticsState.Flowing => "FLOW", StrategicLogisticsState.NoInput => "NO INPUT",
            StrategicLogisticsState.Disconnected => "X DISCONNECTED", StrategicLogisticsState.Blocked => "X BLOCKED",
            StrategicLogisticsState.Congested => "! CONGESTED", StrategicLogisticsState.Damaged => "! DAMAGED",
            StrategicLogisticsState.Destroyed => "X DESTROYED", StrategicLogisticsState.Offline => "X OFF", _ => "? UNKNOWN"
        }
    };
}
