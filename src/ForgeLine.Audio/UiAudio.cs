namespace ForgeLine.Audio;

public enum UiAudioCue : byte { None, Navigation, Intent, Accepted, Rejected }

/// <summary>Nonblocking, optional output. Busy/unavailable devices drop a cue, never queue it.</summary>
public interface IUiAudioOutput : IDisposable
{
    bool TryPlay(UiAudioCue cue, float volume);
    void StopPlayback();
}

/// <summary>A packaged, bounded stereo PCM16 clip. No authoring/import pipeline runs in the player.</summary>
public sealed class UiPcmClip
{
    public const int SampleRate = 48_000;
    public const int MaximumBytes = 38_400; // 200 ms stereo PCM16.
    private readonly byte[] _samples;
    public ReadOnlySpan<byte> Samples => _samples;
    private UiPcmClip(byte[] samples) => _samples = samples;

    public static UiPcmClip Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || stream.Length is < 44 or > MaximumBytes + 1024)
            throw new InvalidDataException("UI clip exceeds its PCM budget.");
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("UI clip is not RIFF.");
        uint riffLength = reader.ReadUInt32();
        if (riffLength != stream.Length - 8 || reader.ReadUInt32() != 0x45564157)
            throw new InvalidDataException("UI clip has an invalid WAVE length.");
        bool format = false;
        byte[]? samples = null;
        while (stream.Position + 8 <= stream.Length)
        {
            uint kind = reader.ReadUInt32(), length = reader.ReadUInt32();
            long end = stream.Position + length + (length & 1);
            if (end > stream.Length) throw new InvalidDataException("UI clip chunk is truncated.");
            if (kind == 0x20746d66)
            {
                if (format || length < 16 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 2 ||
                    reader.ReadUInt32() != SampleRate || reader.ReadUInt32() != SampleRate * 4 ||
                    reader.ReadUInt16() != 4 || reader.ReadUInt16() != 16)
                    throw new InvalidDataException("UI clips require stereo 48 kHz PCM16.");
                format = true;
            }
            else if (kind == 0x61746164)
            {
                if (samples is not null || length is 0 or > MaximumBytes || length % 4 != 0)
                    throw new InvalidDataException("UI clip has invalid PCM data.");
                samples = reader.ReadBytes((int)length);
            }
            stream.Position = end;
        }
        if (!format || samples is null || stream.Position != stream.Length)
            throw new InvalidDataException("UI clip is incomplete.");
        return new(samples);
    }
}
