using System.Runtime.CompilerServices;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed record NavigationSchedulerIdentity(
    string ContentIdentity,
    ulong WorldGeneration,
    int AlgorithmRevision)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ContentIdentity);
        ArgumentOutOfRangeException.ThrowIfZero(WorldGeneration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(AlgorithmRevision);
    }
}

internal sealed record NavigationSchedulerStorageLimits(
    int MaximumSnapshotProfiles,
    long MaximumCheckpointBytes,
    int MaximumKernelCheckpointBytes,
    long ReservedPoolBytes)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSnapshotProfiles);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumSnapshotProfiles, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumCheckpointBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumKernelCheckpointBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReservedPoolBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumKernelCheckpointBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            (long)MaximumKernelCheckpointBytes, MaximumCheckpointBytes);
    }
}

internal readonly record struct NavigationSchedulerSnapshotKey(
    NavigationCapabilities Capabilities,
    ulong KnowledgeScope,
    NavigationSnapshotRevision Revision,
    NavigationModuleAvailability CostAvailability);

internal delegate int NavigationCheckpointEncoder<TCheckpoint>(
    TCheckpoint checkpoint, Span<byte> destination) where TCheckpoint : class;

internal delegate TCheckpoint NavigationCheckpointDecoder<TCheckpoint>(
    ReadOnlySpan<byte> source, out int bytesRead) where TCheckpoint : class;

/// <summary>
/// A concrete, versioned codec chosen at composition. The destination bounds serialized
/// bytes, not allocations inside a kernel's CaptureState or inside the codec delegates.
/// Those transient allocations require a separately bounded concrete kernel contract.
/// </summary>
internal sealed class NavigationSchedulerCheckpointCodec<TCheckpoint> where TCheckpoint : class
{
    private readonly NavigationCheckpointEncoder<TCheckpoint> _encode;
    private readonly NavigationCheckpointDecoder<TCheckpoint> _decode;

    internal NavigationSchedulerCheckpointCodec(int codecRevision,
        NavigationCheckpointEncoder<TCheckpoint> encode,
        NavigationCheckpointDecoder<TCheckpoint> decode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(codecRevision);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(decode);
        CodecRevision = codecRevision;
        _encode = encode;
        _decode = decode;
    }

    internal int CodecRevision { get; }

    internal int Encode(TCheckpoint checkpoint, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        int written = _encode(checkpoint, destination);
        if (written < 0 || written > destination.Length)
            throw new InvalidOperationException("Checkpoint encoding exceeded its reserved destination.");
        return written;
    }

    internal TCheckpoint Decode(ReadOnlySpan<byte> source)
    {
        TCheckpoint state = _decode(source, out int bytesRead);
        if (state is null || bytesRead != source.Length)
            throw new InvalidOperationException("Checkpoint decoding must consume the complete bounded payload and return state.");
        return state;
    }
}

/// <summary>Detached request metadata and bounded encoded kernel state; never a live session or workspace lease.</summary>
internal sealed class NavigationScheduledRequestCheckpoint
{
    // Includes the request, specification, binding and checkpoint object allowances.
    internal const long MetadataBytes = 1024;
    private readonly byte[]? _kernelPayload;

    internal NavigationScheduledRequestCheckpoint(
        NavigationBoundSearch request,
        NavigationServiceClass serviceClass,
        ulong inputSequence,
        SimulationTick queuedAt,
        bool hasQueuedAt,
        bool needsAdmission,
        NavigationSchedulerSnapshotKey snapshotKey,
        bool isActive,
        ReadOnlySpan<byte> kernelPayload,
        int maximumKernelCheckpointBytes)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (!Enum.IsDefined(serviceClass)) throw new ArgumentOutOfRangeException(nameof(serviceClass));
        ArgumentOutOfRangeException.ThrowIfZero(inputSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKernelCheckpointBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumKernelCheckpointBytes, Array.MaxLength);
        if (kernelPayload.Length > maximumKernelCheckpointBytes)
            throw new ArgumentException("Kernel checkpoint exceeds its configured byte limit.", nameof(kernelPayload));
        if (snapshotKey.Capabilities != request.Search.Capabilities ||
            snapshotKey.KnowledgeScope != request.Search.KnowledgeScope ||
            snapshotKey.Revision != request.Search.Revision || !Enum.IsDefined(snapshotKey.CostAvailability) ||
            (snapshotKey.CostAvailability == NavigationModuleAvailability.Available && snapshotKey.Revision.Costs == 0))
            throw new ArgumentException("Checkpoint inputs must retain the exact request snapshot key.", nameof(snapshotKey));
        if (isActive && (needsAdmission || !hasQueuedAt))
            throw new ArgumentException("Active state must already have a recorded admission age.", nameof(isActive));
        if (!isActive && !kernelPayload.IsEmpty)
            throw new ArgumentException("A waiting request cannot retain an unaccounted suspended frontier.", nameof(kernelPayload));
        if (!hasQueuedAt && queuedAt != default)
            throw new ArgumentException("An unset queue age must retain its canonical empty tick.", nameof(queuedAt));

        Request = request;
        ServiceClass = serviceClass;
        InputSequence = inputSequence;
        QueuedAt = queuedAt;
        HasQueuedAt = hasQueuedAt;
        NeedsAdmission = needsAdmission;
        SnapshotKey = snapshotKey;
        IsActive = isActive;
        EstimatedBytes = checked(MetadataBytes + PayloadMemoryBytes(kernelPayload.Length));
        _kernelPayload = isActive ? kernelPayload.ToArray() : null;
    }

    internal NavigationBoundSearch Request { get; }
    internal NavigationServiceClass ServiceClass { get; }
    internal ulong InputSequence { get; }
    internal SimulationTick QueuedAt { get; }
    internal bool HasQueuedAt { get; }
    internal bool NeedsAdmission { get; }
    internal NavigationSchedulerSnapshotKey SnapshotKey { get; }
    internal bool IsActive { get; }
    internal ReadOnlySpan<byte> KernelPayload => _kernelPayload;
    internal long EstimatedBytes { get; }

    internal static long PayloadMemoryBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return length == 0 ? 0 : checked((64L + length + 7) & ~7L);
    }
}

/// <summary>
/// Immutable scheduler data at a completed job barrier. Snapshot keys are resolved from
/// validated immutable publications on restore; live snapshots and services are not saved.
/// Estimated bytes bound retained managed data, not generic kernel capture/decode peaks.
/// </summary>
internal sealed class NavigationSchedulerCheckpoint<TCheckpoint> where TCheckpoint : class
{
    internal const int CurrentRevision = 1;
    private const long FixedMetadataBytes = 4096;

    internal NavigationSchedulerCheckpoint(
        int revision,
        int contractRevision,
        NavigationSchedulerIdentity identity,
        NavigationIntegrationLimits integrationLimits,
        NavigationSchedulingPolicyLimits policyLimits,
        NavigationSchedulerStorageLimits storageLimits,
        int codecRevision,
        NavigationVersion currentGeometry,
        bool hasGeometry,
        SimulationTick lastTick,
        bool hasAdvanced,
        ulong nextSubmissionSequence,
        ReadOnlySpan<NavigationSchedulerSnapshotKey> snapshotKeys,
        NavigationSchedulingPolicyCheckpoint policy,
        ReadOnlySpan<NavigationScheduledRequestCheckpoint> entries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contractRevision);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(integrationLimits);
        ArgumentNullException.ThrowIfNull(policyLimits);
        ArgumentNullException.ThrowIfNull(storageLimits);
        ArgumentNullException.ThrowIfNull(policy);
        identity.Validate();
        integrationLimits.Validate();
        policyLimits.Validate();
        storageLimits.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(codecRevision);
        ArgumentOutOfRangeException.ThrowIfZero(nextSubmissionSequence);
        if (snapshotKeys.Length > storageLimits.MaximumSnapshotProfiles)
            throw new ArgumentException("Snapshot registry exceeds its configured capacity.", nameof(snapshotKeys));
        if (entries.Length > policyLimits.MaximumEntries || policy.Entries.Count > policyLimits.MaximumEntries)
            throw new ArgumentException("Checkpoint entries exceed the scheduling capacity.", nameof(entries));
        if (policy.Limits != policyLimits)
            throw new ArgumentException("Policy checkpoint configuration does not match the scheduler.", nameof(policy));
        if (hasGeometry != currentGeometry.IsValid)
            throw new ArgumentException("Geometry presence and revision must agree.", nameof(currentGeometry));
        if (!hasAdvanced && lastTick != default)
            throw new ArgumentException("An unused scheduler must retain its canonical empty tick.", nameof(lastTick));

        long estimatedBytes = EstimateMetadataBytes(identity, snapshotKeys.Length,
            entries.Length, policy.Entries.Count);
        int active = 0;
        foreach (NavigationSchedulerSnapshotKey key in snapshotKeys)
        {
            if (!key.Revision.IsValid || !Enum.IsDefined(key.Capabilities.MovementClass) ||
                !Enum.IsDefined(key.CostAvailability) ||
                (key.CostAvailability == NavigationModuleAvailability.Available && key.Revision.Costs == 0) ||
                !hasGeometry || key.Revision.Geometry > currentGeometry)
                throw new ArgumentException("Snapshot registry contains an invalid input revision.", nameof(snapshotKeys));
        }
        foreach (NavigationScheduledRequestCheckpoint entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.InputSequence >= nextSubmissionSequence)
                throw new ArgumentException("The next request sequence must follow all retained requests.", nameof(nextSubmissionSequence));
            if (entry.KernelPayload.Length > storageLimits.MaximumKernelCheckpointBytes)
                throw new ArgumentException("Retained kernel checkpoint exceeds its configured capacity.", nameof(entries));
            if (entry.IsActive) active++;
            estimatedBytes = checked(estimatedBytes + PayloadMemoryBytes(entry.KernelPayload.Length));
        }
        if (active > integrationLimits.MaximumActiveSearches)
            throw new ArgumentException("Active checkpoints exceed the configured session limit.", nameof(entries));
        if (estimatedBytes > storageLimits.MaximumCheckpointBytes)
            throw new ArgumentException("Scheduler checkpoint exceeds its complete byte limit.", nameof(entries));

        // Validate total retained capacity before allocating either owned outer array.
        Revision = revision;
        ContractRevision = contractRevision;
        Identity = identity;
        IntegrationLimits = integrationLimits;
        PolicyLimits = policyLimits;
        StorageLimits = storageLimits;
        CodecRevision = codecRevision;
        CurrentGeometry = currentGeometry;
        HasGeometry = hasGeometry;
        LastTick = lastTick;
        HasAdvanced = hasAdvanced;
        NextSubmissionSequence = nextSubmissionSequence;
        SnapshotKeys = Array.AsReadOnly(snapshotKeys.ToArray());
        Policy = policy;
        Entries = Array.AsReadOnly(entries.ToArray());
        EstimatedBytes = estimatedBytes;
    }

    internal int Revision { get; }
    internal int ContractRevision { get; }
    internal NavigationSchedulerIdentity Identity { get; }
    internal NavigationIntegrationLimits IntegrationLimits { get; }
    internal NavigationSchedulingPolicyLimits PolicyLimits { get; }
    internal NavigationSchedulerStorageLimits StorageLimits { get; }
    internal int CodecRevision { get; }
    internal NavigationVersion CurrentGeometry { get; }
    internal bool HasGeometry { get; }
    internal SimulationTick LastTick { get; }
    internal bool HasAdvanced { get; }
    internal ulong NextSubmissionSequence { get; }
    internal IReadOnlyList<NavigationSchedulerSnapshotKey> SnapshotKeys { get; }
    internal NavigationSchedulingPolicyCheckpoint Policy { get; }
    internal IReadOnlyList<NavigationScheduledRequestCheckpoint> Entries { get; }
    internal long EstimatedBytes { get; }

    /// <summary>
    /// Preflight for capture: metadata plus an optional reusable serialization scratch
    /// buffer, excluding encoded payload arrays. Both the temporary and owned outer
    /// arrays are included. Add PayloadMemoryBytes for each payload before copying it.
    /// CaptureState and codec-internal allocations are separate.
    /// </summary>
    internal static long EstimateMetadataBytes(NavigationSchedulerIdentity identity,
        int snapshotCount, int entryCount, int policyEntryCount, int scratchBufferBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(policyEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scratchBufferBytes);
        return checked(FixedMetadataBytes + ArrayBytes(identity.ContentIdentity.Length + 1, sizeof(char)) +
            2 * ArrayBytes(snapshotCount, Unsafe.SizeOf<NavigationSchedulerSnapshotKey>()) +
            2 * ArrayBytes(entryCount, IntPtr.Size) +
            2 * ArrayBytes(policyEntryCount, Unsafe.SizeOf<NavigationPolicyEntryState>()) +
            entryCount * NavigationScheduledRequestCheckpoint.MetadataBytes +
            PayloadMemoryBytes(scratchBufferBytes));
    }

    private static long PayloadMemoryBytes(int length) =>
        NavigationScheduledRequestCheckpoint.PayloadMemoryBytes(length);

    private static long ArrayBytes(int count, int elementBytes) =>
        checked((64L + (long)count * elementBytes + 7) & ~7L);
}
