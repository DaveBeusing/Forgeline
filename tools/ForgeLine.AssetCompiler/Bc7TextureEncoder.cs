using BCnEncoder.Encoder;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

internal static class Bc7TextureEncoder
{
    // Process one texture at a time; reserve CPU capacity for the startup window.
    internal static int WorkerCount => Math.Clamp(Environment.ProcessorCount - 2, 1, 4);
    private const int BandRows = 128;
    private static readonly SemaphoreSlim TextureSlot = new(1, 1);

    internal static IDisposable AcquireTextureSlot(AssetCompilationReporter reporter,
        string? assetId, string path)
    {
        bool acquired = false;
        try
        {
            reporter.Measure("texture-queue", () => { TextureSlot.Wait(); acquired = true; return true; }, assetId, path);
            return new TexturePermit();
        }
        catch
        {
            if (acquired) TextureSlot.Release();
            throw;
        }
    }

    private sealed class TexturePermit : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) TextureSlot.Release();
        }
    }

    internal static byte[] Encode(BcEncoder encoder, RuntimeTextureMipLevel mip,
        AssetCompilationReporter reporter, string? assetId, string path, int level)
    {
        int rowPitch = checked(((mip.Width + 3) / 4) * 16);
        byte[] output = new byte[checked(rowPitch * ((mip.Height + 3) / 4))];
        for (int row = 0; row < mip.Height; row += BandRows)
        {
            int offset = row;
            int rows = Math.Min(BandRows, mip.Height - row);
            byte[] band = reporter.Measure("texture-bc7-band", () =>
                encoder.EncodeToRawBytes(mip.Pixels.AsSpan(checked(offset * mip.Width * 4),
                    checked(rows * mip.Width * 4)), mip.Width, rows, PixelFormat.Rgba32)[0],
                assetId, path, $"mip={level} row={row} rows={rows} height={mip.Height} workers={WorkerCount}");
            band.CopyTo(output, checked(row / 4 * rowPitch));
        }
        return output;
    }
}
