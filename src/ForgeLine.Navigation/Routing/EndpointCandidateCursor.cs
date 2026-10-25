using System.Numerics;

namespace ForgeLine.Navigation;

internal enum EndpointCandidateStep : byte
{
    Pending = 0,
    Candidate = 1,
    EnumerationComplete = 2,
    CandidateLimitReached = 3
}

// A probe is neither a route nor permission to move. The search kernel must
// prove connectivity, oriented clearance and every connector before using it.
internal readonly record struct EndpointCandidateProbe(
    NavigationCellCoordinate Cell,
    Vector3 Position,
    bool IsOriginalDestination);

internal readonly record struct EndpointCandidateCursorState(
    NavigationVersion Geometry,
    Vector2 GridOrigin,
    int GridWidth,
    int GridHeight,
    float CellSizeMeters,
    NavigationCapabilities Capabilities,
    Vector3 OriginalDestination,
    float MaximumProjectionDistance,
    int MaximumCandidates,
    bool InspectedOriginal,
    long Ring,
    long RingOffset,
    int EmittedCandidates);

/// <summary>
/// Enumerates possible endpoints with charged cell/empty perimeter-side inspections.
/// It never projects a start pose or claims that a candidate is reachable.
/// </summary>
internal sealed class EndpointCandidateCursor
{
    private readonly NavigationGrid _grid;
    private readonly NavigationVersion _geometry;
    private readonly NavigationCapabilities _capabilities;
    private readonly Vector3 _originalDestination;
    private readonly NavigationCellCoordinate _origin;
    private readonly float _maximumProjectionDistance;
    private readonly int _maximumCandidates;
    private readonly long _maximumRing;
    private bool _inspectedOriginal;
    private long _ring;
    private long _ringOffset;
    private int _emittedCandidates;

    public EndpointCandidateCursor(
        NavigationGrid grid,
        NavigationVersion geometry,
        NavigationCapabilities capabilities,
        Vector3 originalDestination,
        float maximumProjectionDistance,
        int maximumCandidates)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!geometry.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(geometry));
        }

        if (!Enum.IsDefined(capabilities.MovementClass))
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        if (!float.IsFinite(maximumProjectionDistance) || maximumProjectionDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumProjectionDistance));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidates);
        if (!grid.TryWorldToCell(originalDestination, out _origin))
        {
            throw new ArgumentOutOfRangeException(nameof(originalDestination));
        }

        _grid = grid;
        _geometry = geometry;
        _capabilities = capabilities;
        _originalDestination = originalDestination;
        _maximumProjectionDistance = maximumProjectionDistance;
        _maximumCandidates = maximumCandidates;

        // Clamp in double before conversion so even a finite, enormous radius
        // cannot overflow an integer or expand beyond this finite grid.
        long gridRadius = Math.Max(
            Math.Max(_origin.X, grid.Width - 1 - _origin.X),
            Math.Max(_origin.Z, grid.Height - 1 - _origin.Z));
        _maximumRing = maximumProjectionDistance == 0
            ? -1
            : (long)Math.Min(gridRadius,
                Math.Ceiling((double)maximumProjectionDistance / grid.Settings.CellSizeMeters) + 1);
    }

    public EndpointCandidateStep MoveNext(
        int inspectionAllowance,
        out EndpointCandidateProbe candidate,
        out int workConsumed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inspectionAllowance);
        candidate = default;
        workConsumed = 0;

        while (true)
        {
            if (_inspectedOriginal && _ring > _maximumRing)
            {
                return EndpointCandidateStep.EnumerationComplete;
            }

            if (_emittedCandidates == _maximumCandidates)
            {
                // Candidate count is a work bound, not proof of disconnection.
                return EndpointCandidateStep.CandidateLimitReached;
            }

            if (workConsumed == inspectionAllowance)
            {
                return EndpointCandidateStep.Pending;
            }

            workConsumed++;
            if (!_inspectedOriginal)
            {
                _inspectedOriginal = true;
                if (_grid.IsTraversable(_origin, _capabilities))
                {
                    _emittedCandidates++;
                    candidate = new(_origin, _originalDestination, IsOriginalDestination: true);
                    return EndpointCandidateStep.Candidate;
                }

                continue;
            }

            if (!TryTakeCoordinate(out NavigationCellCoordinate cell))
            {
                continue;
            }

            if (!_grid.IsTraversable(cell, _capabilities))
            {
                continue;
            }

            Vector3 position = _grid.GetCellCenter(cell);
            double deltaX = (double)position.X - _originalDestination.X;
            double deltaZ = (double)position.Z - _originalDestination.Z;
            double distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
            double limitSquared = (double)_maximumProjectionDistance * _maximumProjectionDistance;
            if (position == _originalDestination || distanceSquared > limitSquared)
            {
                continue;
            }

            _emittedCandidates++;
            candidate = new(cell, position, IsOriginalDestination: false);
            return EndpointCandidateStep.Candidate;
        }
    }

    public EndpointCandidateCursorState CaptureState() => new(
        _geometry, _grid.Origin, _grid.Width, _grid.Height, _grid.Settings.CellSizeMeters,
        _capabilities, _originalDestination, _maximumProjectionDistance, _maximumCandidates,
        _inspectedOriginal, _ring, _ringOffset, _emittedCandidates);

    public void RestoreState(in EndpointCandidateCursorState state)
    {
        if (state.Geometry != _geometry || state.GridOrigin != _grid.Origin ||
            state.GridWidth != _grid.Width || state.GridHeight != _grid.Height ||
            state.CellSizeMeters != _grid.Settings.CellSizeMeters ||
            state.Capabilities != _capabilities || state.OriginalDestination != _originalDestination ||
            state.MaximumProjectionDistance != _maximumProjectionDistance ||
            state.MaximumCandidates != _maximumCandidates ||
            state.Ring < 0 || state.Ring > Math.Max(0, _maximumRing + 1) ||
            state.RingOffset < 0 ||
            (state.Ring == 0 && state.RingOffset != 0) ||
            (state.Ring > 0 && state.RingOffset >= 8 * state.Ring) ||
            (state.Ring > _maximumRing && state.RingOffset != 0) ||
            state.EmittedCandidates < 0 || state.EmittedCandidates > _maximumCandidates ||
            (!state.InspectedOriginal &&
                (state.Ring != 0 || state.RingOffset != 0 || state.EmittedCandidates != 0)))
        {
            throw new ArgumentException("Endpoint cursor does not match its pinned input or bounds.", nameof(state));
        }

        // Skipped out-of-grid perimeter spans cannot have produced candidates.
        // The preceding positions therefore give a conservative upper bound.
        double precedingSide = 2 * (double)state.Ring - 1;
        double inspectedCells = state.Ring == 0
            ? 0
            : precedingSide * precedingSide + state.RingOffset;
        if (state.EmittedCandidates > inspectedCells + (state.InspectedOriginal ? 1 : 0))
        {
            throw new ArgumentException("Endpoint cursor contains an impossible candidate count.", nameof(state));
        }

        _inspectedOriginal = state.InspectedOriginal;
        _ring = state.Ring;
        _ringOffset = state.RingOffset;
        _emittedCandidates = state.EmittedCandidates;
    }

    private bool TryTakeCoordinate(out NavigationCellCoordinate cell)
    {
        if (_ring == 0)
        {
            cell = _origin;
            _ring = 1;
            return true;
        }

        long sideLength = 2 * _ring;
        long side = _ringOffset / sideLength;
        long offset = _ringOffset % sideLength;
        bool horizontal = side is 0 or 2;
        long fixedCoordinate = side switch
        {
            0 => _origin.Z - _ring,
            1 => _origin.X + _ring,
            2 => _origin.Z + _ring,
            _ => _origin.X - _ring
        };
        long fixedLimit = horizontal ? _grid.Height : _grid.Width;
        long variableLimit = horizontal ? _grid.Width : _grid.Height;
        long variableOrigin = horizontal ? _origin.X : _origin.Z;
        bool increasing = side < 2;
        long variableStart = variableOrigin + (increasing ? -_ring : _ring);
        long minimum = increasing ? -variableStart : variableStart - (variableLimit - 1);
        long maximum = increasing ? variableLimit - 1 - variableStart : variableStart;
        minimum = Math.Max(offset, Math.Max(0, minimum));
        maximum = Math.Min(sideLength - 1, maximum);

        // Clip each side in constant work. Thin worlds do not walk every
        // out-of-world square-ring position; an empty side costs one inspection.
        if (fixedCoordinate < 0 || fixedCoordinate >= fixedLimit || minimum > maximum)
        {
            SetNextOffset((side + 1) * sideLength);
            cell = default;
            return false;
        }

        long variable = variableStart + (increasing ? minimum : -minimum);
        cell = horizontal
            ? new NavigationCellCoordinate((int)variable, (int)fixedCoordinate)
            : new NavigationCellCoordinate((int)fixedCoordinate, (int)variable);
        SetNextOffset(minimum == maximum
            ? (side + 1) * sideLength
            : side * sideLength + minimum + 1);
        return true;
    }

    private void SetNextOffset(long offset)
    {
        _ringOffset = offset;
        if (_ringOffset == 8 * _ring)
        {
            _ring++;
            _ringOffset = 0;
        }
    }
}
