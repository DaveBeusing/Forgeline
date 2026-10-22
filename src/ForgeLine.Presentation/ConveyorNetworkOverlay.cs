using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class ConveyorNetworkOverlay
{
    public static void Draw(DebugDraw draw, PresentationSnapshot? snapshot,
        IReadOnlyCollection<EntityId> selection, EntityId hovered)
    {
        if(snapshot?.Conveyors is not {} network || network.Tick!=snapshot.Tick) return;
        var focus=selection.Take(32).ToHashSet();
        if(hovered.IsValid)focus.Add(hovered);
        foreach(var row in network.Pieces.Where(p=>focus.Contains(p.Entity)))
        {
            if(row.Flow is not {} flow)continue;
            draw.Enabled=true;
            Vector4 color=row.Status==ConveyorLinkStatus.Flowing?new(.2f,1,.5f,.8f):new(1,.7f,.1f,.8f);
            draw.Box(row.Piece.Placement.Bounds,color);
            Vector3 center=row.Piece.Placement.Bounds.Center+Vector3.UnitY*.6f;
            draw.Label(center,FormattableString.Invariant($"{row.Status} | {flow.OutputPerSecond:0.#}/s | {row.Piece.BufferedQuantity:0.#}/{flow.Capacity}"),color);
            foreach(EntityId target in new[]{flow.OutputA,flow.OutputB})
            {
                if(!target.IsValid)continue;
                var next=network.Pieces.FirstOrDefault(p=>p.Entity==target);
                Vector3? position=next.Entity.IsValid?next.Piece.Placement.Bounds.Center:
                    network.Ports.Where(p=>p.Building==target && p.Input).Select(p=>(Vector3?)p.Position).FirstOrDefault();
                if(position is {} end)draw.Line(center,end+Vector3.UnitY*.6f,color);
            }
        }
    }
}
