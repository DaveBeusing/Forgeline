using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>Bounded, presentation-only world lines. Never dispatches orders or exposes foreign zones.</summary>
public static class LogisticsZoneOverlay
{
    public const int MaximumVisibleStrokes = 512;
    private static readonly Vector4 LoadingColor = new(.92f, .71f, .23f, .78f);
    private static readonly Vector4 HoldingColor = new(.25f, .78f, .82f, .75f);
    private static readonly Vector4 BlockedColor = new(.94f, .42f, .27f, .85f);

    public static int Draw(DebugDraw draw, PresentationSnapshot snapshot,
        IReadOnlyList<EntityId> selected, EntityId hovered = default, bool logisticsMode = false,
        int maximumStrokes = MaximumVisibleStrokes)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStrokes);
        if (!draw.Enabled || snapshot.DockingZones is null ||
            snapshot.PlayerExperience is not { IsMatchComplete: false } experience ||
            experience.Tick != snapshot.Tick || !snapshot.SessionId.IsSpecified) return 0;
        if (snapshot.Hover is not { } hover || hover.Tick != snapshot.Tick ||
            hover.SessionId != snapshot.SessionId || hover.Category != PlayerHoverCategory.Building)
            hovered = default;
        else if (hovered != hover.Entity) hovered = default;

        int emitted = 0;
        foreach (LogisticsZoneStroke stroke in snapshot.DockingZones.Strokes)
        {
            if (emitted >= Math.Min(maximumStrokes, MaximumVisibleStrokes)) break;
            bool relevant = logisticsMode || stroke.Building == hovered || stroke.Vehicle == hovered;
            if (!relevant)
                for (int i = 0; i < selected.Count; i++)
                    if (selected[i] == stroke.Building || selected[i] == stroke.Vehicle)
                    { relevant = true; break; }
            if (!relevant) continue;
            Vector4 color = stroke.Unavailable ? BlockedColor :
                stroke.Kind == LogisticsZoneKind.Loading ? LoadingColor : HoldingColor;
            draw.Line(stroke.Start, stroke.End, color);
            emitted++;
        }
        return emitted;
    }
}
