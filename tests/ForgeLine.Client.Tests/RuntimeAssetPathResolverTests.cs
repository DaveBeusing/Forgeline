using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class RuntimeAssetPathResolverTests
{
    [Fact]
    public void ExplicitRuntimeAssetRootTakesPriority()
    {
        string root =
            CreateTemporaryDirectory();

        try
        {
            string applicationBase =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "app")).FullName;
            string currentDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "cwd")).FullName;
            string packagedRoot =
                CreateRuntimeRoot(
                    applicationBase);
            string overrideRoot =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "override")).FullName;
            WriteManifest(
                overrideRoot);

            RuntimeAssetPathResolution resolution =
                RuntimeAssetPathResolver.Resolve(
                    applicationBase,
                    currentDirectory,
                    overrideRoot);

            Assert.True(
                resolution.Found);
            Assert.Equal(
                Path.GetFullPath(
                    overrideRoot),
                resolution.RuntimeRoot);
            Assert.NotEqual(
                packagedRoot,
                resolution.RuntimeRoot);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void PackagedRuntimeAssetsResolveBesideApplication()
    {
        string root =
            CreateTemporaryDirectory();

        try
        {
            string applicationBase =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "publish")).FullName;
            string runtimeRoot =
                CreateRuntimeRoot(
                    applicationBase);
            string currentDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "elsewhere")).FullName;

            RuntimeAssetPathResolution resolution =
                RuntimeAssetPathResolver.Resolve(
                    applicationBase,
                    currentDirectory,
                    null);

            Assert.Equal(
                runtimeRoot,
                resolution.RuntimeRoot);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void SourceCheckoutPrefersRepositoryRuntimeAssetsOverBuildOutput()
    {
        string root =
            CreateTemporaryDirectory();

        try
        {
            string repositoryRoot =
                Path.Combine(
                    root,
                    "repo");
            Directory.CreateDirectory(
                repositoryRoot);
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    "ForgeLine.sln"),
                string.Empty);
            string repositoryRuntime =
                CreateRuntimeRoot(
                    repositoryRoot);
            string applicationBase =
                Directory.CreateDirectory(
                    Path.Combine(
                        repositoryRoot,
                        "src",
                        "ForgeLine.Client",
                        "bin",
                        "Release",
                        "net10.0-windows",
                        "win-x64")).FullName;
            string outputRuntime =
                CreateRuntimeRoot(
                    applicationBase);
            string currentDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "launcher")).FullName;

            RuntimeAssetPathResolution resolution =
                RuntimeAssetPathResolver.Resolve(
                    applicationBase,
                    currentDirectory,
                    null);

            Assert.Equal(
                repositoryRuntime,
                resolution.RuntimeRoot);
            Assert.NotEqual(
                outputRuntime,
                resolution.RuntimeRoot);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void RepositoryRuntimeAssetsResolveFromNestedBuildOutput()
    {
        string root =
            CreateTemporaryDirectory();

        try
        {
            string repositoryRoot =
                Path.Combine(
                    root,
                    "repo");
            Directory.CreateDirectory(
                repositoryRoot);
            File.WriteAllText(
                Path.Combine(
                    repositoryRoot,
                    "ForgeLine.sln"),
                string.Empty);
            string runtimeRoot =
                CreateRuntimeRoot(
                    repositoryRoot);
            string applicationBase =
                Directory.CreateDirectory(
                    Path.Combine(
                        repositoryRoot,
                        "src",
                        "ForgeLine.Client",
                        "bin",
                        "Release",
                        "net10.0-windows",
                        "win-x64")).FullName;
            string currentDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "launcher")).FullName;

            RuntimeAssetPathResolution resolution =
                RuntimeAssetPathResolver.Resolve(
                    applicationBase,
                    currentDirectory,
                    null);

            Assert.Equal(
                runtimeRoot,
                resolution.RuntimeRoot);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    [Fact]
    public void MissingManifestDoesNotSelectAnEmptyRuntimeRoot()
    {
        string root =
            CreateTemporaryDirectory();

        try
        {
            string applicationBase =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "app")).FullName;
            string currentDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root,
                        "cwd")).FullName;

            RuntimeAssetPathResolution resolution =
                RuntimeAssetPathResolver.Resolve(
                    applicationBase,
                    currentDirectory,
                    null);

            Assert.False(
                resolution.Found);
            Assert.Null(
                resolution.RuntimeRoot);
            Assert.NotEmpty(
                resolution.CandidateRoots);
        }
        finally
        {
            Directory.Delete(
                root,
                recursive: true);
        }
    }

    private static string CreateRuntimeRoot(
        string parent)
    {
        string runtimeRoot =
            Directory.CreateDirectory(
                Path.Combine(
                    parent,
                    "assets",
                    "runtime")).FullName;
        WriteManifest(
            runtimeRoot);
        return runtimeRoot;
    }

    private static void WriteManifest(
        string runtimeRoot)
    {
        Directory.CreateDirectory(
            runtimeRoot);
        File.WriteAllText(
            Path.Combine(
                runtimeRoot,
                RuntimeAssetCatalog.ManifestFileName),
            "{}");
    }

    private static string CreateTemporaryDirectory()
    {
        string path =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-runtime-assets-" +
                Guid.NewGuid().ToString(
                    "N"));
        Directory.CreateDirectory(
            path);
        return path;
    }
}
