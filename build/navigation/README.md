# Logistics navigation CPU probe

The probe measures warmed route-query allocations, full navigation-grid build
cost and cached graph lookups. It does not measure gameplay frame rate or moving
convoy delivery throughput. See [qualification and open gates](../../docs/LogisticsNavigationResilience.md).

Run from the repository root with the SDK selected by global.json:

```powershell
dotnet test --project tests/ForgeLine.Navigation.Tests/ForgeLine.Navigation.Tests.csproj -c Release
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj -c Release -- --filter-class '*LogisticsNavigationRecoveryTests' '*NavigationWaypointOwnershipTests' '*NavigationBlockadeRecoveryTests'
# Run the complete Game project separately; do not interpret the subset as full CI.
dotnet test --project tests/ForgeLine.Game.Tests/ForgeLine.Game.Tests.csproj -c Release
New-Item -ItemType Directory -Force artifacts/navigation | Out-Null
dotnet build build/navigation/NavigationProbe.csproj -c Release
dotnet build/navigation/bin/Release/net10.0/NavigationProbe.dll artifacts/navigation/current-cpu.json
```

The output path is required and its parent must exist. Compare source-matched
binaries on the same host, alternate execution order, retain every raw report,
and verify route/grid signatures before comparing timing or allocations.
Timing is observational, not a brittle shared-runner pass/fail threshold.
The retained workflow is read-only. It never applies patches or commits files.
