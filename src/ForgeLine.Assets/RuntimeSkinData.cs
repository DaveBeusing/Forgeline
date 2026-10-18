using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace ForgeLine.Assets;

public sealed record SkinJoint(string Name, int Parent, Vector3 Translation, Quaternion Rotation, Matrix4x4 InverseBind);
public readonly record struct SkinInfluence(int A, int B, int C, int D, Vector4 Weights);
public sealed record SkinSocket(string Name, int Joint, Vector3 BindPosition);
public sealed record SkinSocketSource(string Name, string Joint);

/// <summary>Immutable bounded skin. Clip sampling reuses the shared animation channel evaluator.</summary>
public sealed class RuntimeSkinData
{
    public const int CurrentVersion = 1, MaximumJoints = 48, MaximumVertices = 32768, MaximumIndices = 98304;
    public const float CullingRadius = 2;
    public const int MaximumPayloadBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, MaxDepth = 32 };
    public RuntimeSkinData(RuntimeMeshData mesh, IReadOnlyList<SkinJoint> joints,
        IReadOnlyList<SkinInfluence> influences, IReadOnlyList<SkinSocket> sockets)
    {
        ArgumentNullException.ThrowIfNull(mesh); ArgumentNullException.ThrowIfNull(joints); ArgumentNullException.ThrowIfNull(influences); ArgumentNullException.ThrowIfNull(sockets);
        if (joints.Count > MaximumJoints || influences.Count > MaximumVertices || sockets.Count > 32) throw new InvalidDataException("Skin declared collections exceed limits.");
        Mesh = mesh; Joints = Array.AsReadOnly(joints.ToArray());
        Influences = Array.AsReadOnly(influences.ToArray()); Sockets = Array.AsReadOnly(sockets.ToArray());
        Validate();
    }
    public RuntimeMeshData Mesh { get; }
    public IReadOnlyList<SkinJoint> Joints { get; }
    public IReadOnlyList<SkinInfluence> Influences { get; }
    public IReadOnlyList<SkinSocket> Sockets { get; }

    private void Validate()
    {
        if (Joints.Count is < 1 or > MaximumJoints || Mesh.Vertices.Count > MaximumVertices || Mesh.Indices.Count > MaximumIndices ||
            Influences.Count != Mesh.Vertices.Count || Sockets.Count > 32 || !Mesh.HasNormals || !Mesh.HasUv0 || !Mesh.HasTangents)
            throw new InvalidDataException("Skin exceeds joint/vertex/index/socket limits or lacks complete vertex attributes.");
        ValidateRig(Joints);
        foreach (var v in Influences)
            if ((uint)v.A >= Joints.Count || (uint)v.B >= Joints.Count || (uint)v.C >= Joints.Count || (uint)v.D >= Joints.Count ||
                !float.IsFinite(v.Weights.LengthSquared()) || v.Weights.X < 0 || v.Weights.Y < 0 || v.Weights.Z < 0 || v.Weights.W < 0 ||
                MathF.Abs(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W - 1) > .0001f)
                throw new InvalidDataException("Skin weights must be finite, nonnegative, normalized and reference existing joints.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var socket in Sockets)
            if (socket is null || string.IsNullOrWhiteSpace(socket.Name) || !names.Add(socket.Name) || (uint)socket.Joint >= Joints.Count || !Finite(socket.BindPosition))
                throw new InvalidDataException("Skin socket binding is invalid.");
    }
    private static void ValidateRig(IReadOnlyList<SkinJoint> joints)
    {
        if (joints.Count is < 1 or > MaximumJoints) throw new InvalidDataException("Skin joint count exceeds limits.");
        Span<Matrix4x4> bind = stackalloc Matrix4x4[MaximumJoints];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
            if (joint is null || string.IsNullOrWhiteSpace(joint.Name) || joint.Name.Length > 128 || !names.Add(joint.Name) ||
                joint.Parent < -1 || joint.Parent >= i || !Finite(joint.Translation) || joint.Translation.Length() > 2 ||
                !float.IsFinite(joint.Rotation.LengthSquared()) || MathF.Abs(joint.Rotation.LengthSquared() - 1) > .0001f ||
                !Finite(joint.InverseBind) || !Matrix4x4.Invert(joint.InverseBind, out _))
                throw new InvalidDataException("Skin requires unique parent-first joints, unit rotations and invertible finite bind transforms.");
            bind[i] = Matrix4x4.CreateFromQuaternion(joint.Rotation) * Matrix4x4.CreateTranslation(joint.Translation);
            if (joint.Parent >= 0) bind[i] *= bind[joint.Parent];
            if (!NearIdentity(joint.InverseBind * bind[i])) throw new InvalidDataException("Skin inverse bind does not match the declared bind hierarchy.");
        }
    }
    public static string RigSignature(IReadOnlyList<SkinJoint> joints)
    {
        ValidateRig(joints);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        foreach (var joint in joints)
        {
            writer.Write(joint.Name); writer.Write(joint.Parent);
            writer.Write(joint.Translation.X);writer.Write(joint.Translation.Y);writer.Write(joint.Translation.Z);
            writer.Write(joint.Rotation.X);writer.Write(joint.Rotation.Y);writer.Write(joint.Rotation.Z);writer.Write(joint.Rotation.W);
            Matrix4x4 matrix = joint.InverseBind;
            foreach (float value in MemoryMarshal.Cast<Matrix4x4,float>(MemoryMarshal.CreateReadOnlySpan(ref matrix,1))) writer.Write(value);
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
    public int[] Bind(RuntimeAnimationData clip)
    {
        if (clip.SkeletonSignature != RigSignature(Joints)) throw new InvalidDataException("Clip bind signature does not match this skin; retargeting is unsupported.");
        var map = new int[Joints.Count];
        for (int i = 0; i < map.Length; i++)
        {
            int node = -1;
            for (int j = 0; j < clip.Nodes.Count; j++) if (clip.Nodes[j].Name == Joints[i].Name) { node = j; break; }
            if (node < 0) throw new InvalidDataException($"Clip has no joint '{Joints[i].Name}'.");
            int parent = clip.Nodes[node].Parent;
            if (Joints[i].Parent >= 0 && (parent < 0 || clip.Nodes[parent].Name != Joints[Joints[i].Parent].Name))
                throw new InvalidDataException("Clip and skin hierarchy mismatch.");
            if (Joints[i].Parent < 0 && parent >= 0 && clip.Channels.Any(c => c.Node == parent))
                throw new InvalidDataException("Animated nonjoint ancestors are unsupported.");
            map[i] = node;
        }
        foreach (var channel in clip.Channels)
            if (!map.Contains(channel.Node)) throw new InvalidDataException("Clip targets a node outside its skin.");
        for (int j = 0; j < Joints.Count; j++)
        {
            float translationBound = 0;
            for (int parent = j; parent >= 0; parent = Joints[parent].Parent)
            {
                float bound = Joints[parent].Translation.Length();
                foreach (var channel in clip.Channels)
                    if (channel.Node == map[parent] && channel.Path == AnimationPath.Translation)
                        foreach (var value in channel.Values) bound = MathF.Max(bound, new Vector3(value.X, value.Y, value.Z).Length());
                translationBound += bound;
            }
            for (int v = 0; v < Mesh.Vertices.Count; v++)
            {
                var inf = Influences[v];
                bool used = inf.A == j && inf.Weights.X > 0 || inf.B == j && inf.Weights.Y > 0 || inf.C == j && inf.Weights.Z > 0 || inf.D == j && inf.Weights.W > 0;
                if (!used) continue;
                var vertex = Mesh.Vertices[v];
                if (Vector3.Transform(new(vertex.X, vertex.Y, vertex.Z), Joints[j].InverseBind).Length() + translationBound > CullingRadius)
                    throw new InvalidDataException("Skin clip exceeds the conservative cosmetic culling radius.");
            }
            foreach (var socket in Sockets)
                if (socket.Joint == j && Vector3.Transform(socket.BindPosition, Joints[j].InverseBind).Length() + translationBound > CullingRadius)
                    throw new InvalidDataException("Skin socket exceeds the cosmetic culling radius.");
        }
        return map;
    }
    public void Evaluate(RuntimeAnimationData clip, ReadOnlySpan<int> nodeMap, float time, Span<Matrix4x4> palette)
    {
        if (nodeMap.Length != Joints.Count || palette.Length < Joints.Count) throw new ArgumentException("Insufficient skin palette storage.");
        Span<Matrix4x4> global = stackalloc Matrix4x4[MaximumJoints];
        for (int i = 0; i < Joints.Count; i++)
        {
            var joint = Joints[i]; Vector3 translation = joint.Translation; Quaternion rotation = joint.Rotation;
            for (int c = 0; c < clip.Channels.Count; c++)
            {
                var channel = clip.Channels[c];
                if (channel.Node != nodeMap[i]) continue;
                var value = channel.Evaluate(time);
                if (channel.Path == AnimationPath.Translation) translation = new(value.X, value.Y, value.Z);
                else rotation = new(value.X, value.Y, value.Z, value.W);
            }
            global[i] = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            if (joint.Parent >= 0) global[i] *= global[joint.Parent];
            palette[i] = joint.InverseBind * global[i];
        }
    }
    public void Deform(ReadOnlySpan<Matrix4x4> palette, Span<RuntimeMeshVertex> destination)
    {
        if (palette.Length < Joints.Count || destination.Length != Mesh.Vertices.Count) throw new ArgumentException("Invalid skinning storage.");
        for (int i = 0; i < destination.Length; i++)
        {
            var v = Mesh.Vertices[i]; var influence = Influences[i];
            Vector3 p = Vector3.Zero, n = Vector3.Zero, t = Vector3.Zero;
            for (int k = 0; k < 4; k++)
            {
                float w = influence.Weights[k]; if (w == 0) continue;
                int j = k switch { 0 => influence.A, 1 => influence.B, 2 => influence.C, _ => influence.D };
                p += Vector3.Transform(new(v.X, v.Y, v.Z), palette[j]) * w;
                n += Vector3.TransformNormal(new(v.NormalX, v.NormalY, v.NormalZ), palette[j]) * w;
                t += Vector3.TransformNormal(new(v.TangentX, v.TangentY, v.TangentZ), palette[j]) * w;
            }
            // Opposing weighted rotations can cancel a basis even for a valid skin.
            // Keep deformation finite at that singular blend instead of failing a frame.
            if (n.LengthSquared() < 1e-12f) n = Vector3.TransformNormal(new(v.NormalX,v.NormalY,v.NormalZ), palette[influence.A]);
            n = Vector3.Normalize(n); t -= n * Vector3.Dot(n, t);
            if (t.LengthSquared() < 1e-12f) t = Vector3.Cross(MathF.Abs(n.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, n);
            t = Vector3.Normalize(t);
            if (!Finite(p) || !Finite(n) || !Finite(t)) throw new InvalidDataException("Skin deformation produced an invalid vertex basis.");
            destination[i] = v with { X = p.X, Y = p.Y, Z = p.Z, NormalX = n.X, NormalY = n.Y, NormalZ = n.Z, TangentX = t.X, TangentY = t.Y, TangentZ = t.Z };
        }
    }
    public Vector3 SocketPosition(string name, ReadOnlySpan<Matrix4x4> palette)
    {
        foreach (var socket in Sockets) if (socket.Name == name) return Vector3.Transform(socket.BindPosition, palette[socket.Joint]);
        throw new InvalidDataException($"Missing skin socket '{name}'.");
    }
    public byte[] ToPayload()
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(CurrentVersion, Mesh.ToPayload(), Joints.ToArray(), Influences.ToArray(), Sockets.ToArray()), Options);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Skin payload exceeds 8MiB.");
        return payload;
    }
    public static RuntimeSkinData FromPayload(byte[] bytes)
    {
        if (bytes.Length > MaximumPayloadBytes) throw new InvalidDataException("Skin payload exceeds 8MiB.");
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
                int limit = reader.ValueTextEquals("Joints") ? MaximumJoints : reader.ValueTextEquals("Influences") ? MaximumVertices : reader.ValueTextEquals("Sockets") ? 32 : -1;
                if (limit < 0) continue;
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("Skin collection must be an array.");
                int count = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                { if (++count > limit) throw new InvalidDataException("Skin collection exceeds its allocation limit."); reader.Skip(); }
            }
            var data = JsonSerializer.Deserialize<Payload>(bytes, Options) ?? throw new InvalidDataException("Empty skin payload.");
            if (data.Version != CurrentVersion || data.Mesh is null || data.Mesh.Length < 24 || data.Joints is null || data.Influences is null || data.Sockets is null ||
                BitConverter.ToInt32(data.Mesh, 8) is < 1 or > MaximumVertices || BitConverter.ToInt32(data.Mesh, 12) is < 3 or > MaximumIndices)
                throw new InvalidDataException("Skin version, collections or embedded mesh counts are invalid.");
            return new(RuntimeMeshData.FromPayload(data.Mesh), data.Joints, data.Influences, data.Sockets);
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid skin payload JSON.", e); }
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Matrix4x4 m) { foreach (float v in MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref m, 1))) if (!float.IsFinite(v)) return false; return true; }
    private static bool NearIdentity(Matrix4x4 m) => Finite(m) && MathF.Abs(m.M11 - 1) < .001f && MathF.Abs(m.M22 - 1) < .001f && MathF.Abs(m.M33 - 1) < .001f && MathF.Abs(m.M44 - 1) < .001f &&
        MathF.Abs(m.M12) + MathF.Abs(m.M13) + MathF.Abs(m.M14) + MathF.Abs(m.M21) + MathF.Abs(m.M23) + MathF.Abs(m.M24) + MathF.Abs(m.M31) + MathF.Abs(m.M32) + MathF.Abs(m.M34) + MathF.Abs(m.M41) + MathF.Abs(m.M42) + MathF.Abs(m.M43) < .003f;
    private sealed record Payload(int Version, byte[] Mesh, SkinJoint[] Joints, SkinInfluence[] Influences, SkinSocket[] Sockets);
}
