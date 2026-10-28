using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Platform;

namespace ForgeLine.Presentation;

public readonly record struct StrategicLogisticsProjectedItem(StrategicLogisticsCapacityItem Item, Vector2 Start, Vector2 End)
{
    public Vector2 Marker => (Start + End) * .5f;
}

/// <summary>Render-owner scratch. Spatial representatives retain their real facts; no averaged or invented rates.</summary>
public sealed class StrategicLogisticsCapacityOverlay
{
    public const int MaximumProjected = 240;
    private readonly StrategicLogisticsProjectedItem[] _items = new StrategicLogisticsProjectedItem[MaximumProjected];
    private readonly HashSet<(int, int, int, bool)> _cells = new(MaximumProjected);
    private int _count;
    public ReadOnlySpan<StrategicLogisticsProjectedItem> Items => _items.AsSpan(0, _count);
    public int Omitted { get; private set; }
    public void Build(PresentationSnapshot? snapshot, RtsCamera camera, in GameplayHudLayout layout, StrategicOverlayMode mode)
    {
        _count = Omitted = 0; _cells.Clear();
        if (!StrategicLogisticsCapacityFilters.IsLogistics(mode) && mode != StrategicOverlayMode.All || layout.SafeArea.IsEmpty ||
            StrategicLogisticsCapacitySnapshot.Resolve(snapshot, mode) is not { } data) return;
        int width = layout.ViewportWidth, height = layout.ViewportHeight;
        var matrix = camera.GetMatrices(width, height).ViewProjection;
        float cell = (camera.Distance >= 350 ? 64 : 24) * layout.Scale;
        Span<int> counts = stackalloc int[3];
        int perNetwork = mode == StrategicOverlayMode.All ? 32 : 80;
        // Keep failure evidence ahead of flowing and unknown samples in each screen bin.
        for (int priority = 0; priority < 4; priority++)
            for (int i = 0; i < data.Items.Count; i++)
            {
                var item = data.Items[i];
                if (item.Priority != priority || !StrategicLogisticsCapacityFilters.Includes(mode, item.Network)) continue;
                if (!Project(item.Start + Vector3.UnitY, out var start) || !Project(item.End + Vector3.UnitY, out var end)) { Omitted++; continue; }
                var mid = (start + end) * .5f;
                var key = ((int)item.Network, (int)(mid.X / cell), (int)(mid.Y / cell), item.IsLink);
                if (counts[(int)item.Network] == perNetwork || _count == MaximumProjected || !_cells.Add(key)) { Omitted++; continue; }
                counts[(int)item.Network]++;
                _items[_count++] = new(item, start, end);
            }

        bool Project(Vector3 point, out Vector2 screen)
        {
            var clip = Vector4.Transform(new Vector4(point, 1), matrix);
            screen = default;
            if (!float.IsFinite(clip.W) || clip.W <= 0 || !float.IsFinite(clip.X) || !float.IsFinite(clip.Y) || !float.IsFinite(clip.Z)) return false;
            var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            if (ndc.Z < 0 || ndc.Z > 1 || MathF.Abs(ndc.X) > 1 || MathF.Abs(ndc.Y) > 1) return false;
            screen = new((ndc.X + 1) * .5f * width, (1 - ndc.Y) * .5f * height);
            return true;
        }
    }
    public bool TryPick(Vector2 position, float scale, out StrategicLogisticsCapacityItem item)
    {
        item = default;
        float nearest = 100 * scale * scale;
        bool found = false;
        foreach (var row in Items)
        {
            float distance = Vector2.DistanceSquared(row.Marker, position);
            if (distance >= nearest) continue;
            nearest = distance; item = row.Item; found = true;
        }
        return found;
    }
}

public static class StrategicLogisticsCapacityFilters
{
    public static bool IsLogistics(StrategicOverlayMode mode) => mode is StrategicOverlayMode.Logistics or
        StrategicOverlayMode.Conveyors or StrategicOverlayMode.Cargo or StrategicOverlayMode.FieldLogistics;
    public static StrategicOverlayMode Next(StrategicOverlayMode mode) => mode switch
    {
        StrategicOverlayMode.Logistics => StrategicOverlayMode.Conveyors,
        StrategicOverlayMode.Conveyors => StrategicOverlayMode.Cargo,
        StrategicOverlayMode.Cargo => StrategicOverlayMode.FieldLogistics,
        _ => StrategicOverlayMode.Logistics
    };
    public static bool Includes(StrategicOverlayMode mode, StrategicLogisticsNetwork network) => mode is StrategicOverlayMode.All or StrategicOverlayMode.Logistics ||
        mode == StrategicOverlayMode.Conveyors && network == StrategicLogisticsNetwork.Conveyor ||
        mode == StrategicOverlayMode.Cargo && network == StrategicLogisticsNetwork.Cargo ||
        mode == StrategicOverlayMode.FieldLogistics && network == StrategicLogisticsNetwork.Supply;
    public static string NetworkLabel(StrategicLogisticsNetwork network) => network switch
    { StrategicLogisticsNetwork.Conveyor => "BELT", StrategicLogisticsNetwork.Cargo => "CARGO", _ => "FIELD" };
}

/// <summary>Owned marker click opens existing Operations. It never creates a simulation request.</summary>
public sealed class StrategicLogisticsCapacityInteraction
{
    private readonly StrategicLogisticsCapacityOverlay _overlay = new();
    private ulong _press;
    private bool _armed, _owned;
    private EntityId _entity, _destination;
    private ForgeLine.Simulation.SimulationSessionId _session;
    private StrategicOverlayMode _mode;
    private GameplayHudLayout _layout;
    public bool Update(InputState input, PresentationSnapshot? snapshot, RtsCamera camera,
        in GameplayHudLayout layout, StrategicOverlayMode mode, bool blocked, out EntityId target)
    {
        target = default;
        ulong press = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = press != _press; _press = press;
        bool captured = _owned;
        if (blocked || input.FocusLostThisFrame || (!StrategicLogisticsCapacityFilters.IsLogistics(mode) && mode != StrategicOverlayMode.All) ||
            snapshot is null || _armed && (_session != snapshot.SessionId || _mode != mode || _layout != layout))
        { _armed = false; if (!input.IsMouseButtonDown(PlatformMouseButton.Left)) _owned = false; return captured; }
        _overlay.Build(snapshot, camera, layout, mode);
        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin))
        {
            _armed = _overlay.TryPick(origin, layout.Scale, out var row) && OperationsWorldOverlay.CanFocus(snapshot, row.Entity);
            if (_armed)
            {
                _entity = row.Entity; _destination = row.Destination; _session = snapshot.SessionId; _mode = mode; _layout = layout;
                captured = _owned = true;
            }
        }
        if (_armed && input.WasMouseButtonReleased(PlatformMouseButton.Left) && input.HasPointerPosition)
        {
            if (_overlay.TryPick(input.PointerPosition, layout.Scale, out var row) && row.Entity == _entity && row.Destination == _destination &&
                OperationsWorldOverlay.CanFocus(snapshot, row.Entity)) target = row.Entity;
            _armed = false;
        }
        if (!input.IsMouseButtonDown(PlatformMouseButton.Left)) { _owned = false; _armed = false; }
        return captured;
    }
}
