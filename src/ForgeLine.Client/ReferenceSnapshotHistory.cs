using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// Reference-only preparation; no simulation commands, pooling or graphics ownership.
internal static class ReferenceSnapshotHistory
{
    internal const int MaximumPairedInstances = 100_000;

    internal static RenderWorld Create(PresentationSnapshot current, PresentationSnapshot? previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is not null)
        {
            if (previous.SessionId != current.SessionId || previous.Tick >= current.Tick ||
                previous.TickDuration != current.TickDuration)
                throw new ArgumentException("Reference history requires older snapshots from the same session and tick duration.", nameof(previous));
            if (previous.InstanceCount > MaximumPairedInstances || current.InstanceCount > MaximumPairedInstances)
                throw new ArgumentException("Paired reference snapshots exceed the 100,000-instance budget.", nameof(previous));
        }

        var world = new RenderWorld();
        var buffer = new PresentationSnapshotBuffer();
        if (previous is not null)
        {
            buffer.Publish(previous);
            world.Update(buffer);
        }
        buffer.Publish(current);
        world.Update(buffer);
        return world;
    }

    internal static string Fingerprint(string currentFingerprint, PresentationSnapshot? previous)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFingerprint);
        if (previous is null) return currentFingerprint;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            historyVersion = 1, currentFingerprint,
            previous.Tick, previous.TickDuration, previous.SessionId, previous.SimulationEntityCount,
            instances = previous.Instances.ToArray()
        }, VisualReferenceCapture.JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
