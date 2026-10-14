using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum DecalKind { None, TireTrack, TrackedVehicle, Crater, Scorch, Hit }
public enum DecalSurface { Ground, Receiver }

public sealed record DecalSettings(int Capacity = 512, int MaximumPerChunk = 16,
    int MaximumPublished = 128, float ChunkSizeMeters = 256)
{
    public void Validate()
    {
        if (Capacity is < 1 or > 4096 || MaximumPerChunk is < 1 or > 64 ||
            MaximumPublished < 1 || MaximumPublished > Capacity ||
            !float.IsFinite(ChunkSizeMeters) || ChunkSizeMeters is < 16 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(Capacity), "Decal storage and publication must be bounded.");
    }
}

/// <summary>Ground transform is world-space; object rotation is receiver-local and its anchor is normalized to receiver scale.</summary>
public readonly record struct DecalRequest(DecalKind Kind, DecalSurface Surface, RenderTransform Transform,
    uint LifetimeTicks = 1200, float Strength = .5f, EntityId Receiver = default, Vector3 ReceiverLocalAnchor = default)
{
    public void Validate()
    {
        if (Kind == DecalKind.None || !Enum.IsDefined(Kind) || !Enum.IsDefined(Surface) ||
            !Finite(Transform.Position) || !Finite(Transform.Scale) || !Finite(ReceiverLocalAnchor) ||
            MathF.Abs(Transform.Position.X) > 16_777_216 || MathF.Abs(Transform.Position.Y) > 16_777_216 || MathF.Abs(Transform.Position.Z) > 16_777_216 ||
            Transform.Scale.X is <= 0 or > 32 || Transform.Scale.Y is <= 0 or > 32 || Transform.Scale.Z is <= 0 or > 32 ||
            !float.IsFinite(Transform.Rotation.LengthSquared()) || MathF.Abs(Transform.Rotation.LengthSquared() - 1) > .001f ||
            LifetimeTicks is < 1 or > 6000 || !float.IsFinite(Strength) || Strength is < 0 or > .65f ||
            (Surface == DecalSurface.Receiver) != Receiver.IsValid || ReceiverLocalAnchor.LengthSquared() > 4)
            throw new ArgumentOutOfRangeException(nameof(Transform), "Decals require finite bounded footprints, lifetime and receiver identity.");
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct DecalHandle(int Slot, uint Generation);
public readonly record struct DecalFeaturePresentationMetadata(DecalKind Kind, float Opacity, DecalSurface Surface = DecalSurface.Ground)
{
    public bool IsSpecified => Kind != DecalKind.None;
}
public readonly record struct DecalPresentationMetrics(int Active, int Capacity, int Published, int Chunks,
    ulong Spawned, ulong Dropped, ulong Expired, ulong Retired, ulong Migrated);

/// <summary>Only currently authorized receiver poses may be returned; an unavailable receiver retires its marks.</summary>
public interface IDecalVisibility
{
    bool IsGroundVisible(Vector3 position);
    bool TryGetReceiver(EntityId receiver, out RenderTransform transform);
}

public sealed class DecalPool
{
    private readonly DecalSettings _settings;
    private readonly Slot[] _slots;
    private readonly Dictionary<Cell, int> _cells;
    private int _active, _published, _search;
    private ulong _spawned, _dropped, _expired, _retired, _migrated;
    private ulong? _lastTick;

    public DecalPool(DecalSettings? settings = null)
    {
        _settings = settings ?? new(); _settings.Validate();
        _slots = new Slot[_settings.Capacity]; _cells = new(_settings.Capacity);
    }

    public int MaximumPublished => _settings.MaximumPublished;
    public DecalPresentationMetrics Metrics => new(_active, _slots.Length, _published, _cells.Count,
        _spawned, _dropped, _expired, _retired, _migrated);

    public void Reset()
    {
        // Keep generations so a cancelled handle cannot address a later session's slot.
        for (int i = 0; i < _slots.Length; i++) _slots[i].Active = false;
        _cells.Clear(); _active = _published = _search = 0; _lastTick = null;
        _spawned = _dropped = _expired = _retired = _migrated = 0;
    }

    public void BeginTick(SimulationTick tick)
    {
        if (_lastTick is { } previous && tick.Value < previous) Reset();
        _lastTick = tick.Value; _published = 0;
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i].Active && tick.Value >= _slots[i].Expires) { Remove(i); _expired++; }
    }

    public bool TrySpawn(in DecalRequest request, SimulationTick tick, out DecalHandle handle)
    {
        request.Validate(); handle = default;
        ulong expires = checked(tick.Value + request.LifetimeTicks);
        if (_lastTick != tick.Value) BeginTick(tick);
        Cell cell = CellAt(request.Transform.Position);
        if (_active == _slots.Length || Count(cell) >= _settings.MaximumPerChunk) { _dropped++; return false; }
        for (int offset = 0; offset < _slots.Length; offset++)
        {
            int index = (_search + offset) % _slots.Length;
            if (_slots[index].Active) continue;
            uint generation = checked(_slots[index].Generation + 1);
            _slots[index] = new() { Active = true, Generation = generation, Request = request, Expires = expires, Cell = cell };
            _cells[cell] = Count(cell) + 1; _active++; _spawned++; _search = (index + 1) % _slots.Length;
            handle = new(index, generation); return true;
        }
        throw new InvalidOperationException("Decal occupancy is inconsistent.");
    }

    public bool Cancel(DecalHandle handle)
    {
        if ((uint)handle.Slot >= _slots.Length || !_slots[handle.Slot].Active || _slots[handle.Slot].Generation != handle.Generation) return false;
        Remove(handle.Slot); _retired++; return true;
    }

    public int CopyVisibleTo(Span<RenderInstance> destination, SimulationTick tick, IDecalVisibility visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        if (destination.Length < MaximumPublished) throw new ArgumentException("Decal publication scratch is too small.", nameof(destination));
        BeginTick(tick);
        for (int index = 0; index < _slots.Length; index++)
        {
            ref Slot slot = ref _slots[index];
            if (!slot.Active) continue;
            DecalRequest request = slot.Request;
            RenderTransform transform = request.Transform;
            if (request.Surface == DecalSurface.Receiver)
            {
                if (!visibility.TryGetReceiver(request.Receiver, out RenderTransform receiver) || !Finite(receiver)) { Remove(index); _retired++; continue; }
                transform = transform with
                {
                    Position = receiver.Position + Vector3.Transform(request.ReceiverLocalAnchor * receiver.Scale, receiver.Rotation),
                    Rotation = Quaternion.Normalize(receiver.Rotation * transform.Rotation)
                };
                if (!Finite(transform)) { Remove(index); _retired++; continue; }
                Cell cell = CellAt(transform.Position);
                if (cell != slot.Cell)
                {
                    if (Count(cell) >= _settings.MaximumPerChunk) { Remove(index); _retired++; continue; }
                    Decrement(slot.Cell); _cells[cell] = Count(cell) + 1; slot.Cell = cell; _migrated++;
                }
            }
            if (request.Surface == DecalSurface.Ground && !visibility.IsGroundVisible(transform.Position) || _published == MaximumPublished) continue;
            float remaining = (float)(slot.Expires - tick.Value);
            float fade = Math.Clamp(remaining / Math.Max(1, request.LifetimeTicks * .2f), 0, 1);
            WorldVisualId visual = request.Kind switch
            {
                DecalKind.TireTrack => WorldVisualId.DecalTireTracks,
                DecalKind.TrackedVehicle => WorldVisualId.DecalTrackedVehicleMarks,
                DecalKind.Crater => WorldVisualId.DecalShellImpact,
                DecalKind.Scorch => WorldVisualId.DecalScorchMark,
                _ => WorldVisualId.DecalBlastMark
            };
            uint identity = checked(0xD000_0000u + (uint)index);
            destination[_published++] = new(new EntityId(identity, slot.Generation), transform, new(5000u + (uint)visual),
                RenderMaterialHandle.Default, RenderVisibilityMask.World, identity,
                WorldFeature: new(visual, WorldPresentationKind.Decal, default, false),
                DecalFeature: new(request.Kind, request.Strength * fade, request.Surface));
        }
        return _published;
    }

    private Cell CellAt(Vector3 position) => new(checked((int)MathF.Floor(position.X / _settings.ChunkSizeMeters)),
        checked((int)MathF.Floor(position.Z / _settings.ChunkSizeMeters)));
    private static bool Finite(in RenderTransform transform) =>
        float.IsFinite(transform.Position.X) && float.IsFinite(transform.Position.Y) && float.IsFinite(transform.Position.Z) &&
        MathF.Abs(transform.Position.X) <= 16_777_216 && MathF.Abs(transform.Position.Y) <= 16_777_216 && MathF.Abs(transform.Position.Z) <= 16_777_216 &&
        float.IsFinite(transform.Scale.X) && float.IsFinite(transform.Scale.Y) && float.IsFinite(transform.Scale.Z) &&
        float.IsFinite(transform.Rotation.LengthSquared()) && MathF.Abs(transform.Rotation.LengthSquared() - 1) <= .001f;
    private int Count(Cell cell) => _cells.TryGetValue(cell, out int count) ? count : 0;
    private void Decrement(Cell cell) { int count = Count(cell); if (count == 1) _cells.Remove(cell); else _cells[cell] = count - 1; }
    private void Remove(int index) { Decrement(_slots[index].Cell); _slots[index].Active = false; _active--; }
    private readonly record struct Cell(int X, int Z);
    private struct Slot
    {
        public bool Active;
        public uint Generation;
        public DecalRequest Request;
        public ulong Expires;
        public Cell Cell;
    }
}
