using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class SkinImporter
{
    public static ImportedAssetPayload Import(string path, string sourceRoot, SourceAssetDefinition definition, MeshMaterialRequirement[] requirements)
    {
        try { return Read(path, sourceRoot, definition, requirements); }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        { throw new InvalidDataException("Malformed skinned glTF: " + e.Message, e); }
    }
    private static ImportedAssetPayload Read(string path, string sourceRoot, SourceAssetDefinition definition, MeshMaterialRequirement[] requirements)
    {
        using var gltf = GltfImporter.Load(path, sourceRoot, RuntimeSkinData.MaximumPayloadBytes);
        var root = gltf.Document.RootElement;
        if (root.GetProperty("skins").GetArrayLength() != 1 || root.GetProperty("meshes").GetArrayLength() != 1 ||
            root.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength() != 1 ||
            root.TryGetProperty("extensionsRequired", out var extensions) && extensions.GetArrayLength() > 0)
            throw new InvalidDataException("Skin subset requires one skin, one mesh, one triangle primitive and no required extensions.");
        var joints = ReadRig(root, gltf.Buffers, out var remap);
        var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0]; var attributes = primitive.GetProperty("attributes");
        if (primitive.TryGetProperty("targets", out _) || attributes.TryGetProperty("JOINTS_1", out _) || attributes.TryGetProperty("WEIGHTS_1", out _) ||
            !attributes.TryGetProperty("NORMAL", out _) || !attributes.TryGetProperty("TEXCOORD_0", out _) || !attributes.TryGetProperty("TANGENT", out _))
            throw new InvalidDataException("Skin requires complete NORMAL/UV/TANGENT and at most JOINTS_0/WEIGHTS_0.");
        if (root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32() > RuntimeSkinData.MaximumVertices)
            throw new InvalidDataException("Skin position count exceeds 32768.");
        int[] ids = ReadJoints(root, gltf.Buffers, attributes.GetProperty("JOINTS_0").GetInt32());
        float[] weights = GltfImporter.ReadFloatAccessor(root, gltf.Buffers, attributes.GetProperty("WEIGHTS_0").GetInt32(), "VEC4", RuntimeSkinData.MaximumVertices);
        var imported = GltfImporter.Import(path, sourceRoot, definition.MaterialReferences, requirements, allowSkin: true);
        var mesh = RuntimeMeshData.FromPayload(imported.Payload);
        if (ids.Length != mesh.Vertices.Count * 4 || weights.Length != ids.Length) throw new InvalidDataException("Skin attributes do not match compiled vertex order/count.");
        var influences = new SkinInfluence[mesh.Vertices.Count];
        for (int i = 0; i < influences.Length; i++)
        {
            int k = i * 4;
            for (int j = 0; j < 4; j++) if ((uint)ids[k+j] >= remap.Length) throw new InvalidDataException("Skin vertex references a missing joint.");
            influences[i] = new(remap[ids[k]],remap[ids[k+1]],remap[ids[k+2]],remap[ids[k+3]],new(weights[k],weights[k+1],weights[k+2],weights[k+3]));
        }
        var sockets = definition.SkinSockets.Select(binding =>
        {
            int j = Array.FindIndex(joints, joint => joint.Name == binding.Joint);
            var socket = definition.Sockets.FirstOrDefault(socket => socket.Name == binding.Name);
            if (j < 0 || !socket.IsValid) throw new InvalidDataException("Skin socket has no joint or canonical bind socket.");
            return new SkinSocket(binding.Name, j, new(socket.X, socket.Y, socket.Z));
        }).ToArray();
        var data = new RuntimeSkinData(mesh, joints, influences, sockets);
        return new(data.ToPayload(), imported.Bounds, []);
    }
    internal static SkinJoint[] ReadRig(JsonElement root, byte[][] buffers, out int[] remap)
    {
        var nodes = root.GetProperty("nodes");
        if (nodes.GetArrayLength() > RuntimeAnimationData.MaximumNodes) throw new InvalidDataException("Too many skin source nodes.");
        var sourceSkin = root.GetProperty("skins")[0];
        int[] sourceJoints = sourceSkin.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
        if (sourceJoints.Length is < 1 or > RuntimeSkinData.MaximumJoints || sourceJoints.Distinct().Count() != sourceJoints.Length)
            throw new InvalidDataException("Invalid or duplicated skin joints.");
        int[] parents = Enumerable.Repeat(-1, nodes.GetArrayLength()).ToArray(); int skinMeshes = 0;
        for (int i = 0; i < nodes.GetArrayLength(); i++)
        {
            var node = nodes[i];
            if (node.TryGetProperty("matrix", out _) || node.TryGetProperty("weights", out _) || node.TryGetProperty("extensions", out _))
                throw new InvalidDataException("Skin node matrices, morphs and extensions are unsupported; export TRS.");
            if (node.TryGetProperty("scale", out var scale) && (scale.GetArrayLength() != 3 || scale.EnumerateArray().Any(v => !float.IsFinite(v.GetSingle()) || MathF.Abs(v.GetSingle() - 1) > .000001f)))
                throw new InvalidDataException("Skin requires unit joint/node scale.");
            if (node.TryGetProperty("skin", out var skinIndex))
            {
                if (skinIndex.GetInt32() != 0 || node.GetProperty("mesh").GetInt32() != 0) throw new InvalidDataException("Missing skin/mesh target.");
                skinMeshes++;
            }
            if (!sourceJoints.Contains(i))
            {
                if (Translation(node) != Vector3.Zero || Rotation(node) != Quaternion.Identity)
                    throw new InvalidDataException("Nonjoint skin ancestors and mesh nodes must have identity rest transforms.");
            }
            if (node.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray())
            {
                int c = child.GetInt32();
                if ((uint)c >= parents.Length || c == i || parents[c] != -1) throw new InvalidDataException("Malformed skin hierarchy.");
                parents[c] = i;
            }
        }
        if (skinMeshes != 1) throw new InvalidDataException("Exactly one node must instantiate the skin mesh.");
        var ordered = new List<int>(); var visiting = new HashSet<int>(); var visited = new HashSet<int>();
        void Visit(int node)
        {
            if ((uint)node >= nodes.GetArrayLength() || !visiting.Add(node)) throw new InvalidDataException("Missing or cyclic skin joint.");
            if (visited.Contains(node)) { visiting.Remove(node); return; }
            int p = parents[node];
            if (p >= 0 && sourceJoints.Contains(p)) Visit(p);
            else if (p >= 0 && parents[p] >= 0) throw new InvalidDataException("Only one identity nonjoint rig root is supported.");
            visited.Add(node); visiting.Remove(node); ordered.Add(node);
        }
        foreach (int joint in sourceJoints) Visit(joint);
        float[] inverse = GltfImporter.ReadFloatAccessor(root, buffers, sourceSkin.GetProperty("inverseBindMatrices").GetInt32(), "MAT4", RuntimeSkinData.MaximumJoints);
        if (inverse.Length != sourceJoints.Length * 16) throw new InvalidDataException("Missing or mismatched inverse bind matrix table.");
        var joints = new SkinJoint[ordered.Count]; remap = new int[sourceJoints.Length];
        for (int i = 0; i < ordered.Count; i++)
        {
            int node = ordered[i], original = Array.IndexOf(sourceJoints, node), k = original * 16;
            remap[original] = i;
            joints[i] = new(nodes[node].GetProperty("name").GetString()!, ordered.IndexOf(parents[node]), Translation(nodes[node]), Rotation(nodes[node]),
                new(inverse[k],inverse[k+1],inverse[k+2],inverse[k+3],inverse[k+4],inverse[k+5],inverse[k+6],inverse[k+7],inverse[k+8],inverse[k+9],inverse[k+10],inverse[k+11],inverse[k+12],inverse[k+13],inverse[k+14],inverse[k+15]));
        }
        return joints;
    }
    private static Vector3 Translation(JsonElement node)
    { if (!node.TryGetProperty("translation", out var v)) return Vector3.Zero; if (v.GetArrayLength() != 3) throw new InvalidDataException("Malformed joint translation."); return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle()); }
    private static Quaternion Rotation(JsonElement node)
    { if (!node.TryGetProperty("rotation", out var v)) return Quaternion.Identity; if (v.GetArrayLength() != 4) throw new InvalidDataException("Malformed joint rotation."); return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle(),v[3].GetSingle()); }
    private static int[] ReadJoints(JsonElement root, byte[][] buffers, int index)
    {
        var accessor = root.GetProperty("accessors")[index];
        int type = accessor.GetProperty("componentType").GetInt32(), count = accessor.GetProperty("count").GetInt32();
        if (type is not (5121 or 5123) || count is < 1 or > RuntimeSkinData.MaximumVertices || accessor.GetProperty("type").GetString() != "VEC4" ||
            accessor.TryGetProperty("sparse", out _) || accessor.TryGetProperty("normalized", out var normalized) && normalized.GetBoolean())
            throw new InvalidDataException("JOINTS_0 requires bounded unsigned byte/short VEC4.");
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()]; int buffer = view.GetProperty("buffer").GetInt32();
        int width = type == 5121 ? 1 : 2, size = width * 4;
        int viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        int offset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        int stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : size;
        int length = view.GetProperty("byteLength").GetInt32();
        if ((uint)buffer >= buffers.Length || viewOffset < 0 || offset < 0 || length < 0 || stride < size || stride > 252 || offset % width != 0 ||
            (long)offset + (long)(count - 1) * stride + size > length || (long)viewOffset + length > buffers[buffer].Length)
            throw new InvalidDataException("JOINTS_0 exceeds its buffer view.");
        int[] values = new int[count * 4];
        for (int i = 0; i < count; i++) for (int j = 0; j < 4; j++)
        {
            int k = viewOffset + offset + i * stride + j * width;
            values[i*4+j] = width == 1 ? buffers[buffer][k] : BinaryPrimitives.ReadUInt16LittleEndian(buffers[buffer].AsSpan(k, 2));
        }
        return values;
    }
}
