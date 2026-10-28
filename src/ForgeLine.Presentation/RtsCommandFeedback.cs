using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum RtsCommandMarkerKind : byte { Move, Attack, AttackMove }
public enum RtsCommandMarkerState : byte { Accepted, Pending, Partial, Rejected }
public readonly record struct RtsCommandMarkerView(bool Visible, Vector3 Position, bool Valid, RtsCommandMarkerKind Kind)
{
    public RtsCommandMarkerState State { get; init; }
}

public sealed class RtsCommandFeedback
{
    private float _remainingSeconds;
    private ulong _hostSequence;
    private SimulationSessionId _session;
    private PlayerCommandCorrelationId _correlation;
    private readonly PlayerCommandResultReadModel[] _earlyResults = new PlayerCommandResultReadModel[128];
    private int _earlyCount;
    public bool IsVisible => _remainingSeconds > 0.0f;
    public Vector3 Position { get; private set; }
    public bool IsValid { get; private set; }
    public bool IsAttack { get; private set; }
    public RtsCommandMarkerState State { get; private set; }
    private bool _attackMove;
    public RtsCommandMarkerView View => new(IsVisible, Position, IsValid,
        _attackMove ? RtsCommandMarkerKind.AttackMove : IsAttack ? RtsCommandMarkerKind.Attack : RtsCommandMarkerKind.Move)
        { State = State };

    // A valid local target is pending intent, never proof of simulation acceptance.
    public void Show(Vector3 position, bool valid, bool attack = false, bool attackMove = false)
    {
        _hostSequence = 0; _correlation = default; _session = default; _earlyCount = 0;
        Position = position;
        IsValid = valid;
        IsAttack = attack;
        _attackMove = attack && attackMove;
        State = valid ? RtsCommandMarkerState.Pending : RtsCommandMarkerState.Rejected;
        _remainingSeconds = 0.8f;
    }

    public void TrackSubmission(ulong hostSequence, SimulationSessionId session)
    {
        _hostSequence = hostSequence;
        _session = session;
        _correlation = default;
        _earlyCount = 0;
        State = RtsCommandMarkerState.Pending;
        _remainingSeconds = float.PositiveInfinity;
    }

    public void ObserveSubmission(ulong hostSequence, PlayerCommandSubmissionReceipt? receipt)
    {
        if (_hostSequence == 0 || hostSequence != _hostSequence) return;
        _hostSequence = 0;
        if (receipt is not { Accepted: true } accepted || accepted.SessionId != _session)
        {
            Resolve(RtsCommandMarkerState.Rejected);
            return;
        }
        _correlation = accepted.CorrelationId;
        for (int i = 0; i < _earlyCount; i++)
            if (_earlyResults[i].SessionId == _session && _earlyResults[i].CorrelationId == _correlation)
            { Apply(_earlyResults[i]); break; }
        _earlyCount = 0;
    }

    public void ObserveResult(in PlayerCommandResultReadModel result)
    {
        if (result.SessionId != _session) return;
        if (_correlation.IsSpecified && result.CorrelationId == _correlation) Apply(result);
        // The render owner may drain a result before its host receipt. Keep bounded copied results.
        else if (_hostSequence != 0 && _earlyCount < _earlyResults.Length) _earlyResults[_earlyCount++] = result;
    }

    private void Apply(in PlayerCommandResultReadModel result)
    {
        Resolve(result.State switch
        {
            PlayerCommandFeedbackState.Accepted => RtsCommandMarkerState.Accepted,
            PlayerCommandFeedbackState.Partial => RtsCommandMarkerState.Partial,
            _ => RtsCommandMarkerState.Rejected
        });
    }

    private void Resolve(RtsCommandMarkerState state)
    {
        State = state;
        IsValid = state is RtsCommandMarkerState.Accepted or RtsCommandMarkerState.Partial;
        _correlation = default;
        _earlyCount = 0;
        _remainingSeconds = 0.8f;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
            _remainingSeconds = MathF.Max(0.0f, _remainingSeconds - (float)elapsed.TotalSeconds);
    }

    public void Clear()
    {
        _remainingSeconds = 0.0f;
        _hostSequence = 0; _session = default; _correlation = default; _earlyCount = 0;
    }

    public void Draw(DebugDraw draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!IsVisible) return;
        Vector4 color = State is RtsCommandMarkerState.Pending or RtsCommandMarkerState.Partial
            ? new(1.0f, 0.72f, 0.18f, 1.0f) : IsAttack ? new(1.0f, 0.72f, 0.18f, 1.0f) : new(0.2f, 0.95f, 0.85f, 1.0f);
        RtsWorldMarkerVisualization.DrawTarget(draw, Position, IsValid, color, new(1.0f, 0.18f, 0.12f, 1.0f));
        if (State == RtsCommandMarkerState.Accepted && IsAttack)
            draw.Circle(Position + Vector3.UnitY * 0.2f, 2.0f, color, 12);
    }
}
