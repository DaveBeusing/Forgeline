using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Headless;

internal sealed record VerticalSliceHeadlessReport(
    string Runtime,
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount,
    string Profile,
    ulong RequestedTicksPerMatch,
    int RequestedMatches,
    double ElapsedMilliseconds,
    IReadOnlyList<VerticalSliceMatchReport> Matches)
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            WriteIndented = true
        };

    public static VerticalSliceHeadlessReport Create(
        HeadlessOptions options,
        TimeSpan elapsed,
        IReadOnlyList<VerticalSliceMatchReport> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        return new VerticalSliceHeadlessReport(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            options.Profile.ToString(),
            options.TickCount,
            options.MatchCount,
            elapsed.TotalMilliseconds,
            matches);
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath =
            Path.GetFullPath(path);
        string? directory =
            Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json =
            JsonSerializer.Serialize(
                this,
                s_jsonOptions);
        File.WriteAllText(
            fullPath,
            json);
    }
}

internal sealed record VerticalSliceMatchReport(
    int MatchIndex,
    ulong Seed,
    ulong ExecutedTicks,
    double LogicalSeconds,
    double ElapsedMilliseconds,
    double ThroughputTicksPerSecond,
    string MatchStatus,
    ulong Winner,
    ulong CompletedAtTick,
    int EntityCount,
    int PendingCommands,
    double AverageTickMilliseconds,
    double MaximumTickMilliseconds,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long CompletedCargoOrders,
    long FailedCargoOrders,
    long RouteFailures,
    double DeliveredCargoQuantity,
    int PendingDistributionRequests,
    long CompletedDistributionRequests,
    long FailedDistributionRequests,
    double TotalFuelTransferred,
    double TotalAmmunitionTransferred,
    ulong TotalArtilleryShots,
    ulong TotalArtilleryImpacts,
    double TotalArtilleryAmmunitionConsumed,
    VerticalSliceSideReport West,
    VerticalSliceSideReport East)
{
    public static VerticalSliceMatchReport Capture(
        int matchIndex,
        ulong seed,
        ulong executedTicks,
        TimeSpan elapsed,
        VerticalSliceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        MatchState match =
            scenario.GetMatchState();
        SimulationDiagnosticsSnapshot diagnostics =
            scenario.Simulation.Diagnostics.Capture(
                scenario.Simulation);
        double elapsedSeconds =
            elapsed.TotalSeconds;

        return new VerticalSliceMatchReport(
            matchIndex,
            seed,
            executedTicks,
            executedTicks /
                (double)scenario.Simulation.Clock.TicksPerSecond,
            elapsed.TotalMilliseconds,
            elapsedSeconds > 0.0
                ? executedTicks / elapsedSeconds
                : 0.0,
            match.Status.ToString(),
            match.Winner.Value,
            match.CompletedAtTick.Value,
            scenario.Simulation.Entities.EntityCount,
            scenario.Simulation.PendingCommandCount,
            diagnostics.AverageTickDuration.TotalMilliseconds,
            diagnostics.MaxTickDuration.TotalMilliseconds,
            diagnostics.ObservedAllocatedBytes,
            diagnostics.ObservedGen0Collections,
            diagnostics.ObservedGen1Collections,
            diagnostics.ObservedGen2Collections,
            scenario.CargoTransport.Metrics.CompletedOrderCount,
            scenario.CargoTransport.Metrics.FailedTransportCount,
            scenario.CargoTransport.Metrics.RouteFailureCount,
            scenario.CargoTransport.Metrics.DeliveredQuantity,
            scenario.AutomatedDistribution.Metrics.PendingRequestCount,
            scenario.AutomatedDistribution.Metrics.CompletedRequestCount,
            scenario.AutomatedDistribution.Metrics.FailedRequestCount,
            scenario.BattlefieldSupply.Metrics.TotalFuelTransferred,
            scenario.BattlefieldSupply.Metrics.TotalAmmunitionTransferred,
            scenario.Artillery.Metrics.TotalShotsFired,
            scenario.Artillery.Metrics.TotalImpacts,
            scenario.Artillery.Metrics.TotalAmmunitionConsumed,
            CaptureSide(
                scenario,
                scenario.West),
            CaptureSide(
                scenario,
                scenario.East));
    }

    private static VerticalSliceSideReport CaptureSide(
        VerticalSliceScenario scenario,
        SkirmishStartingBase side)
    {
        SkirmishOpponentState opponent =
            scenario.GetOpponentState(
                side.Player);
        SkirmishOpponentDebugReadModel debug =
            scenario.Opponents.DebugSnapshot.Single(
                entry =>
                    entry.Player ==
                    side.Player);

        return new VerticalSliceSideReport(
            side.Player.Value,
            opponent.StrategicState.ToString(),
            opponent.ActiveGoal.ToString(),
            opponent.DecisionsTaken,
            debug.Economy.PowerGeneration,
            debug.Economy.PowerDemand,
            debug.Economy.OfflineConsumers,
            debug.Economy.ProductionFacilities,
            debug.Economy.UnitProductionFacilities,
            debug.Force.KnownHostileContacts,
            debug.Force.CurrentHostileContacts,
            debug.Force.AverageReadiness,
            debug.Force.MinimumSupply,
            scenario.CountBuildings(
                side.Player,
                BuildingIds.PowerPlant),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.Extractor),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.Smelter),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.Refinery),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.ElectronicsPlant),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.LogisticsHub),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.Barracks),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.VehicleFactory),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.AmmunitionPlant),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.SupplyDepot),
            scenario.CountBuildings(
                side.Player,
                BuildingIds.Radar),
            scenario.CountUnits(
                side.Player,
                UnitIds.ScoutVehicle),
            scenario.CountUnits(
                side.Player,
                UnitIds.MainBattleTank),
            scenario.CountUnits(
                side.Player,
                UnitIds.MobileArtillery),
            scenario.CountUnits(
                side.Player,
                UnitIds.CargoTruck),
            scenario.CountUnits(
                side.Player,
                UnitIds.SupplyTruck));
    }
}

internal sealed record VerticalSliceSideReport(
    ulong Player,
    string StrategicState,
    string ActiveGoal,
    int DecisionsTaken,
    double PowerGeneration,
    double PowerDemand,
    int OfflineConsumers,
    int ProductionFacilities,
    int UnitProductionFacilities,
    int KnownHostileContacts,
    int CurrentHostileContacts,
    double AverageReadiness,
    double MinimumSupply,
    int PowerPlants,
    int Extractors,
    int Smelters,
    int Refineries,
    int ElectronicsPlants,
    int LogisticsHubs,
    int Barracks,
    int VehicleFactories,
    int AmmunitionPlants,
    int SupplyDepots,
    int Radars,
    int Scouts,
    int MainBattleTanks,
    int MobileArtillery,
    int CargoTrucks,
    int SupplyTrucks);
