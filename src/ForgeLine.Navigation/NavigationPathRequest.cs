using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Navigation;

public readonly record struct NavigationPathRequest
{
    public NavigationPathRequest(
        ulong requestId,
        EntityId requester,
        Vector3 start,
        Vector3 destination,
        NavigationCapabilities capabilities,
        NavigationVersion navigationVersion)
    {
        if (requestId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestId));
        }

        if (!requester.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(requester));
        }

        if (!IsFinite(start))
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (!IsFinite(destination))
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (!navigationVersion.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(navigationVersion));
        }

        RequestId = requestId;
        Requester = requester;
        Start = start;
        Destination = destination;
        Capabilities = capabilities;
        NavigationVersion = navigationVersion;
    }

    public ulong RequestId { get; }

    public EntityId Requester { get; }

    public Vector3 Start { get; }

    public Vector3 Destination { get; }

    public NavigationCapabilities Capabilities { get; }

    public NavigationVersion NavigationVersion { get; }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}

public readonly record struct NavigationPathResult(
    NavigationPathRequest Request,
    NavigationSearchResult Search,
    TimeSpan Latency)
{
    public bool Succeeded => Search.Succeeded;
}
