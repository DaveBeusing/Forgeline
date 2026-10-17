using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Audio;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class IndustrialAudioLibraryTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "assets/source/audio/library.json"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Industrial audio source library was not found.");
    }

    [Fact]
    public void AllSixtyClipsPassTheirSeparatePcmContractsAndManifestIdentity()
    {
        string root = Root();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "assets/source/audio/library.json")));
        Assert.False(manifest.RootElement.GetProperty("runtime_manifest_supported_by_master").GetBoolean());
        int count = 0, loops = 0, ui = 0;
        long total = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.RootElement.GetProperty("assets").EnumerateArray())
        {
            Assert.True(ids.Add(entry.GetProperty("id").GetString()!));
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, entry.GetProperty("path").GetString()!));
            Assert.Equal(entry.GetProperty("sha256").GetString()!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(bytes)));
            bool loop = entry.GetProperty("loop").GetBoolean();
            using var stream = new MemoryStream(bytes);
            if (entry.GetProperty("bus").GetString() == "ui")
            {
                Assert.Equal(entry.GetProperty("pcm_bytes").GetInt32(), UiPcmClip.Read(stream).Samples.Length);
                ui++;
            }
            else
            {
                var clip = WorldPcmClip.Read(stream, loop ? entry.GetProperty("loop_start_frame").GetInt32() : null,
                    loop ? entry.GetProperty("loop_end_frame_exclusive").GetInt32() : null);
                Assert.Equal(entry.GetProperty("frames").GetInt32(), clip.SampleFrames);
                Assert.Equal(entry.GetProperty("channels").GetInt32(), clip.Channels);
                Assert.Equal(entry.GetProperty("pcm_bytes").GetInt32(), clip.Samples.Length);
                Assert.Equal(loop, clip.LoopStartFrame.HasValue);
                if (clip.Channels == 1) Assert.Throws<InvalidDataException>(() => UiPcmClip.Read(new MemoryStream(bytes)));
            }
            if (loop) loops++;
            total += entry.GetProperty("pcm_bytes").GetInt32(); count++;
        }
        Assert.Equal(60, count); Assert.Equal(15, loops); Assert.Equal(4, ui); Assert.Equal(20852160, total);
    }

    [Theory]
    [InlineData(-1, 100)] [InlineData(100, 100)] [InlineData(200, 100)] [InlineData(0, int.MaxValue)]
    public void LoopEndpointsAreExclusiveFrameIndicesWithinTheClip(int start, int end)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Root(), "assets/source/audio/vehicles/truck_idle_loop.wav"));
        Assert.Throws<InvalidDataException>(() => WorldPcmClip.Read(new MemoryStream(bytes), start, end));
        Assert.Throws<InvalidDataException>(() => WorldPcmClip.Read(new MemoryStream(bytes), start, null));
    }

    [Fact]
    public void WorldParserRejectsOversizedOrMalformedContent()
    {
        Assert.Throws<InvalidDataException>(() => WorldPcmClip.Read(new MemoryStream(new byte[44])));
        Assert.Throws<InvalidDataException>(() => WorldPcmClip.Read(new MemoryStream(new byte[WorldPcmClip.MaximumBytes + 1025])));
    }
}
