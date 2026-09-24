namespace ForgeLine.Jobs;

public enum JobCompletionStatus
{
    Succeeded,
    Faulted,
    Canceled
}

public readonly record struct JobTimingSample(
    long Sequence,
    int WorkerIndex,
    TimeSpan WaitDuration,
    TimeSpan ExecutionDuration,
    JobCompletionStatus Status);
