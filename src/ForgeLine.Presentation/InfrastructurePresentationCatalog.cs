using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum InfrastructurePresentationState : byte
{
    None = 0,
    Operational = 1,
    Restoring = 2,
    Disabled = 3
}

public readonly record struct InfrastructureFeaturePresentationMetadata(
    InfrastructurePresentationKind Kind,
    InfrastructurePresentationState State)
{
    public static InfrastructureFeaturePresentationMetadata None => default;

    public bool IsSpecified =>
        Enum.IsDefined(Kind) &&
        State != InfrastructurePresentationState.None;
}

public static class InfrastructurePresentationCatalog
{
    public const string RoadMaterialAssetId =
        "material.directorate.infrastructure.road";
    public const string BridgeMaterialAssetId =
        "material.directorate.infrastructure.bridge";

    public static string ResolveMeshAssetId(
        in InfrastructureFeaturePresentationMetadata feature)
    {
        if (!feature.IsSpecified)
        {
            throw new ArgumentException(
                "Infrastructure presentation metadata must be specified.",
                nameof(feature));
        }

        return feature.Kind switch
        {
            InfrastructurePresentationKind.RoadBridge =>
                feature.State switch
                {
                    InfrastructurePresentationState.Operational =>
                        "infrastructure.directorate.bridge.road.intact",
                    InfrastructurePresentationState.Restoring =>
                        "infrastructure.directorate.bridge.road.damaged",
                    InfrastructurePresentationState.Disabled =>
                        "infrastructure.directorate.bridge.road.destroyed",
                    _ =>
                        "infrastructure.directorate.bridge.road.intact"
                },
            InfrastructurePresentationKind.RoadSegment or
            InfrastructurePresentationKind.Ford =>
                feature.State switch
                {
                    InfrastructurePresentationState.Restoring =>
                        "infrastructure.directorate.road.damaged",
                    InfrastructurePresentationState.Disabled =>
                        "infrastructure.directorate.road.destroyed",
                    _ =>
                        "infrastructure.directorate.road.straight"
                },
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(feature))
        };
    }

    public static string ResolveMaterialAssetId(
        in InfrastructureFeaturePresentationMetadata feature) =>
        feature.Kind ==
        InfrastructurePresentationKind.RoadBridge
            ? BridgeMaterialAssetId
            : RoadMaterialAssetId;

    public static string ResolveStrategicSymbolAssetId(
        in InfrastructureFeaturePresentationMetadata feature) =>
        feature.Kind ==
        InfrastructurePresentationKind.RoadBridge
            ? "material.directorate.symbol.infrastructure.bridge"
            : "material.directorate.symbol.infrastructure.road";

    public static Vector4 ResolveFallbackTint(
        in InfrastructureFeaturePresentationMetadata feature)
    {
        Vector4 baseTint =
            feature.Kind ==
            InfrastructurePresentationKind.RoadBridge
                ? new Vector4(
                    0.31f,
                    0.32f,
                    0.30f,
                    1.0f)
                : new Vector4(
                    0.23f,
                    0.24f,
                    0.23f,
                    1.0f);

        return feature.State switch
        {
            InfrastructurePresentationState.Restoring =>
                baseTint *
                new Vector4(
                    0.74f,
                    0.68f,
                    0.60f,
                    1.0f),
            InfrastructurePresentationState.Disabled =>
                new Vector4(
                    0.12f,
                    0.12f,
                    0.11f,
                    1.0f),
            _ =>
                baseTint
        };
    }

    public static Matrix4x4 AdjustWorldTransform(
        in InfrastructureFeaturePresentationMetadata feature,
        Matrix4x4 world)
    {
        float localLift =
            feature.Kind switch
            {
                InfrastructurePresentationKind.RoadBridge =>
                    0.25f,
                InfrastructurePresentationKind.RoadSegment or
                InfrastructurePresentationKind.Ford =>
                    0.48f,
                _ =>
                    0.0f
            };

        return Matrix4x4.CreateTranslation(
                   0.0f,
                   localLift,
                   0.0f) *
               world;
    }
}
