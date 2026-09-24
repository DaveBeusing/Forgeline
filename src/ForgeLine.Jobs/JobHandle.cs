namespace ForgeLine.Jobs;

public readonly struct JobHandle
{
    internal JobHandle(JobCompletion completion)
    {
        Completion = completion;
    }

    internal JobCompletion? Completion { get; }

    public bool IsValid => Completion is not null;

    public bool IsCompleted => Completion is null || Completion.IsCompleted;

    public bool IsFaulted => Completion?.Status == JobCompletionStatus.Faulted;

    public bool IsCanceled => Completion?.Status == JobCompletionStatus.Canceled;

    public void Wait()
    {
        Completion?.Owner.Wait(this);
    }
}
