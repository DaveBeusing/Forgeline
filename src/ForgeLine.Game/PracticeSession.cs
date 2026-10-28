using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum PracticeStatus : byte { Active, Complete, Failed }
public sealed record PracticeSnapshot(string LessonKey, int DefinitionVersion, PlayerId Player, SimulationSessionId Session,
    SimulationTick Tick, PracticeStatus Status, IReadOnlyList<ulong?> CompletedAtTicks, string Failure);

/// <summary>Completed-tick evidence only. Retains at most four timestamps, never an event history.</summary>
public sealed class PracticeSession : ISimulationTickObserver, ISimulationSystem
{
    public const int MaximumInspectedEntities = 2048;
    private readonly MatchRuntime _runtime;
    private readonly PracticeLesson _lesson;
    private readonly ulong?[] _completed;
    private readonly PlayerId _player = new(1);
    private readonly EntityId _scout, _target, _squad, _truck;
    private readonly Vector3 _scoutStart;
    private ulong _tick;
    private int _setup;
    private readonly PlayerCommandCorrelationId[] _setupResults=new PlayerCommandCorrelationId[4];
    private Vector3 _extractorPosition;
    private EntityId _receiver;
    private ulong? _conveyorTick,_loadedAt,_fieldAt;
    private GameplayProgressEvent? _pendingConveyor,_pendingProcessing;
    private PracticeStatus _status;
    private string _failure="";

    internal PracticeSession(MatchRuntime runtime, PracticeLesson lesson)
    {
        _runtime=runtime;_lesson=lesson;_completed=new ulong?[lesson.Objectives.Count];
        var own=runtime.GetBase(_player).StartingUnits;
        _scout=own.FirstOrDefault(e=>runtime.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.ScoutVehicle);
        _squad=own.FirstOrDefault(e=>runtime.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.RifleSquad);
        _truck=own.FirstOrDefault(e=>runtime.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.SupplyTruck);
        if(_scout.IsValid)_scoutStart=runtime.Simulation.Entities.GetComponent<WorldTransform>(_scout).Position;
        if(lesson.Key==PracticeLessons.ReconKey)_target=runtime.GetBase(new(2)).StartingUnits[^1];
        runtime.Progress.Recorded+=ObserveAction;
    }
    public SimulationPhase Phase=>SimulationPhase.OrderProcessing;
    public void Execute(SimulationContext context)
    {
        int count=_lesson.Key==PracticeLessons.IndustryKey ? 1 : _lesson.Key==PracticeLessons.SupplyKey ? 4 : 0;
        if(_setup>=count||_status!=PracticeStatus.Active)return;
        var core=_runtime.GetBase(_player).CommandCore;
        var origin=context.Entities.GetComponent<WorldTransform>(core).Position;
        BuildingId building=_lesson.Key==PracticeLessons.IndustryKey||_setup==1 ? BuildingIds.Extractor : _setup is 0 or 3 ? BuildingIds.PowerPlant : BuildingIds.Smelter;
        if(building==BuildingIds.Extractor)
        {
            int inspected=0;double nearest=double.PositiveInfinity;var reference=origin;
            foreach(var entity in context.Entities.Query<ResourceDeposit>())
            {
                if(inspected++==128)break;
                var deposit=context.Entities.GetComponent<ResourceDeposit>(entity);
                if(deposit.ResourceId!=ResourceIds.FerrousOre)continue;
                var point=deposit.Bounds.Center;
                double distance=Vector3.DistanceSquared(point,reference);
                if(distance<nearest){nearest=distance;origin=point;}
            }
        }
        else if(building==BuildingIds.Smelter)origin=_extractorPosition+new Vector3(64,0,0);
        else origin+=new Vector3(0,0,80);
        bool placed=false;
        for(int z=-5;z<=5&&!placed;z++)for(int x=-5;x<=5&&!placed;x++)
        {
            var point=origin+new Vector3(x*16,0,z*16);
            var result=_runtime.Services.BuildingPlacement.Evaluate(context.Entities,_player,building,point,BuildingOrientation.North);
            if(!result.IsValid)continue;
            var correlation=new PlayerCommandCorrelationId(ulong.MaxValue-(ulong)_setup);
            _setupResults[_setup]=correlation;
            new BuildCommand(_player,building,point,BuildingOrientation.North,core,context.Tick,correlation).Execute(context);
            if(building==BuildingIds.Extractor)_extractorPosition=point;
            placed=true;
        }
        if(!placed)Fail("Practice setup has no valid placement. Retry the lesson.");
        _setup++;
    }

    public void OnTickCompleted(SimulationContext context)
    {
        if(context.Tick.Value<=_tick)return;
        bool gap=context.Tick.Value!=_tick+1;_tick=context.Tick.Value;
        if(gap){Fail("Practice observation was interrupted; retry for a complete result.");return;}
        if(_status!=PracticeStatus.Active)return;
        for(int i=0;i<_setupResults.Length;i++)
            if(_setupResults[i].IsSpecified&&_runtime.Services.BuildingCommands.TryTakeResult(_setupResults[i],out var result))
            {
                _setupResults[i]=default;
                if(!result.Accepted){Fail("Practice setup was blocked or could not be funded. Retry from Pause.");return;}
            }
        var entities=context.Entities;
        if(!entities.IsAlive(_runtime.GetBase(_player).CommandCore)||
            _lesson.Key==PracticeLessons.ReconKey&&!entities.IsAlive(_scout)||
            _lesson.Key==PracticeLessons.SupplyKey&&(!entities.IsAlive(_squad)||!entities.IsAlive(_truck)))
        {Fail("An owned practice unit or Core was destroyed. Retry from Pause.");return;}
        bool moved=_scout.IsValid&&entities.TryGetComponent(_scout,out WorldTransform pose)&&Vector3.DistanceSquared(pose.Position,_scoutStart)>=400;
        bool contact=_target.IsValid&&_runtime.Intelligence.TryResolveCurrentlyIdentifiedEntity(new(1),
            ForgeLine.Intelligence.IntelligenceContactKey.FromEntity(_target),out var identified)&&identified==_target;
        bool damage=false;int events=0;
        foreach(var item in _runtime.Services.CombatRuntime.Events)
        {
            if(events++==256){Fail("Practice combat observation budget exceeded; retry the lesson.");return;}
            if(item.Tick==context.Tick&&item.Type==CombatEventType.DamageApplied&&item.Amount>0&&item.Target==_target&&
                entities.TryGetComponent(item.Source,out ControllableEntity source)&&source.Owner==_player)damage=true;
        }
        bool power=false,smelter=false;int visited=0;
        foreach(var entity in entities.Query<ControllableEntity>())
        {
            if(visited++==MaximumInspectedEntities){Fail("Practice observation budget exceeded; retry with a smaller lesson state.");return;}
            if(entities.GetComponent<ControllableEntity>(entity).Owner!=_player||
                !entities.TryGetComponent(entity,out CompletedBuilding building)||building.CompletedAtTick.Value==0)continue;
            if(building.BuildingId==BuildingIds.PowerPlant&&entities.TryGetComponent(entity,out PowerGenerator generator)&&generator.Enabled&&generator.State==PowerGeneratorState.Generating)power=true;
            if(building.BuildingId==BuildingIds.Smelter)smelter=true;
        }
        bool steel=false;
        if(_pendingConveyor is {} delivery&&delivery.Tick.Value<=_tick)
        {_conveyorTick??=delivery.Tick.Value;_receiver=delivery.Target;}
        bool conveyor=_conveyorTick.HasValue;
        bool processed=_conveyorTick is {} first&&_pendingProcessing is {} output&&output.Actor==_receiver&&output.Tick.Value>first&&output.Tick.Value<=_tick;
        bool loaded=_loadedAt is {} loadTick&&loadTick<=_tick;
        bool field=_fieldAt is {} fieldTick&&fieldTick<=_tick;
        foreach(var item in _runtime.Progress.Capture(_player,context.Tick))
        {
            if(item.Player!=_player||item.Quantity<=0||item.Tick.Value>_tick)continue;
            steel|=item.Kind==GameplayProgressKind.SteelCompleted;
        }
        if(_lesson.Key==PracticeLessons.SupplyKey&&!field&&!_completed[^1].HasValue&&
            entities.TryGetComponent(_squad,out AmmunitionState squadAmmo)&&
            _runtime.Inventories.GetQuantity(squadAmmo.InventoryId,ResourceIds.Ammunition)>0)
        {Fail("The squad recovered without a Supply Truck. Retry to practise the mobile supply leg.");return;}
        for(int i=0;i<_completed.Length;i++)
            if(_completed[i] is null&&(_lesson.Objectives[i].Goal switch {
                PracticeGoal.Move=>moved,PracticeGoal.Contact=>contact,PracticeGoal.Damage=>damage,
                PracticeGoal.Power=>power,PracticeGoal.Smelter=>smelter,PracticeGoal.Steel=>steel,
                PracticeGoal.Conveyor=>conveyor,PracticeGoal.ProcessingAfterConveyor=>processed,
                PracticeGoal.LoadSupply=>loaded,PracticeGoal.FieldSupply=>field,_=>false}))_completed[i]=_tick;
        if(_completed.All(t=>t.HasValue))_status=PracticeStatus.Complete;
    }
    private void ObserveAction(GameplayProgressEvent item)
    {
        if(item.Player!=_player||item.Quantity<=0)return;
        if(item.Kind==GameplayProgressKind.ConveyorDelivered&&item.DefinitionId==BuildingIds.Smelter.Value&&item.Resource==ResourceIds.FerrousOre)
            _pendingConveyor??=item;
        if(item.Kind==GameplayProgressKind.ProductionCompleted&&(item.Actor==_receiver||item.Actor==_pendingConveyor?.Target))
            _pendingProcessing=item;
        if(item.Kind==GameplayProgressKind.SupplyLoadCompleted&&item.Target==_truck)_loadedAt??=item.Tick.Value;
        if(item.Kind==GameplayProgressKind.FieldAmmunitionTransferred&&item.Target==_squad)_fieldAt??=item.Tick.Value;
    }
    private void Fail(string reason){_status=PracticeStatus.Failed;_failure=reason;}
    public PracticeSnapshot? Capture(PlayerId player,SimulationTick completedTick)=>player==_player&&completedTick.Value==_tick
        ?new(_lesson.Key,1,_player,_runtime.Simulation.SessionId,completedTick,_status,Array.AsReadOnly((ulong?[])_completed.Clone()),_failure):null;
}
