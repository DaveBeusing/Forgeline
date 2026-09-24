namespace ForgeLine.Jobs;

public readonly record struct JobSchedulerMetrics(
    long SubmittedJobs,
    long CompletedJobs,
    long FaultedJobs,
    long CanceledJobs,
    long PendingJobs,
    long RunningJobs,
    int WorkerCount,
    int PeakRunningJobs,
    TimeSpan TotalWaitDuration,
    TimeSpan TotalExecutionDuration,
    long InstrumentationFailures);
