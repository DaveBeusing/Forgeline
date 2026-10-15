using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceCaptureManifestTests
{
    [Fact]
    public void ConcurrentObserverRetainsACompleteSnapshotWhileNextManifestIsPublished()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "manifest.json");
            const string first = "{\"captures\":[1]}";
            const string second = "{\"captures\":[1,2]}";
            ReferenceCaptureManifest.Write(path, first);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var observer = new StreamReader(stream))
            {
                ReferenceCaptureManifest.Write(path, second);
                Assert.Equal(first, observer.ReadToEnd());
                Assert.Equal(second, File.ReadAllText(path));
            }
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
