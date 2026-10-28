using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public enum PracticeGoal : byte { Move, Contact, Damage, Power, Smelter, Steel, Conveyor, ProcessingAfterConveyor, LoadSupply, FieldSupply }
public sealed record PracticeObjective(PracticeGoal Goal, string Title, string Hint, string Explanation);
public sealed record PracticeLesson(string Key, string Title, IReadOnlyList<PracticeObjective> Objectives);

public static class PracticeLessons
{
    public const string ReconKey = "practice.command-recon.v1";
    public const string IndustryKey = "practice.build-production.v1";
    public const string SupplyKey = "practice.supply-recovery.v1";
    public static IReadOnlyList<PracticeLesson> All { get; } = Array.AsReadOnly(new[] {
        new PracticeLesson(ReconKey, "Practice: Command & Recon", Array.AsReadOnly(new[] {
            new PracticeObjective(PracticeGoal.Move,"MOVE YOUR SCOUT","Select the Scout and move east along the road.","Move at least 20 metres from its authored start. Only actual completed-tick movement counts, not a click or an accepted request."),
            new PracticeObjective(PracticeGoal.Contact,"RECON THE TRAINING AREA","Scout towards the public training area at X 1020, Z 1536.","Establish a current identified hostile contact through your own sensors. The stationary training opponent cannot fire; fog of war and line of sight still apply."),
            new PracticeObjective(PracticeGoal.Damage,"ENGAGE AN IDENTIFIED TARGET","Use Attack or Attack Move after identification.","Apply real damage with an owned unit. Normal weapon compatibility, ammunition, range and intelligence checks remain in force.") })),
        new PracticeLesson(IndustryKey, "Practice: Build / Power / Production", Array.AsReadOnly(new[] {
            new PracticeObjective(PracticeGoal.Power,"BUILD WORKING POWER","Build a Power Plant near your Core.","A completed owned plant must actually generate power. The practice setup buys one extractor using finite starting stock; construction takes normal time."),
            new PracticeObjective(PracticeGoal.Smelter,"BUILD A SMELTER","Place a Smelter near the practice extractor.","Use the normal Build ring and terrain-valid placement. Construction costs and completion are unchanged."),
            new PracticeObjective(PracticeGoal.Steel,"PRODUCE REAL STEEL","Inspect the Smelter input. Use Logistics / Inputs to arrange Ore delivery.","Ore must really enter its input inventory via Cargo or a compatible conveyor. Powered automatic processing must complete a normal Steel cycle. Starting stock does not count as produced Steel.") })),
        new PracticeLesson(SupplyKey, "Practice: Supply Disruption & Recovery", Array.AsReadOnly(new[] {
            new PracticeObjective(PracticeGoal.Conveyor,"CONNECT LOCAL ORE FLOW","After setup construction, connect the Extractor output to the Smelter input with a conveyor.","The setup buys two Power Plants, Extractor and Smelter with finite stock. The empty Smelter input is a real interruption. A real Ferrous Ore conveyor unload into that Smelter confirms local flow."),
            new PracticeObjective(PracticeGoal.ProcessingAfterConveyor,"RECOVER PROCESSING","Wait for a powered Steel cycle after the conveyor delivery.","Only a later completed cycle in the same receiving Smelter counts. A connection, incoming reservation or unrelated factory output cannot confirm recovery."),
            new PracticeObjective(PracticeGoal.LoadSupply,"LOAD A SUPPLY TRUCK","Select the empty Supply Truck near the Core and request supply.","The normal loading action must physically load Fuel and Ammunition from a real owned provider. Wait for the truck to leave the loading bay before driving to the squad. Truck cargo, loading and propulsion remain separate."),
            new PracticeObjective(PracticeGoal.FieldSupply,"RESTORE FIELD AMMUNITION","Move the loaded Supply Truck near the unsupplied Rifle Squad east of the Core; request resupply for the squad.","The authored empty squad has automatic recovery disabled and waits for your supply plan. A real battlefield ammunition transfer from a Supply Truck confirms recovery. Conveyors feed fixed inventories; a physical Supply Truck bridges the battlefield leg. Neither route grants free resources.") })) });

    public static bool TryGet(string key, out PracticeLesson? lesson)
    { lesson = All.FirstOrDefault(x => x.Key == key); return lesson is not null; }

    public static MatchRuntimeSettings Configure(MatchRuntimeSettings settings)
    {
        if (!TryGet(settings.Composition.Key, out _)) return settings;
        return settings with { Participants = CentralDivideScenario.CreateDefaultParticipants(false,false),
            Scenario = settings.Scenario with { Profile=MatchScenarioProfile.Gameplay,
                StartingStock = SkirmishStartingStock.Standard with { Steel=1800 } } };
    }

    internal static ScenarioDescriptor Descriptor(PracticeLesson lesson) => ScenarioRegistry.CentralDivideDescriptor with
    { Key=lesson.Key, DisplayName=lesson.Title, Description="OPTIONAL PRACTICE - NORMAL ORDERS / RETRY FROM PAUSE",
      Identity=ScenarioRegistry.CentralDivideDescriptor.Identity with { StartRule="practice-start.v1:"+lesson.Key } };

    internal static MatchComposition Compose(MatchComposition basis, PracticeLesson lesson) => basis with
    {
        Key=lesson.Key, Identity=Descriptor(lesson).Identity,
        CreateStartingBase=(entities,inventories,factory,terrain,start,content,stock) =>
        {
            var result=basis.CreateStartingBase(entities,inventories,factory,terrain,start,content,stock);
            var extra=new List<EntityId>();
            if(start.Player.Value==1)
            {
                if(lesson.Key==ReconKey) extra.Add(factory.Create(content.Units[UnitIds.ScoutVehicle],start.Position+new Vector3(30,0,-40),start.Player));
                if(lesson.Key==SupplyKey)
                {
                    extra.Add(factory.Create(content.Units[UnitIds.SupplyTruck],start.Position+new Vector3(30,0,65),start.Player));
                    var squad=factory.Create(content.Units[UnitIds.RifleSquad],start.Position+new Vector3(200,0,65),start.Player);
                    var ammo=entities.GetComponent<AmmunitionState>(squad);
                    var quantity=inventories.GetQuantity(ammo.InventoryId,ResourceIds.Ammunition);
                    if(quantity>0 && !inventories.Remove(ammo.InventoryId,ResourceIds.Ammunition,quantity).Succeeded)
                        throw new InvalidOperationException("Cannot author empty practice ammunition.");
                    if(entities.HasComponent<AutomaticResupplyPolicy>(squad))
                        entities.SetComponent(squad,new AutomaticResupplyPolicy(enabled:false));
                    else entities.AddComponent(squad,new AutomaticResupplyPolicy(enabled:false));
                    extra.Add(squad);
                }
            }
            else if(lesson.Key==ReconKey)
            {
                var target=factory.Create(content.Units[UnitIds.RifleSquad],new Vector3(1020,0,1536),start.Player);
                entities.SetComponent(target,new FirePolicyState(FirePolicy.HoldFire));
                entities.SetComponent(target,new AutoTargetState(false));extra.Add(target);
            }
            return result with { StartingUnits=Array.AsReadOnly(result.StartingUnits.Concat(extra).ToArray()) };
        }
    };
}
