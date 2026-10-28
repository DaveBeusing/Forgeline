using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal sealed partial class OperationsHudSurface
{
    private void RenderDebrief(in GameplayHudRenderContext context,in MatchDebriefView view)
    {
        _bounds=MatchDebriefLayout.Panel(context.Layout);_scale=MatchDebriefLayout.Scale(context.Layout);
        if(_bounds.IsEmpty || _scale<=0)return;
        var panel=_bounds;_panelOpacity=1;_panelText=true;LastPanelTextSize=MathF.Max(14,14*_scale);
        if(!_skin.Panel(panel,"panel.primary",.9f))Quad(panel,GameplayHudVisualStyle.PanelBackground with {W=.9f});
        Quad(panel with {Width=3*_scale},GameplayHudVisualStyle.Focus);
        var experience=context.Snapshot.PlayerExperience!.Value;
        var report=view.Resolve(context.Snapshot);
        Span<char> text=stackalloc char[256];var b=new OperationsTextBuilder(text);
        b.Append(PlayerSystemHudModel.ResolveMatchResultLabel(experience.MatchStatus));b.Append(" / AFTER ACTION");
        DebriefLine(panel,8,b.Written,true);
        b=new(text);b.Append(view.SavedReport is null?"CURRENT FIRST/LATEST":"SAVED FIRST/LATEST");
        if(report is not null)
        {
            b.Append(" / ");b.Append(report.Outcome switch {MatchDebriefOutcome.Victory=>"VICTORY",MatchDebriefOutcome.Defeat=>"DEFEAT",MatchDebriefOutcome.Draw=>"DRAW",_=>"INCOMPLETE"});
            b.Append(" / ");b.Number(((report.ResultAtTick??report.ObservedThroughTick)-report.StartedAtTick)/(double)report.TickRate,"0.0");b.Append("s / ");b.Append(report.Scenario);
        }
        else b.Append(" / EVIDENCE UNAVAILABLE");
        DebriefLine(panel,34,b.Written);
        DebriefLine(panel,62,string.IsNullOrEmpty(view.Feedback)?ReasonText(report?.Reason??MatchDebriefReason.Unknown):view.Feedback);
        DebriefControl(context.Layout,view,2,CategoryText(view.Filter));
        b=new(text);b.Append("PAGE ");b.Number(Math.Max(0,view.Page)+1,"0");b.Append(" / NEXT");
        DebriefControl(context.Layout,view,3,b.Written,report is not null);
        DebriefControl(context.Layout,view,4,view.Coverage?"EVENT DETAILS":"COVERAGE");
        for(int row=0;row<MatchDebriefLayout.Rows;row++)
        {
            int index=MatchDebriefLayout.EventIndex(report,view,row);b=new(text);
            if(index>=0)
            {
                var item=report!.Events[index];b.Number((item.Tick-report.StartedAtTick)/(double)report.TickRate,"0.0");b.Append("s / ");
                b.Append(EventText(item.Kind));b.Append(" / ");b.Append(item.Subject);
                if(item.Quantity is { } quantity){b.Append(" ");b.Number(quantity,"0.##");}
            }
            else if(row==0)b.Append(report is null?"TIMELINE UNAVAILABLE":"NO RECORDED EVENTS FOR THIS FILTER");
            DebriefControl(context.Layout,view,10+row,b.Written,index>=0,index==view.Selected && index>=0);
        }
        if(view.Coverage || report is null || view.Selected<0 || view.Selected>=report.Events.Count)
        {
            DebriefLine(panel,300,report is {Truncated:true}?"LIMITED RETAINED RECORDS / FIRST AND LATEST ONLY":"FIRST AND LATEST RECORDED OBSERVATIONS ONLY",true);
            DebriefLine(panel,324,view.Filter switch
            {
                MatchDebriefCategory.Forces=>"CASUALTY AND BATTLE HISTORY UNAVAILABLE",
                MatchDebriefCategory.Reconnaissance=>"SCOUT COMPLETIONS ONLY / CONTACT TIMES UNAVAILABLE",
                MatchDebriefCategory.Infrastructure=>"CONVEYOR HANDOFFS ONLY / TERRITORY HISTORY UNAVAILABLE",
                _=>"EVENT QUANTITIES ARE OBSERVATIONS, NOT MATCH TOTALS"
            });
            DebriefLine(panel,348,"STRATEGIC CAUSES UNKNOWN / NO CAUSAL LINKS RECORDED");
            DebriefLine(panel,372,"NO RECORDED SAFE MAP COORDINATES / R RESTART / ESC RETURN");
        }
        else
        {
            var item=report.Events[view.Selected];b=new(text);b.Append(item.Subject);
            if(item.SubjectId>0){b.Append(" / OWN SUBJECT ");b.Number(item.SubjectId,"0");}
            b.Append(" / ");b.Append(item.Evidence switch {MatchDebriefEvidence.First=>"FIRST",MatchDebriefEvidence.Latest=>"LATEST",MatchDebriefEvidence.PublicResult=>"PUBLIC RESULT",_=>"FIRST AND LATEST"});
            DebriefLine(panel,300,b.Written,true);
            DebriefLine(panel,324,item.Kind==MatchDebriefEventKind.MatchEnded?ReasonText(report.Reason):EventExplanation(item.Kind));
            DebriefLine(panel,348,item.Kind is MatchDebriefEventKind.WorkBlocked or MatchDebriefEventKind.WorkResumed?
                "THE TRIGGER IS UNKNOWN / NO POWER OR SUPPLY CAUSE INFERRED":"EVENT ORDER DOES NOT ESTABLISH STRATEGIC CAUSATION");
            DebriefLine(panel,372,"REPORT-LOCAL SUBJECTS / NO ENTITY IDS OR MAP TARGETS");
        }
        bool current=(view with {SavedReport=null}).Resolve(context.Snapshot) is not null;
        DebriefControl(context.Layout,view,5,"SAVE CURRENT",current);
        DebriefControl(context.Layout,view,6,"LOAD SAVED");
        DebriefControl(context.Layout,view,7,"CURRENT");
        DebriefControl(context.Layout,view,0,"RETURN TO MENU / ESC");
        DebriefControl(context.Layout,view,1,"RESTART / R");
        _bounds=panel;
    }
    private void DebriefControl(in GameplayHudLayout layout,in MatchDebriefView view,int index,ReadOnlySpan<char> label,bool enabled=true,bool selected=false)
    {
        var previous=_bounds;_bounds=MatchDebriefLayout.Control(layout,index);_line=0;
        if(!_skin.Panel(_bounds,"control.button_compact",.85f))Quad(_bounds,(selected?GameplayHudVisualStyle.PanelSelected:GameplayHudVisualStyle.PanelRaised) with {W=.85f});
        if(selected || view.Focus==index)Quad(_bounds with {Width=2*_scale},GameplayHudVisualStyle.Focus);
        Line(label,enabled);_bounds=previous;
    }
    private void DebriefLine(HudRect panel,float y,ReadOnlySpan<char> text,bool primary=false)
    {var old=_bounds;_bounds=new(panel.X+4*_scale,panel.Y+y*_scale,panel.Width-8*_scale,26*_scale);_line=0;Line(text,primary);_bounds=old;}
    private static string CategoryText(MatchDebriefCategory category)=>category switch
    {MatchDebriefCategory.Industry=>"INDUSTRY",MatchDebriefCategory.Supply=>"SUPPLY",MatchDebriefCategory.Reconnaissance=>"RECON",MatchDebriefCategory.Infrastructure=>"INFRASTRUCTURE",MatchDebriefCategory.Forces=>"FORCE PRESERVATION",MatchDebriefCategory.Result=>"RESULT",_=>"ALL EVENTS"};
    private static string EventText(MatchDebriefEventKind kind)=>kind switch
    {
        MatchDebriefEventKind.Extraction=>"EXTRACTION",MatchDebriefEventKind.CargoLoaded=>"CARGO PICKUP",MatchDebriefEventKind.CargoDelivered=>"CARGO DELIVERED",
        MatchDebriefEventKind.ConveyorDelivered=>"CONVEYOR HANDOFF",MatchDebriefEventKind.ProductionCompleted=>"OUTPUT COMMITTED",
        MatchDebriefEventKind.ScoutProduced=>"SCOUT PRODUCED",MatchDebriefEventKind.SupplyTruckProduced=>"SUPPLY TRUCK PRODUCED",
        MatchDebriefEventKind.FieldFuelTransferred=>"FIELD FUEL TRANSFER",MatchDebriefEventKind.FieldAmmunitionTransferred=>"FIELD AMMO TRANSFER",
        MatchDebriefEventKind.SupplyLoadCompleted=>"SUPPLY LOAD COMPLETED",MatchDebriefEventKind.ResearchCompleted=>"RESEARCH COMPLETED",
        MatchDebriefEventKind.WorkBlocked=>"WORK BLOCK OBSERVED",MatchDebriefEventKind.WorkResumed=>"WORK RESUMED",_=>"MATCH RESULT"
    };
    private static string EventExplanation(MatchDebriefEventKind kind)=>kind switch
    {
        MatchDebriefEventKind.Extraction=>"COMPLETED EXTRACTION / NOT A DEPOSIT OR STOCK TOTAL",
        MatchDebriefEventKind.CargoLoaded=>"PHYSICAL PICKUP / DELIVERY IS A SEPARATE EVENT",
        MatchDebriefEventKind.CargoDelivered or MatchDebriefEventKind.ConveyorDelivered=>"POSITIVE COMMITTED DELIVERY TO AN OWNED RECEIVER",
        MatchDebriefEventKind.ProductionCompleted=>"COMMITTED OUTPUT / STARTING STOCK IS NOT PRODUCTION",
        MatchDebriefEventKind.ResearchCompleted=>"RECORDED COMPLETION / CAPABILITY EFFECTS NOT INFERRED",
        MatchDebriefEventKind.WorkBlocked=>"AN OWNED PRODUCTION FACILITY WAS OBSERVED BLOCKED",
        MatchDebriefEventKind.WorkResumed=>"OWNED PRODUCTION RESUMED AFTER ITS OBSERVED BLOCK",
        MatchDebriefEventKind.FieldFuelTransferred or MatchDebriefEventKind.FieldAmmunitionTransferred=>"POSITIVE PHYSICAL TRANSFER TO AN OWNED UNIT",
        MatchDebriefEventKind.SupplyLoadCompleted=>"A COMPLETED SUPPLY LOADING VISIT WAS RECORDED",
        _=>"RECORDED UNIT COMPLETION / STARTING UNITS ARE EXCLUDED"
    };
    private static string ReasonText(MatchDebriefReason reason)=>reason switch
    {
        MatchDebriefReason.CommandCoreDestroyed=>"RECORDED TERMINATION: COMMAND CORE DESTROYED",
        MatchDebriefReason.Surrender=>"RECORDED TERMINATION: SURRENDER",
        MatchDebriefReason.MutualCommandCoreDestruction=>"RECORDED TERMINATION: BOTH COMMAND CORES DESTROYED",
        MatchDebriefReason.AllParticipantsEliminated=>"RECORDED TERMINATION: ALL PARTICIPANTS ELIMINATED",
        _=>"TERMINATION CAUSE UNAVAILABLE"
    };
}
