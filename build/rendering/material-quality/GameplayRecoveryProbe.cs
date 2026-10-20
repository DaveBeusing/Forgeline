using System.Numerics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

if (args.Length != 3 || File.Exists(args[0]))
    throw new ArgumentException("Supply a fresh JSONL path, tick limit and stationary-west flag.");
int limit = int.Parse(args[1], CultureInfo.InvariantCulture);
using var scenario = CentralDivideScenario.Create(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Validation), seed: 2026);
var entities = scenario.Simulation.Entities;
scenario.Opponents.DebugCaptureEnabled = true;
if (bool.Parse(args[2]))
    foreach (var controller in entities.Query<SkirmishOpponentController>())
        if (entities.GetComponent<SkirmishOpponentController>(controller).Player == new PlayerId(1))
        { entities.RemoveComponent<SkirmishOpponentController>(controller); break; }
using var output = new StreamWriter(args[0]);
output.WriteLine(JsonSerializer.Serialize(new { source = typeof(CentralDivideScenario).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, limit, stationaryWest = args[2] }));
for (int tick = 1; tick <= limit; tick++)
{
    scenario.Simulation.AdvanceOneTick();
    if (tick % 1000 != 0) continue;
    var vehicles = new List<object>();
    foreach (var truck in entities.Query<CargoTransport, WorldTransform>())
    {
        var transform = entities.GetComponent<WorldTransform>(truck);
        var fuel = entities.GetComponent<UnitFuelState>(truck);
        var cargo = entities.GetComponent<CargoTransport>(truck);
        vehicles.Add(new { entity = truck.ToString(), owner = cargo.Owner.Value, position = transform.Position,
            fuel = scenario.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel),
            movement = entities.GetComponent<GroundMovementState>(truck).ToString(),
            order = entities.TryGetComponent(truck, out MovementOrder m) ? m.ToString() : null,
            dock = entities.TryGetComponent(truck, out VehicleDockVisit v) ? v.ToString() : null,
            queue = entities.TryGetComponent(truck, out VehicleQueueEntry q) ? q.ToString() : null,
            load = entities.TryGetComponent(truck, out SupplyLoadingOrder l) ? l.ToString() : null,
            service = entities.TryGetComponent(truck, out CargoFuelServiceState s) ? s.ToString() : null,
            resupply = entities.TryGetComponent(truck, out ResupplyOrder r) ? r.ToString() : null,
            rescue = entities.TryGetComponent(truck, out SupplyRescueAssignment a) ? a.ToString() : null });
    }
    var facilities = new List<object>();
    foreach (var entity in entities.Query<UnitProductionFacility>())
    {
        var inventory = entities.GetComponent<UnitProductionFacility>(entity).InputInventory;
        facilities.Add(new { entity = entity.ToString(), state = entities.GetComponent<UnitProductionFacility>(entity).ToString(),
            steel = scenario.Inventories.GetQuantity(inventory, ResourceIds.Steel),
            electronics = scenario.Inventories.GetQuantity(inventory, ResourceIds.Electronics),
            fuel = scenario.Inventories.GetQuantity(inventory, ResourceIds.Fuel),
            ammunition = scenario.Inventories.GetQuantity(inventory, ResourceIds.Ammunition) });
    }
    var buildings = new List<object>();
    foreach (var entity in entities.Query<CompletedBuilding>())
    {
        var building = entities.GetComponent<CompletedBuilding>(entity);
        buildings.Add(new { entity = entity.ToString(), owner = building.Owner.Value, id = building.BuildingId.Value });
    }
    output.WriteLine(JsonSerializer.Serialize(new { tick, vehicles, facilities, buildings,
        opponents = scenario.Opponents.DebugSnapshot.Select(d => new { player = d.Player.Value, goal = d.ActiveGoal.ToString(), economy = d.Economy.ToString(), force = d.Force.ToString(), admission = d.OffensiveAdmission.ToString() }) }, new JsonSerializerOptions { IncludeFields = true }));
    output.Flush();
}
