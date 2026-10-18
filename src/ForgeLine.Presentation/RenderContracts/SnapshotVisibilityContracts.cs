namespace ForgeLine.Presentation.RenderContracts;

// Read-only correspondence crosses ownership boundaries; implementations retain GC-owned snapshots.
internal interface IRenderSnapshotCorrespondence
{
    int PreviousIndex(int index);
}

internal interface IInstanceVisibilityIndex
{
    long RetainedBytes { get; }
    void Rebuild(PresentationSnapshot current, PresentationSnapshot? previous, IRenderSnapshotCorrespondence correspondence);
    bool IsVisible(int firstIndex, in ViewFrustum frustum);
}
