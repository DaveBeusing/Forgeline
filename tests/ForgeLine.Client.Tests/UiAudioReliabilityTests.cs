using System.Diagnostics;
using System.Runtime.InteropServices;
using ForgeLine.Audio;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

[Collection("Exclusive native and timing qualification")]
public sealed class UiAudioReliabilityTests
{
    private static readonly System.Text.Json.JsonSerializerOptions s_json = new() { WriteIndented = true };
    [Theory]
    [InlineData(49)] [InlineData(50)] [InlineData(59)] [InlineData(60)]
    public void ResultsBypassDecorativeThrottle(int milliseconds)
    {
        var mailbox = new UiAudioMailbox();
        mailbox.Offer(new(UiAudioCue.Intent, TimeSpan.Zero));
        Assert.Equal(UiAudioCue.Intent, mailbox.Take(TimeSpan.Zero).Cue);
        var now = TimeSpan.FromMilliseconds(milliseconds);
        mailbox.Offer(new(UiAudioCue.Accepted, now));
        Assert.Equal(UiAudioCue.Accepted, mailbox.Take(now).Cue);
        mailbox.Offer(new(UiAudioCue.Rejected, now));
        Assert.Equal(UiAudioCue.Rejected, mailbox.Take(now).Cue);
    }

    [Theory]
    [InlineData(49, false)] [InlineData(50, false)] [InlineData(59, false)] [InlineData(60, true)]
    public void DecorativeThrottleStillBoundsNavigation(int milliseconds, bool expected)
    {
        var mailbox = new UiAudioMailbox();
        mailbox.Offer(new(UiAudioCue.Navigation, TimeSpan.Zero));
        Assert.Equal(UiAudioCue.Navigation, mailbox.Take(TimeSpan.Zero).Cue);
        var now = TimeSpan.FromMilliseconds(milliseconds);
        mailbox.Offer(new(UiAudioCue.Navigation, now));
        Assert.Equal(expected, mailbox.Take(now).Cue == UiAudioCue.Navigation);
    }

    [Theory]
    [InlineData(95)] [InlineData(110)] [InlineData(150)]
    public void BusyResultsKeepOriginalExpiryAndRejectOutranksInput(int milliseconds)
    {
        var mailbox = new UiAudioMailbox();
        mailbox.Offer(new(UiAudioCue.Rejected, TimeSpan.Zero));
        var pending = mailbox.Take(TimeSpan.FromMilliseconds(10));
        mailbox.Offer(pending); // A busy output returns the original request, never refreshes its age.
        for (int i = 0; i < 1000; i++) mailbox.Offer(new(UiAudioCue.Navigation, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(1, mailbox.Count);
        Assert.Equal(UiAudioCue.Rejected, mailbox.Take(TimeSpan.FromMilliseconds(milliseconds)).Cue);
        mailbox.Offer(pending);
        Assert.Equal(UiAudioCue.None, mailbox.Take(TimeSpan.FromMilliseconds(151)).Cue);
        Assert.Equal(0, mailbox.Count);
    }

    [Fact]
    public void NewResultReplacesAnOlderFailedAttemptAndCancellationClearsEverything()
    {
        var mailbox = new UiAudioMailbox();
        var old = new UiAudioRequest(UiAudioCue.Accepted, TimeSpan.Zero);
        mailbox.Offer(new(UiAudioCue.Accepted, TimeSpan.FromMilliseconds(20)));
        mailbox.Offer(old);
        Assert.Equal(TimeSpan.FromMilliseconds(20), mailbox.Take(TimeSpan.FromMilliseconds(30)).CreatedAt);
        mailbox.Offer(new(UiAudioCue.Rejected, TimeSpan.FromMilliseconds(40)));
        mailbox.Clear();
        Assert.Equal(UiAudioCue.None, mailbox.Take(TimeSpan.FromMilliseconds(50)).Cue);
        mailbox.Offer(new(UiAudioCue.Intent, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(UiAudioCue.Intent, mailbox.Take(TimeSpan.FromMilliseconds(50)).Cue);
    }

    [Fact]
    public void ClipFailuresAreIsolatedWithoutIncreasingThePcmBudget()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-audio-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "intent.wav"), new byte[44]);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "assets/runtime/audio/ui/accepted.wav"), Path.Combine(root, "accepted.wav"));
            var api = new FakeApi();
            using var voice = new WaveOutUiVoice(root, api);
            voice.PrepareClips();
            Assert.Equal(3, voice.ClipFailures);
            Assert.False(voice.TryPlay(UiAudioCue.Intent, .25f));
            Assert.True(voice.TryPlay(UiAudioCue.Accepted, .25f));
            Assert.Equal(1, api.OpenCount);
            Assert.Equal(38400, UiPcmClip.MaximumBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DeviceRecoveryUsesBoundedBackoffAndAnExplicitNewEnableCanRetry()
    {
        var api = new FakeApi { OpenError = 1 };
        long milliseconds = 0;
        using var voice = new WaveOutUiVoice(api: api, clock: () => milliseconds * Stopwatch.Frequency / 1000);
        foreach (int value in new[] { 0, 249, 250, 1249, 1250, 5249, 5250, 99999 })
        {
            milliseconds = value;
            Assert.False(voice.TryPlay(UiAudioCue.Accepted, .25f));
        }
        Assert.Equal(4, api.OpenCount);
        Assert.Equal(4, voice.DeviceFailures);
        Assert.False(voice.OwnsNativeResources);
        api.OpenError = 0;
        voice.ResetRecovery();
        Assert.True(voice.TryPlay(UiAudioCue.Accepted, .25f));
        Assert.Equal(5, api.OpenCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PrepareAndWriteFailureCanRecoverAfterSafeRelease(bool prepareFailure)
    {
        long milliseconds = 0;
        var api = new FakeApi { PrepareError = prepareFailure ? 1u : 0, WriteError = prepareFailure ? 0u : 1 };
        using var voice = new WaveOutUiVoice(api: api, clock: () => milliseconds * Stopwatch.Frequency / 1000);
        Assert.False(voice.TryPlay(UiAudioCue.Accepted, .25f));
        Assert.False(voice.OwnsNativeResources);
        api.PrepareError = api.WriteError = 0;
        milliseconds = 250;
        Assert.True(voice.TryPlay(UiAudioCue.Rejected, .25f));
        Assert.Equal(2, api.OpenCount);
    }

    [Fact]
    public void RefusedUnprepareAndCloseRetainNativeMemoryUntilTheDriverReturnsIt()
    {
        var api = new FakeApi { UnprepareError = 1 };
        var voice = new WaveOutUiVoice(api: api);
        Assert.True(voice.TryPlay(UiAudioCue.Intent, .25f));
        voice.Dispose();
        Assert.True(voice.OwnsNativeResources);
        _ = Marshal.ReadByte(api.LastData);
        Assert.Equal(0, api.CloseCount);
        api.UnprepareError = 0; api.CloseError = 1;
        voice.Dispose();
        Assert.True(voice.OwnsNativeResources);
        _ = Marshal.ReadByte(api.LastData);
        api.CloseError = 0;
        voice.Dispose(); voice.Dispose();
        Assert.False(voice.OwnsNativeResources);
        Assert.Equal(2, api.CloseCount);
    }

    [Fact]
    public void AStuckPlaybackIsResetBeforeADeviceIsReopened()
    {
        long milliseconds = 0;
        var api = new FakeApi();
        using var voice = new WaveOutUiVoice(api: api, clock: () => milliseconds * Stopwatch.Frequency / 1000);
        Assert.True(voice.TryPlay(UiAudioCue.Intent, .25f));
        Assert.False(voice.TryPlay(UiAudioCue.Accepted, .25f));
        milliseconds = 1000;
        Assert.False(voice.TryPlay(UiAudioCue.Rejected, .25f));
        Assert.False(voice.OwnsNativeResources);
        milliseconds = 1250;
        Assert.True(voice.TryPlay(UiAudioCue.Rejected, .25f));
        Assert.Equal(2, api.OpenCount);
    }

    [Fact]
    public void BlockingDeviceWorkAndDisposalStayOnTheAudioOwner()
    {
        using var output = new BlockingOutput();
        using var audio = new WaveOutUiAudio(() => output);
        var now = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        audio.Update(UiAudioCue.Accepted, new(SoundEnabled: true), now, true);
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                var current = TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                audio.Update(UiAudioCue.Accepted, new(SoundEnabled: true), current, true);
                return output.Entered.IsSet;
            }, TimeSpan.FromSeconds(5)));
            Assert.NotEqual(Environment.CurrentManagedThreadId, output.PlayThread);
            audio.Update(UiAudioCue.Rejected, new(SoundEnabled: true), now, true);
            audio.StopPlayback();
            Assert.Equal(0, audio.Diagnostics.QueueDepth);
            audio.Dispose(); audio.Dispose();
            Assert.False(audio.WaitForStop(TimeSpan.Zero));
        }
        finally { output.Continue.Set(); }
        Assert.True(audio.WaitForStop(TimeSpan.FromSeconds(5)));
        Assert.Equal(output.PlayThread, output.DisposeThread);
        Assert.Equal(1, output.PlayCount);
        Assert.True(audio.Diagnostics.OwnerStopped);
    }

    [Fact(Explicit = true)]
    public void NativeBuffersAndAsynchronousResultPlaybackReleaseOnTheRealDevice()
    {
        var results = new List<object>();
        using (var voice = new WaveOutUiVoice())
        {
            uint? originalVolume = null;
            foreach (var cue in new[] { UiAudioCue.Navigation, UiAudioCue.Intent, UiAudioCue.Accepted, UiAudioCue.Rejected })
            {
                Assert.True(voice.TryPlay(cue, .04f));
                originalVolume ??= voice.DeviceVolume;
                voice.StopPlayback();
                Assert.True(voice.TryPlay(cue, .04f));
                voice.StopPlayback();
                Assert.Equal(originalVolume, voice.DeviceVolume);
                results.Add(new { Cue = cue.ToString(), Submitted = true, ResetAndRestarted = true });
            }
            voice.Dispose();
            Assert.False(voice.OwnsNativeResources);
        }
        using var audio = new WaveOutUiAudio();
        static TimeSpan Now() => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        audio.Update(UiAudioCue.Intent, new(SoundEnabled: true, SoundVolume: .04f), Now(), true);
        Assert.True(SpinWait.SpinUntil(() => audio.Diagnostics.Played >= 1, TimeSpan.FromSeconds(5)));
        audio.Update(UiAudioCue.Accepted, new(SoundEnabled: true, SoundVolume: .04f), Now(), true);
        Assert.True(SpinWait.SpinUntil(() => audio.Diagnostics.Played >= 2, TimeSpan.FromSeconds(5)));
        audio.Dispose();
        Assert.True(audio.WaitForStop(TimeSpan.FromSeconds(5)));
        Assert.False(audio.OwnsNativeResources);
        Assert.True(audio.Diagnostics.OwnerStopped);
        string? report = Environment.GetEnvironmentVariable("FORGELINE_NATIVE_AUDIO_REPORT");
        if (!string.IsNullOrWhiteSpace(report))
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new
            { DirectCues = results, AsynchronousOwner = audio.Diagnostics,
                Limitation = "Buffer submission and lifetime only; no subjective listening or physical device-removal test." },
                s_json));
    }

    private sealed class BlockingOutput : IUiAudioOutput
    {
        internal readonly ManualResetEventSlim Entered = new(false), Continue = new(false);
        internal int PlayThread, DisposeThread, PlayCount;
        public bool TryPlay(UiAudioCue cue, float volume)
        {
            PlayThread = Environment.CurrentManagedThreadId; PlayCount++;
            Entered.Set();
            return Continue.Wait(TimeSpan.FromSeconds(10));
        }
        public void StopPlayback() { }
        public void Dispose() { DisposeThread = Environment.CurrentManagedThreadId; }
    }

    private sealed class FakeApi : WaveOutUiVoice.IWaveOutApi
    {
        internal uint OpenError, PrepareError, WriteError, UnprepareError, CloseError;
        internal int OpenCount, CloseCount;
        internal IntPtr LastData;
        public uint Open(out IntPtr device, uint id, ref WaveOutUiVoice.WaveFormat format, IntPtr callback, IntPtr instance, uint flags)
        { OpenCount++; device = OpenError == 0 ? new IntPtr(1) : IntPtr.Zero; return OpenError; }
        public uint Prepare(IntPtr device, IntPtr header, uint size) => PrepareError;
        public uint Unprepare(IntPtr device, IntPtr header, uint size) => UnprepareError;
        public uint Write(IntPtr device, IntPtr header, uint size)
        { LastData = Marshal.PtrToStructure<WaveOutUiVoice.WaveHeader>(header).Data; return WriteError; }
        public uint Reset(IntPtr device) => 0;
        public uint Close(IntPtr device) { CloseCount++; return CloseError; }
        public uint GetVolume(IntPtr device, out uint volume) { volume = uint.MaxValue; return 0; }
    }
}
