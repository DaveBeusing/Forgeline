using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class RuntimeSkinBinding
{
    private static readonly string[] Names = ["idle","walk","run","aim","fire","reload","hit","suppression","death"];
    private readonly BoundClip?[] _clips = new BoundClip?[Names.Length];
    internal RuntimeSkinBinding(RuntimeSkinData data, IEnumerable<RuntimeAnimationData> clips)
    {
        Data = data;
        foreach (var clip in clips)
        {
            int action = Array.IndexOf(Names, clip.Name);
            if (action < 0 || _clips[action] is not null) throw new InvalidDataException("Skin clip names must uniquely identify supported infantry actions.");
            _clips[action] = new(clip, data.Bind(clip));
        }
        if (_clips.Any(c => c is null)) throw new InvalidDataException("Infantry skins require idle/walk/run/aim/fire/reload/hit/suppression/death clips.");
    }
    internal RuntimeSkinData Data { get; }
    internal BoundClip Get(InfantryAction action) => (uint)action < _clips.Length ? _clips[(int)action]! : throw new ArgumentOutOfRangeException(nameof(action));
    internal sealed record BoundClip(RuntimeAnimationData Clip, int[] Nodes);
}

/// <summary>Frame-index-owned upload vertices; the D3D12 owner waits for that slot's fence before reuse.</summary>
internal sealed class SkinFrameCache(IGraphicsDevice graphics) : IDisposable
{
    internal const int MaximumVariants = 16, MaximumFrameSlots = 4;
    internal const long MaximumSlotBytes = 16 * 1024 * 1024;
    private readonly Dictionary<int, Slot> _frames = [];
    private Slot? _current;
    private int _lastIndex = -1;
    private bool _afterShadow;
    public int EvaluatedPoses { get; private set; }
    public int StaticFallbacks { get; private set; }
    // EvaluatedPoses retains the existing admitted-variant diagnostic contract.
    // These counters distinguish actual CPU deformation and upload work from reuse.
    public int DeformationEvaluations { get; private set; }
    public int ReusedPoses { get; private set; }
    public int ReusedUploads { get; private set; }
    public long ResidentBytes => _frames.Values.Sum(slot => slot.Bytes);
    public void Begin(int frameIndex, bool shadow)
    {
        if ((uint)frameIndex >= MaximumFrameSlots) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (!_frames.TryGetValue(frameIndex, out var slot)) { slot = new(); _frames.Add(frameIndex, slot); }
        if (shadow || !_afterShadow || _lastIndex != frameIndex)
        {
            slot.Lookup.Clear(); slot.Used = 0; EvaluatedPoses = 0; StaticFallbacks = 0;
            DeformationEvaluations = ReusedPoses = ReusedUploads = 0;
        }
        _current = slot; _lastIndex = frameIndex; _afterShadow = shadow;
    }
    public RuntimeMeshBuffers Resolve(AssetId meshId, RuntimeMeshBuffers mesh, InfantryAnimationPose pose, float detail, out int variant)
    {
        variant = -1;
        if (_current is not { } slot || mesh.Skin is not { } binding || !pose.IsSpecified || detail <= 0) return mesh;
        var bound = binding.Get(pose.Action);
        float time = pose.Loop ? bound.Clip.LoopTime(pose.Phase) : bound.Clip.Duration * (float)Math.Clamp(pose.Phase, 0, 1);
        int frame = (int)MathF.Floor(time * 24), blend = Math.Clamp((int)MathF.Round(detail * 3), 0, 3);
        if (blend == 0) return mesh;
        var key = new Key(binding, meshId, pose.Action, frame, blend);
        if (slot.Lookup.TryGetValue(key, out int cached)) { variant = cached; return mesh with { VertexBuffer = slot.Pool[cached].Buffer!, MechanicalParts = null }; }
        if (slot.Used >= MaximumVariants) { StaticFallbacks++; return mesh; }
        int count = binding.Data.Mesh.Vertices.Count; ulong bytes = checked((ulong)count * RuntimeMeshData.VertexStride);
        // Only unused entries may move: active batch indices and in-flight frame
        // slots keep their original buffers. Reading another slot's CPU copy is safe.
        for (int index = slot.Used; index < slot.Pool.Count; index++)
        {
            if (slot.Pool[index].Content != key) continue;
            (slot.Pool[slot.Used], slot.Pool[index]) = (slot.Pool[index], slot.Pool[slot.Used]);
            break;
        }
        if (slot.Pool.Count == slot.Used) slot.Pool.Add(new());
        var upload = slot.Pool[slot.Used];
        if (upload.Content == key && upload.Buffer is not null)
        {
            variant = slot.Used++; slot.Lookup.Add(key, variant); EvaluatedPoses++;
            ReusedPoses++; ReusedUploads++;
            return mesh with { VertexBuffer = upload.Buffer, MechanicalParts = null };
        }
        Upload? retained = FindRetainedPose(key);
        upload.Content = null; long previous = upload.Buffer is null ? 0 : (long)upload.Buffer.Description.SizeInBytes;
        if (slot.Bytes - previous + Math.Max(previous, (long)bytes) > MaximumSlotBytes) { StaticFallbacks++; return mesh; }
        if (upload.Buffer is null || upload.Buffer.Description.SizeInBytes < bytes)
        {
            // This entry was not used in this frame; frame-slot fence ownership permits replacement.
            upload.Buffer?.Dispose(); upload.Buffer = graphics.CreateBuffer(new(bytes, GraphicsBufferMemory.Upload));
            slot.Bytes += (long)bytes - previous;
        }
        if (upload.Vertices.Length < count) upload.Vertices = new RuntimeMeshVertex[count];
        var vertices = upload.Vertices.AsSpan(0, count);
        if (retained is not null)
        {
            retained.Vertices.AsSpan(0, count).CopyTo(vertices);
            ReusedPoses++;
        }
        else
        {
            Span<Matrix4x4> palette = stackalloc Matrix4x4[RuntimeSkinData.MaximumJoints];
            binding.Data.Evaluate(bound.Clip, bound.Nodes, MathF.Min(bound.Clip.Duration, frame / 24f), palette);
            binding.Data.Deform(palette, vertices);
            if (blend < 3)
            {
                float weight = blend / 3f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var original = binding.Data.Mesh.Vertices[i]; var v = vertices[i];
                    Vector3 p = Vector3.Lerp(new(original.X,original.Y,original.Z), new(v.X,v.Y,v.Z), weight);
                    Vector3 n = Vector3.Lerp(new(original.NormalX,original.NormalY,original.NormalZ),new(v.NormalX,v.NormalY,v.NormalZ),weight);
                    if (n.LengthSquared() < 1e-12f) n = new(original.NormalX,original.NormalY,original.NormalZ);
                    n = Vector3.Normalize(n);
                    Vector3 t = Vector3.Lerp(new(original.TangentX,original.TangentY,original.TangentZ),new(v.TangentX,v.TangentY,v.TangentZ),weight);
                    t -= n * Vector3.Dot(n,t);
                    if (t.LengthSquared() < 1e-12f) t = Vector3.Cross(MathF.Abs(n.Y) < .9f ? Vector3.UnitY : Vector3.UnitX,n);
                    t = Vector3.Normalize(t);
                    vertices[i] = v with { X=p.X,Y=p.Y,Z=p.Z,NormalX=n.X,NormalY=n.Y,NormalZ=n.Z,TangentX=t.X,TangentY=t.Y,TangentZ=t.Z };
                }
            }
            DeformationEvaluations++;
        }
        upload.Buffer.SetData<RuntimeMeshVertex>(vertices);
        upload.Content = key;
        variant = slot.Used++; slot.Lookup.Add(key, variant); EvaluatedPoses++;
        return mesh with { VertexBuffer = upload.Buffer, MechanicalParts = null };
    }
    private Upload? FindRetainedPose(in Key key)
    {
        // At most four frame slots of sixteen variants. Reuse the already bounded
        // CPU arrays rather than creating a second, separately growing pose cache.
        foreach (Slot frame in _frames.Values)
            foreach (Upload upload in frame.Pool)
                if (upload.Content == key) return upload;
        return null;
    }
    public void Dispose()
    {
        foreach (var slot in _frames.Values) foreach (var upload in slot.Pool) upload.Buffer?.Dispose();
        _frames.Clear(); _current = null;
    }
    private readonly record struct Key(RuntimeSkinBinding Binding, AssetId Mesh, InfantryAction Action, int Frame, int Blend);
    private sealed class Slot
    {
        public Dictionary<Key,int> Lookup { get; } = new(MaximumVariants);
        public List<Upload> Pool { get; } = new(MaximumVariants);
        public int Used { get; set; }
        public long Bytes { get; set; }
    }
    private sealed class Upload
    {
        public IGraphicsBuffer? Buffer { get; set; }
        public Key? Content { get; set; }
        public RuntimeMeshVertex[] Vertices { get; set; } = [];
    }
}
