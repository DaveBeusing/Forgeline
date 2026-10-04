namespace ForgeLine.UI;

public enum FrontendLoadingPhase : byte
{
    Starting = 1,
    LoadingSettings = 2,
    LoadingAssets = 3,
    PreparingFrontend = 4,
    Ready = 5,
    Failed = 6
}

public readonly record struct FrontendLoadingState(
    FrontendLoadingPhase Phase,
    string Status,
    int CompletedSteps,
    int TotalSteps,
    string? FailureMessage = null)
{
    public bool IsComplete => Phase == FrontendLoadingPhase.Ready;

    public bool HasFailed => Phase == FrontendLoadingPhase.Failed;

    public bool HasDeterminateProgress => TotalSteps > 0;

    public float Progress =>
        HasDeterminateProgress
            ? Math.Clamp((float)CompletedSteps / TotalSteps, 0f, 1f)
            : 0f;
}

public sealed class FrontendLoadingController
{
    public const string ProductName = "FORGELINE";
    public const string Tagline = "Build. Supply. Conquer.";

    private FrontendLoadingState _state = new(
        FrontendLoadingPhase.Starting,
        "Starting",
        0,
        0);

    public FrontendLoadingState State => _state;

    public event Action<FrontendLoadingState>? StateChanged;

    public void BeginPhase(
        FrontendLoadingPhase phase,
        string status,
        int totalSteps = 0)
    {
        if (phase is FrontendLoadingPhase.Ready or FrontendLoadingPhase.Failed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(phase),
                "Ready and Failed are terminal loading states.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentOutOfRangeException.ThrowIfNegative(totalSteps);

        SetState(new FrontendLoadingState(phase, status, 0, totalSteps));
    }

    public void ReportProgress(int completedSteps, string? status = null)
    {
        if (_state.IsComplete || _state.HasFailed)
        {
            throw new InvalidOperationException("Loading has already reached a terminal state.");
        }

        if (!_state.HasDeterminateProgress)
        {
            throw new InvalidOperationException(
                "Progress cannot be reported for an indeterminate loading phase.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(completedSteps);

        if (completedSteps > _state.TotalSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedSteps),
                "Completed steps cannot exceed total steps.");
        }

        SetState(_state with
        {
            CompletedSteps = completedSteps,
            Status = status ?? _state.Status
        });
    }

    public void Complete(string status = "Ready")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        SetState(new FrontendLoadingState(
            FrontendLoadingPhase.Ready,
            status,
            1,
            1));
    }

    public void Fail(string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        SetState(new FrontendLoadingState(
            FrontendLoadingPhase.Failed,
            "Startup failed",
            0,
            0,
            failureMessage));
    }

    private void SetState(FrontendLoadingState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }
}
