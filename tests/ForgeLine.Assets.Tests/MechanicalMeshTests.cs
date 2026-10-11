using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class MechanicalMeshTests
{
    private static readonly AssetSocket[] Sockets = [new("pivot", 0, 1, 0)];
    private static MechanicalMeshPart Wheel => new("wheel", 0, 6, -1, MechanicalMotionKind.Wheel, "pivot");

    [Fact]
    public void OptionalMetadataRetainsStaticMeshes()
    {
        MechanicalMeshValidation.Validate([], [], 36);
        MechanicalMeshValidation.Validate([Wheel], Sockets, 6);
    }

    [Fact]
    public void RejectsMissingAnchorsCyclesGapsOverlapsAndMalformedMotion()
    {
        MechanicalMeshPart[] invalid =
        [
            Wheel with { Socket = "missing" }, Wheel with { Parent = 0 },
            Wheel with { FirstIndex = 3 }, Wheel with { IndexCount = 5 },
            Wheel with { Rate = float.NaN }, Wheel with { Travel = float.PositiveInfinity },
            Wheel with { Motion = (MechanicalMotionKind)100 }
        ];
        foreach (MechanicalMeshPart part in invalid)
            Assert.Throws<InvalidDataException>(() => MechanicalMeshValidation.Validate([part], Sockets, 6));
        Assert.Throws<InvalidDataException>(() => MechanicalMeshValidation.Validate([Wheel], Sockets, 12));
        Assert.Throws<InvalidDataException>(() => MechanicalMeshValidation.Validate([Wheel, Wheel], Sockets, 12));
    }

    [Fact]
    public void RejectsUnboundedHierarchy()
    {
        var parts = Enumerable.Range(0, 33).Select(i => Wheel with
            { Name = "wheel" + i, FirstIndex = i * 6, Parent = i - 1 }).ToArray();
        Assert.Throws<InvalidDataException>(() => MechanicalMeshValidation.Validate(parts, Sockets, 198));
    }
}
