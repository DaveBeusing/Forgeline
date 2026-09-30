using ForgeLine.Core;

namespace ForgeLine.Game;

public enum WorldPresentationKind : byte
{
    Prop = 1,
    Vegetation = 2,
    Decal = 3,
    ResourceDeposit = 4
}

public enum WorldVisualId : ushort
{
    None = 0,

    ResourceFerrousOre = 1,
    ResourceSilicates = 2,
    ResourceVolatiles = 3,
    ResourceRareElements = 4,

    PropRock = 100,
    PropBarrier = 101,
    PropConcreteBlock = 102,
    PropCrate = 103,
    PropDrum = 104,
    PropPallet = 105,
    PropPipeSection = 106,
    PropUtilityBox = 107,
    PropFence = 108,
    PropIndustrialLightSignage = 109,
    PropRubble = 110,

    VegetationConifer = 200,
    VegetationScrub = 201,
    VegetationGrassClump = 202,

    DecalTireTracks = 300,
    DecalTrackedVehicleMarks = 301,
    DecalRoadWear = 302,
    DecalOilStain = 303,
    DecalBlastMark = 304,
    DecalShellImpact = 305,
    DecalScorchMark = 306,
    DecalConcreteCrack = 307
}

public readonly record struct WorldPresentationIdentity(
    WorldVisualId Visual,
    WorldPresentationKind Kind,
    bool Inspectable = false)
{
    public bool IsSpecified =>
        Visual != WorldVisualId.None &&
        Enum.IsDefined(Visual) &&
        Enum.IsDefined(Kind);

    public static WorldPresentationIdentity ForResource(
        ResourceId resourceId) =>
        new(
            resourceId == ResourceIds.FerrousOre
                ? WorldVisualId.ResourceFerrousOre
                : resourceId == ResourceIds.Silicates
                    ? WorldVisualId.ResourceSilicates
                    : resourceId == ResourceIds.Volatiles
                        ? WorldVisualId.ResourceVolatiles
                        : resourceId == ResourceIds.RareElements
                            ? WorldVisualId.ResourceRareElements
                            : throw new ArgumentOutOfRangeException(
                                nameof(resourceId),
                                resourceId,
                                "Resource does not have a world-deposit presentation."),
            WorldPresentationKind.ResourceDeposit,
            Inspectable: true);
}
