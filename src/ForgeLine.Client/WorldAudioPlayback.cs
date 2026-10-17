using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Audio;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>Audio-owner-only adapter for immutable authorized snapshots.</summary>
internal sealed class WorldAudioPlayback(WorldPcmMixer mixer, SimulationSessionId? expectedSession = null)
{
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _tick;
    private WorldAudioEventCursor? _cursor;
    private bool _active, _terminal;
    private PresentationSnapshot? _lastSnapshot;
    private AudioListener _lastListener;
    private WorldAudioSettings _lastSettings;
    private readonly WorldAudioEvent[] _events = new WorldAudioEvent[256];
    private readonly AudioMixSource[] _loops = new AudioMixSource[1024];
    internal bool Active => _active;
    internal void Update(PresentationSnapshot? snapshot, AudioListener listener, WorldAudioSettings settings, bool active, long now)
    {
        if (_cursor is null && snapshot is { SessionId.IsSpecified: true, PlayerExperience: { } experience } &&
            (!expectedSession.HasValue || expectedSession.Value == snapshot.SessionId) &&
            WorldAudioSnapshot.Resolve(snapshot, snapshot.SessionId, experience.Player, default) is not null)
        { _session = snapshot.SessionId; _player = experience.Player; _cursor = new(_session, _player); }
        var audio = WorldAudioSnapshot.Resolve(snapshot, _session, _player, _tick);
        if (audio is null) { mixer.Clear(); _active = false; return; }
        _tick = audio.Tick;
        mixer.Configure(listener, settings);
        bool fresh = now >= audio.CapturedAtTimestamp && Stopwatch.GetElapsedTime(audio.CapturedAtTimestamp, now) <= TimeSpan.FromMilliseconds(250);
        bool terminal = snapshot!.PlayerExperience!.Value.MatchStatus is PlayerMatchStatus.Victory or PlayerMatchStatus.Defeat;
        // A freshly started result cue may finish after the start deadline; no stale cue can start.
        if (_terminal && terminal && active && settings.Enabled && settings.Master > 0 && listener.IsValid && mixer.ActiveVoices > 0)
        { _active = true; return; }
        if (!active || !fresh || !listener.IsValid || !settings.Enabled || settings.Master <= 0)
        { _cursor!.SuppressThrough(snapshot!); mixer.Clear(); _active = false; return; }
        if (_active && !terminal && ReferenceEquals(snapshot, _lastSnapshot) && listener == _lastListener && settings == _lastSettings) return;
        _lastSnapshot = snapshot; _lastListener = listener; _lastSettings = settings;
        if (terminal && !_terminal) mixer.Clear();
        _terminal = terminal;
        if (!_active) _cursor!.SuppressThrough(snapshot!);
        _active = true;
        int loops = 0;
        foreach (var item in audio.Loops)
        {
            if (loops == _loops.Length) break;
            var definition = WorldAudioCatalog.Get(item.Cue);
            if (definition.Bound && definition.Loop) _loops[loops++] = new((int)item.Cue, item.Entity, 0, item.Position, item.Spatial, (AudioMixBus)definition.Bus, 0);
        }
        mixer.SetLoops(_loops.AsSpan(0, loops));
        int count = _cursor!.CopyFreshEvents(snapshot!, _events, now);
        // Admit low priorities first so combat and alerts can replace them under pressure.
        for (int priority = 0; priority <= 2; priority++)
            for (int i = 0; i < count; i++)
            {
                var item = _events[i]; var definition = WorldAudioCatalog.Get(item.Cue);
                if (!definition.Bound || definition.Loop || Priority(item.Cue) != priority) continue;
                mixer.Play(new((int)item.Cue, item.Entity, item.Sequence, item.Position, item.Spatial, (AudioMixBus)definition.Bus, priority));
            }
    }
    internal static int Priority(WorldAudioCue cue) => WorldAudioCatalog.Get(cue).Bus == WorldAudioBus.Alerts ? 2 :
        cue is >= WorldAudioCue.CombatRifleFire01 and <= WorldAudioCue.CombatImpactConcrete ? 1 : 0;
}

internal static class WorldAudioClips
{
    internal static int Load(WorldPcmMixer mixer, string? directory = null)
    {
        string root = directory ?? Path.Combine(AppContext.BaseDirectory, "assets", "runtime", "audio");
        string manifestPath = Path.Combine(root, "playback.json");
        if (new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("World audio manifest exceeds its budget.");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var entries = manifest.RootElement.GetProperty("clips");
        if (manifest.RootElement.GetProperty("schema").GetInt32() != 1 || entries.GetArrayLength() != WorldAudioCatalog.All.Length)
            throw new InvalidDataException("World audio playback manifest is invalid.");
        int loaded = 0; long total = 0;
        foreach (var definition in WorldAudioCatalog.All)
        {
            try
            {
                var entry = entries.GetPropertyById(definition.AssetId);
                // Runtime paths derive only from the production catalog, never manifest input.
                string relative = definition.AssetId[6..].Replace('.', Path.DirectorySeparatorChar) + ".wav";
                using var stream = File.OpenRead(Path.Combine(root, relative));
                if (stream.Length > WorldPcmClip.MaximumBytes + 1024) throw new InvalidDataException("World audio clip exceeds its budget.");
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("World audio hash mismatch.");
                stream.Position = 0;
                var clip = WorldPcmClip.Read(stream, definition.Loop ? entry.GetProperty("loop_start_frame").GetInt32() : null,
                    definition.Loop ? entry.GetProperty("loop_end_frame_exclusive").GetInt32() : null);
                total += clip.Samples.Length;
                if (total > 24 * 1024 * 1024) throw new InvalidDataException("World PCM library exceeds its memory budget.");
                mixer.SetClip((int)definition.Cue, clip); loaded++;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException or FormatException or InvalidOperationException)
            { Console.Error.WriteLine($"[world:audio:clip-unavailable] id={definition.AssetId} error={error.GetType().Name}"); }
        }
        return loaded;
    }
    private static JsonElement GetPropertyById(this JsonElement array, string id)
    {
        foreach (var item in array.EnumerateArray()) if (item.GetProperty("id").GetString() == id) return item;
        throw new InvalidDataException("Missing world audio binding.");
    }
}
