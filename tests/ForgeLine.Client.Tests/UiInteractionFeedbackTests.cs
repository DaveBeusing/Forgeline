using ForgeLine.Audio;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class UiInteractionFeedbackTests
{
    private static PresentationSnapshot Snapshot(ulong tick, PlayerCommandFeedbackState state = default, ulong session = 1) =>
        new(new(tick), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(session),
            playerExperience: default(PlayerExperienceSnapshot) with
            {
                Tick = new(tick), Feedback = PlayerCommandFeedback.None with { Kind = PlayerCommandFeedbackKind.Movement, State = state, ResolvedAtTick = new(tick) }
            });

    [Fact]
    public void IntentCannotReportAcceptanceAndCapturedResultPlaysOnce()
    {
        var feedback = new UiInteractionFeedback();
        var panel = default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Construction };
        feedback.Gameplay(panel, Snapshot(1), TimeSpan.Zero, true, default, RenderQuality.Medium);
        feedback.Gameplay(panel with { ContextualPending = true, ContextualSessionId = new(1) }, Snapshot(2), TimeSpan.FromMilliseconds(16), true, default, RenderQuality.Medium);
        Assert.Equal(UiAudioCue.Intent, feedback.Cue);
        var accepted = Snapshot(3, PlayerCommandFeedbackState.Accepted);
        feedback.Gameplay(panel, accepted, TimeSpan.FromMilliseconds(32), true, default, RenderQuality.Medium);
        Assert.Equal(UiAudioCue.Accepted, feedback.Cue);
        feedback.Gameplay(panel, accepted, TimeSpan.FromMilliseconds(48), true, default, RenderQuality.Medium);
        Assert.Equal(UiAudioCue.None, feedback.Cue);
        feedback.Gameplay(panel, Snapshot(4, PlayerCommandFeedbackState.Partial), TimeSpan.FromMilliseconds(64), true, default, RenderQuality.Medium);
        Assert.Equal(UiAudioCue.Rejected, feedback.Cue);
    }

    [Fact]
    public void SessionFocusAndStaleSnapshotCannotReplayAnOldResult()
    {
        var feedback = new UiInteractionFeedback(); var panel = default(PlayerActionPanelView);
        feedback.Gameplay(panel, Snapshot(2, PlayerCommandFeedbackState.Accepted), TimeSpan.Zero, true, default, RenderQuality.High);
        Assert.Equal(UiAudioCue.None, feedback.Cue);
        feedback.Gameplay(panel, Snapshot(3, PlayerCommandFeedbackState.Rejected), TimeSpan.FromMilliseconds(16), false, default, RenderQuality.High);
        feedback.Gameplay(panel, Snapshot(3, PlayerCommandFeedbackState.Rejected), TimeSpan.FromMilliseconds(32), true, default, RenderQuality.High);
        Assert.Equal(UiAudioCue.None, feedback.Cue);
        feedback.Cancel();
        feedback.Gameplay(panel, Snapshot(4, PlayerCommandFeedbackState.Accepted), TimeSpan.FromMilliseconds(48), true, default, RenderQuality.High);
        Assert.Equal(UiAudioCue.None, feedback.Cue);
        feedback.Gameplay(panel, Snapshot(1, PlayerCommandFeedbackState.Rejected, 2), TimeSpan.FromMilliseconds(64), true, default, RenderQuality.High);
        Assert.Equal(UiAudioCue.None, feedback.Cue);
    }

    [Fact]
    public void FrontendKeepsContentAndGeometryOperableDuringRapidTransitions()
    {
        var feedback = new UiInteractionFeedback();
        var first = FrontendSurfaceView.Detail("FIRST", [], "ESC BACK", "APPLY");
        feedback.Frontend(first, TimeSpan.Zero, true);
        var second = first with { Title = "SECOND" };
        var animated = feedback.Frontend(second, TimeSpan.FromMilliseconds(16), true);
        Assert.Equal(0, animated.Motion!.Value.Reveal);
        Assert.Equal(second.PrimaryAction, animated.PrimaryAction);
        Assert.Equal(second.Layout, animated.Layout);
        Assert.Equal(UiAudioCue.Navigation, feedback.Cue);
        var reduced = feedback.Frontend(first with { FeedbackSettings = new(ReducedMotion: true) }, TimeSpan.FromMilliseconds(32), true);
        Assert.Equal(UiMotionView.Settled, reduced.Motion);
    }

    [Fact]
    public void SettingsRoundTripPreservesCameraAndSupportsAdditiveDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-ui-settings-" + Guid.NewGuid());
        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(new ClientUserSettings { CameraPanSpeedMultiplier = 1.4f });
            var adapter = new ClientSettingsFrontendAdapter(store); var model = adapter.Load();
            model.SetUiFeedback(new(true, true, .25f)); adapter.Apply(model);
            var saved = store.Load().Settings;
            Assert.True(saved.ReducedUiMotion); Assert.True(saved.UiSoundEnabled); Assert.Equal(.25f, saved.UiSoundVolume);
            Assert.Equal(1.4f, saved.CameraPanSpeedMultiplier);
            Assert.False(new ClientUserSettings().UiSoundEnabled);
            Assert.Throws<InvalidDataException>(() => (saved with { UiSoundVolume = float.NaN }).Validate());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MutedAudioDoesNotOpenADeviceAndDisposalIsIdempotent()
    {
        using var audio = new WaveOutUiAudio();
        audio.Update(UiAudioCue.Accepted, new(SoundEnabled: false, SoundVolume: 1), TimeSpan.Zero, true);
        audio.Update(UiAudioCue.Rejected, new(SoundEnabled: true, SoundVolume: 0), TimeSpan.FromSeconds(1), true);
        Assert.False(audio.Available);
        Assert.False(audio.TryPlay(UiAudioCue.Intent, float.NaN));
        audio.Dispose(); audio.Dispose();
        Assert.False(audio.TryPlay(UiAudioCue.Intent, .5f));
    }

    [Fact]
    public void PackagedCuesAreBoundedPcmAndMalformedClipsFailClosed()
    {
        foreach (string name in new[] { "navigation", "intent", "accepted", "rejected" })
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets/runtime/audio/ui", name + ".wav"));
            var clip = UiPcmClip.Read(stream);
            Assert.InRange(clip.Samples.Length, 4, UiPcmClip.MaximumBytes);
        }
        Assert.Throws<InvalidDataException>(() => UiPcmClip.Read(new MemoryStream(new byte[44])));
        Assert.Throws<InvalidDataException>(() => UiPcmClip.Read(new MemoryStream(new byte[UiPcmClip.MaximumBytes + 1025])));
    }
}
