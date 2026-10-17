using System.Buffers.Binary;
using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Audio;

public readonly record struct WorldAudioSettings(bool Enabled, float Master, float World, float Alerts, float Ambience)
{
    public static WorldAudioSettings Default => new(true, .65f, .8f, .9f, .35f);
    public void Validate()
    {
        foreach (float value in new[] { Master, World, Alerts, Ambience })
            if (!float.IsFinite(value) || value is < 0 or > 1) throw new InvalidDataException("Audio volumes must be finite and between zero and one.");
    }
}

public enum AudioMixBus : byte { World, Alerts, Ambience }
public readonly record struct AudioListener(Vector3 Focus, Vector3 Right, float Range)
{
    public bool IsValid => Finite(Focus) && Finite(Right) && Right.LengthSquared() is > .99f and < 1.01f && float.IsFinite(Range) && Range is >= 50 and <= 2000;
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
public readonly record struct AudioMixSource(int Clip, EntityId Entity, ulong Sequence, Vector3 Position,
    bool Spatial, AudioMixBus Bus, int Priority);

/// <summary>Single-owner 48 kHz stereo mixer. Fixed voice storage, no device, file or simulation access.</summary>
public sealed class WorldPcmMixer
{
    public const int MaximumVoices = 64, MaximumLoopVoices = 16, BlockFrames = 480;
    private readonly WorldPcmClip?[] _clips = new WorldPcmClip?[128];
    private readonly Voice[] _voices = new Voice[MaximumVoices];
    private readonly AudioMixSource[] _chosen = new AudioMixSource[MaximumLoopVoices];
    private readonly float[] _scores = new float[MaximumLoopVoices];
    private readonly float[] _mix = new float[BlockFrames * 2];
    private AudioListener _listener;
    private WorldAudioSettings _settings;
    private long _frames;
    public long DroppedShots { get; private set; }
    public long LimitedSamples { get; private set; }
    public int ActiveVoices { get { int n = 0; foreach (var v in _voices) if (v.Active) n++; return n; } }
    public int ActiveLoops { get { int n = 0; foreach (var v in _voices) if (v.Active && v.Loop && v.Wanted) n++; return n; } }
    public void SetClip(int id, WorldPcmClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (id is <= 0 or >= 128) throw new ArgumentOutOfRangeException(nameof(id));
        _clips[id] = clip;
    }
    public void Clear() => Array.Clear(_voices);
    public void Configure(AudioListener listener, WorldAudioSettings settings)
    {
        _listener = listener; _settings = settings;
        if (!listener.IsValid || !settings.Enabled || !ValidVolume(settings.Master) || !ValidVolume(settings.World) || !ValidVolume(settings.Alerts) || !ValidVolume(settings.Ambience))
        { _settings = default; Clear(); }
    }
    private static bool ValidVolume(float v) => float.IsFinite(v) && v is >= 0 and <= 1;
    public float Score(in AudioMixSource source)
    {
        if (source.Clip is <= 0 or >= 128 || _clips[source.Clip] is null || !Enum.IsDefined(source.Bus)) return 0;
        float bus = source.Bus switch { AudioMixBus.Alerts => _settings.Alerts, AudioMixBus.Ambience => _settings.Ambience, _ => _settings.World };
        if (!_settings.Enabled || !_listener.IsValid) return 0;
        float attenuation = 1;
        if (source.Spatial)
        {
            Vector3 offset = source.Position - _listener.Focus;
            float distance = offset.Length();
            if (!float.IsFinite(distance) || distance >= _listener.Range) return 0;
            float t = distance / _listener.Range;
            attenuation = (1 - t) * (1 - t);
        }
        return attenuation * _settings.Master * bus;
    }
    public void Play(in AudioMixSource source)
    {
        float score = Score(source);
        if (score <= .0001f || _clips[source.Clip]!.LoopStartFrame.HasValue) { DroppedShots++; return; }
        int start = source.Bus == AudioMixBus.Alerts ? 24 : 0, end = source.Bus == AudioMixBus.Alerts ? 28 : 24;
        int slot = -1;
        for (int i = start; i < end; i++)
        {
            if (!_voices[i].Active) { slot = i; break; }
            if (slot < 0 || _voices[i].Source.Priority < _voices[slot].Source.Priority ||
                _voices[i].Source.Priority == _voices[slot].Source.Priority && Score(_voices[i].Source) < Score(_voices[slot].Source)) slot = i;
        }
        if (_voices[slot].Active && (_voices[slot].Source.Priority > source.Priority ||
            _voices[slot].Source.Priority == source.Priority && Score(_voices[slot].Source) > score)) { DroppedShots++; return; }
        _voices[slot] = new() { Active = true, Wanted = true, Source = source };
    }
    public void SetLoops(ReadOnlySpan<AudioMixSource> sources)
    {
        int count = 0;
        Array.Clear(_scores);
        foreach (var source in sources)
        {
            float score = Score(source);
            if (score <= .0001f || _clips[source.Clip]?.LoopStartFrame is null || source.Bus == AudioMixBus.Alerts) continue;
            // Ambience receives a modest selection floor; exactly three global beds exist.
            if (source.Bus == AudioMixBus.Ambience) score += 1;
            int at = count;
            for (int i = 0; i < count; i++) if (score > _scores[i]) { at = i; break; }
            if (at >= MaximumLoopVoices) continue;
            int last = Math.Min(count, MaximumLoopVoices - 1);
            for (int i = last; i > at; i--) { _scores[i] = _scores[i - 1]; _chosen[i] = _chosen[i - 1]; }
            _scores[at] = score; _chosen[at] = source; count = Math.Min(count + 1, MaximumLoopVoices);
        }
        for (int i = 28; i < MaximumVoices; i++) _voices[i].Wanted = false;
        for (int s = 0; s < count; s++)
        {
            var source = _chosen[s]; int slot = -1;
            for (int i = 28; i < MaximumVoices; i++)
                if (_voices[i].Active && _voices[i].Source.Clip == source.Clip && _voices[i].Source.Entity == source.Entity) { slot = i; break; }
            if (slot < 0)
            {
                for (int i = 28; i < MaximumVoices; i++) if (!_voices[i].Active) { slot = i; break; }
                if (slot < 0)
                    for (int i = 28; i < MaximumVoices; i++) if (!_voices[i].Wanted && (slot < 0 || _voices[i].Gain < _voices[slot].Gain)) slot = i;
                if (slot < 0) continue;
                var clip = _clips[source.Clip]!;
                int begin = clip.LoopStartFrame!.Value, length = clip.LoopEndFrameExclusive!.Value - begin;
                _voices[slot] = new() { Active = true, Loop = true, Frame = begin + (int)(_frames % length) };
            }
            _voices[slot].Source = source; _voices[slot].Wanted = true;
        }
    }
    public void Render(Span<short> destination)
    {
        if (destination.Length != BlockFrames * 2) throw new ArgumentException("One 10 ms stereo block is required.", nameof(destination));
        Array.Clear(_mix);
        for (int v = 0; v < MaximumVoices; v++)
        {
            ref var voice = ref _voices[v]; if (!voice.Active) continue;
            var clip = _clips[voice.Source.Clip]!;
            float target = voice.Wanted ? Score(voice.Source) : 0;
            float pan = 0;
            if (voice.Source.Spatial)
            {
                Vector3 offset = voice.Source.Position - _listener.Focus;
                if (offset.LengthSquared() > .001f) pan = Math.Clamp(Vector3.Dot(Vector3.Normalize(offset), _listener.Right), -1, 1);
            }
            float left = MathF.Sqrt((1 - pan) * .5f), right = MathF.Sqrt((1 + pan) * .5f);
            float headroom = voice.Source.Bus switch { AudioMixBus.Alerts => .5f, AudioMixBus.Ambience => .1f, _ => .18f };
            for (int frame = 0; frame < BlockFrames; frame++)
            {
                if (voice.Frame >= (voice.Loop ? clip.LoopEndFrameExclusive!.Value : clip.SampleFrames))
                {
                    if (voice.Loop) voice.Frame = clip.LoopStartFrame!.Value;
                    else { voice.Active = false; break; }
                }
                voice.Gain += Math.Clamp(target - voice.Gain, -1f / 2400, 1f / 2400);
                int offset = voice.Frame * clip.Channels * 2;
                float a = BinaryPrimitives.ReadInt16LittleEndian(clip.Samples[offset..]) / 32768f;
                float b = clip.Channels == 2 ? BinaryPrimitives.ReadInt16LittleEndian(clip.Samples[(offset + 2)..]) / 32768f : a;
                float gain = voice.Gain * headroom;
                if (voice.Source.Spatial) { float mono = (a + b) * .5f; a = mono * left; b = mono * right; }
                _mix[frame * 2] += a * gain; _mix[frame * 2 + 1] += b * gain;
                voice.Frame++;
            }
            if (voice.Loop && !voice.Wanted && voice.Gain <= 0) voice.Active = false;
        }
        for (int i = 0; i < _mix.Length; i++)
        {
            float x = _mix[i]; if (MathF.Abs(x) > .9f) LimitedSamples++;
            // Continuous bounded soft limiter; leaves at least 10% PCM headroom.
            destination[i] = (short)(.9f * x / (1 + MathF.Abs(x)) * 32767);
        }
        _frames += BlockFrames;
    }
    private struct Voice
    {
        internal bool Active, Loop, Wanted;
        internal AudioMixSource Source;
        internal int Frame;
        internal float Gain;
    }
}
