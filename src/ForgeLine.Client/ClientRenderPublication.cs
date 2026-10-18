namespace ForgeLine.Client;

// GC-owned copies have indefinite reader lifetime; this mailbox never returns arrays to a pool.
internal sealed class ClientRenderPublication
{
    internal object Gate { get; } = new();
    private ClientRenderFrame? _latest;
    internal static ClientRenderFrame Copy(in ClientRenderFrame frame) => frame with
            {
                GameplayLines =
                    frame.GameplayLines.ToArray(),
                GameplayLabels =
                    frame.GameplayLabels.ToArray(),
                DebugLines =
                    frame.DebugLines.ToArray(),
                DebugLabels =
                    frame.DebugLabels.ToArray(),
                InformationLayer =
                    frame.InformationLayer with
                    {
                        SelectedEntities =
                            frame.InformationLayer.SelectedEntities?
                                .ToArray() ??
                            []
                    }
            };
    internal void StoreCopied(in ClientRenderFrame frame)
    {
        if (!Monitor.IsEntered(Gate)) throw new InvalidOperationException("Publication requires the shared mailbox gate.");
        _latest = frame;
    }
    internal ClientRenderFrame? ReadLatest()
    {
        lock (Gate) return _latest;
    }
}
