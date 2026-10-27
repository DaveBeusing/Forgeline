namespace ForgeLine.Navigation;

/// <summary>
/// Optional single-coordinator phases inside the same reserved search grant.
/// Prepare runs before dispatch; Complete runs after every worker has joined.
/// Workers call only Advance. All three phases consume the one request ledger.
/// </summary>
public interface INavigationCoordinatorSearchSession
{
    NavigationSearchAdvance Prepare(int workAllowance);
    NavigationSearchAdvance Complete(NavigationSearchAdvance workerResult, int workAllowance);
}
