using ForgeLine.Benchmarks;

namespace ForgeLine.Navigation.Benchmarks;

internal static class NavigationScalabilityMeasurements
{
    public static void Run(string output, int samples)
    {
        var routes = new CentralDividePathfindingBenchmarks();
        routes.Setup();
        object[] results =
        [
            ScalabilityMeasurement.Measure("central-divide-cross-map", "One tracked route search; static cache-warmed navigation world", routes.CrossMapRoute, samples),
            ScalabilityMeasurement.Measure("central-divide-expansion", "One tracked expansion route search; static cache-warmed navigation world", routes.ExpansionRoute, samples),
            ScalabilityMeasurement.Measure("central-divide-bridge-loss", "One alternate route search after north bridge loss; static cache-warmed navigation world", routes.AlternateRouteAfterBridgeLoss, samples)
        ];
        ScalabilityMeasurement.Write(output, "Headless navigation CPU; no graphics resources", results,
            [typeof(HierarchicalPathfinder).Assembly.Location, typeof(NavigationScalabilityMeasurements).Assembly.Location]);
    }
}
