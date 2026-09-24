namespace ForgeLine.Jobs;

public delegate void JobAction(CancellationToken cancellationToken);

public delegate void JobRangeAction(
    int startInclusive,
    int endExclusive,
    CancellationToken cancellationToken);
