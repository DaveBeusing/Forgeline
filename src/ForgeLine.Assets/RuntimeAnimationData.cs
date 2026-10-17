using System.Numerics;
using System.Text;

namespace ForgeLine.Assets;

public enum AnimationPath { Translation, Rotation }
public enum AnimationInterpolation { Linear, Step }
public sealed record AnimationNode(string Name, int Parent);

public sealed class AnimationChannel
{
    public AnimationChannel(int node, AnimationPath path, AnimationInterpolation interpolation,
        IReadOnlyList<float> times, IReadOnlyList<Vector4> values)
    {
        Node = node; Path = path; Interpolation = interpolation;
        Times = Array.AsReadOnly(times.ToArray()); Values = Array.AsReadOnly(values.ToArray());
    }
    public int Node { get; }
    public AnimationPath Path { get; }
    public AnimationInterpolation Interpolation { get; }
    public IReadOnlyList<float> Times { get; }
    public IReadOnlyList<Vector4> Values { get; }

    public Vector4 Evaluate(float seconds)
    {
        int low = 0, high = Times.Count - 1;
        if (seconds <= Times[0]) return Values[0];
        if (seconds >= Times[high]) return Values[high];
        while (high - low > 1)
        {
            int mid = low + (high - low) / 2;
            if (Times[mid] <= seconds) low = mid; else high = mid;
        }
        if (Interpolation == AnimationInterpolation.Step) return Values[low];
        float alpha = (seconds - Times[low]) / (Times[high] - Times[low]);
        if (Path == AnimationPath.Translation) return Vector4.Lerp(Values[low], Values[high], alpha);
        Vector4 a = Values[low], b = Values[high];
        Quaternion q = Quaternion.Slerp(new(a.X, a.Y, a.Z, a.W), new(b.X, b.Y, b.Z, b.W), alpha);
        return new(q.X, q.Y, q.Z, q.W);
    }
}

/// <summary>Immutable cosmetic rigid-node clip. All coordinates are meter-space deltas around mesh sockets.</summary>
public sealed class RuntimeAnimationData
{
    public const int CurrentVersion = 1;
    public const int MaximumNodes = 32, MaximumChannels = 64, MaximumKeysPerChannel = 1024, MaximumKeys = 8192;
    public const int MaximumPayloadBytes = 1_048_576;
    public const float MaximumDuration = 120;

    public RuntimeAnimationData(string name, IReadOnlyList<AnimationNode> nodes, IReadOnlyList<AnimationChannel> channels)
    {
        Name = name;
        Nodes = Array.AsReadOnly(nodes.ToArray()); Channels = Array.AsReadOnly(channels.ToArray());
        Validate();
        Duration = Channels.Max(c => c.Times[^1]);
    }
    public string Name { get; }
    public IReadOnlyList<AnimationNode> Nodes { get; }
    public IReadOnlyList<AnimationChannel> Channels { get; }
    public float Duration { get; }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Encoding.UTF8.GetByteCount(Name) > 128 ||
            Nodes.Count is < 1 or > MaximumNodes || Channels.Count is < 1 or > MaximumChannels)
            throw new InvalidDataException("Clip name, node or channel count exceeds the rigid animation contract.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Nodes.Count; i++)
            if (Nodes[i] is not { } node || string.IsNullOrWhiteSpace(node.Name) || Encoding.UTF8.GetByteCount(node.Name) > 128 ||
                !names.Add(node.Name) || node.Parent < -1 || node.Parent >= i)
                throw new InvalidDataException("Clip nodes require unique bounded names and preceding parents.");
        var paths = new HashSet<(int, AnimationPath)>();
        int total = 0;
        foreach (var channel in Channels)
        {
            if (channel is null || (uint)channel.Node >= Nodes.Count || !Enum.IsDefined(channel.Path) ||
                !Enum.IsDefined(channel.Interpolation) || !paths.Add((channel.Node, channel.Path)) ||
                channel.Times.Count is < 2 or > MaximumKeysPerChannel || channel.Values.Count != channel.Times.Count ||
                (total += channel.Times.Count) > MaximumKeys)
                throw new InvalidDataException("Clip has invalid or duplicate channels, targets or key counts.");
            float previous = -1;
            for (int i = 0; i < channel.Times.Count; i++)
            {
                float time = channel.Times[i]; Vector4 value = channel.Values[i];
                if (!float.IsFinite(time) || time < 0 || time <= previous || time > MaximumDuration ||
                    !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W) ||
                    (channel.Path == AnimationPath.Translation ? new Vector3(value.X, value.Y, value.Z).Length() > 2 || value.W != 0 :
                        MathF.Abs(value.LengthSquared() - 1) > .0001f))
                    throw new InvalidDataException("Clip requires finite increasing times, translations within 2m and unit quaternions.");
                previous = time;
            }
            if (channel.Times[0] != 0) throw new InvalidDataException("Clip channels must begin at zero.");
        }
    }

    public float LoopTime(double activeSeconds) => !double.IsFinite(activeSeconds) || activeSeconds <= 0 ? 0 : (float)(activeSeconds % Duration);

    public byte[] ToPayload()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(CurrentVersion); writer.Write(Name); writer.Write(Nodes.Count); writer.Write(Channels.Count);
        foreach (var node in Nodes) { writer.Write(node.Name); writer.Write(node.Parent); }
        foreach (var channel in Channels)
        {
            writer.Write(channel.Node); writer.Write((int)channel.Path); writer.Write((int)channel.Interpolation); writer.Write(channel.Times.Count);
            for (int i = 0; i < channel.Times.Count; i++)
            {
                writer.Write(channel.Times[i]); Vector4 v = channel.Values[i];
                writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(v.W);
            }
        }
        return stream.ToArray();
    }

    public static RuntimeAnimationData FromPayload(byte[] payload)
    {
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("Clip payload exceeds 1MiB.");
        try
        {
            using var stream = new MemoryStream(payload, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() != CurrentVersion) throw new InvalidDataException("Unsupported rigid clip version; recompile source assets.");
            string name = ReadName(reader);
            int nodes = reader.ReadInt32(), channels = reader.ReadInt32();
            if (nodes is < 1 or > MaximumNodes || channels is < 1 or > MaximumChannels)
                throw new InvalidDataException("Clip counts exceed production limits.");
            var nodeTable = new AnimationNode[nodes];
            for (int i = 0; i < nodes; i++) nodeTable[i] = new(ReadName(reader), reader.ReadInt32());
            var channelTable = new AnimationChannel[channels]; int total = 0;
            for (int c = 0; c < channels; c++)
            {
                int node = reader.ReadInt32(); var path = (AnimationPath)reader.ReadInt32();
                var interpolation = (AnimationInterpolation)reader.ReadInt32(); int count = reader.ReadInt32();
                if (count is < 2 or > MaximumKeysPerChannel || (total += count) > MaximumKeys || (long)count * 20 > stream.Length - stream.Position)
                    throw new InvalidDataException("Clip key count or section length is invalid.");
                var times = new float[count]; var values = new Vector4[count];
                for (int k = 0; k < count; k++) { times[k] = reader.ReadSingle(); values[k] = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); }
                channelTable[c] = new(node, path, interpolation, times, values);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing clip data.");
            return new(name, nodeTable, channelTable);
        }
        catch (Exception e) when (e is EndOfStreamException or FormatException or DecoderFallbackException)
        { throw new InvalidDataException("Truncated or malformed rigid clip.", e); }
    }
    private static string ReadName(BinaryReader reader)
    {
        int length = reader.Read7BitEncodedInt();
        if (length is < 1 or > 128) throw new InvalidDataException("Clip name exceeds bounded UTF-8 length.");
        byte[] data = reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(data);
    }
}
