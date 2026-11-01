using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceBoundInputProvenanceTests
{
    private static VisualReferenceCaptureRequest Request() => new()
    {
        OutputRoot = "cpu-only", RuntimeRoot = "cpu-only", Commit = new string('a', 40),
        Scenes = [VisualReferenceSceneKind.IndustrialBase], Profiles = [RenderQuality.Medium],
        Displays = [new(1920, 1080)], Zooms = [RtsReferenceZoom.NormalGameplay],
        RunProfile = ReferenceRunProfile.Warm, CameraPath = ReferenceCameraPath.Fixed,
        CollectorMode = ReferenceCollectorMode.Full, Warmups = 64, Samples = 512, RepeatCaptures = 8,
        OwnerObservationVersion = 1, CaptureBoundInputProvenanceVersion = 1
    };
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public void OnlyExplicitNativeValidationAcceptsSelectedVersion()
    {
        var request = Request();
        Assert.Throws<NotSupportedException>(() => request.Validate());
        request.Validate(allowBoundInputProvenance: true);
        (request with { CaptureBoundInputProvenanceVersion = null }).Validate();
        Assert.Throws<NotSupportedException>(() => (request with { CaptureBoundInputProvenanceVersion = 2 }).Validate(true));
    }
    [Fact]
    public void SelectedVersionCannotBorrowStageHostTraceOrMotionModes()
    {
        var request = Request();
        VisualReferenceCaptureRequest[] unsupported =
        [
            request with { CaptureStageDiagnostics = true }, request with { CaptureShadowDiagnostics = true },
            request with { OwnerTraceControlDirectory = "trace" }, request with { CollectorMode = ReferenceCollectorMode.CollectorOff },
            request with { CameraPath = ReferenceCameraPath.Sweep }, request with { RunProfile = ReferenceRunProfile.RendererCold },
            request with { Displays = [new(2560, 1440)] }, request with { Scenes = [VisualReferenceSceneKind.IndustrialBase, VisualReferenceSceneKind.DaylightTerrain] }
        ];
        foreach (var value in unsupported) Assert.Throws<NotSupportedException>(() => value.Validate(true));
    }
    [Fact]
    public void DefaultSelectorDoesNotAddLegacyWireField()
    {
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(Request() with { CaptureBoundInputProvenanceVersion = null }, VisualReferenceCapture.JsonOptions));
        Assert.False(json.RootElement.TryGetProperty("captureBoundInputProvenanceVersion", out _));
    }
    [Fact]
    public void NonemptyActualTypedExporterCreatesIndependentHashGraph()
    {
        using var root = new Output(); var fixture = Fixture();
        object reference = ReferenceBoundInputProvenance.Write(fixture.Capture, root.Path, "cpu", 0,
            new string('b', 64), fixture.RecipeHash, fixture.RecipeBytes, fixture.Camera, fixture.Profile, new string('c', 64));
        byte[] refBytes = JsonSerializer.SerializeToUtf8Bytes(reference, VisualReferenceCapture.JsonOptions);
        using var refs = JsonDocument.Parse(refBytes);
        string file = refs.RootElement.GetProperty("file").GetString()!;
        byte[] raw = File.ReadAllBytes(System.IO.Path.Combine(root.Path, file));
        Assert.Equal(Hash(raw), refs.RootElement.GetProperty("sha256").GetString());
        using var companion = JsonDocument.Parse(raw);
        Assert.Equal("reference-bound-input-cpu-provenance", companion.RootElement.GetProperty("kind").GetString());
        var journalRef = companion.RootElement.GetProperty("journalReference");
        string journalPath = System.IO.Path.Combine(root.Path, journalRef.GetProperty("file").GetString()!);
        Assert.Equal(Hash(File.ReadAllBytes(journalPath)), journalRef.GetProperty("sha256").GetString());
        Assert.Equal("world-draw", companion.RootElement.GetProperty("provenance").GetProperty("events")[1].GetProperty("kind").GetString());
        string? destination = Environment.GetEnvironmentVariable("FORGELINE_BOUND_INPUT_CPU_FIXTURE_OUTPUT");
        if (destination is not null)
        {
            Assert.False(Directory.Exists(destination)); Directory.CreateDirectory(destination);
            foreach (string path in Directory.GetFiles(root.Path)) File.Copy(path, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(path)));
            File.WriteAllBytes(System.IO.Path.Combine(destination, "producer-reference.json"), refBytes);
            File.WriteAllText(System.IO.Path.Combine(destination, "synthetic-provenance.json"), JsonSerializer.Serialize(new
            {
                synthetic = true, qualified = false, rendererExecuted = false, nativeDeviceCreated = false,
                sourceTreeCleanVerified = false, scope = "Actual typed C# serializer/exporter graph only; no physical/copy execution proof."
            }, VisualReferenceCapture.JsonOptions));
        }
    }
    [Fact]
    public void ExportFailureKeepsOriginalAndKnownCurrentPrefixWithoutSuccessfulTuple()
    {
        using var root = new Output(); var fixture = Fixture();
        string target = System.IO.Path.Combine(root.Path, "cpu-readback-00-world-bindings.json"); File.WriteAllText(target, "prior evidence");
        var failure = Assert.Throws<IOException>(() => ReferenceBoundInputProvenance.Write(fixture.Capture, root.Path, "cpu", 0,
            new string('b', 64), fixture.RecipeHash, fixture.RecipeBytes, fixture.Camera, fixture.Profile, new string('c', 64)));
        Assert.NotNull(failure); Assert.Equal("prior evidence", File.ReadAllText(target));
        using var prefix = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path, "cpu-readback-00-bound-input-capture-failure.json")));
        var value = prefix.RootElement.GetProperty("provenance");
        Assert.False(value.GetProperty("complete").GetBoolean()); Assert.Equal(JsonValueKind.Null, value.GetProperty("submission").ValueKind);
        Assert.Equal(2, value.GetProperty("events").GetArrayLength());
    }
    [Fact]
    public void EarlyFailureHasActualScopeFactsButNoClaimedCaptureOrNativeDevice()
    {
        using var root = new Output(); using var scope = new GraphicsBoundInputProvenanceScope();
        var original = new IOException("original initializer error");
        ReferenceBoundInputProvenance.RetainScopeFailure(original, scope, root.Path);
        using var json = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path, "bound-input-scope-failure.json")));
        Assert.False(json.RootElement.GetProperty("captureTokenIncluded").GetBoolean());
        Assert.False(json.RootElement.GetProperty("scope").GetProperty("deviceClaimed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("scope").GetProperty("deviceGeneration").ValueKind);
        ReferenceBoundInputProvenance.RetainCaptureFailure(original, root.Path, "unarmed", static () => null, null, null);
        Assert.False(File.Exists(System.IO.Path.Combine(root.Path, "unarmed-bound-input-capture-failure.json")));
    }
    [Fact]
    public void ExportRejectsIndependentJournalAndCompanionHeaderTampering()
    {
        var fixture = Fixture(); var original = fixture.Capture;
        GraphicsStageCapture[] invalid =
        [
            original with { BoundInputProvenance = original.BoundInputProvenance! with { ResourceGeneration = 99 } },
            original with { WorldBindings = original.WorldBindings! with { ResourceGeneration = 99 } },
            original with { WorldBindings = original.WorldBindings! with { DeviceGeneration = 99 } },
            original with { WorldBindings = original.WorldBindings! with { DeviceSession = new string('f', 32) } },
            original with { WorldBindings = original.WorldBindings! with { PresentedBefore = 9 } },
            original with { WorldBindings = original.WorldBindings! with { PresentedAfter = 12 } }
        ];
        foreach (var capture in invalid)
        {
            using var root = new Output();
            Assert.Throws<InvalidDataException>(() => ReferenceBoundInputProvenance.Write(capture, root.Path, "cpu", 0,
                new string('b', 64), fixture.RecipeHash, fixture.RecipeBytes, fixture.Camera, fixture.Profile, new string('c', 64)));
            Assert.False(File.Exists(System.IO.Path.Combine(root.Path, "cpu-readback-00-bound-input-provenance.json")));
        }
    }

    [Fact]
    public void ActualNonemptyExporterHonorsNearBoundCompactWireAndRefusesLargerBody()
    {
        var fixture = Fixture(); var original = fixture.Capture.BoundInputProvenance!;
        var entry = original.Events[0];
        long capacity = GraphicsBoundInputProvenance.WireCapacity - GraphicsBoundInputProvenance.EnvelopeCapacity;
        int entryBytes = GraphicsWorldBindingJournal.SerializeSidecar(entry).Length + 1;
        int count = checked((int)((capacity - GraphicsWorldBindingJournal.SerializeSidecar(original with { Events = [] }).Length - 8192) / entryBytes));
        Assert.InRange(count, 1, GraphicsBoundInputProvenance.EventCapacity);
        var near = fixture.Capture with { BoundInputProvenance = original with { Events = Enumerable.Repeat(entry, count).ToArray() } };
        using var root = new Output();
        var reference = ReferenceBoundInputProvenance.Write(near, root.Path, "near", 0, new string('b', 64),
            fixture.RecipeHash, fixture.RecipeBytes, fixture.Camera, fixture.Profile, new string('c', 64));
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(reference, VisualReferenceCapture.JsonOptions));
        long bytes = new FileInfo(System.IO.Path.Combine(root.Path, json.RootElement.GetProperty("file").GetString()!)).Length;
        Assert.InRange(bytes, capacity - 16384, GraphicsBoundInputProvenance.WireCapacity);
        var oversized = near with { BoundInputProvenance = original with { Events = Enumerable.Repeat(entry, count + 128).ToArray() } };
        Assert.Throws<InvalidDataException>(() => ReferenceBoundInputProvenance.Write(oversized, root.Path, "oversized", 0,
            new string('b', 64), fixture.RecipeHash, fixture.RecipeBytes, fixture.Camera, fixture.Profile, new string('c', 64)));
        Assert.False(File.Exists(System.IO.Path.Combine(root.Path, "oversized-readback-00-bound-input-provenance.json")));
        // This tests actual typed serialization capacity only; repeated synthetic events are not valid capture chronology.
    }

    [Fact]
    public void OversizedContextRetainsAcceptedCurrentPrefixAndPhaseNeutralSupplement()
    {
        using var root = new Output(); using var scope = new GraphicsBoundInputProvenanceScope(); var fixture = Fixture();
        var error = Assert.Throws<InvalidDataException>(() => ReferenceBoundInputProvenance.Write(fixture.Capture, root.Path, "context", 0,
            new string('b', 64), fixture.RecipeHash, new string('x', GraphicsBoundInputProvenance.EnvelopeCapacity + 1), fixture.Camera, fixture.Profile, new string('c', 64)));
        ReferenceBoundInputProvenance.RetainScopeFailure(error, scope, root.Path);
        using var prefix = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path, "context-readback-00-bound-input-capture-failure.json")));
        Assert.Equal(2, prefix.RootElement.GetProperty("provenance").GetProperty("events").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, prefix.RootElement.GetProperty("provenance").GetProperty("submission").ValueKind);
        using var supplement = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path, "bound-input-scope-failure.json")));
        Assert.Equal("bound-input-supplementary-scope-failure", supplement.RootElement.GetProperty("kind").GetString());
        Assert.False(supplement.RootElement.GetProperty("captureTokenIncluded").GetBoolean());
        Assert.False(supplement.RootElement.GetProperty("scope").TryGetProperty("captureEvidenceAvailable", out _));
    }

    private sealed class Output : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("forgeline-bound-cpu-");
        internal string Path => _root.FullName;
        public void Dispose() => _root.Delete(true);
    }
    private static (GraphicsStageCapture Capture, string RecipeHash, string RecipeBytes, RtsCameraState Camera, RenderQualityProfile Profile) Fixture()
    {
        byte[] vb = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray(), ib = new byte[64], pixels = new byte[64], constants = BitConverter.GetBytes(1f);
        var submission = new GraphicsFrameSubmission(578, 700, 1, 2);
        var identity = new GraphicsDeviceTelemetryIdentity("0000000000000001", 1, 0, 1, 1, Environment.ProcessId, 1, new string('e', 32));
        var pipeline = new GraphicsWorldPipelineBinding(new string('a', 64), new string('b', 64), "VSMain", "PSMain", "cpu-vs", "cpu-ps",
            [new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)], 1, 1, GraphicsPrimitiveTopology.TriangleList, GraphicsCullMode.None,
            true, false, false, false, true, false, GraphicsFrameTargetFormat.Rgba16Float);
        GraphicsWorldBindingDefinition[] definitions =
        [
            new(17, "pipeline", Pipeline: pipeline), new(18, "buffer", Buffer: new(128, GraphicsBufferMemory.Upload)),
            new(19, "buffer", Buffer: new(64, GraphicsBufferMemory.GpuLocal)),
            new(20, "texture", Texture: new(4, 4, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1), Descriptor: 9)
        ];
        GraphicsWorldBindingEvent[] journalEvents =
        [
            new(0, "Viewport", Viewport: [0, 0, 1, 1]), new(1, "Scissor", Scissor: [0, 0, 1, 1]), new(2, "Pipeline", IdentityId: 17, Emitted: true),
            new(3, "VertexBuffer", IdentityId: 18, Slot: 0, Stride: 12, Offset: 0, RangeBytes: 128),
            new(4, "IndexBuffer", IdentityId: 19, Offset: 0, RangeBytes: 64, IndexFormat: "SixteenBit"),
            new(5, "Texture", IdentityId: 20, Slot: 0, Emitted: true),
            new(6, "Constants", ConstantBytesBase64: Convert.ToBase64String(constants), ConstantSha256: Hash(constants), ConstantMode: "buffered",
                Implicit: new("buffered-vertex-constants", 2, 0, null, 0, null, 256)),
            new(7, "DrawIndexedInstanced", DrawArguments: [6, 1, 0, 0, 0], DrawOrdinal: 0)
        ];
        var journal = new GraphicsWorldBindingJournal(1, "opaque-world-binding-journal", true, null, 1, 2, identity.SessionId, 0,
            submission, 700, 10, 11, 1, 4096, definitions, journalEvents);
        var vbWrite = new GraphicsBoundSourceWrite(1, 1, "upload-set-data", 0, 128, 1, Hash(vb), 128, null, null);
        var ibWrite = new GraphicsBoundSourceWrite(2, 2, "initialized-static", 0, 64, 1, Hash(ib), 64, 2, null);
        var textureWrite = new GraphicsBoundSourceWrite(3, 3, "texture-mip", 0, 64, 1, Hash(pixels), 64, 3,
            new(0, 4, 4, 20, 16, 4, new(4, 4, 256, 1024, "R8G8B8A8_UNorm")));
        var constantsWrite = new GraphicsBoundSourceWrite(4, 4, "implicit-buffered-constants", 256, 4, 4, Hash(constants), 4, null, null);
        GraphicsBoundResource[] baseline =
        [
            new(1, "buffer", true, new(128, GraphicsBufferMemory.Upload), null, null, null, "created", null, true, [new(0, 128, 0, vbWrite)]),
            new(2, "buffer", true, new(64, GraphicsBufferMemory.GpuLocal), null, null, null, "initialized-static-gpu-local", "Index", true, [new(0, 64, 0, ibWrite)]),
            new(3, "texture", true, null, new(4, 4, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1), null, 9, "immutable-texture", null, true, [new(0, 64, 0, textureWrite)]),
            new(4, "implicit-constant-arena", false, new(4096*256, GraphicsBufferMemory.Upload), null,
                new("shadow-and-vertex-constant-arena", 2, 0, null, null, null, 0), null, "created", null, true, [])
        ];
        GraphicsBoundAssociation[] bindings =
        [
            new("vertex", 0, 18, 1, 0, 128, null, [1], []), new("index", null, 19, 2, 0, 64, null, [2], []),
            new("texture", 0, 20, 3, 0, 64, 0, [3], []), new("buffered-constants", null, null, 4, 256, 4, null, [4], [])
        ];
        var provenance = new GraphicsBoundInputProvenance(1, "bound-input-cpu-source-provenance", true, null, new string('b', 32),
            Environment.CurrentManagedThreadId, 1, identity.SessionId, 2, 0, 3, 4, submission, 700, 10, 11, baseline,
            [new(2, 0, 1, 512, 64, "signaled", 650, null), new(3, 0, 1, 1024, 1024, "signaled", 650, null)],
            [new(0, "source-write", Write: constantsWrite), new(1, "world-draw", Draw: new(0, 4, null, bindings))]);
        var capture = new GraphicsStageCapture(new byte[4], submission, 700, 0, 10, 11, identity,
            [new(GraphicsCaptureStages.FinalColor, new(1, 1, 256, 0, "R8G8B8A8_UNorm"), new byte[256], null)])
            { BufferCount = 3, WorldBindings = journal, BoundInputProvenance = provenance };
        var profile = new RenderQualitySettings().Resolve(true, false, 1, 1, 3);
        byte[] recipe = JsonSerializer.SerializeToUtf8Bytes(new { display = new { width = 1, height = 1 }, settings = profile.RequestedSettings, runtimeFingerprint = new string('b', 64) }, VisualReferenceCapture.JsonOptions);
        return (capture, Hash(recipe), Convert.ToBase64String(recipe), new(Vector3.Zero, .5f, .6f, 50, false, Vector2.Zero), profile);
    }
}
