using System.Buffers.Binary;
using ForgeLine.AssetCompiler;
using Xunit;

namespace ForgeLine.Assets.Tests;

[CollectionDefinition("Texture scheduling", DisableParallelization = true)]
public sealed class TextureSchedulingTestGroup;

[Collection("Texture scheduling")]
public sealed class TextureSchedulingTests
{
    [Fact]
    public void FailedDecodeReleasesTextureSlotForTheNextCompilation()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-texture-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            string invalid = WriteSource(root, "invalid");
            File.WriteAllBytes(Path.Combine(invalid, "image.tga"), []);
            Assert.False(AssetPipelineCompiler.Compile(invalid, Path.Combine(root, "invalid-runtime")).Success);
            string valid = WriteSource(root, "valid");
            Assert.True(AssetPipelineCompiler.Compile(valid, Path.Combine(root, "valid-runtime")).Success);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentCompilersWaitBeforeDecodingAndReleaseTheSlot()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-texture-queue-" + Guid.NewGuid().ToString("N"));
        using var firstEntered = new ManualResetEventSlim();
        using var queued = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<AssetCompilationResult>? first = null, second = null;
        try
        {
            string firstSource = WriteSource(root, "first");
            string secondSource = WriteSource(root, "second");
            first = Task.Factory.StartNew(() => AssetPipelineCompiler.Compile(firstSource,
                Path.Combine(root, "first-runtime"), progress: p =>
                {
                    if (p.Stage != "texture-decode" || p.State != "started") return;
                    firstEntered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken)) throw new TimeoutException("Texture test release timed out.");
                }), TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            second = Task.Factory.StartNew(() => AssetPipelineCompiler.Compile(secondSource,
                Path.Combine(root, "second-runtime"), progress: p =>
                {
                    if (p.Stage == "texture-queue" && p.State == "started") queued.Set();
                    if (p.Stage == "texture-decode" && p.State == "started") secondEntered.Set();
                }), TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(queued.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.False(secondEntered.IsSet);
            release.Set();
            AssetCompilationResult[] results = await Task.WhenAll(first, second)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(results[0].Success);
            Assert.True(results[1].Success);
            Assert.True(secondEntered.IsSet);
        }
        finally
        {
            release.Set();
            try
            {
                if (first is not null) await first;
                if (second is not null) await second;
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        }
    }

    private static string WriteSource(string root, string name)
    {
        string source = Path.Combine(root, name);
        Directory.CreateDirectory(source);
        byte[] tga = new byte[18 + 4 * 4 * 4];
        tga[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), 4);
        tga[16] = 32;
        tga[17] = 0x20;
        Array.Fill(tga, (byte)255, 18, tga.Length - 18);
        File.WriteAllBytes(Path.Combine(source, "image.tga"), tga);
        File.WriteAllText(Path.Combine(source, "image.asset.json"),
            """{"id":"texture.test.queue","type":"texture","source":"image.tga"}""");
        return source;
    }
}
