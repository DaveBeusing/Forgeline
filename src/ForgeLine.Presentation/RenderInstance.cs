using ForgeLine.Core;

namespace ForgeLine.Presentation;

public readonly record struct RenderInstance(
    EntityId Entity,
    RenderTransform Transform,
    RenderMeshHandle Mesh,
    RenderMaterialHandle Material,
    RenderVisibilityMask Visibility,
    uint DebugIdentity = 0,
    SelectablePresentationMetadata Selectable = default);
