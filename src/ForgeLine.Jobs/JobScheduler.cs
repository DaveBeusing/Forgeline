using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace ForgeLine.Jobs;

public sealed class JobScheduler : IDisposable
{
    [ThreadStatic]
    private static JobScheduler? s_currentScheduler;

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
        ArgumentNullException.ThrowIfNull(action);

        lock (_gate)
        {
            ThrowIfNotAcceptingLocked();

            var completion = new JobCompletion(this, _nextSequence++);
            var node = JobNode.CreateAction(completion, action);

            _unfinishedNodes.Add(node);
            _submittedJobs++;
            _unfinishedJobs++;
            EnqueueReadyLocked(node);

            return new JobHandle(completion);
        }
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
                    node.Action!(_shutdownCancellation.Token);
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

        Monitor.PulseAll(_gate);
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
    private JobNode(JobCompletion completion, JobAction action)
    {
        Completion = completion;
        Action = action;
        SubmittedTimestamp = Stopwatch.GetTimestamp();
    }

    public JobCompletion Completion { get; }

    public JobAction? Action { get; }

    public long SubmittedTimestamp { get; }

    public bool IsRunning { get; set; }

    public static JobNode CreateAction(JobCompletion completion, JobAction action)
    {
        return new JobNode(completion, action);
    }
}
