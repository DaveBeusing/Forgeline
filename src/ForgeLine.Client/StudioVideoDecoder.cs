using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace ForgeLine.Client;

internal sealed record StudioVideoFrame(long Timestamp, int Width, int Height, byte[] Pixels);

/// <summary>Bounded CPU decoding; no GPU or platform-loop ownership crosses this thread.</summary>
internal sealed class StudioVideoDecoder : IDisposable
{
    private readonly BlockingCollection<StudioVideoFrame> _frames = new(2);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private StudioVideoFrame? _pending;
    private Exception? _failure;
    private bool _disposed;

    internal StudioVideoDecoder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _worker = new Thread(() => Decode(path)) { IsBackground = true, Name = "ForgeLine Studio Video" };
        _worker.Start();
    }

    internal Exception? Failure => Volatile.Read(ref _failure);
    internal bool WorkerCompleted => !_worker.IsAlive;

    internal bool TryReadFirst(out StudioVideoFrame? frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = _pending;
        _pending = null;
        return frame is not null || _frames.TryTake(out frame);
    }

    internal bool TryRead(long timestamp, out StudioVideoFrame? frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = null;
        while (true)
        {
            if (_pending is null && !_frames.TryTake(out _pending)) break;
            if (_pending.Timestamp > timestamp) break;
            frame = _pending;
            _pending = null;
        }
        return frame is not null;
    }

    private void Decode(string path)
    {
        bool started = false;
        int com = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            Marshal.ThrowExceptionForHR(com);
            MediaFactory.MFStartup().CheckError();
            started = true;
            using IMFAttributes attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
            using IMFSourceReader reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            using IMFMediaType requested = MediaFactory.MFCreateMediaType();
            requested.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            requested.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requested);
            using IMFMediaType actual = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            ulong size = actual.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            int width = checked((int)(size >> 32)), height = checked((int)(size & uint.MaxValue));
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
                throw new InvalidDataException("Studio video exceeds the supported frame dimensions.");
            int stride = unchecked((int)actual.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
            int storageHeight = height;
            while (!_stop.IsCancellationRequested)
            {
                using IMFSample? sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream,
                    SourceReaderControlFlag.None, out _, out SourceReaderFlag flags, out long timestamp);
                if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                {
                    // The H.264 decoder can finish negotiating stride/rate on its first sample.
                    using IMFMediaType updated = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                    ulong coded = updated.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                    int codedWidth = checked((int)(coded >> 32)), codedHeight = checked((int)(coded & uint.MaxValue));
                    // H.264 expands storage to whole macroblocks; keep the visible display rectangle.
                    if ((codedWidth != width && codedWidth != ((width + 15) & ~15)) ||
                        (codedHeight != height && codedHeight != ((height + 15) & ~15)) ||
                        updated.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.Rgb32)
                        throw new InvalidDataException("Studio video changed its visible frame format.");
                    storageHeight = codedHeight;
                    stride = unchecked((int)updated.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
                }
                if (sample is not null)
                {
                    using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
                    buffer.Lock(out IntPtr data, out _, out int length);
                    try
                    {
                        if (Math.Abs((long)stride) < width * 4L || Math.Abs((long)stride) * (storageHeight - 1) + width * 4L > length)
                            throw new InvalidDataException("Studio video has an invalid frame stride.");
                        var pixels = new byte[checked(width * height * 4)];
                        for (int row = 0; row < height; row++)
                        {
                            int offset = stride < 0 ? (storageHeight - 1 - row) * -stride : row * stride;
                            Marshal.Copy(IntPtr.Add(data, offset), pixels, row * width * 4, width * 4);
                        }
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                            pixels[i + 3] = 255;
                        }
                        _frames.Add(new StudioVideoFrame(timestamp, width, height, pixels), _stop.Token);
                    }
                    finally { buffer.Unlock(); }
                }
                if ((flags & SourceReaderFlag.EndOfStream) != 0) break;
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { Volatile.Write(ref _failure, error); }
        finally
        {
            if (started) MediaFactory.MFShutdown();
            if (com >= 0) CoUninitialize();
            _frames.CompleteAdding();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _stop.Cancel();
        _worker.Join();
        _frames.Dispose();
        _pending = null;
        _stop.Dispose();
        _disposed = true;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
