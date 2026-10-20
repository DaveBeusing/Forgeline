namespace ForgeLine.Graphics;

internal static class UploadArenaLimits
{
    internal const int SlotCount = 4;
    internal const ulong NormalBytes = 4 * 1024 * 1024;
    internal const ulong MaximumTransferBytes = 128 * 1024 * 1024;
    internal const ulong MaximumResidentBytes = 3 * NormalBytes + MaximumTransferBytes;
    internal const int MaximumTransfersPerBatch = 32;

    internal static ulong Align(ulong bytes) => checked((bytes + 511) & ~511UL);
    internal static ulong Capacity(ulong bytes)
    {
        if (bytes == 0 || bytes > MaximumTransferBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Upload transfer must be between one byte and 128 MiB.");
        return checked((bytes + NormalBytes - 1) / NormalBytes * NormalBytes);
    }
    internal static bool CanAppend(ulong used, ulong capacity, ulong bytes, int transfers) =>
        transfers < MaximumTransfersPerBatch && Align(used) <= capacity && bytes <= capacity - Align(used);
    internal static bool CanReset(ulong submitted, ulong completed) =>
        submitted != ulong.MaxValue && submitted <= completed;
}

internal enum SchedulingOperation { AllocateUpload, MapUpload, CloseUpload, ExecuteUpload, SignalUpload, ExecuteFrame, SignalFrame, Present, Resize, Dispose }

internal readonly record struct UploadArenaDiagnostics(
    int Slots, ulong ResidentBytes, ulong PeakResidentBytes, long Allocations,
    long Transfers, long Submissions, long BackpressureWaits, long UploadedBytes,
    double? MemorySampleAgeMilliseconds);
