using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using ForgeLine.Client;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceWorldInputDiagnosticsTests
{
    [Fact]
    public void NativeTwoSitesThenSingleSitePreserveUnknownSkippedPredicateAndTypedNulls()
    {
        using var data=new Fixture();var inspector=data.Create("native-reference");
        using(inspector.Arm(0)){NativeZero(inspector);data.Complete(inspector);var reference=inspector.Persist(data.Stage());Assert.NotNull(reference);}
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));
        var rows=json.RootElement.GetProperty("observations");Assert.Equal(5,rows.GetArrayLength());
        Assert.Equal(JsonValueKind.Null,rows[2].GetProperty("preparationInvoked").ValueKind);Assert.False(rows[3].GetProperty("preparationInvoked").GetBoolean());
        var facts=rows[4].GetProperty("stateAndActivityFacts");Assert.Equal(JsonValueKind.Null,facts.GetProperty("logicalMemory").ValueKind);Assert.Equal(JsonValueKind.Null,facts.GetProperty("instances").GetProperty("assets").ValueKind);
        Assert.False(facts.GetProperty("terrain").GetProperty("last").TryGetProperty("cpuSubmissionMilliseconds",out _));Assert.True(rows[4].GetProperty("timingFacts").TryGetProperty("terrain.last.cpuSubmissionMilliseconds",out _));
        Assert.Equal(0,json.RootElement.GetProperty("captured").GetProperty("frameIndex").GetInt32());Assert.Equal(1,facts.GetProperty("graphics").GetProperty("surface").GetProperty("frameIndex").GetInt32());
        data.Advance();
        using(inspector.Arm(1)){inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,true);data.Complete(inspector);inspector.Persist(data.Stage());}
        Assert.Equal(8,inspector.Count);Assert.False(inspector.Armed);
    }
    [Fact]
    public void StageEnabledButUnarmedSharedHelperCallsDoNotConsumeFixedSlotsOrReadFacts()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        for(int i=0;i<8;i++){inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,true);inspector.AfterCapture(null);Assert.Null(inspector.Persist(null));}
        Assert.Equal(0,data.Reads);Assert.Equal(0,inspector.Count);Assert.Empty(Directory.GetFiles(data.Root,"*world-inputs.json"));
        using(inspector.Arm(0)){Host(inspector);data.Complete(inspector);inspector.Persist(data.Stage());}
        inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);Assert.Equal(3,inspector.Count);
    }
    [Theory]
    [InlineData(0)][InlineData(129)][InlineData(int.MaxValue)]
    public void UnsupportedStorageRejectedBeforeAllocation(int repeats)
    {using var data=new Fixture();Assert.Throws<ArgumentOutOfRangeException>(()=>data.Create("native-reference",repeats));}
    [Fact]
    public void MissingDuplicateRegressingOwnerAndPredicatePhasesCannotPublishComplete()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        Assert.Throws<InvalidOperationException>(()=>inspector.Arm(1));
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidDataException>(()=>inspector.BeforeSite(WorldInputSite.HostReadbackPrepare));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,false));
            Exception? foreign=null;var thread=new Thread(()=>{try{inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true);}catch(Exception e){foreign=e;}});thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(5)));Assert.IsType<InvalidOperationException>(foreign);
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());Assert.Single(json.RootElement.GetProperty("observations").EnumerateArray());
    }
    [Theory]
    [InlineData("fence")][InlineData("slot")][InlineData("slot-in-range")][InlineData("resource")][InlineData("owner")][InlineData("present")]
    public void ActualCaptureTupleMustMatchPublicAfterFacts(string mutation)
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");using var arm=inspector.Arm(0);Host(inspector);
        var capture=mutation switch {"fence"=>data.Capture with {CompletedFence=1},"slot"=>data.Capture with {FrameIndex=3},"slot-in-range"=>data.Capture with {FrameIndex=2},"resource"=>data.Capture with {Submission=data.Capture.Submission with {ResourceGeneration=2}},"owner"=>data.Capture with {DeviceIdentity=data.Capture.DeviceIdentity with {ProcessId=1}},_=>data.Capture with {PresentedAfter=3}};
        Assert.Throws<InvalidDataException>(()=>data.Complete(inspector,capture));
    }
    [Fact]
    public void PartialPrefixRetainsOriginalExceptionAndCreateNewNeverOverwrites()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");var original=new InvalidOperationException("prepare failed");
        Action fail=()=>{using var arm=inspector.Arm(0);inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);throw original;};
        var caught=Assert.Throws<InvalidOperationException>(fail);Assert.Same(original,caught);
        byte[] prefix=File.ReadAllBytes(data.Output(0));
        using var json=JsonDocument.Parse(prefix);Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var second=data.Create("actual-render-host");using(var arm=second.Arm(0)){Host(second);data.Complete(second);Assert.Throws<IOException>(()=>second.Persist(data.Stage()));}
        Assert.Equal(prefix,File.ReadAllBytes(data.Output(0)));
    }
    [Fact]
    public void StageHashTamperCannotPublishACompleteInspector()
    {using var data=new Fixture();var inspector=data.Create("actual-render-host");using var arm=inspector.Arm(0);Host(inspector);data.Complete(inspector);Assert.Throws<InvalidDataException>(()=>inspector.Persist(new {file="stage.json",sha256=new string('0',64)}));}
    [Fact]
    public void RejectedOverlappingArmCannotResumeOriginalScopeIntoCompleteEvidence()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidOperationException>(()=>inspector.Arm(0));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true));
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }
    [Fact]
    public void FailedArmedPhaseCannotBeCorrectedIntoACompleteRecord()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,false));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true));
            Assert.Throws<InvalidDataException>(()=>data.Complete(inspector));
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Throws<InvalidOperationException>(()=>inspector.Arm(0));
    }
    [Fact]
    public void ReplayedCompletedTupleCannotBePublishedForNextRepetition()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0)){Host(inspector);data.Complete(inspector);inspector.Persist(data.Stage());}
        using(inspector.Arm(1)){Host(inspector);Assert.Throws<InvalidDataException>(()=>data.Complete(inspector));Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));}
    }
    [Fact]
    public void CompiledSampleAndPreparationHelpersHaveNoInspectorHooks()
    {
        var methods=AllTypes(typeof(VisualReferenceCapture)).SelectMany(t=>t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)).ToArray();
        foreach(string name in new[]{"g__PrepareTerrain","g__RecordFrame"})
        {var method=Assert.Single(methods,m=>m.Name.Contains(name,StringComparison.Ordinal));Assert.DoesNotContain(Calls(method),m=>m.DeclaringType==typeof(ReferenceWorldInputDiagnostics));}
        var capture=Assert.Single(methods,m=>m.Name.Contains("g__CaptureFrame",StringComparison.Ordinal));Assert.Single(Calls(capture),m=>m.Name.Contains("g__PrepareTerrain",StringComparison.Ordinal));
    }

    [Fact]
    public void WorldBindingStageExporterWritesActualSiblingHashGraphBeforeComparison()
    {
        using var data = new Fixture();
        var capture = JournalCapture();
        var inputs = JournalInputs();
        var reference = ReferenceStageDiagnostics.Write(capture, data.Root, "journal", 0, "native-reference",
            new string('b', 64), inputs.RecipeHash, inputs.RecipeBytes, inputs.Camera, inputs.Profile, new string('d', 64));
        using var refJson = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(reference, VisualReferenceCapture.JsonOptions));
        string file = refJson.RootElement.GetProperty("file").GetString()!;
        Assert.Equal(VisualReferenceCapture.FileHash(Path.Combine(data.Root, file)), refJson.RootElement.GetProperty("sha256").GetString());
        using var stage = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data.Root, file)));
        var journalRef = stage.RootElement.GetProperty("worldBindingJournal");
        Assert.Equal(1, stage.RootElement.GetProperty("worldBindingJournalVersion").GetInt32());
        string journalFile = Path.Combine(data.Root, journalRef.GetProperty("file").GetString()!);
        Assert.Equal(VisualReferenceCapture.FileHash(journalFile), journalRef.GetProperty("sha256").GetString());
        Assert.Equal(new FileInfo(journalFile).Length, journalRef.GetProperty("bytes").GetInt64());
        using var journal = JsonDocument.Parse(File.ReadAllBytes(journalFile));
        Assert.True(journal.RootElement.GetProperty("journal").GetProperty("complete").GetBoolean());
        Assert.Equal("effective-quality-profile", journal.RootElement.GetProperty("profileDomain").GetString());
        RetainSyntheticExporterFixture(data.Root, reference);
        byte[] original = File.ReadAllBytes(Path.Combine(data.Root, file));
        Assert.Throws<IOException>(() => ReferenceStageDiagnostics.Write(capture, data.Root, "journal", 0, "native-reference",
            new string('b', 64), inputs.RecipeHash, inputs.RecipeBytes, inputs.Camera, inputs.Profile, new string('d', 64)));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(data.Root, file)));
        using var prefix = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data.Root, "journal-world-bindings-failure.json")));
        Assert.False(prefix.RootElement.GetProperty("journal").GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, prefix.RootElement.GetProperty("journal").GetProperty("submission").ValueKind);
    }

    [Fact]
    public void WorldBindingPrefixWriteOrContextFailureCannotReplaceOriginal()
    {
        using var data = new Fixture();
        var original = new IOException("original rendering failure");
        var capture = JournalCapture();
        string prefix = Path.Combine(data.Root, "journal-world-bindings-failure.json");
        File.WriteAllText(prefix, "previous evidence");
        Action fail = () =>
        {
            try { throw original; }
            catch (Exception error)
            {
                ReferenceStageDiagnostics.RetainWorldBindingFailure(error, data.Root, "journal", static () => new { synthetic = true }, capture);
                throw;
            }
        };
        Assert.Same(original, Assert.Throws<IOException>(fail));
        Assert.Equal("previous evidence", File.ReadAllText(prefix));
        ReferenceStageDiagnostics.RetainWorldBindingFailure(original, data.Root, "context",
            static () => throw new InvalidOperationException("secondary context failure"), capture);
        using var unavailable = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data.Root, "context-world-bindings-failure.json")));
        Assert.Equal(JsonValueKind.Null, unavailable.RootElement.GetProperty("context").ValueKind);
        Assert.Equal(JsonValueKind.String, unavailable.RootElement.GetProperty("contextUnavailable").ValueKind);
        Assert.False(unavailable.RootElement.GetProperty("journal").GetProperty("complete").GetBoolean());
        ReferenceStageDiagnostics.RetainWorldBindingFailure(original, data.Root, "unarmed", static () => null);
        Assert.False(File.Exists(Path.Combine(data.Root, "unarmed-world-bindings-failure.json")));
    }


    [Fact]
    public void KnownCurrentCaptureWinsOverReusedExceptionAndUnboundPrefixIsUnavailable()
    {
        using var data = new Fixture();
        var error = new IOException("reused render exception");
        var current = JournalCapture();
        var stale = current.WorldBindings! with { DeviceSession = "old capture" };
        using var attempt = GraphicsWorldBindingJournal.BeginAttempt();
        typeof(GraphicsWorldBindingAttempt).GetMethod("ClaimCurrent", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        typeof(GraphicsWorldBindingJournal).GetMethod("AttachFailure", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [error, stale, attempt]);
        ReferenceStageDiagnostics.RetainWorldBindingFailure(error, data.Root, "current", static () => null, current, attempt);
        using var retained = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(data.Root, "current-world-bindings-failure.json")));
        var journal = retained.RootElement.GetProperty("journal");
        Assert.Equal(current.WorldBindings.DeviceSession, journal.GetProperty("deviceSession").GetString());
        Assert.Equal(JsonValueKind.Null, journal.GetProperty("submission").ValueKind);
        ReferenceStageDiagnostics.RetainWorldBindingFailure(error, data.Root, "prearm", static () => null);
        Assert.False(File.Exists(Path.Combine(data.Root, "prearm-world-bindings-failure.json")));
    }

    [Fact]
    public void OversizedFailureContextRetainsCompleteAcceptedPrefixAsUnavailableContext()
    {
        using var data = new Fixture();
        var capture = JournalCapture();
        var original = new InvalidOperationException("original render failure");
        ReferenceStageDiagnostics.RetainWorldBindingFailure(original, data.Root, "large-context",
            static () => new { payload = new string('\u03bb', GraphicsWorldBindingJournal.EnvelopeCapacity) }, capture);
        string file = Path.Combine(data.Root, "large-context-world-bindings-failure.json");
        Assert.True(new FileInfo(file).Length <= GraphicsWorldBindingJournal.PayloadCapacity);
        using var value = JsonDocument.Parse(File.ReadAllBytes(file));
        Assert.Equal(JsonValueKind.Null, value.RootElement.GetProperty("context").ValueKind);
        Assert.Equal(JsonValueKind.String, value.RootElement.GetProperty("contextUnavailable").ValueKind);
        Assert.False(value.RootElement.GetProperty("journal").GetProperty("complete").GetBoolean());
        Assert.Equal(capture.WorldBindings!.Events.Length, value.RootElement.GetProperty("journal").GetProperty("events").GetArrayLength());
        Assert.Equal(capture.WorldBindings.Definitions.Length, value.RootElement.GetProperty("journal").GetProperty("definitions").GetArrayLength());
    }


    [Fact]
    public void NonemptyTypedWorldJournalExporterPreservesNestedWireShapes()
    {
        using var data = new Fixture();
        var capture = JournalCapture();
        var pipeline = new GraphicsWorldPipelineBinding(new string('a', 64), new string('b', 64),
            "VSMain", "PSMain", "synthetic-vertex.hlsl", "synthetic-pixel.hlsl",
            [new GraphicsVertexElement("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)],
            1, 1, GraphicsPrimitiveTopology.TriangleList, GraphicsCullMode.None, true, false,
            false, false, true, true, GraphicsFrameTargetFormat.Rgba16Float);
        byte[] constants = BitConverter.GetBytes(1f);
        byte[] shadow = new byte[96];
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        GraphicsWorldBindingDefinition[] definitions =
        [
            new(1, "pipeline", Pipeline: pipeline),
            new(2, "buffer", Buffer: new GraphicsBufferDescription(128, GraphicsBufferMemory.Upload)),
            new(3, "buffer", Buffer: new GraphicsBufferDescription(64, GraphicsBufferMemory.GpuLocal)),
            new(4, "texture", Texture: new GraphicsTextureDescription(4, 4, GraphicsTextureFormat.Bc7Unorm,
                GraphicsTextureColorSpace.Srgb, 1) { DeferUploadWait = true }, Descriptor: 9)
        ];
        GraphicsWorldBindingEvent[] events =
        [
            new(0, "Viewport", Viewport: [float.MaxValue, float.MinValue, 1, 1]),
            new(1, "Scissor", Scissor: [0, 0, 1, 1]),
            new(2, "ShadowFrame", ConstantBytesBase64: Convert.ToBase64String(shadow), ConstantSha256: Hash(shadow),
                ConstantMode: "implicit-shadow-frame", Implicit: new("directional-shadow-frame", 1, 0, null, null, null, 0)),
            new(3, "Pipeline", IdentityId: 1, Emitted: true),
            new(4, "VertexBuffer", IdentityId: 2, Slot: 0, Stride: 12, Offset: 0, RangeBytes: 128),
            new(5, "IndexBuffer", IdentityId: 3, Offset: 0, RangeBytes: 64, IndexFormat: "SixteenBit"),
            new(6, "Texture", IdentityId: 4, Slot: 0, Emitted: true),
            new(7, "Constants", ConstantBytesBase64: Convert.ToBase64String(constants), ConstantSha256: Hash(constants),
                ConstantMode: "buffered", Implicit: new("buffered-vertex-constants", 1, 0, null, 0, null, 256)),
            new(8, "ShadowReceiver", Emitted: true, Implicit: new("directional-shadow-receiver", 1, 0, 17, 2, 15, 0, 3)),
            new(9, "DrawIndexedInstanced", DrawArguments: [6, 2, 1, -2, 3], DrawOrdinal: 0)
        ];
        capture = capture with { WorldBindings = capture.WorldBindings! with
            { DrawCount = 1, Definitions = definitions, Events = events } };
        var inputs = JournalInputs();
        object reference = ReferenceStageDiagnostics.Write(capture, data.Root, "nonempty", 0, "native-reference",
            new string('b', 64), inputs.RecipeHash, inputs.RecipeBytes, inputs.Camera, inputs.Profile, new string('d', 64));
        byte[] wire = File.ReadAllBytes(Path.Combine(data.Root, "nonempty-readback-00-world-bindings.json"));
        using var document = JsonDocument.Parse(wire);
        var journal = document.RootElement.GetProperty("journal");
        Assert.Equal(1, journal.GetProperty("drawCount").GetInt32());
        Assert.Equal(float.MaxValue, journal.GetProperty("events")[0].GetProperty("viewport")[0].GetSingle());
        Assert.Equal(float.MinValue, journal.GetProperty("events")[0].GetProperty("viewport")[1].GetSingle());
        Assert.Equal("Bc7Unorm", journal.GetProperty("definitions")[3].GetProperty("texture").GetProperty("format").GetString());
        Assert.Equal("Float3", journal.GetProperty("definitions")[0].GetProperty("pipeline").GetProperty("vertexElements")[0].GetProperty("format").GetString());
        Assert.Equal("DrawIndexedInstanced", journal.GetProperty("events")[9].GetProperty("kind").GetString());
        RetainSyntheticExporterFixture(data.Root, reference, "FORGELINE_WORLD_BINDING_NONEMPTY_FIXTURE_OUTPUT");
    }

    private static (RenderQualityProfile Profile, string RecipeBytes, string RecipeHash, RtsCameraState Camera) JournalInputs()
    {
        var profile = new RenderQualitySettings { Shadows = DirectionalShadowQuality.Low }.Resolve(true, false, 1, 1, 3);
        byte[] recipe = JsonSerializer.SerializeToUtf8Bytes(new
        {
            display = new { width = 1, height = 1 }, settings = profile.RequestedSettings, runtimeFingerprint = new string('b', 64)
        }, VisualReferenceCapture.JsonOptions);
        return (profile, Convert.ToBase64String(recipe),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(recipe)),
            new(System.Numerics.Vector3.Zero, .5f, .6f, 50, false, System.Numerics.Vector2.Zero));
    }

    private static void RetainSyntheticExporterFixture(string root, object reference,
        string environmentVariable = "FORGELINE_WORLD_BINDING_CPU_FIXTURE_OUTPUT")
    {
        string? destination = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(destination)) return;
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Synthetic serializer fixture output must be fresh.");
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.GetFiles(root))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: false);
        File.WriteAllBytes(Path.Combine(destination, "stage-reference.json"),
            JsonSerializer.SerializeToUtf8Bytes(reference, VisualReferenceCapture.JsonOptions));
        File.WriteAllText(Path.Combine(destination, "synthetic-provenance.json"), JsonSerializer.Serialize(new
        {
            kind = "synthetic-cpu-exporter-fixture", synthetic = true, qualified = false, sourceTreeCleanVerified = false,
            targetDeviceCreated = false, rendererExecuted = false,
            scope = "Actual C# DTO/exporter wire shape only; no GPU, physical or clean-source evidence."
        }, VisualReferenceCapture.JsonOptions));
    }

    [Fact]
    public void StorageEstimateReservesEveryJournalCapThrough128Repeats()
    {
        var capture = JournalCapture();
        var footprints = capture.Stages.Select(stage => new GraphicsStageFootprint(stage.Stage, stage.Layout,
            (ulong)stage.RawBytes.Length, null)).ToArray();
        var storage = ReferenceStageDiagnostics.EstimateStorage(footprints, 128);
        Assert.Equal(128UL * (ulong)GraphicsWorldBindingJournal.PayloadCapacity, storage.JournalBytes);
        ulong raw = footprints.Aggregate(0UL, (total, stage) => total + stage.RawBytes);
        Assert.Equal(raw * 128 + 4UL * 128 * 2 + storage.JournalBytes + 64UL * 1024 * 1024, storage.EstimatedBytes);
        Assert.Equal((storage.EstimatedBytes * 3 + 1) / 2, storage.RequiredBytes);
    }

    private static GraphicsStageCapture JournalCapture()
    {
        var submission = new GraphicsFrameSubmission(1, 5, 1, 1);
        var identity = new GraphicsDeviceTelemetryIdentity("0000000000000001", 1, 0, 1, 1, Environment.ProcessId, 1, new string('e', 32));
        var journal = new GraphicsWorldBindingJournal(1, "opaque-world-binding-journal", true, null, 1, 1, identity.SessionId,
            0, submission, 5, 0, 1, 0, 4096, [], []);
        var stages = new[] { GraphicsCaptureStages.ShadowDepth, GraphicsCaptureStages.WorldDepth, GraphicsCaptureStages.SceneLinear,
            GraphicsCaptureStages.ComposedColor, GraphicsCaptureStages.FinalColor }.Select(stage => new GraphicsStageReadback(stage,
                new GraphicsTextureReadbackLayout(stage == GraphicsCaptureStages.ShadowDepth ? 1024 : 1, stage == GraphicsCaptureStages.ShadowDepth ? 1024 : 1, stage == GraphicsCaptureStages.ShadowDepth ? 4096 : 256, 0, stage == GraphicsCaptureStages.SceneLinear ? "R16G16B16A16_Float" :
                    stage is GraphicsCaptureStages.ShadowDepth or GraphicsCaptureStages.WorldDepth ? "R32_Float" : "R8G8B8A8_UNorm"), new byte[stage == GraphicsCaptureStages.ShadowDepth ? 4 * 1024 * 1024 : 256], null)).ToArray();
        return new(new byte[4], submission, 5, 0, 0, 1, identity, stages) { BufferCount = 3, WorldBindings = journal };
    }

    private static void NativeZero(ReferenceWorldInputDiagnostics inspector)
    {inspector.BeforeSite(WorldInputSite.NativeFixedPoseRestore);inspector.AfterSite(WorldInputSite.NativeFixedPoseRestore,true);inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,false);}
    private static void Host(ReferenceWorldInputDiagnostics inspector){inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true);}
    private static IEnumerable<Type> AllTypes(Type type)=>new[]{type}.Concat(type.GetNestedTypes(BindingFlags.NonPublic|BindingFlags.Public).SelectMany(AllTypes));
    private static readonly Dictionary<short,OpCode> s_codes=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)!).GroupBy(c=>c.Value).ToDictionary(g=>g.Key,g=>g.First());
    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        byte[] il=method.GetMethodBody()?.GetILAsByteArray() ?? [];int p=0;
        while(p<il.Length){short code=il[p++];if(code==0xfe)code=(short)(0xfe00|il[p++]);var op=s_codes[code];if(op.OperandType==OperandType.InlineMethod && method.Module.ResolveMethod(BitConverter.ToInt32(il,p)) is { } called)yield return called;
            p+=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,p),_=>4};}
    }
    private sealed class Fixture:IDisposable
    {
        private readonly DirectoryInfo _directory=Directory.CreateTempSubdirectory("forgeline-world-input-");
        internal string Root=>_directory.FullName;internal int Reads;private bool _afterCapture;
        internal GraphicsStageCapture Capture {get;private set;}
        internal Fixture()
        {var identity=new GraphicsDeviceTelemetryIdentity("0000000000000001",1,0,1,1,Environment.ProcessId,1,new string('e',32));Capture=new([],new(1,5,1,1),5,0,0,1,identity,[]){BufferCount=3};File.WriteAllText(Path.Combine(Root,"stage.json"),"{}");}
        internal ReferenceWorldInputDiagnostics Create(string producer,int repeats=2)=>new(Root,"case",producer,new string('b',64),new string('c',64),"e30=",new {quality="cpu-fixture"},repeats,Facts,static()=>default);
        internal void Complete(ReferenceWorldInputDiagnostics inspector,GraphicsStageCapture? capture=null){_afterCapture=true;try{inspector.AfterCapture(capture ?? Capture);}finally{_afterCapture=false;}}
        internal void Advance()=>Capture=Capture with {Submission=new(Capture.Submission.Submission+1,Capture.Submission.SubmittedFence+3,1,1),CompletedFence=Capture.Submission.SubmittedFence+3,FrameIndex=(Capture.FrameIndex+1)%3,PresentedBefore=Capture.PresentedAfter,PresentedAfter=Capture.PresentedAfter+1};
        internal object Stage()=>new {file="stage.json",sha256=VisualReferenceCapture.FileHash(Path.Combine(Root,"stage.json"))};
        internal string Output(int repeat)=>Path.Combine(Root,"case-readback-"+repeat.ToString("D2",System.Globalization.CultureInfo.InvariantCulture)+"-world-inputs.json");
        private WorldInputFacts Facts(){Reads++;var g=new GraphicsDiagnostics(new("CPU-only",0,true,"fixture",false),new(2,2,3,_afterCapture ? (Capture.FrameIndex+1)%3 : Capture.FrameIndex,false,"fixture"){PresentedFrameCount=Capture.PresentedAfter});return new(new(true,default,default,default),new(true,default,default,default,default,default,null,default),default,g,null,null,Capture.Submission,Capture.DeviceIdentity,null);}
        public void Dispose()=>_directory.Delete(recursive:true);
    }
}
