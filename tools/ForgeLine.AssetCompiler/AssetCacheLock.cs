using System.Diagnostics;

namespace ForgeLine.AssetCompiler;

internal static class AssetCacheLock
{
    internal static FileStream Acquire(string runtimeRoot, TimeSpan timeout)
    {
        string path = Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar) + ".compile.lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (elapsed.Elapsed < timeout)
            {
                Thread.Sleep(50);
            }
            catch (IOException exception)
            {
                throw new IOException($"Runtime asset cache '{runtimeRoot}' is busy or inaccessible after {timeout.TotalSeconds:F0}s. Retry after the other compiler finishes.", exception);
            }
        }
    }
}
