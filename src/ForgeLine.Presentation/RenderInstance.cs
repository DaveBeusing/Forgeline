using ForgeLine.Core;

namespace ForgeLine.Presentation;

public readonly record struct RenderInstance(
    EntityId Entity,
    RenderTransform Transform,
    RenderMeshHandle Mesh,
    RenderMaterialHandle Material,
    RenderVisibilityMask Visibility,
    uint DebugIdentity = 0,
    SelectablePresentationMetadata Selectable = default,
    WorldFeaturePresentationMetadata WorldFeature = default,
    UnitFeaturePresentationMetadata UnitFeature = default,
    BuildingFeaturePresentationMetadata BuildingFeature = default,
    InfrastructureFeaturePresentationMetadata InfrastructureFeature = default,
    VfxFeaturePresentationMetadata VfxFeature = default,
    MechanicalPose MechanicalPose = default,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    DecalFeaturePresentationMetadata DecalFeature = default,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    ConveyorFeaturePresentationMetadata ConveyorFeature = default,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ResourceDepositField? ResourceField = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public DecorationRenderMetadata Decoration { get; init; }
}
