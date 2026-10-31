using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class FixturePreflightTests
{
    [Fact]
    public void InventoryMatchesSharedPythonUnicodeOrderingAndLowercaseHashes()
    {
        using var data = JsonDocument.Parse(File.ReadAllBytes(GoldenPath()));
        var temporary = Directory.CreateTempSubdirectory("forgeline-preflight-inventory-");
        try
        {
            foreach (var file in data.RootElement.GetProperty("files").EnumerateArray())
                File.WriteAllText(Path.Combine(temporary.FullName, file.GetProperty("path").GetString()!), file.GetProperty("text").GetString()!, new UTF8Encoding(false));
            var actual = ReferenceFixturePreflight.Inventory(temporary.FullName);
            var expected = data.RootElement.GetProperty("expected").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Equal(expected[i].GetProperty("path").GetString(), actual[i].Path);
                Assert.Equal(expected[i].GetProperty("bytes").GetInt64(), actual[i].Bytes);
                Assert.Equal(expected[i].GetProperty("sha256").GetString(), actual[i].Sha256);
            }
        }
        finally { temporary.Delete(recursive: true); }
    }
    [Fact]
    public void CapabilitySnapshotFeedsResolveOnceWithoutDrawingOrApplying()
    {
        using var source = new CapabilityDevice();
        using var snapshot = new ReferenceFixturePreflight.CapabilitySnapshot(source);
        var actual = ClientRenderConfiguration.Resolve(snapshot, new() { Quality = RenderQuality.Medium }, 1280, 720,
            SceneLightingSettings.Default, GraphicsAtmosphereSettings.Default);
        Assert.Equal(RenderQuality.Medium, actual.Profile.Requested);
        Assert.Equal(6, source.Reads); Assert.Equal(0, source.Draws);
        Assert.Throws<NotSupportedException>(() => snapshot.RenderFrame(default));
        Assert.Equal(0, source.Draws);
    }
    [Fact]
    public void UnsafeOutputIsRejectedBeforeDirectoryOrFailureFileCanMutatePayload()
    {
        string payload = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        string rejected = Path.Combine(payload, "rejected-preflight-" + Guid.NewGuid().ToString("N"));
        var before = ReferenceFixturePreflight.Inventory(payload);
        string requestFile = Path.GetTempFileName();
        try
        {
            var request = new VisualReferenceCaptureRequest { OutputRoot = rejected, RuntimeRoot = Path.GetTempPath(),
                Commit = ClientBuildIdentity.SourceRevision, Scenes = [VisualReferenceSceneKind.IndustrialBase],
                Profiles = [RenderQuality.Medium], Zooms = [RtsReferenceZoom.NormalGameplay],
                Displays = [new(640,360)], Software = true, Warmups = 64, Samples = 512 };
            File.WriteAllText(requestFile, JsonSerializer.Serialize(request, VisualReferenceCapture.JsonOptions));
            Assert.Equal(1, ReferenceFixturePreflight.Run(requestFile));
            Assert.False(Directory.Exists(rejected));
            Assert.Equal(before, ReferenceFixturePreflight.Inventory(payload));
        }
        finally { File.Delete(requestFile); }
    }
    private static string GoldenPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "build/rendering/render-engine-upgrade/testdata/payload-inventory-golden.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Shared source fixture missing.");
    }
    private sealed class CapabilityDevice : IGraphicsDevice
    {
        internal int Reads, Draws;
        public GraphicsDiagnostics Diagnostics { get { Reads++; return new(new("CPU capability fixture",0,false,"contract",false),new(1280,720,3,0,false,"Immediate")); } }
        public bool SupportsPostProcessing { get { Reads++; return true; } }
        public bool SupportsParticleRendering { get { Reads++; return true; } }
        public bool SupportsDirectionalShadows { get { Reads++; return true; } }
        public bool SupportsAtmosphere { get { Reads++; return true; } }
        public ulong? AvailableMemoryBudgetBytes { get { Reads++; return 8UL*1024*1024*1024; } }
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription value) => throw new NotSupportedException();
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription value) => throw new NotSupportedException();
        public void RenderFrame(GraphicsColor clear, Action<IGraphicsCommandContext>? commands = null) { Draws++; throw new NotSupportedException(); }
        public void Resize(int width, int height) => throw new NotSupportedException();
        public void WaitForIdle() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
