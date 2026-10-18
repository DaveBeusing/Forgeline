using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Audio;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.UI;
using Xunit;
using static ForgeLine.Client.WaveOutUiVoice;

namespace ForgeLine.Client.Tests;

public sealed class WorldAudioMixerTests
{
    private static AudioListener Listener => new(Vector3.Zero, Vector3.UnitX, 300);
    private static readonly WorldAudioSettings Settings = new(true, 1, 1, 1, 1);
    private static WorldPcmClip Clip(bool loop = false, int channels = 1, int frames = 48000)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        w.Write(0x46464952u); w.Write(36 + frames * channels * 2); w.Write(0x45564157u);
        w.Write(0x20746d66u); w.Write(16); w.Write((ushort)1); w.Write((ushort)channels);
        w.Write(48000); w.Write(48000 * channels * 2); w.Write((ushort)(channels * 2)); w.Write((ushort)16);
        w.Write(0x61746164u); w.Write(frames * channels * 2);
        for (int i = 0; i < frames; i++) for (int c = 0; c < channels; c++) w.Write((short)(12000 * Math.Sin(i * .13 + c * .3)));
        stream.Position = 0; return WorldPcmClip.Read(stream, loop ? 0 : null, loop ? frames : null);
    }
    private static WorldPcmMixer Mixer()
    {
        var m = new WorldPcmMixer(); m.SetClip(1, Clip()); m.SetClip(2, Clip(true)); m.Configure(Listener, Settings); return m;
    }
    private static AudioMixSource Source(int clip = 1, float x = 0, AudioMixBus bus = AudioMixBus.World, int priority = 0, uint entity = 1) =>
        new(clip, new(entity, 1), entity, new(x, 0, 0), true, bus, priority);
    private static long Energy(short[] b, int channel) { long result = 0; for (int i = channel; i < b.Length; i += 2) result += Math.Abs((int)b[i]); return result; }

    [Theory]
    [InlineData(-100, true)] [InlineData(100, false)]
    public void SourcePansToTheCameraSideAndRotationReversesIt(float x, bool left)
    {
        var m = Mixer(); var b = new short[960]; m.Play(Source(x: x)); for (int i = 0; i < 8; i++) m.Render(b);
        Assert.True(left ? Energy(b, 0) > Energy(b, 1) * 10 : Energy(b, 1) > Energy(b, 0) * 10);
        m.Configure(Listener with { Right = -Vector3.UnitX }, Settings); m.Render(b);
        Assert.True(left ? Energy(b, 1) > Energy(b, 0) * 10 : Energy(b, 0) > Energy(b, 1) * 10);
    }
    [Fact]
    public void AttenuationRejectsDistantAndNonfiniteSources()
    {
        var m = Mixer(); Assert.True(m.Score(Source(x: 5)) > m.Score(Source(x: 150)));
        Assert.Equal(0, m.Score(Source(x: 300))); Assert.Equal(0, m.Score(Source(x: float.NaN)));
        m.Play(Source(x: float.PositiveInfinity)); Assert.Equal(0, m.ActiveVoices);
        m.Configure(Listener with { Right = Vector3.Zero }, Settings); m.Play(Source()); Assert.Equal(0, m.ActiveVoices);
    }
    [Fact]
    public void CombatStealsDecorativeVoicesAndAlertsHaveReservedSlots()
    {
        var m = Mixer(); for (uint i = 1; i <= 1000; i++) m.Play(Source(entity: i));
        Assert.Equal(24, m.ActiveVoices);
        m.Play(Source(priority: 1)); Assert.Equal(24, m.ActiveVoices);
        for (uint i = 1; i <= 1000; i++) m.Play(Source(bus: AudioMixBus.Alerts, priority: 2, entity: i));
        Assert.Equal(28, m.ActiveVoices);
        for (uint i = 1; i <= 24; i++) m.Play(Source(priority: 1, entity: i));
        long before = m.DroppedShots; for (uint i = 1; i <= 1000; i++) m.Play(Source(entity: i));
        Assert.True(m.DroppedShots >= before + 1000);
    }
    [Fact]
    public void NearestLoopsAreSelectedAndReplacementCrossfadesWithinFixedStorage()
    {
        var m = Mixer(); var candidates = Enumerable.Range(1, 1024).Select(i => Source(2, i % 280, entity: (uint)i)).ToArray();
        m.SetLoops(candidates); Assert.Equal(16, m.ActiveLoops);
        var block = new short[960]; for (int i = 0; i < 10; i++) m.Render(block);
        candidates = [Source(2, 0, entity: 2000)]; m.SetLoops(candidates); Assert.Equal(1, m.ActiveLoops);
        Assert.InRange(m.ActiveVoices, 2, 64); for (int i = 0; i < 10; i++) m.Render(block);
        Assert.Equal(1, m.ActiveVoices); m.SetLoops([]); for (int i = 0; i < 10; i++) m.Render(block); Assert.Equal(0, m.ActiveVoices);
    }
    [Fact]
    public void RepeatedLoopSnapshotsPreserveSamplePositionAndWarmMixingAllocatesNothing()
    {
        var a = Mixer(); var b = Mixer(); AudioMixSource[] source = [Source(2)]; a.SetLoops(source); b.SetLoops(source);
        short[] x = new short[960], y = new short[960];
        for (int i = 0; i < 512; i++) { a.SetLoops(source); a.Render(x); b.Render(y); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { a.SetLoops(source); a.Render(x); b.Render(y); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(y, x); Assert.Equal(0, bytes);
    }
    [Fact]
    public void StereoAmbienceRetainsChannelsAndAllBusControlsSilenceTheirVoices()
    {
        var m = Mixer(); m.SetClip(3, Clip(true, 2));
        var s = Source(3, bus: AudioMixBus.Ambience) with { Spatial = false };
        m.SetLoops([s]); var block = new short[960]; for (int i = 0; i < 10; i++) m.Render(block);
        Assert.True(Energy(block, 0) > 0); Assert.NotEqual(block[150], block[151]);
        m.Configure(Listener, Settings with { Ambience = 0 }); for (int i = 0; i < 10; i++) m.Render(block);
        Assert.All(block, sample => Assert.Equal(0, sample));
        m.Configure(Listener, Settings with { Enabled = false }); Assert.Equal(0, m.ActiveVoices);
    }
    [Fact]
    public void MaximumConcurrentMixRemainsFiniteAndBelowPcmHeadroom()
    {
        var m = Mixer(); for (uint i = 1; i <= 1000; i++) { m.Play(Source(entity: i)); m.Play(Source(bus: AudioMixBus.Alerts, entity: i)); }
        m.SetLoops(Enumerable.Range(1, 1024).Select(i => Source(2, entity: (uint)i)).ToArray());
        var b = new short[960]; for (int i = 0; i < 12; i++) m.Render(b);
        Assert.All(b, s => Assert.InRange((int)s, -29490, 29490)); Assert.True(Energy(b, 0) > 0); Assert.True(m.ActiveVoices <= 64);
        m.Clear(); m.Render(b); Assert.All(b, s => Assert.Equal(0, s));
    }
    private static PresentationSnapshot Packet(long now, ulong tick = 1, ulong session = 1, uint player = 1, ulong sequence = 1, bool loops = true, bool terminal = false)
    {
        var exp = default(PlayerExperienceSnapshot) with { Player = new(player), Tick = new(tick), MatchStatus = terminal ? PlayerMatchStatus.Victory : PlayerMatchStatus.Active };
        var audio = new WorldAudioSnapshot(new(session), new(player), new(tick),
            [new(sequence, new(tick), new(tick + 5), terminal ? WorldAudioCue.EventsVictory : WorldAudioCue.CombatRifleFire01, new(1, 1), Vector3.Zero, true, now)],
            loops && !terminal ? [new(WorldAudioCue.VehiclesScoutIdleLoop, new(1, 1), Vector3.Zero)] : [], default, now);
        return new(new(tick), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(session), playerExperience: exp) { WorldAudio = audio };
    }
    private static WorldPcmMixer PlaybackMixer()
    { var m = Mixer(); m.SetClip((int)WorldAudioCue.VehiclesScoutIdleLoop, Clip(true)); return m; }
    [Fact]
    public void ScopeMuteExpiryAndRollbackStopVoicesWithoutReplayingHistory()
    {
        var m = PlaybackMixer(); var p = new WorldAudioPlayback(m, new SimulationSessionId(1)); var packet = Packet(0);
        p.Update(packet, Listener, Settings, true, 0); Assert.Equal(1, m.ActiveLoops);
        p.Update(Packet(0, tick: 2, sequence: 2), Listener, Settings, true, 0); Assert.Equal(2, m.ActiveVoices);
        p.Update(Packet(0, tick: 2, session: 2), Listener, Settings, true, 0); Assert.Equal(0, m.ActiveVoices);
        p.Update(packet, Listener, Settings, true, 0); Assert.Equal(0, m.ActiveVoices); // tick rollback
        packet = Packet(0, tick: 3, sequence: 3);
        p.Update(packet, Listener, Settings, false, 0); p.Update(packet, Listener, Settings, true, 0); Assert.Equal(1, m.ActiveVoices);
        p.Update(packet, Listener, Settings, true, Stopwatch.Frequency); Assert.False(p.Active); Assert.Equal(0, m.ActiveVoices);
        p.Update(Packet(0, tick: 4, player: 2), Listener, Settings, true, 0); Assert.False(p.Active);
    }
    [Fact]
    public void DuplicatePacketsDoNotReplayShotsAndStaleEventsCannotStart()
    {
        var m = PlaybackMixer(); var p = new WorldAudioPlayback(m); p.Update(Packet(0), Listener, Settings, true, 0);
        var packet = Packet(0, tick: 2, sequence: 2); p.Update(packet, Listener, Settings, true, 0);
        Assert.Equal(2, m.ActiveVoices); p.Update(packet, Listener, Settings, true, 0); Assert.Equal(2, m.ActiveVoices);
        p.Update(Packet(0, tick: 3, sequence: 3), Listener, Settings, true, Stopwatch.Frequency); Assert.Equal(0, m.ActiveVoices);
    }
    [Fact]
    public void FreshResultStopsWorldVoicesAndCanFinishBeyondItsStartDeadline()
    {
        var m = PlaybackMixer(); m.SetClip((int)WorldAudioCue.EventsVictory, Clip());
        var p = new WorldAudioPlayback(m); p.Update(Packet(0), Listener, Settings, true, 0);
        var result = Packet(0, tick: 2, sequence: 2, terminal: true);
        p.Update(result, Listener, Settings, true, 0); Assert.Equal(1, m.ActiveVoices); Assert.Equal(0, m.ActiveLoops);
        p.Update(result, Listener, Settings, true, Stopwatch.Frequency / 2); Assert.True(p.Active); Assert.Equal(1, m.ActiveVoices);
        var block = new short[960]; for (int i = 0; i < 101; i++) m.Render(block);
        p.Update(result, Listener, Settings, true, Stopwatch.Frequency * 2); Assert.False(p.Active); Assert.Equal(0, m.ActiveVoices);
        p.Update(result, Listener, Settings, false, 0); p.Update(result, Listener, Settings, true, Stopwatch.Frequency); Assert.Equal(0, m.ActiveVoices);
    }
    [Fact]
    public void ThousandSourceSelectionAndFullMixStayAllocationFreeAfterWarmup()
    {
        var m = Mixer(); var sources = Enumerable.Range(1, 1024).Select(i => Source(2, i % 280, entity: (uint)i)).ToArray();
        var block = new short[960];
        void Step() { m.SetLoops(sources); for (uint i = 1; i <= 24; i++) m.Play(Source(priority: 1, entity: i)); for (uint i = 1; i <= 4; i++) m.Play(Source(bus: AudioMixBus.Alerts, entity: i)); m.Render(block); }
        double TimedStep() { long start = Stopwatch.GetTimestamp(); Step(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
        _ = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) _ = TimedStep();
        double[] times = new double[64]; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < times.Length; i++) times[i] = TimedStep();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.True(m.ActiveVoices <= 64); Assert.True(Energy(block, 0) > 0);
        Assert.All(block, sample => Assert.InRange((int)sample, -29490, 29490)); Array.Sort(times);
        string? report = Environment.GetEnvironmentVariable("FORGELINE_WORLD_MIX_MEASUREMENT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, JsonSerializer.Serialize(new { Sources = sources.Length, WarmupBlocks = 512, MeasuredBlocks = times.Length, WorldShotsPerBlock = 24, AlertShotsPerBlock = 4, BlockFrames = 480, MedianMilliseconds = times[32], P95Milliseconds = times[60], ManagedBytes = allocated, Voices = m.ActiveVoices, Stage = "Source selection and PCM mixing only; excludes driver and acoustic latency" }));
    }
    [Fact]
    public void RuntimeManifestLoadsAllFiftySixClipsAndChecksSourceHashes()
    {
        string root = RepositoryRoot(); var m = new WorldPcmMixer(); Assert.Equal(56, WorldAudioClips.Load(m, Path.Combine(root, "assets/source/audio")));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "assets/source/audio/playback.json")));
        using var source = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "assets/source/audio/library.json")));
        var originals = source.RootElement.GetProperty("assets").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!);
        foreach (var entry in manifest.RootElement.GetProperty("clips").EnumerateArray())
        {
            var original = originals[entry.GetProperty("id").GetString()!];
            foreach (string key in new[] { "sha256", "loop_start_frame", "loop_end_frame_exclusive" }) Assert.Equal(original.GetProperty(key).ToString(), entry.GetProperty(key).ToString());
        }
    }
    [Fact]
    public void CorruptedIndividualClipIsIsolatedAndCannotReachTheMixer()
    {
        string directory = Path.Combine(Path.GetTempPath(), "forgeline-world-audio-" + Guid.NewGuid());
        string source = Path.Combine(RepositoryRoot(), "assets/source/audio"); Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(source, "playback.json"), Path.Combine(directory, "playback.json"));
            foreach (var definition in WorldAudioCatalog.All)
            {
                string relative = definition.AssetId[6..].Replace('.', Path.DirectorySeparatorChar) + ".wav";
                string path = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(Path.Combine(source, relative), path);
            }
            File.WriteAllBytes(Path.Combine(directory, "combat/rifle_fire_01.wav"), new byte[44]);
            var mixer = new WorldPcmMixer(); Assert.Equal(55, WorldAudioClips.Load(mixer, directory)); mixer.Configure(Listener, Settings);
            mixer.Play(Source()); Assert.Equal(0, mixer.ActiveVoices);
        }
        finally { Directory.Delete(directory, true); }
    }
    internal static string RepositoryRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "assets/source/audio"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException();
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-.01f)] [InlineData(1.01f)]
    public void InvalidPersistedVolumeIsRejected(float value) => Assert.Throws<InvalidDataException>(() => (Settings with { World = value }).Validate());
    [Fact]
    public void AudioMenuControlsRoundTripAndRetainIndependentInterfacePreference()
    {
        var defaults = new ClientUserSettings(); var snapshot = ClientSettingsFrontendAdapter.ToSnapshot(defaults);
        var model = new SettingsModel(snapshot); var interaction = new SettingsInteractionModel(); interaction.Focus(FrontendSettingsField.WorldSoundEnabled);
        interaction.Adjust(model, 1); Assert.False(model.Settings.WorldAudio.Enabled);
        interaction.Focus(FrontendSettingsField.MasterSoundVolume); interaction.Adjust(model, -1); Assert.True(model.Settings.WorldAudio.Master < defaults.WorldAudio.Master);
        Assert.Equal(snapshot.FeedbackSettings, model.Settings.FeedbackSettings);
        string json = JsonSerializer.Serialize(defaults with { WorldAudio = model.Settings.WorldAudio });
        Assert.Equal(model.Settings.WorldAudio, JsonSerializer.Deserialize<ClientUserSettings>(json)!.WorldAudio);
        Assert.Equal(WorldAudioSettings.Default, JsonSerializer.Deserialize<ClientUserSettings>("{}")!.WorldAudio);
    }

    [Fact]
    public void ThreeNativeBuffersRemainOwnedUntilDriverReturnsThem()
    {
        var api = new FakeWave(); var output = new WaveOutWorldOutput(api, () => 0); var block = new short[960];
        output.Write(block); output.Write(block); output.Write(block); Assert.False(output.Ready);
        api.Complete = true; Assert.True(output.Ready); output.Write(block);
        api.RefuseReturn = true; output.Dispose(); Assert.True(output.OwnsNativeResources);
        api.RefuseReturn = false; output.Dispose(); Assert.False(output.OwnsNativeResources); Assert.Equal(4, api.Writes);
    }
    [Fact]
    public void StalledNativeOutputFailsWithinItsDeadline()
    {
        long clock = 0; var api = new FakeWave(); using var output = new WaveOutWorldOutput(api, () => clock); var block = new short[960];
        for (int i = 0; i < 3; i++) output.Write(block); clock = Stopwatch.Frequency + 1;
        Assert.Throws<InvalidOperationException>(() => { _ = output.Ready; });
    }
    [Fact]
    public void MissingDeviceRetriesAreBoundedAndShutdownDoesNotWaitForTheRenderer()
    {
        int attempts = 0; using var owner = new WaveOutWorldAudio(() => { Interlocked.Increment(ref attempts); throw new InvalidOperationException("Unavailable"); }, _ => { });
        long now = Stopwatch.GetTimestamp(); owner.Publish(Packet(now), Listener, Settings, true);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref attempts) == 1, TimeSpan.FromSeconds(5)));
        Assert.InRange(Volatile.Read(ref attempts), 1, 4); owner.Dispose(); Assert.True(owner.WaitForStop(TimeSpan.FromSeconds(5))); Assert.True(owner.Diagnostics.Stopped);
    }
    [Fact]
    public void OwnerHeartbeatStopsNativeQueueAfterRenderPublicationStalls()
    {
        var output = new FakeOutput(); var clip = Clip(true); using var owner = new WaveOutWorldAudio(() => output, m => m.SetClip((int)WorldAudioCue.VehiclesScoutIdleLoop, clip));
        long now = Stopwatch.GetTimestamp(); owner.Publish(Packet(now), Listener, Settings, true);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref output.Writes) > 0, TimeSpan.FromSeconds(5)));
        // Stop runs on the audio owner before it publishes the completed mixer state.
        // The output counter alone is not a completion barrier for Diagnostics.
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref output.Stops) > 0 && owner.Diagnostics.Voices == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, owner.Diagnostics.Voices); owner.Dispose(); Assert.True(owner.WaitForStop(TimeSpan.FromSeconds(5))); Assert.True(output.Disposed);
    }
    [Fact(Explicit = true)]
    public void NativeWorldMixerStreamsAndReleasesOnTheRealDevice()
    {
        var mixer = new WorldPcmMixer(); Assert.Equal(56, WorldAudioClips.Load(mixer, Path.Combine(RepositoryRoot(), "assets/source/audio")));
        mixer.Configure(Listener, Settings with { Master = .08f });
        mixer.SetLoops([new((int)WorldAudioCue.VehiclesScoutIdleLoop, new(1, 1), 0, new(30, 0, 0), true, AudioMixBus.World, 0)]);
        var output = new WaveOutWorldOutput(); var block = new short[960]; var timer = Stopwatch.StartNew(); int blocks = 0;
        try
        {
            while (blocks < 50 && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (output.Ready) { mixer.Render(block); output.Write(block); blocks++; }
                else Thread.Sleep(1);
            }
            Assert.Equal(50, blocks); output.Stop();
        }
        finally { output.Dispose(); }
        Assert.False(output.OwnsNativeResources);
        string? report = Environment.GetEnvironmentVariable("FORGELINE_WORLD_AUDIO_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, JsonSerializer.Serialize(new { Blocks = blocks, Frames = blocks * 480, Released = !output.OwnsNativeResources, Device = "Windows default output", PhysicalListeningQualified = false }));
    }
    private sealed class FakeOutput : IWorldAudioOutput
    {
        internal int Writes, Stops; internal bool Disposed;
        public bool Ready => true;
        public void Write(short[] block) { Interlocked.Increment(ref Writes); }
        public void Stop() { Interlocked.Increment(ref Stops); }
        public void Dispose() => Disposed = true;
    }
    private sealed class FakeWave : IWaveOutApi
    {
        private bool _complete;
        internal bool Complete
        {
            get => _complete;
            set { _complete = value; if (value) foreach (var p in _headers) { var h = Marshal.PtrToStructure<WaveHeader>(p); h.Flags |= 1; Marshal.StructureToPtr(h, p, false); } }
        }
        internal bool RefuseReturn; internal int Writes;
        private readonly List<IntPtr> _headers = [];
        public uint Open(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags) { device = new(1); return 0; }
        public uint Prepare(IntPtr device, IntPtr header, uint size) { _headers.Add(header); return 0; }
        public uint Unprepare(IntPtr device, IntPtr header, uint size) => RefuseReturn ? 33u : 0;
        public uint Write(IntPtr device, IntPtr header, uint size)
        {
            Writes++; if (Complete) foreach (var p in _headers) { var h = Marshal.PtrToStructure<WaveHeader>(p); h.Flags |= 1; Marshal.StructureToPtr(h, p, false); }
            return 0;
        }
        public uint Reset(IntPtr device) => 0;
        public uint Close(IntPtr device) => 0;
        public uint GetVolume(IntPtr device, out uint volume) { volume = uint.MaxValue; return 0; }
    }
}
