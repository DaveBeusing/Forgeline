using ForgeLine.AssetCompiler;
using Xunit;

[assembly: AssemblyFixture(typeof(ForgeLine.Assets.Tests.ProductionAssetFixture))]

namespace ForgeLine.Assets.Tests;

/// <summary>Compile immutable repository art once, then isolate each test's runtime files.</summary>
public sealed class ProductionAssetFixture : IDisposable
{
    private static readonly Lazy<(string Root, AssetCompilationResult Result)> Fixture = new(Build);

    public void Dispose()
    {
        if (Fixture.IsValueCreated && Directory.Exists(Fixture.Value.Root))
            Directory.Delete(Fixture.Value.Root, recursive: true);
    }

    public static AssetCompilationResult CompileTo(string runtimeRoot)
    {
        var fixture = Fixture.Value;
        if (!fixture.Result.Success) return fixture.Result;
        Directory.CreateDirectory(runtimeRoot);
        foreach (string source in Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(runtimeRoot, Path.GetRelativePath(fixture.Root, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        return fixture.Result;
    }

    private static (string Root, AssetCompilationResult Result) Build()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ForgeLine.sln")))
            repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("Cannot locate production source assets.");
        string root = Path.Combine(Path.GetTempPath(), "forgeline-production-fixture-" + Guid.NewGuid().ToString("N"));
        var result = AssetPipelineCompiler.Compile(Path.Combine(repository.FullName, "assets", "source"), root, clean: true);
        return (root, result);
    }
}
