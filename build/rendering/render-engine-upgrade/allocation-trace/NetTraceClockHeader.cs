using System.Buffers.Binary;
using System.Security.Cryptography;

// Narrow original-clock reader; source: pinned TraceEvent4.0.0 NetTraceFormat_v5.md.
// This does not parse events or attest producer identity. Unknown envelopes stay unavailable.
internal static class NetTraceClockHeader
{
    internal static object Read(Stream stream, int expectedPid)
    {
        byte[] bytes = new byte[checked((int)Math.Min(stream.Length, 512))];
        stream.ReadExactly(bytes);
        try
        {
            if (bytes.Length < 12 || !bytes.AsSpan(0, 8).SequenceEqual("Nettrace"u8))
                throw new InvalidDataException("Original Nettrace magic is missing.");
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) == 20)
                return ReadLegacy(bytes, expectedPid);
            return ReadV6(bytes, stream.Length);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException)
        {
            return Evidence(bytes, bytes.Length, false, "unsupported-or-invalid-nettrace-clock", null, null, -1,
                null, null, null, null, error.Message);
        }
    }
    private static object ReadLegacy(byte[] bytes, int expectedPid)
    {
        var reader = new PrefixReader(bytes);
        reader.Expect("Nettrace"u8);
        if (reader.Int32() != 20) throw new InvalidDataException("Unsupported FastSerialization string length.");
        reader.Expect("!FastSerialization.1"u8);
        reader.Tag(5); reader.Tag(5); reader.Tag(1);
        int version = reader.Int32(), minimum = reader.Int32();
        if (version != 4 || minimum != 4) throw new InvalidDataException("Only the documented Trace4/min4 clock envelope is supported.");
        if (reader.Int32() != 5) throw new InvalidDataException("Unexpected Trace type length.");
        reader.Expect("Trace"u8); reader.Tag(6);
        int payload = reader.Position;
        ValidateUtc(reader.Bytes(16));
        long sync = reader.Int64(), frequency = reader.Int64();
        int pointer = reader.Int32(), pid = reader.Int32(), processors = reader.Int32(), expectedSamplingRate = reader.Int32();
        reader.Tag(6);
        if (reader.Position != 102 || reader.Position - payload != 49) throw new InvalidDataException("Unexpected legacy clock envelope size.");
        if (sync < 0 || frequency <= 0) throw new InvalidDataException("Original QPC origin/frequency is unavailable.");
        if (pointer is not (4 or 8) || pid <= 0 || pid != expectedPid)
            throw new InvalidDataException("Original pointer/PID metadata does not match this trace process.");
        // Supported capture scope, not a universal FastSerialization format rule.
        // The historical sampling-rate field is unused: zero is valid and never a QPC frequency.
        if (processors <= 0 || expectedSamplingRate < 0) throw new InvalidDataException("Legacy auxiliary metadata is unsupported for this capture scope.");
        return Evidence(bytes, reader.Position, true, "pinned-nettrace-fastserialization-trace-v4", frequency, sync, payload,
            pid, pointer, processors, expectedSamplingRate, null);
    }
    private static object ReadV6(byte[] bytes, long length)
    {
        if (bytes.Length < 64 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) != 6)
            throw new InvalidDataException("Unsupported original trace clock version.");
        uint block = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
        if (block >> 24 != 1 || (block & 0xFFFFFF) < 40 || 24L + (block & 0xFFFFFF) > length)
            throw new InvalidDataException("Original v6 TraceBlock length/kind is invalid.");
        ValidateUtc(bytes.AsSpan(24, 16));
        long sync = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(40));
        long frequency = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(48));
        if (sync < 0 || frequency <= 0) throw new InvalidDataException("Original QPC origin/frequency is unavailable.");
        return Evidence(bytes, 64, true, "pinned-nettrace-v6-raw-header", frequency, sync, 24, null, null, null, null, null);
    }
    private static void ValidateUtc(ReadOnlySpan<byte> raw)
    {
        int year = BinaryPrimitives.ReadUInt16LittleEndian(raw), month = BinaryPrimitives.ReadUInt16LittleEndian(raw[2..]);
        int weekday = BinaryPrimitives.ReadUInt16LittleEndian(raw[4..]), day = BinaryPrimitives.ReadUInt16LittleEndian(raw[6..]);
        int hour = BinaryPrimitives.ReadUInt16LittleEndian(raw[8..]), minute = BinaryPrimitives.ReadUInt16LittleEndian(raw[10..]);
        int second = BinaryPrimitives.ReadUInt16LittleEndian(raw[12..]), millisecond = BinaryPrimitives.ReadUInt16LittleEndian(raw[14..]);
        var utc = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
        if (weekday != (int)utc.DayOfWeek) throw new InvalidDataException("Original SYSTEMTIME weekday differs from its UTC date.");
    }
    private static object Evidence(byte[] bytes, int length, bool available, string format, long? frequency, long? sync, int utcOffset,
        int? process, int? pointer, int? processors, int? rate, string? reason)
    {
        var header = bytes.AsSpan(0, length);
        return new { available, format, headerLength = length, headerBytesBase64 = Convert.ToBase64String(header),
            headerSha256 = Convert.ToHexStringLower(SHA256.HashData(header)), tickFrequency = frequency, syncTicks = sync,
            utcSystemTimeBytesBase64 = utcOffset < 0 ? null : Convert.ToBase64String(bytes.AsSpan(utcOffset, 16)),
            traceProcessId = process, pointerSize = pointer, processorCount = processors, expectedCpuSamplingRate = rate,
            unavailableReason = reason, correlation = "Independent original producer/process/source/runtime/session and UTC/QPC compatibility are still required" };
    }
    private ref struct PrefixReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        internal int Position { get; private set; }
        internal ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > _bytes.Length - Position) throw new InvalidDataException("Truncated original trace clock prefix.");
            var value = _bytes.Slice(Position, count); Position += count; return value;
        }
        internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));
        internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Bytes(8));
        internal void Tag(byte expected) { if (Bytes(1)[0] != expected) throw new InvalidDataException("Invalid FastSerialization clock object tag/footer."); }
        internal void Expect(ReadOnlySpan<byte> expected) { if (!Bytes(expected.Length).SequenceEqual(expected)) throw new InvalidDataException("Invalid original trace clock type/magic."); }
    }
}
