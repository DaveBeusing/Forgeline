using System.Text;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientDebriefStoreTests
{
    [Fact]
    public void StoreRoundTripIsScopedAndForeignSaveDoesNotReplaceOwnedEvidence()
    {
        string path=Temporary();
        try
        {
            var store=new ClientDebriefStore(path,1);Assert.True(store.TrySave(Report(1)));
            byte[] owned=File.ReadAllBytes(path);Assert.False(store.TrySave(Report(2)));
            Assert.Equal(owned,File.ReadAllBytes(path));Assert.True(store.TryLoad(out var loaded,out var failure));
            Assert.Equal(1UL,loaded!.Player);Assert.Equal(MatchDebriefReadFailure.None,failure);
            Assert.False(new ClientDebriefStore(path,2).TryLoad(out _,out var foreign));Assert.Equal(MatchDebriefReadFailure.ForeignPlayer,foreign);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingCorruptOrUnsupportedFilesDoNotBlockTheTerminalReturnPath(bool future)
    {
        string path=Temporary();
        try
        {
            File.WriteAllText(path,future?Encoding.UTF8.GetString(MatchDebriefSerializer.Serialize(Report(1))).Replace("\"SchemaVersion\":1","\"SchemaVersion\":99",StringComparison.Ordinal):"{broken");
            var store=new ClientDebriefStore(path,1);Assert.False(store.TryLoad(out _,out var failure));
            var snapshot=new PresentationSnapshot(new(10),TimeSpan.FromMilliseconds(50),0,[],sessionId:new(1),
                playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(10),MatchStatus=PlayerMatchStatus.Victory});
            var controller=new MatchDebriefController();var input=new InputState();var layout=GameplayHudLayout.Create(1280,720,96);
            controller.Update(input,snapshot,layout);controller.ShowSaved(null,ClientDebriefStore.Explain(failure));
            input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.Enter));
            Assert.True(controller.Update(input,snapshot,layout).Return);Assert.Null(controller.View.Resolve(snapshot));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    [Fact]
    public void FailedWriteRetainsTheCurrentReportAndLeavesNoPartialTemporaryFile()
    {
        string directory=Path.Combine(Path.GetTempPath(),"forgeline-debrief-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var report=Report(1);var store=new ClientDebriefStore(directory,1);
            Assert.False(store.TrySave(report));Assert.Equal(MatchDebriefOutcome.Victory,report.Outcome);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally{Directory.Delete(directory);}
    }
    private static string Temporary()=>Path.Combine(Path.GetTempPath(),"forgeline-debrief-"+Guid.NewGuid().ToString("N")+".json");
    private static MatchDebriefSummary Report(ulong player)=>new(1,1,player,"fixture",20,0,10,10,MatchDebriefOutcome.Victory,
        MatchDebriefReason.Surrender,false,[new(10,MatchDebriefEventKind.MatchEnded,0,"PUBLIC MATCH RESULT",null,MatchDebriefEvidence.PublicResult)]);
}
