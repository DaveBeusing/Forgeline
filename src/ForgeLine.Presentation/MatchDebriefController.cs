using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct MatchDebriefView(bool Open, SimulationSessionId Session,
    MatchDebriefCategory Filter = MatchDebriefCategory.All, int Page = 0, int Selected = -1,
    int Focus = 0, bool Coverage = false, MatchDebriefSummary? SavedReport = null, string Feedback = "")
{
    public bool IsCurrent(PresentationSnapshot? snapshot) => Open && snapshot is not null && Session.IsSpecified &&
        snapshot.SessionId == Session && snapshot.PlayerExperience is { IsMatchComplete: true };
    public MatchDebriefSummary? Resolve(PresentationSnapshot? snapshot)
    {
        if (!IsCurrent(snapshot) || snapshot!.PlayerExperience is not { } experience) return null;
        if (SavedReport is { } saved) return saved.Player == experience.Player.Value ? saved : null;
        var report = snapshot.MatchDebrief;
        if (report is null || report.SessionId != Session.Value || report.Player != experience.Player.Value ||
            report.ObservedThroughTick > snapshot.Tick.Value || report.Outcome == MatchDebriefOutcome.Incomplete) return null;
        return experience.MatchStatus switch
        {
            PlayerMatchStatus.Victory when report.Outcome != MatchDebriefOutcome.Victory => null,
            PlayerMatchStatus.Defeat when report.Outcome != MatchDebriefOutcome.Defeat => null,
            PlayerMatchStatus.Draw when report.Outcome != MatchDebriefOutcome.Draw => null,
            _ => report
        };
    }
}
public readonly record struct MatchDebriefInteraction(bool Captured = false, bool Return = false,
    bool Restart = false, bool Save = false, bool Load = false, bool SuppressTerminalKeys = false);

public static class MatchDebriefLayout
{
    public const int Rows = 6;
    public static HudRect Panel(in GameplayHudLayout layout)
    {
        var safe=layout.SafeArea; float gap=8*MathF.Min(layout.Scale,1.25f);
        float width=MathF.Max(0,MathF.Min(760*layout.Scale,safe.Width-2*gap));
        float height=MathF.Max(0,MathF.Min(480*layout.Scale,safe.Height-2*gap));
        return new(safe.X+(safe.Width-width)/2,safe.Y+(safe.Height-height)/2,width,height);
    }
    public static float Scale(in GameplayHudLayout layout) {var p=Panel(layout);return p.IsEmpty?0:MathF.Min(layout.Scale,p.Height/480);}
    public static HudRect Control(in GameplayHudLayout layout,int index)
    {
        var p=Panel(layout);float s=Scale(layout), column=(p.Width-24*s)/3;
        if(s<=0)return default;
        return index switch
        {
            2 or 3 or 4=>new(p.X+(8+index-2)*s+(index-2)*column,p.Y+88*s,column-4*s,26*s),
            >=10 and <16=>new(p.X+8*s,p.Y+(122+(index-10)*28)*s,p.Width-16*s,26*s),
            5 or 6 or 7=>new(p.X+(8+index-5)*s+(index-5)*column,p.Y+400*s,column-4*s,26*s),
            0=>new(p.X+8*s,p.Y+438*s,(p.Width-24*s)/2,28*s),
            1=>new(p.X+p.Width/2+4*s,p.Y+438*s,(p.Width-24*s)/2,28*s),
            _=>default
        };
    }
    public static int EventIndex(MatchDebriefSummary? report,in MatchDebriefView view,int row)
    {
        if(report is null)return -1;
        long skip=(long)Math.Max(0,view.Page)*Rows+row;
        for(int i=0;i<report.Events.Count;i++)
            if((view.Filter==MatchDebriefCategory.All || report.Events[i].Category==view.Filter) && skip--==0)return i;
        return -1;
    }
}

public sealed class MatchDebriefController(GameplayBindingRegistry? bindings=null)
{
    private static readonly int[] FocusOrder=[0,1,2,3,4,10,11,12,13,14,15,5,6,7];
    private readonly GameplayBindingRegistry _bindings=bindings??GameplayBindingRegistry.Default;
    private GameplayHudLayout _layout;
    private SimulationSessionId _session;
    private bool _terminal,_armed;
    private ulong _press;
    private int _control,_row;
    private MatchDebriefSummary? _pressedReport;
    public MatchDebriefView View {get;private set;}
    public void Feedback(string text)=>View=View with {Feedback=text};
    public void ShowSaved(MatchDebriefSummary? report,string feedback)
    {View=View with {SavedReport=report,Page=0,Selected=-1,Feedback=feedback};_armed=false;}

    public MatchDebriefInteraction Update(InputState input,PresentationSnapshot? snapshot,in GameplayHudLayout layout,bool blocked=false)
    {
        bool terminal=snapshot is {SessionId.IsSpecified:true,PlayerExperience:{IsMatchComplete:true}};
        ulong press=input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh=press!=_press;_press=press;
        bool sessionChange=_session!=snapshot?.SessionId;
        bool transition=!_terminal || sessionChange || _layout!=layout;
        _layout=layout;_session=snapshot?.SessionId??default;
        if(!terminal){_terminal=false;_armed=false;View=default;return default;}
        if(!_terminal || sessionChange)View=new(true,_session,Selected:-1,Feedback:"");
        _terminal=true;
        var panel=MatchDebriefLayout.Panel(layout);
        bool captured=input.HasPointerPosition && panel.Contains(input.PointerPosition);
        if(transition || blocked || input.FocusLostThisFrame || panel.IsEmpty)
        { _armed=false;input.SuppressHeldInput();return new(captured,SuppressTerminalKeys:true); }
        var report=View.Resolve(snapshot);
        if(MatchDebriefLayout.EventIndex(report,View,0)<0 && View.Page>0)View=View with {Page=0,Selected=-1};
        if(input.WasKeyPressed(_bindings.Key(GameplayAction.NextItem)))
            View=View with {Focus=FocusOrder[(Array.IndexOf(FocusOrder,View.Focus)+1)%FocusOrder.Length]};
        if(input.WasKeyPressed(_bindings.Key(GameplayAction.Activate)))
        { _armed=false;var result=Activate(View.Focus,snapshot!,report);input.SuppressHeldInput();return result with {Captured=true}; }
        if(fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left,out var origin))
        {
            _armed=false;
            foreach(int index in FocusOrder)
                if(MatchDebriefLayout.Control(layout,index).Contains(origin))
                { _armed=true;_control=index;_pressedReport=report;_row=index is >=10 and <16?MatchDebriefLayout.EventIndex(report,View,index-10):-1;View=View with {Focus=index};break; }
        }
        captured|=_armed;
        if(!input.WasMouseButtonReleased(PlatformMouseButton.Left))return new(captured);
        bool activate=_armed && input.HasPointerPosition && MatchDebriefLayout.Control(layout,_control).Contains(input.PointerPosition);
        _armed=false;
        if(!activate || _control is not (0 or 1) && !ReferenceEquals(report,_pressedReport))return new(captured);
        if(_control is >=10 and <16 && _row!=MatchDebriefLayout.EventIndex(report,View,_control-10))return new(true);
        return Activate(_control,snapshot!,report) with {Captured=true};
    }
    private MatchDebriefInteraction Activate(int index,PresentationSnapshot snapshot,MatchDebriefSummary? report)
    {
        if(index==0)return new(Return:true);
        if(index==1)return new(Restart:true);
        if(index==6)return new(Load:true);
        if(index==5)return new(Save:(View with {SavedReport=null}).Resolve(snapshot) is not null);
        if(index==7){View=View with {SavedReport=null,Page=0,Selected=-1,Feedback="CURRENT RESULT"};return default;}
        if(index==2){View=View with {Filter=(MatchDebriefCategory)(((int)View.Filter+1)%7),Page=0,Selected=-1};return default;}
        if(index==4){View=View with {Coverage=!View.Coverage};return default;}
        if(index==3)
        {
            int next=View.Page==int.MaxValue?0:View.Page+1;
            View=View with {Page=MatchDebriefLayout.EventIndex(report,View with {Page=next},0)<0?0:next,Selected=-1};
            return default;
        }
        if(index is >=10 and <16 && MatchDebriefLayout.EventIndex(report,View,index-10) is >=0 and var selected)
            View=View with {Selected=selected,Coverage=false};
        return default;
    }
}
