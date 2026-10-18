using Xunit;

namespace ForgeLine.Assets.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AssetWriterQualificationGroup
{
    public const string Name = "Exclusive asset writer qualification";
}
