namespace ForgeLine.Jobs;

public sealed class JobSchedulerOptions
{
    public int WorkerCount { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    public string WorkerNamePrefix { get; init; } = "ForgeLine Worker";

    public Action<JobTimingSample>? TimingObserver { get; init; }
}
