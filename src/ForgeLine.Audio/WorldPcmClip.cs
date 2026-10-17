namespace ForgeLine.Audio;

/// <summary>Bounded world/ambience PCM with explicit frame-based loop endpoints. Separate from short UI clips.</summary>
public sealed class WorldPcmClip
{
    public const int SampleRate = 48_000;
    public const int MaximumBytes = 4 * 1024 * 1024;
    private readonly byte[] _samples;
    public ReadOnlySpan<byte> Samples => _samples;
    public int Channels { get; }
    public int SampleFrames => _samples.Length / (Channels * 2);
    public int? LoopStartFrame { get; }
    public int? LoopEndFrameExclusive { get; }
    private WorldPcmClip(byte[] samples, int channels, int? start, int? end)
    { _samples = samples; Channels = channels; LoopStartFrame = start; LoopEndFrameExclusive = end; }

    public static WorldPcmClip Read(Stream stream, int? loopStartFrame = null, int? loopEndFrameExclusive = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || stream.Length is < 44 or > MaximumBytes + 1024)
            throw new InvalidDataException("World clip exceeds its PCM budget.");
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("World clip is not RIFF.");
        uint riffLength = reader.ReadUInt32();
        if (riffLength != stream.Length - 8 || reader.ReadUInt32() != 0x45564157)
            throw new InvalidDataException("World clip has an invalid WAVE length.");
        bool format = false;
        ushort channels = 0;
        byte[]? samples = null;
        while (stream.Position + 8 <= stream.Length)
        {
            uint kind = reader.ReadUInt32(), length = reader.ReadUInt32();
            long end = stream.Position + length + (length & 1);
            if (end > stream.Length) throw new InvalidDataException("World clip chunk is truncated.");
            if (kind == 0x20746d66)
            {
                if (format || length < 16 || reader.ReadUInt16() != 1 || (channels = reader.ReadUInt16()) is < 1 or > 2 ||
                    reader.ReadUInt32() != SampleRate || reader.ReadUInt32() != SampleRate * channels * 2 ||
                    reader.ReadUInt16() != channels * 2 || reader.ReadUInt16() != 16)
                    throw new InvalidDataException("World clips require mono or stereo 48 kHz PCM16.");
                format = true;
            }
            else if (kind == 0x61746164)
            {
                if (samples is not null || length is 0 or > MaximumBytes || length % 2 != 0)
                    throw new InvalidDataException("World clip has invalid PCM data.");
                samples = reader.ReadBytes((int)length);
            }
            stream.Position = end;
        }
        if (!format || samples is null || stream.Position != stream.Length)
            throw new InvalidDataException("World clip is incomplete.");
        if (samples.Length % (channels * 2) != 0) throw new InvalidDataException("PCM frames are incomplete.");
        int frames = samples.Length / (channels * 2);
        if (loopStartFrame.HasValue != loopEndFrameExclusive.HasValue ||
            loopStartFrame is { } start && (start < 0 || loopEndFrameExclusive <= start || loopEndFrameExclusive > frames))
            throw new InvalidDataException("Loop endpoints must enclose complete sampleframes.");
        return new(samples, channels, loopStartFrame, loopEndFrameExclusive);
    }
}
