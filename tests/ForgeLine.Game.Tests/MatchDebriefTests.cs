using System.Collections;
using System.Text;
using System.Text.Json.Nodes;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchDebriefTests
{
    [Theory]
    [InlineData(false,1)] [InlineData(false,2)] [InlineData(true,1)] [InlineData(true,2)]
    public void RealCoreLossAndSurrenderUseTheActualScopedOutcomeWithoutMutation(bool surrender,uint loser)
    {
        using var runtime=CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,7511) with
            {Participants=CentralDivideScenario.CreateDefaultParticipants(false,false)},TestContext.Current.CancellationToken);
        if(surrender)
        {
            var gateway=new PlayerCommandGateway(runtime.Simulation,runtime.Services.BuildingCommands,runtime.BattlefieldRuntime.MatchStateEntity);
            runtime.Simulation.RegisterTickObserver(gateway);
            Assert.True(PlayerActionRequestDispatcher.TryDispatch(PlayerActionRequest.Surrender(),new(loser),gateway,runtime.Simulation.CurrentTick,out var receipt));
            Assert.True(receipt.Accepted);
        }
        else Assert.True(runtime.Simulation.Entities.DestroyEntity(runtime.GetBase(new(loser)).CommandCore));
        runtime.Simulation.AdvanceOneTick();
        string before=MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        var report=MatchDebriefCapture.Capture(runtime,new(1));
        Assert.Equal(loser==1?MatchDebriefOutcome.Defeat:MatchDebriefOutcome.Victory,report.Outcome);
        Assert.Equal(surrender?MatchDebriefReason.Surrender:MatchDebriefReason.CommandCoreDestroyed,report.Reason);
        Assert.Equal(runtime.GetMatchState().CompletedAtTick.Value,report.ResultAtTick);
        Assert.Equal(before,MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
        Assert.Single(report.Events,e=>e.Kind==MatchDebriefEventKind.MatchEnded);
    }

    [Fact]
    public void CancellationAfterCompletedTicksProducesIncompleteOwnedEvidence()
    {
        using var cancellation=new CancellationTokenSource();
        using var runtime=CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,7511),cancellation.Token);
        runtime.Simulation.AdvanceOneTick();cancellation.Cancel();
        string before=MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        var report=MatchDebriefCapture.Capture(runtime,new(1));
        Assert.Equal(MatchDebriefOutcome.Incomplete,report.Outcome);Assert.Null(report.ResultAtTick);
        Assert.DoesNotContain(report.Events,e=>e.Kind==MatchDebriefEventKind.MatchEnded);
        Assert.Equal(before,MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
        Assert.Equal(report.ObservedThroughTick,runtime.Simulation.CurrentTick.Value);
    }

    [Fact]
    public void FirstLatestProjectionRejectsForeignFutureAndWrongSessionEvidenceAndExportsNoGlobalIds()
    {
        var own=Event(123456789,7);var foreign=own with {Identity=500000,Player=new(2)};
        var report=Project([own,foreign,own with {Identity=9,SessionId=new(2)},own with {Identity=10,Tick=new(11)}],[own]);
        var row=Assert.Single(report.Events,e=>e.Kind!=MatchDebriefEventKind.MatchEnded);
        Assert.Equal(MatchDebriefEvidence.First|MatchDebriefEvidence.Latest,row.Evidence);
        Assert.Equal(1,row.SubjectId);Assert.Equal(7UL,row.Tick);Assert.Equal(10,row.Quantity);
        string json=Encoding.UTF8.GetString(MatchDebriefSerializer.Serialize(report));
        foreach(string secret in new[]{"123456789","999123","500000","Actor","Target","OrderIdentity","Position","Debug","Winner"})Assert.DoesNotContain(secret,json);
        var restored=MatchDebriefSerializer.Deserialize(Encoding.UTF8.GetBytes(json),1);
        Assert.Equal(json,Encoding.UTF8.GetString(MatchDebriefSerializer.Serialize(restored)));
    }

    [Fact]
    public void ResultAndUnavailableCauseDoNotRequireProgressAndInterruptedReportsNeverInventVictory()
    {
        var empty=Project([],[]);
        Assert.Single(empty.Events);Assert.Equal(MatchDebriefReason.Unknown,empty.Reason);
        var state=MatchState.Active with {Outcome=MatchOutcome.Victory,Winner=new(1)};
        var interrupted=MatchDebriefCapture.FromPublished(new(77),new(1),"fixture",20,new(10),state,[Event(1,2)],[],_=>"Fuel");
        Assert.Equal(MatchDebriefOutcome.Incomplete,interrupted.Outcome);Assert.Null(interrupted.ResultAtTick);
        Assert.Equal(MatchDebriefReason.Unknown,interrupted.Reason);
        Assert.DoesNotContain(interrupted.Events,e=>e.Kind==MatchDebriefEventKind.MatchEnded);
        var draw=MatchDebriefCapture.FromPublished(new(77),new(1),"fixture",20,new(10),new(MatchStatus.Draw,default,new(10)),[],[],_=>"unused");
        Assert.Equal(MatchDebriefOutcome.Draw,draw.Outcome);
    }

    [Fact]
    public void SourceVisitsAndRetainedOutputAreBoundedAndCopiesStayIndependent()
    {
        var source=new LargeSource();var report=Project(source,[]);
        Assert.Equal(128,source.Reads);Assert.Equal(MatchDebriefSummary.MaximumEvents,report.Events.Count);Assert.True(report.Truncated);
        var rows=report.Events.ToArray();var copy=new MatchDebriefSummary(1,77,1,"fixture",20,0,1000,10,
            report.Outcome,report.Reason,true,rows);
        rows[0]=rows[0] with {Subject="changed"};Assert.NotEqual("changed",copy.Events[0].Subject);
        Assert.Throws<NotSupportedException>(()=>((IList<MatchDebriefEvent>)copy.Events)[0]=rows[0]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void VersionedReadRejectsCorruptForeignOversizedAndUnknownFieldsWithoutThrowing(int invalid)
    {
        string path=Path.Combine(Path.GetTempPath(),"forgeline-debrief-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var node=JsonNode.Parse(MatchDebriefSerializer.Serialize(Project([],[])))!.AsObject();
            switch(invalid)
            {
                case 0:node["SchemaVersion"]=2;break;
                case 1:node["Player"]=2;break;
                case 2:node["EnemyInventory"]=9000;break;
                case 3:node["Events"]!.AsArray()[0]!["Kind"]=255;break;
                case 4:File.WriteAllBytes(path,new byte[MatchDebriefSerializer.MaximumBytes+1]);break;
                case 5:File.WriteAllText(path,"{broken");break;
                case 6:node["SchemaVersion"]="wrong type";break;
                default:node["Player"]="wrong type";break;
            }
            if(invalid<4 || invalid>=6)File.WriteAllText(path,node.ToJsonString());
            Assert.False(MatchDebriefSerializer.TryRead(path,1,out var report,out var failure));Assert.Null(report);
            Assert.Equal(invalid switch {0 or 6=>MatchDebriefReadFailure.UnsupportedVersion,1 or 7=>MatchDebriefReadFailure.ForeignPlayer,4=>MatchDebriefReadFailure.TooLarge,_=>MatchDebriefReadFailure.InvalidData},failure);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Fact]
    public void AtomicFileRoundTripAndMissingEvidenceRetainAUsablePublicResult()
    {
        string path=Path.Combine(Path.GetTempPath(),"forgeline-debrief-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            Assert.False(MatchDebriefSerializer.TryRead(path,1,out _,out var missing));Assert.Equal(MatchDebriefReadFailure.Missing,missing);
            var report=Project([],[]);MatchDebriefSerializer.Write(path,report);
            Assert.True(MatchDebriefSerializer.TryRead(path,1,out var loaded,out var failure));Assert.Equal(MatchDebriefReadFailure.None,failure);
            Assert.Equal(report.Outcome,loaded!.Outcome);Assert.Single(loaded.Events);
            Assert.InRange(new FileInfo(path).Length,1,MatchDebriefSerializer.MaximumBytes);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    private static MatchDebriefSummary Project(IReadOnlyList<GameplayProgressEvent> first,IReadOnlyList<GameplayProgressEvent> latest)=>
        MatchDebriefCapture.FromPublished(new(77),new(1),"fixture",20,new(1000),new(MatchStatus.Victory,new(1),new(10)),first,latest,
            item=>item.Player==new PlayerId(1)?"Fuel":throw new InvalidOperationException("Foreign data was inspected."));
    private static GameplayProgressEvent Event(ulong identity,ulong tick)=>
        new(identity,new(77),new(tick),new(1),GameplayProgressKind.CargoUnloaded,new(999123,99),new(999123,99),new(999123,99),400000,new(5),10,0);
    private sealed class LargeSource:IReadOnlyList<GameplayProgressEvent>
    {
        public int Count=>int.MaxValue;public int Reads{get;private set;}
        public GameplayProgressEvent this[int index]{get{Reads++;Assert.InRange(index,0,127);return Event((ulong)index+1,1);}}
        public IEnumerator<GameplayProgressEvent> GetEnumerator()=>throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
