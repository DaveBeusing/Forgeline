using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;

public sealed class NativeMixedCombatCargoGridlockTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecordedOncomingCombatAndCargoBodiesCanClearThroughOrdinaryMovement(bool hasFuel)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities;
        var owner = new PlayerId(1);
        var catalog = DirectorateContent.CreateUnitCatalog();
        var samples = new[]
        {
            (UnitIds.CargoTruck, new Vector3(713.8465f,3.8246577f,1561.5065f),2.172616f,new Vector3(1020,-3.3239303f,1536),.5434845805005151),
            (UnitIds.CargoTruck, new Vector3(710.7501f,3.9181888f,1566.6038f),-2.5725582f,new Vector3(1020,-3.3239303f,1536),.8381750693871762),
            (UnitIds.MobileArtillery, new Vector3(717.2251f,4.1145873f,1566.0952f),-.9329991f,new Vector3(537.16766f,10.978979f,1542.6898f),.2859103663146057),
            (UnitIds.MainBattleTank, new Vector3(719.8574f,4.094695f,1557.4961f),-.8768718f,new Vector3(537.4919f,10.978979f,1537.0464f),.34722134859369813)
        };
        var units = new EntityId[samples.Length];
        var initialFuel = new double[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            var (id, position, heading, goal, fraction) = samples[i];
            var unit = scene.UnitFactory.Create(catalog[id], position, owner); units[i] = unit;
            var pose = entities.GetComponent<WorldTransform>(unit);
            entities.SetComponent(unit, pose with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, heading) });
            entities.SetComponent(unit, GroundMovementState.Stationary(heading));
            var automatic = new AutomaticResupplyPolicy(.3, .35, enabled: false);
            if (entities.HasComponent<AutomaticResupplyPolicy>(unit)) entities.SetComponent(unit, automatic); else entities.AddComponent(unit, automatic);
            var fuel = entities.GetComponent<UnitFuelState>(unit);
            initialFuel[i] = hasFuel ? fuel.Capacity * fraction : 0;
            Assert.True(scene.Inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) - initialFuel[i]).Succeeded);
            entities.AddComponent(unit, new MovementOrder(owner, goal, SimulationTick.Zero, new(1)));
        }
        double FuelTotal() => scene.Inventories.CaptureSnapshot().Inventories
            .SelectMany(inventory => inventory.Resources).Where(resource => resource.ResourceId == ResourceIds.Fuel).Sum(resource => resource.Quantity);
        double initialTotal = FuelTotal();
        // Forty seconds permits the existing stopped wait and serial checked
        // reversals. Original full-match and production bounds are unchanged.
        scene.Simulation.RunTicks(800, TestContext.Current.CancellationToken);
        for (int i = 0; i < units.Length; i++)
        {
            var position = entities.GetComponent<WorldTransform>(units[i]).Position;
            if (!hasFuel)
            {
                Assert.Equal(new Vector2(samples[i].Item2.X, samples[i].Item2.Z), new Vector2(position.X, position.Z));
                Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitY, samples[i].Item3), entities.GetComponent<WorldTransform>(units[i]).Rotation);
            }
            else Assert.True(Vector3.Distance(position, samples[i].Item2) > 10, $"unit={i}; position={position}; state={entities.GetComponent<GroundMovementState>(units[i])}");
            if (i < 2) Assert.Equal(samples[i].Item4, Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors, actor => actor.Entity == units[i]).OriginalOrder.WorldTarget);
            var fuel = entities.GetComponent<UnitFuelState>(units[i]);
            Assert.InRange(scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), 0, fuel.Capacity);
            if (!hasFuel) Assert.Equal(0, scene.Inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel));
        }
        Assert.InRange(FuelTotal(), 0, initialTotal);
        if (hasFuel) Assert.True(FuelTotal() < initialTotal);
        else Assert.Equal(initialTotal, FuelTotal());
    }
}
