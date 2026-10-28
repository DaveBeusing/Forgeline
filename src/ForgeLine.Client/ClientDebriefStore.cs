using ForgeLine.Game;

namespace ForgeLine.Client;

internal sealed class ClientDebriefStore(string path,ulong player)
{
    internal bool TrySave(MatchDebriefSummary? report)
    {
        if(report is null || report.Player!=player)return false;
        try {MatchDebriefSerializer.Write(path,report);return true;}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or NotSupportedException)
        {return false;}
    }
    internal bool TryLoad(out MatchDebriefSummary? report,out MatchDebriefReadFailure failure)=>
        MatchDebriefSerializer.TryRead(path,player,out report,out failure);
    internal static string Explain(MatchDebriefReadFailure failure)=>failure switch
    {
        MatchDebriefReadFailure.Missing=>"NO SAVED REPORT / CURRENT RESULT RETAINED",
        MatchDebriefReadFailure.ForeignPlayer=>"SAVED REPORT HAS A DIFFERENT PLAYER SCOPE",
        MatchDebriefReadFailure.UnsupportedVersion=>"SAVED REPORT VERSION IS UNSUPPORTED",
        MatchDebriefReadFailure.TooLarge=>"SAVED REPORT EXCEEDS THE SIZE LIMIT",
        MatchDebriefReadFailure.StorageUnavailable=>"SAVED REPORT STORAGE UNAVAILABLE",
        _=>"SAVED REPORT INVALID / CURRENT RESULT RETAINED"
    };
}
