using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace ForgeLine.Jobs;

public sealed class JobScheduler : IDisposable
{
    [ThreadStatic]
    private static JobScheduler? s_currentScheduler;

    private static readonly JobAction s_noOpJob = static _ => { };

    private readonly object _gate = new();
    private readonly Queue<JobNode> _ready = new();
    private readonly HashSet<JobNode> _unfinishedNodes = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly Thread[] _workers;
    private readonly Action<JobTimingSample>? _timingObserver;

    private bool _accepting = true;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;
    private bool _cancelPending;
    private bool _stopWorkers;
    private bool _resourcesDisposed;
    private long _nextSequence;
    private long _submittedJobs;
    private long _completedJobs;
    private long _faultedJobs;
    private long _canceledJobs;
    private long _unfinishedJobs;
    private long _runningJobs;
    private int _peakRunningJobs;
    private long _totalWaitTicks;
    private long _totalExecutionTicks;
    private long _instrumentationFailures;

    public JobScheduler()
        : this(new JobSchedulerOptions())
    {
    }

    public JobScheduler(JobSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.WorkerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.WorkerCount,
                "Worker count must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkerNamePrefix))
        {
            throw new ArgumentException(
                "Worker name prefix must not be empty.",
                nameof(options));
        }

        _timingObserver = options.TimingObserver;
        _workers = new Thread[options.WorkerCount];

        for (int index = 0; index < _workers.Length; index++)
        {
            int workerIndex = index;
            var thread = new Thread(() => WorkerLoop(workerIndex))
            {
                IsBackground = true,
                Name = $"{options.WorkerNamePrefix} {workerIndex}"
            };

            _workers[index] = thread;
            thread.Start();
        }
    }

    public int WorkerCount => _workers.Length;

    public bool IsShutdown
    {
        get
        {
            lock (_gate)
            {
                return _shutdownCompleted;
            }
        }
    }

    public JobHandle Schedule(JobAction action)
    {
        return Schedule(action, ReadOnlySpan<JobHandle>.Empty);
    }

    public JobHandle Schedule(
        JobAction action,
        ReadOnlySpan<JobHandle> dependencies)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            ThrowIfNotAcceptingLocked();
            return CreateJobLocked(
                action,
                rangeAction: null,
                rangeStart: 0,
                rangeEnd: 0,
                dependencies);
        }
    }

    public JobHandle ParallelFor(
        int startInclusive,
        int endExclusive,
        int batchSize,
        JobRangeAction action)
    {
        return ParallelFor(
            startInclusive,
            endExclusive,
            batchSize,
            action,
            ReadOnlySpan<JobHandle>.Empty);
    }

    public JobHandle ParallelFor(
        int startInclusive,
        int endExclusive,
        int batchSize,
        JobRangeAction action,
        ReadOnlySpan<JobHandle> dependencies)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (endExclusive < startInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endExclusive),
                endExclusive,
                "Range end must be greater than or equal to range start.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                "Batch size must be greater than zero.");
        }

        long length = (long)endExclusive - startInclusive;

        if (length == 0)
        {
            return CreateFence(dependencies);
        }

        long batchCountLong = (length + batchSize - 1L) / batchSize;

        if (batchCountLong > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                "The requested range produces too many scheduler batches.");
        }

        int batchCount = (int)batchCountLong;
        JobHandle[] handles = ArrayPool<JobHandle>.Shared.Rent(batchCount);

        try
        {
            lock (_gate)
            {
                ThrowIfNotAcceptingLocked();

                for (int index = 0; index < batchCount; index++)
                {
                    long rangeStartLong = (long)startInclusive + ((long)index * batchSize);
                    int rangeStart = (int)rangeStartLong;
                    int rangeEnd = (int)Math.Min(rangeStartLong + batchSize, endExclusive);

                    handles[index] = CreateJobLocked(
                        action: null,
                        rangeAction: action,
                        rangeStart,
                        rangeEnd,
                        dependencies);
                }

                if (batchCount == 1)
                {
                    return handles[0];
                }

                return CreateJobLocked(
                    s_noOpJob,
                    rangeAction: null,
                    rangeStart: 0,
                    rangeEnd: 0,
                    handles.AsSpan(0, batchCount));
            }
        }
        finally
        {
            Array.Clear(handles, 0, batchCount);
            ArrayPool<JobHandle>.Shared.Return(handles);
        }
    }

    public JobHandle CreateFence(ReadOnlySpan<JobHandle> dependencies)
    {
        if (dependencies.Length == 0)
        {
            return default;
        }

        lock (_gate)
        {
            ThrowIfNotAcceptingLocked();
            return CreateJobLocked(
                s_noOpJob,
                rangeAction: null,
                rangeStart: 0,
                rangeEnd: 0,
                dependencies);
        }
    }

    public void WaitAll(ReadOnlySpan<JobHandle> handles)
    {
        Wait(CreateFence(handles));
    }

    public void Wait(JobHandle handle)
    {
        JobCompletion? completion = handle.Completion;

        if (completion is null)
        {
            return;
        }

        if (!ReferenceEquals(completion.Owner, this))
        {
            throw new ArgumentException(
                "The job handle belongs to a different scheduler.",
                nameof(handle));
        }

        if (ReferenceEquals(s_currentScheduler, this) && !completion.IsCompleted)
        {
            throw new InvalidOperationException(
                "Worker jobs must not synchronously wait on unfinished jobs from the same scheduler.");
        }

        ExceptionDispatchInfo? failure;
        JobCompletionStatus status;

        lock (_gate)
        {
            while (!completion.IsCompleted)
            {
                Monitor.Wait(_gate);
            }

            failure = completion.Failure;
            status = completion.Status;
        }

        if (status == JobCompletionStatus.Faulted)
        {
            failure!.Throw();
        }

        if (status == JobCompletionStatus.Canceled)
        {
            throw new OperationCanceledException(
                "The job was canceled during scheduler shutdown.");
        }
    }

    public JobSchedulerMetrics GetMetrics()
    {
        long unfinished = Interlocked.Read(ref _unfinishedJobs);
        long running = Interlocked.Read(ref _runningJobs);

        return new JobSchedulerMetrics(
            Interlocked.Read(ref _submittedJobs),
            Interlocked.Read(ref _completedJobs),
            Interlocked.Read(ref _faultedJobs),
            Interlocked.Read(ref _canceledJobs),
            Math.Max(0, unfinished - running),
            running,
            WorkerCount,
            Volatile.Read(ref _peakRunningJobs),
            StopwatchTicksToTimeSpan(Interlocked.Read(ref _totalWaitTicks)),
            StopwatchTicksToTimeSpan(Interlocked.Read(ref _totalExecutionTicks)),
            Interlocked.Read(ref _instrumentationFailures));
    }

    public void Shutdown(JobShutdownMode mode = JobShutdownMode.Drain)
    {
        if (ReferenceEquals(s_currentScheduler, this))
        {
            throw new InvalidOperationException(
                "A scheduler worker cannot shut down its own scheduler.");
        }

        bool cancelPending;

        lock (_gate)
        {
            if (_shutdownCompleted)
            {
                return;
            }

            if (_shutdownStarted)
            {
                while (!_shutdownCompleted)
                {
                    Monitor.Wait(_gate);
                }

                return;
            }

            _shutdownStarted = true;
            _accepting = false;
            _cancelPending = mode == JobShutdownMode.CancelPending;
            cancelPending = _cancelPending;
        }

        if (cancelPending)
        {
            _shutdownCancellation.Cancel();

            lock (_gate)
            {
                JobNode[] snapshot = _unfinishedNodes.ToArray();

                foreach (JobNode node in snapshot)
                {
                    if (!node.IsRunning && !node.Completion.IsCompleted)
                    {
                        CompleteNodeLocked(
                            node,
                            JobCompletionStatus.Canceled,
                            failure: null);
                    }
                }

                _ready.Clear();
            }
        }

        lock (_gate)
        {
            while (_unfinishedJobs > 0)
            {
                Monitor.Wait(_gate);
            }

            _stopWorkers = true;
            _ready.Clear();
        }

        _signal.Release(_workers.Length);

        foreach (Thread worker in _workers)
        {
            worker.Join();
        }

        lock (_gate)
        {
            _shutdownCompleted = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Dispose()
    {
        Shutdown(JobShutdownMode.Drain);

        lock (_gate)
        {
            if (_resourcesDisposed)
            {
                return;
            }

            _resourcesDisposed = true;
        }

        _shutdownCancellation.Dispose();
        _signal.Dispose();
    }

    private JobHandle CreateJobLocked(
        JobAction? action,
        JobRangeAction? rangeAction,
        int rangeStart,
        int rangeEnd,
        ReadOnlySpan<JobHandle> dependencies)
    {
        var completion = new JobCompletion(this, _nextSequence++);
        var node = new JobNode(
            completion,
            action,
            rangeAction,
            rangeStart,
            rangeEnd);

        _unfinishedNodes.Add(node);
        _submittedJobs++;
        _unfinishedJobs++;

        AttachDependenciesLocked(node, dependencies);
        return new JobHandle(completion);
    }

    private void AttachDependenciesLocked(
        JobNode node,
        ReadOnlySpan<JobHandle> dependencies)
    {
        for (int index = 0; index < dependencies.Length; index++)
        {
            JobCompletion? dependency = dependencies[index].Completion;

            if (dependency is null)
            {
                continue;
            }

            if (!ReferenceEquals(dependency.Owner, this))
            {
                CompleteNodeLocked(
                    node,
                    JobCompletionStatus.Faulted,
                    ExceptionDispatchInfo.Capture(
                        new ArgumentException(
                            "All dependency handles must belong to the same scheduler.",
                            nameof(dependencies))));
                return;
            }

            if (dependency.IsCompleted)
            {
                MergeDependencyOutcome(node, dependency);
                continue;
            }

            node.RemainingDependencies++;
            dependency.Dependents ??= new List<JobNode>();
            dependency.Dependents.Add(node);
        }

        if (node.Completion.IsCompleted)
        {
            return;
        }

        if (node.RemainingDependencies == 0)
        {
            ResolveReadyOrFailedDependencyLocked(node);
        }
    }

    private void WorkerLoop(int workerIndex)
    {
        s_currentScheduler = this;

        try
        {
            while (true)
            {
                _signal.Wait();

                JobNode? node;

                lock (_gate)
                {
                    if (_stopWorkers && _ready.Count == 0)
                    {
                        return;
                    }

                    if (_ready.Count == 0)
                    {
                        continue;
                    }

                    node = _ready.Dequeue();

                    if (node.Completion.IsCompleted)
                    {
                        continue;
                    }

                    if (_cancelPending)
                    {
                        CompleteNodeLocked(
                            node,
                            JobCompletionStatus.Canceled,
                            failure: null);
                        continue;
                    }

                    node.IsRunning = true;
                    _runningJobs++;
                    UpdatePeakRunningLocked((int)_runningJobs);
                }

                long started = Stopwatch.GetTimestamp();
                ExceptionDispatchInfo? failure = null;
                JobCompletionStatus status = JobCompletionStatus.Succeeded;

                try
                {
                    node.Execute(_shutdownCancellation.Token);
                }
                catch (OperationCanceledException exception)
                    when (_shutdownCancellation.IsCancellationRequested)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                    status = JobCompletionStatus.Canceled;
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                    status = JobCompletionStatus.Faulted;
                }

                long completed = Stopwatch.GetTimestamp();
                long waitTicks = started - node.SubmittedTimestamp;
                long executionTicks = completed - started;

                lock (_gate)
                {
                    node.IsRunning = false;
                    _runningJobs--;
                    _totalWaitTicks += waitTicks;
                    _totalExecutionTicks += executionTicks;
                    CompleteNodeLocked(node, status, failure);
                }

                PublishTiming(
                    new JobTimingSample(
                        node.Completion.Sequence,
                        workerIndex,
                        StopwatchTicksToTimeSpan(waitTicks),
                        StopwatchTicksToTimeSpan(executionTicks),
                        status));
            }
        }
        finally
        {
            s_currentScheduler = null;
        }
    }

    private void EnqueueReadyLocked(JobNode node)
    {
        _ready.Enqueue(node);
        _signal.Release();
    }

    private void ResolveReadyOrFailedDependencyLocked(JobNode node)
    {
        if (node.DependencyFailure is not null)
        {
            CompleteNodeLocked(
                node,
                JobCompletionStatus.Faulted,
                node.DependencyFailure);
            return;
        }

        if (node.DependencyCanceled)
        {
            CompleteNodeLocked(
                node,
                JobCompletionStatus.Canceled,
                failure: null);
            return;
        }

        EnqueueReadyLocked(node);
    }

    private void CompleteNodeLocked(
        JobNode node,
        JobCompletionStatus status,
        ExceptionDispatchInfo? failure)
    {
        if (node.Completion.IsCompleted)
        {
            return;
        }

        node.Completion.Complete(status, failure);
        _unfinishedNodes.Remove(node);
        _unfinishedJobs--;

        switch (status)
        {
            case JobCompletionStatus.Succeeded:
                _completedJobs++;
                break;
            case JobCompletionStatus.Faulted:
                _faultedJobs++;
                break;
            case JobCompletionStatus.Canceled:
                _canceledJobs++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        List<JobNode>? dependents = node.Completion.Dependents;

        if (dependents is not null)
        {
            foreach (JobNode dependent in dependents)
            {
                if (dependent.Completion.IsCompleted)
                {
                    continue;
                }

                MergeDependencyOutcome(dependent, node.Completion);
                dependent.RemainingDependencies--;

                if (dependent.RemainingDependencies == 0)
                {
                    ResolveReadyOrFailedDependencyLocked(dependent);
                }
            }

            dependents.Clear();
        }

        Monitor.PulseAll(_gate);
    }

    private static void MergeDependencyOutcome(
        JobNode dependent,
        JobCompletion dependency)
    {
        if (dependency.Status == JobCompletionStatus.Faulted &&
            dependent.DependencyFailure is null)
        {
            dependent.DependencyFailure = dependency.Failure;
        }
        else if (dependency.Status == JobCompletionStatus.Canceled)
        {
            dependent.DependencyCanceled = true;
        }
    }

    private void PublishTiming(JobTimingSample sample)
    {
        if (_timingObserver is null)
        {
            return;
        }

        try
        {
            _timingObserver(sample);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _instrumentationFailures);
        }
    }

    private void UpdatePeakRunningLocked(int running)
    {
        if (running > _peakRunningJobs)
        {
            _peakRunningJobs = running;
        }
    }

    private void ThrowIfNotAcceptingLocked()
    {
        if (!_accepting)
        {
            throw new InvalidOperationException(
                "The scheduler is shutting down and no longer accepts jobs.");
        }
    }

    private static TimeSpan StopwatchTicksToTimeSpan(long ticks)
    {
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }
}

internal sealed class JobCompletion
{
    private int _completed;

    public JobCompletion(JobScheduler owner, long sequence)
    {
        Owner = owner;
        Sequence = sequence;
    }

    public JobScheduler Owner { get; }

    public long Sequence { get; }

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public JobCompletionStatus Status { get; private set; }

    public ExceptionDispatchInfo? Failure { get; private set; }

    public List<JobNode>? Dependents { get; set; }

    public void Complete(
        JobCompletionStatus status,
        ExceptionDispatchInfo? failure)
    {
        Status = status;
        Failure = failure;
        Volatile.Write(ref _completed, 1);
    }
}

internal sealed class JobNode
{
    public JobNode(
        JobCompletion completion,
        JobAction? action,
        JobRangeAction? rangeAction,
        int rangeStart,
        int rangeEnd)
    {
        Completion = completion;
        Action = action;
        RangeAction = rangeAction;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        SubmittedTimestamp = Stopwatch.GetTimestamp();
    }

    public JobCompletion Completion { get; }

    public JobAction? Action { get; }

    public JobRangeAction? RangeAction { get; }

    public int RangeStart { get; }

    public int RangeEnd { get; }

    public long SubmittedTimestamp { get; }

    public int RemainingDependencies { get; set; }

    public ExceptionDispatchInfo? DependencyFailure { get; set; }

    public bool DependencyCanceled { get; set; }

    public bool IsRunning { get; set; }

    public void Execute(CancellationToken cancellationToken)
    {
        if (Action is not null)
        {
            Action(cancellationToken);
            return;
        }

        RangeAction!(RangeStart, RangeEnd, cancellationToken);
    }
}
