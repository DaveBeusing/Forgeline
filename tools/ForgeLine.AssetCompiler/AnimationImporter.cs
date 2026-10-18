using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class AnimationImporter
{
    public static ImportedAssetPayload Import(string path, string sourceRoot, bool skeletal = false, string? clipName = null)
    {
        try { return Read(path, sourceRoot, skeletal, clipName); }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        { throw new InvalidDataException("Malformed rigid animation glTF: " + e.Message, e); }
    }

    private static ImportedAssetPayload Read(string path, string sourceRoot, bool skeletal, string? clipName)
    {
        using var gltf = GltfImporter.Load(path, sourceRoot, 8 * 1024 * 1024);
        JsonElement root = gltf.Document.RootElement;
        if (root.GetProperty("asset").GetProperty("version").GetString() != "2.0" ||
            !skeletal && root.TryGetProperty("skins", out _) || root.TryGetProperty("extensionsRequired", out var required) && required.GetArrayLength() > 0)
            throw new InvalidDataException("Rigid clips require glTF 2.0 without skins or required extensions.");
        JsonElement nodes = root.GetProperty("nodes"), animations = root.GetProperty("animations");
        if (nodes.GetArrayLength() < 1 || nodes.GetArrayLength() > (skeletal ? RuntimeAnimationData.MaximumNodes : 32) || animations.GetArrayLength() < 1 || animations.GetArrayLength() > 9 || clipName is null && animations.GetArrayLength() != 1)
            throw new InvalidDataException("A clip asset requires one animation and 1..32 nodes.");
        int count = nodes.GetArrayLength(); var parents = Enumerable.Repeat(-1, count).ToArray();
        for (int i = 0; i < count; i++)
        {
            JsonElement node = nodes[i];
            if (node.TryGetProperty("matrix", out _) || !skeletal && node.TryGetProperty("skin", out _) || node.TryGetProperty("weights", out _))
                throw new InvalidDataException("Rigid clips reject matrix, skin and morph node transforms.");
            if (!skeletal) { CheckRest(node, "translation", [0, 0, 0]); CheckRest(node, "rotation", [0, 0, 0, 1]); } CheckRest(node, "scale", [1, 1, 1]);
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray())
                {
                    int c = child.GetInt32();
                    if ((uint)c >= count || c == i || parents[c] != -1) throw new InvalidDataException("Clip hierarchy has invalid or multiple parents.");
                    parents[c] = i;
                }
        }
        var order = new List<int>(); var visited = new byte[count];
        void Visit(int i)
        {
            if (visited[i] == 1) throw new InvalidDataException("Clip hierarchy contains a cycle.");
            if (visited[i] == 2) return;
            visited[i] = 1; if (parents[i] >= 0) Visit(parents[i]); visited[i] = 2; order.Add(i);
        }
        for (int i = 0; i < count; i++) Visit(i);
        int[] map = new int[count]; for (int i = 0; i < count; i++) map[order[i]] = i;
        var table = order.Select(i => new AnimationNode(nodes[i].GetProperty("name").GetString()!, parents[i] < 0 ? -1 : map[parents[i]])).ToArray();
        var matches = animations.EnumerateArray().Where(a => clipName is null || a.GetProperty("name").GetString() == clipName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Animation clip name must select exactly one authored clip.");
        var animation = matches[0]; var sourceChannels = animation.GetProperty("channels"); var samplers = animation.GetProperty("samplers");
        if (sourceChannels.GetArrayLength() < 1 || sourceChannels.GetArrayLength() > (skeletal ? 192 : 64) || samplers.GetArrayLength() > (skeletal ? 192 : 64))
            throw new InvalidDataException("Clip channel/sampler count exceeds 64.");
        var channels = new List<AnimationChannel>(); int total = 0;
        foreach (var channel in sourceChannels.EnumerateArray())
        {
            var target = channel.GetProperty("target"); int node = target.GetProperty("node").GetInt32();
            if ((uint)node >= count) throw new InvalidDataException("Clip targets a missing node.");
            bool unitScale = skeletal && target.GetProperty("path").GetString() == "scale";
            AnimationPath channelPath = (unitScale ? "translation" : target.GetProperty("path").GetString()) switch
            {
                "translation" => AnimationPath.Translation, "rotation" => AnimationPath.Rotation,
                _ => throw new InvalidDataException("Only rigid translation and rotation channels are supported; scale and morph animation are unsupported.")
            };
            int samplerIndex = channel.GetProperty("sampler").GetInt32();
            if ((uint)samplerIndex >= samplers.GetArrayLength()) throw new InvalidDataException("Missing clip sampler.");
            var sampler = samplers[samplerIndex];
            AnimationInterpolation interpolation = (sampler.TryGetProperty("interpolation", out var mode) ? mode.GetString() : "LINEAR") switch
            {
                "LINEAR" => AnimationInterpolation.Linear, "STEP" => AnimationInterpolation.Step,
                _ => throw new InvalidDataException("Only STEP and LINEAR clip interpolation are supported.")
            };
            float[] times = GltfImporter.ReadFloatAccessor(root, gltf.Buffers, sampler.GetProperty("input").GetInt32(), "SCALAR", RuntimeAnimationData.MaximumKeysPerChannel);
            if ((total += times.Length) > RuntimeAnimationData.MaximumKeys) throw new InvalidDataException("Clip exceeds 8192 keys.");
            int components = channelPath == AnimationPath.Rotation ? 4 : 3;
            float[] data = GltfImporter.ReadFloatAccessor(root, gltf.Buffers, sampler.GetProperty("output").GetInt32(), components == 4 ? "VEC4" : "VEC3", RuntimeAnimationData.MaximumKeysPerChannel);
            if (data.Length != times.Length * components) throw new InvalidDataException("Clip input/output key counts differ.");
            var values = new Vector4[times.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int k = i * components; Vector4 v = new(data[k], data[k + 1], data[k + 2], components == 4 ? data[k + 3] : 0);
                if (components == 4)
                {
                    if (!float.IsFinite(v.LengthSquared()) || MathF.Abs(v.LengthSquared() - 1) > .001f)
                        throw new InvalidDataException("Clip quaternion is not normalized.");
                    v = Vector4.Normalize(v);
                }
                values[i] = v;
            }
            if (unitScale)
            {
                float previous = -1;
                for (int i = 0; i < times.Length; i++)
                {
                    if (!float.IsFinite(times[i]) || times[i] < 0 || times[i] <= previous || times[i] > RuntimeAnimationData.MaximumDuration ||
                        i == 0 && times[i] != 0 || !float.IsFinite(values[i].LengthSquared()) || Vector3.Distance(new(values[i].X, values[i].Y, values[i].Z), Vector3.One) > .000001f)
                        throw new InvalidDataException("Only finite constant unit-scale skeletal channels are supported.");
                    previous = times[i];
                }
                continue;
            }
            channels.Add(new(map[node], channelPath, interpolation, times, values));
        }
        string? signature = skeletal ? RuntimeSkinData.RigSignature(SkinImporter.ReadRig(root, gltf.Buffers, out _)) : null;
        var result = new RuntimeAnimationData(animation.GetProperty("name").GetString()!, table, channels, signature);
        return new(result.ToPayload(), null, []);
    }
    private static void CheckRest(JsonElement node, string property, float[] identity)
    {
        if (!node.TryGetProperty(property, out var value)) return;
        if (value.GetArrayLength() != identity.Length || value.EnumerateArray().Select(v => v.GetSingle()).Where((v, i) => !float.IsFinite(v) || MathF.Abs(v - identity[i]) > .000001f).Any())
            throw new InvalidDataException("Clip authoring requires identity rest TRS; bake mesh rest transforms before export.");
    }
}
