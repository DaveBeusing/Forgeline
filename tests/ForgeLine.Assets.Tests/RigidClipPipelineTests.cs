using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class RigidClipPipelineTests
{
    [Fact]
    public void BlenderExportCompilesSealsSkipsAndDeploysWithoutSources()
    {
        using var fixture = new ClipWorkspace();
        var first = fixture.Compile();
        Assert.True(first.Success, string.Join("; ", first.Diagnostics.Select(d => d.Message)));
        Assert.Equal(3, first.CompiledCount);
        Assert.Equal(3, fixture.Compile().SkippedCount);
        var catalog = RuntimeAssetCatalog.LoadCancellable(fixture.Runtime, CancellationToken.None, true);
        var clip = RuntimeAnimationData.FromPayload(catalog.Read(AssetId.Parse("animation.test.arm_cycle")).Payload);
        Assert.Equal(3, clip.Duration, 5);
        Assert.Equal(2, clip.Nodes.Count); Assert.Equal(2, clip.Channels.Count);
        AnimationChannel translation = Assert.Single(clip.Channels, c => c.Path == AnimationPath.Translation);
        Assert.InRange(translation.Evaluate(1).X, .03999f, .04001f);
        using var exported = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Source, "export.json")));
        foreach (var sample in exported.RootElement.GetProperty("samples").EnumerateArray())
        {
            float time = sample.GetProperty("seconds").GetSingle();
            float[] t = sample.GetProperty("translation").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            float[] q = sample.GetProperty("rotation").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            Assert.InRange(Vector3.Distance(new(t[0], t[1], t[2]), new(translation.Evaluate(time).X, translation.Evaluate(time).Y, translation.Evaluate(time).Z)), 0, .000002f);
            Vector4 actual = Assert.Single(clip.Channels, c => c.Path == AnimationPath.Rotation).Evaluate(time);
            Assert.InRange(1 - MathF.Abs(Vector4.Dot(actual, new(q[0], q[1], q[2], q[3]))), -.000002f, .000002f);
        }
        Assert.Equal(clip.ToPayload(), RuntimeAnimationData.FromPayload(clip.ToPayload()).ToPayload());
        Assert.True(RuntimeAssetQualification.Run(fixture.Runtime, true).Success);
        Directory.Delete(fixture.Source, true);
        Assert.True(RuntimeAssetQualification.Run(fixture.Runtime, true).Success);
        Assert.Equal(1, RuntimeAssetQualification.Run(fixture.Runtime, true).AnimationCount);
        string path = Path.Combine(fixture.Runtime, catalog.Get(AssetId.Parse("animation.test.arm_cycle")).RuntimePath);
        byte[] bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        Assert.False(RuntimeAssetQualification.Run(fixture.Runtime, true).Success);
    }

    [Fact]
    public void ExternalBufferEditInvalidatesClipAndDependentMeshOnly()
    {
        using var fixture = new ClipWorkspace();
        JsonObject root = fixture.ConvertToGltf(external: true);
        Assert.True(fixture.Compile().Success);
        Assert.Equal(3, fixture.Compile().SkippedCount);
        byte[] data = File.ReadAllBytes(Path.Combine(fixture.Source, "clip.bin"));
        int accessor = root["animations"]![0]!["samplers"]![1]!["output"]!.GetValue<int>();
        int view = root["accessors"]![accessor]!["bufferView"]!.GetValue<int>();
        int offset = root["bufferViews"]![view]!["byteOffset"]!.GetValue<int>();
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset + 12, 4), .03f);
        File.WriteAllBytes(Path.Combine(fixture.Source, "clip.bin"), data);
        var changed = fixture.Compile(); Assert.True(changed.Success);
        Assert.Equal(2, changed.CompiledCount); Assert.Equal(1, changed.SkippedCount);
    }

    [Theory]
    [InlineData("null-references")][InlineData("missing-node")][InlineData("duplicate-channel")][InlineData("cubic")]
    [InlineData("scale")][InlineData("cycle")][InlineData("rest")][InlineData("sparse")]
    [InlineData("count-bomb")][InlineData("view-range")][InlineData("zero-duration")]
    [InlineData("bad-times")][InlineData("nonfinite")][InlineData("quaternion")]
    [InlineData("too-long")][InlineData("too-far")][InlineData("missing-range")]
    [InlineData("skin")][InlineData("wrong-type")][InlineData("code-motion")]
    [InlineData("swept-bounds")][InlineData("parent-mismatch")][InlineData("missing-pivot")]
    public void InvalidClipsFailWithControlledDiagnostics(string scenario)
    {
        using var fixture = new ClipWorkspace(); JsonObject root = fixture.ConvertToGltf();
        JsonObject animation = root["animations"]![0]!.AsObject();
        int timeIndex = animation["samplers"]![0]!["input"]!.GetValue<int>();
        int rotationIndex = animation["samplers"]![0]!["output"]!.GetValue<int>();
        int translationIndex = animation["samplers"]![1]!["output"]!.GetValue<int>();
        void Float(int accessor, int index, float value)
        {
            byte[] data = Convert.FromBase64String(root["buffers"]![0]!["uri"]!.GetValue<string>().Split(',')[1]);
            int view = root["accessors"]![accessor]!["bufferView"]!.GetValue<int>();
            int offset = root["bufferViews"]![view]!["byteOffset"]!.GetValue<int>();
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset + index * 4, 4), value);
            root["buffers"]![0]!["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(data);
        }
        var mesh = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Source, "frame.asset.json")))!;
        switch (scenario)
        {
            case "null-references": mesh["animationReferences"] = null; break;
            case "missing-node": animation["channels"]![0]!["target"]!["node"] = 100; break;
            case "duplicate-channel": animation["channels"]!.AsArray().Add(animation["channels"]![0]!.DeepClone()); break;
            case "cubic": animation["samplers"]![0]!["interpolation"] = "CUBICSPLINE"; break;
            case "scale": animation["channels"]![0]!["target"]!["path"] = "scale"; break;
            case "cycle": root["nodes"]![0]!["children"] = new JsonArray(1); root["nodes"]![1]!["children"] = new JsonArray(0); break;
            case "rest": root["nodes"]![0]!["translation"] = new JsonArray(1, 0, 0); break;
            case "sparse": root["accessors"]![timeIndex]!["sparse"] = new JsonObject(); break;
            case "count-bomb": root["accessors"]![timeIndex]!["count"] = int.MaxValue; break;
            case "view-range": root["bufferViews"]![root["accessors"]![timeIndex]!["bufferView"]!.GetValue<int>()]!["byteLength"] = 4; break;
            case "zero-duration": Float(timeIndex, 1, 0); break;
            case "bad-times": Float(timeIndex, 1, -1); break;
            case "nonfinite": Float(timeIndex, 1, float.NaN); break;
            case "quaternion": Float(rotationIndex, 3, 0); break;
            case "too-long": Float(timeIndex, 3, 121); break;
            case "too-far": Float(translationIndex, 3, 3); break;
            case "skin": root["skins"] = new JsonArray(); break;
            case "missing-range": root["nodes"]![0]!["name"] = "absent"; break;
            case "parent-mismatch": root["nodes"]![1]!["children"] = new JsonArray(0); break;
            case "wrong-type": mesh["animationReferences"] = new JsonArray("material.test.frame"); break;
            case "code-motion": mesh["mechanicalParts"]![0]!["motion"] = "radar"; break;
            case "missing-pivot": mesh["mechanicalParts"]![0]!["socket"] = "absent"; break;
            case "swept-bounds": Float(translationIndex, 3, 1); break;
        }
        fixture.WriteGltf(root); File.WriteAllText(Path.Combine(fixture.Source, "frame.asset.json"), mesh.ToJsonString());
        var result = fixture.Compile(); Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Severity == AssetCompilerDiagnosticSeverity.Error);
    }

    [Fact]
    public void PayloadRejectsTruncationVersionBombsAndTrailingBytes()
    {
        var clip = new RuntimeAnimationData("test", [new("arm", -1)],
            [new(0, AnimationPath.Translation, AnimationInterpolation.Step, [0, 1], [Vector4.Zero, new(1, 0, 0, 0)])]);
        byte[] payload = clip.ToPayload();
        for (int length = 0; length < payload.Length; length++)
            Assert.Throws<InvalidDataException>(() => RuntimeAnimationData.FromPayload(payload[..length]));
        Assert.Throws<InvalidDataException>(() => RuntimeAnimationData.FromPayload([..payload, 0]));
        byte[] unknown = (byte[])payload.Clone(); unknown[0] = 99;
        Assert.Throws<InvalidDataException>(() => RuntimeAnimationData.FromPayload(unknown));
        Assert.Throws<InvalidDataException>(() => RuntimeAnimationData.FromPayload(new byte[RuntimeAnimationData.MaximumPayloadBytes + 1]));
        Assert.Equal(Vector4.Zero, clip.Channels[0].Evaluate(.5f));
        Assert.Equal(new Vector4(1, 0, 0, 0), clip.Channels[0].Evaluate(1));
        Assert.Equal(0, clip.LoopTime(double.PositiveInfinity));
    }

    private sealed class ClipWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rigid-clip-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(_root, "source");
        public string Runtime => Path.Combine(_root, "runtime");
        public ClipWorkspace()
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "ForgeLine.sln"))) repo = repo.Parent;
            Assert.NotNull(repo);
            Directory.CreateDirectory(Source);
            foreach (string path in Directory.EnumerateFiles(Path.Combine(repo.FullName, "tools/fixtures/rigid-clips")))
                File.Copy(path, Path.Combine(Source, Path.GetFileName(path)));
        }
        public AssetCompilationResult Compile() => AssetPipelineCompiler.Compile(Source, Runtime);
        public JsonObject ConvertToGltf(bool external = false)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Source, "rigid_clip.glb"));
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
            var root = JsonNode.Parse(bytes.AsSpan(20, length))!.AsObject();
            byte[] binary = bytes[(28 + length)..];
            root["buffers"]![0]!["uri"] = external ? "clip.bin" : "data:application/octet-stream;base64," + Convert.ToBase64String(binary);
            if (external) File.WriteAllBytes(Path.Combine(Source, "clip.bin"), binary);
            var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(Source, "arm.asset.json")))!;
            definition["source"] = "clip.gltf";
            File.WriteAllText(Path.Combine(Source, "arm.asset.json"), definition.ToJsonString());
            WriteGltf(root); return root;
        }
        public void WriteGltf(JsonObject root) => File.WriteAllText(Path.Combine(Source, "clip.gltf"), root.ToJsonString());
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
