using ForgeLine.Simulation;
using ForgeLine.Input;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum GuidanceExplanationState : byte { Offered, Viewed, Deferred }
public enum GuidanceNavigation : byte { None, PreviousSection, NextSection, PreviousPage, NextPage, Read, Later }
public readonly record struct EarlyGameGuidanceView(bool Visible, string Objective, string Context, string Binding, string Next,
    SimulationSessionId SessionId = default, string ContentKey = "", int Section = 0, int Page = 0, int PageCount = 1,
    GuidanceExplanationState ExplanationState = default, bool GoalConfirmed = false)
{
    public int SectionCount { get; init; } = EarlyGameGuidanceCatalog.Steps.Length;
}

/// <summary>Advisory copied observations and independent, session-local reading state.</summary>
public sealed class EarlyGameGuidanceController
{
    private SimulationSessionId _session;
    private SimulationTick _tick;
    private PlayerGuidanceMilestone _observed;
    private readonly GuidanceExplanationState[] _reading = new GuidanceExplanationState[EarlyGameGuidanceCatalog.Steps.Length];
    private int _section, _page;
    private int _sectionCount=EarlyGameGuidanceCatalog.Steps.Length;
    private string _cachedKey = "";
    private GameplayBindingRegistry? _cachedBindings;
    private GameplayHudLayout _cachedLayout;
    private IReadOnlyList<string> _pages = Array.Empty<string>();
    private string _binding = "";
    private bool _cachedConfirmed;
    private string _cachedHelp = "";
    public PlayerGuidanceMilestone Observed => _observed;

    private static string Status(bool confirmed, GuidanceExplanationState state) => (confirmed, state) switch
    {
        (true, GuidanceExplanationState.Viewed) => "GOAL CONFIRMED / EXPLANATION VIEWED",
        (true, GuidanceExplanationState.Deferred) => "GOAL CONFIRMED / EXPLANATION DEFERRED",
        (true, _) => "GOAL CONFIRMED / EXPLANATION OFFERED",
        (false, GuidanceExplanationState.Viewed) => "GOAL NOT CONFIRMED / EXPLANATION VIEWED",
        (false, GuidanceExplanationState.Deferred) => "GOAL NOT CONFIRMED / EXPLANATION DEFERRED",
        _ => "GOAL NOT CONFIRMED / EXPLANATION OFFERED"
    };

    public void Navigate(GuidanceNavigation action)
    {
        switch (action)
        {
            case GuidanceNavigation.PreviousSection: _section = Math.Max(0, _section - 1); _page = 0; break;
            case GuidanceNavigation.NextSection: _section = Math.Min(_sectionCount - 1, _section + 1); _page = 0; break;
            case GuidanceNavigation.PreviousPage: _page = Math.Max(0, _page - 1); break;
            case GuidanceNavigation.NextPage: _page = Math.Min(Math.Max(0, _pages.Count - 1), _page + 1); break;
            case GuidanceNavigation.Read:
                if (_page + 1 < _pages.Count) { _page++; break; }
                _reading[_section] = GuidanceExplanationState.Viewed;
                _section = Math.Min(_sectionCount - 1, _section + 1); _page = 0; break;
            case GuidanceNavigation.Later:
                _reading[_section] = GuidanceExplanationState.Deferred;
                _section = Math.Min(_sectionCount - 1, _section + 1); _page = 0; break;
        }
    }

    public EarlyGameGuidanceView Update(PresentationSnapshot? snapshot, bool enabled, bool blocked = false,
        GameplayBindingRegistry? bindings = null, GameplayHudLayout layout = default)
    {
        if (snapshot is null || !snapshot.SessionId.IsSpecified) return default;
        if (_session != snapshot.SessionId)
        {
            _session = snapshot.SessionId; _tick = default; _observed = default;
            Array.Clear(_reading); _section = _page = 0; _cachedKey = "";
        }
        if (snapshot.PlayerExperience is not { IsMatchComplete: false } experience ||
            snapshot.Guidance is not { } summary || summary.SessionId != snapshot.SessionId ||
            summary.Tick != snapshot.Tick || summary.Player != experience.Player || snapshot.Tick.Value < _tick.Value) return default;
        if (summary.Evidence is { } evidence)
            foreach (var item in evidence)
                if (item.SessionId != snapshot.SessionId || item.Player != experience.Player || item.Tick.Value > snapshot.Tick.Value) return default;
        _tick = snapshot.Tick;
        _observed |= summary.Observed;
        if(summary.Practice is {} practice) return UpdatePractice(practice,snapshot,enabled&&!blocked,bindings,layout);
        _sectionCount=EarlyGameGuidanceCatalog.Steps.Length;
        if (!enabled || blocked) return default;
        bindings ??= GameplayBindingRegistry.Default;
        if (layout.SafeArea.IsEmpty) layout = GameplayHudLayout.Create(1920, 1080, 96);
        var step = EarlyGameGuidanceCatalog.Steps[_section];
        bool confirmed = step.Milestone != 0 && (_observed & step.Milestone) == step.Milestone;
        string help = experience.Tick == snapshot.Tick && experience.Selection.Work.State == ForgeLine.Game.PlayerWorkState.Blocked
            ? bindings.Text(ProductionContextFeedback.Explain(experience.Selection.Work.BlockReason)) : "";
        if (snapshot.PlayerActions is { } actions && actions.SessionId == snapshot.SessionId && actions.Tick == snapshot.Tick && actions.Logistics is { } logistics)
            foreach (var policy in logistics.Policies)
                if (policy.FailureReason != ForgeLine.Game.LogisticsTransportRequestFailureReason.None)
                { help = LogisticsStatusCatalog.Advice(policy.FailureReason); break; }
        if (help.Length == 0 && OperationsDiagnosisSnapshot.Resolve(snapshot) is { Rows.Count: > 0 } diagnosis &&
            OperationsActionGuidance.ForEntity(diagnosis, diagnosis.Rows[0].Entity) is { } issue &&
            OperationsActionGuidance.NextAction(issue.PrimaryReason) is { Length: > 0 } nextAction)
            help = OperationsDiagnosisSnapshot.Label(issue.PrimaryReason) + " / " + nextAction;
        if (_cachedHelp != help || _cachedConfirmed != confirmed || _cachedKey != step.Key || !ReferenceEquals(_cachedBindings, bindings) || _cachedLayout != layout)
        {
            _cachedHelp = help;
            _cachedConfirmed = confirmed;
            _cachedKey = step.Key; _cachedBindings = bindings; _cachedLayout = layout;
            _binding = bindings.Prompt(step.Action) + " " + step.Action.ToString().ToUpperInvariant() + " / " +
                bindings.Prompt(GameplayAction.Activate) + " ACTIVATE / " + bindings.Prompt(GameplayAction.NextItem) + " NEXT ITEM / " +
                bindings.Prompt(GameplayAction.SecondarySetting) + " THRESHOLD / " + bindings.Prompt(GameplayAction.PrimarySetting) + " PRIORITY / " +
                bindings.Prompt(GameplayAction.Decrease) + " - / " + bindings.Prompt(GameplayAction.Increase) + " +";
            _pages = GuidanceCardLayout.Pages((confirmed ? "Already confirmed: use this explanation as a reference; no repeat action is required. " : "") + step.Context + (help.Length > 0 ? " CURRENT SELECTION HELP: " + help : "") + " CONTROLS: " + _binding, layout);
        }
        _page = Math.Clamp(_page, 0, Math.Max(0, _pages.Count - 1));
        return new(true, step.Objective, _pages.Count > 0 ? _pages[_page] : "", _binding,
            step.Milestone == 0 ? _reading[_section] switch
            {
                GuidanceExplanationState.Viewed => "EXPLANATION VIEWED / NO SIMULATION GOAL",
                GuidanceExplanationState.Deferred => "EXPLANATION DEFERRED / NO SIMULATION GOAL",
                _ => "EXPLANATION OFFERED / NO SIMULATION GOAL"
            } : Status(confirmed, _reading[_section]),
            _session, step.Key, _section, _page, _pages.Count, _reading[_section], confirmed);
    }
    private EarlyGameGuidanceView UpdatePractice(PracticeSnapshot practice,PresentationSnapshot snapshot,bool enabled,
        GameplayBindingRegistry? bindings,GameplayHudLayout layout)
    {
        if(practice.Session!=snapshot.SessionId||practice.Tick!=snapshot.Tick||practice.Player!=snapshot.PlayerExperience!.Value.Player||
            !PracticeLessons.TryGet(practice.LessonKey,out var lesson)||practice.CompletedAtTicks.Count!=lesson!.Objectives.Count)return default;
        _sectionCount=lesson.Objectives.Count;_section=Math.Clamp(_section,0,_sectionCount-1);
        if(!enabled)return default;
        bindings??=GameplayBindingRegistry.Default;
        if(layout.SafeArea.IsEmpty)layout=GameplayHudLayout.Create(1920,1080,96);
        var goal=lesson.Objectives[_section];bool confirmed=practice.CompletedAtTicks[_section].HasValue;
        string key=lesson.Key+":"+_section.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string status=practice.Status+practice.Failure;
        if(_cachedKey!=key||_cachedHelp!=status||_cachedConfirmed!=confirmed||_cachedLayout!=layout||!ReferenceEquals(_cachedBindings,bindings))
        {
            _cachedKey=key;_cachedHelp=status;_cachedConfirmed=confirmed;_cachedLayout=layout;_cachedBindings=bindings;
            _binding=bindings.Prompt(GameplayAction.Activate)+" READ / "+bindings.Prompt(GameplayAction.NextItem)+" GUIDE FOCUS / ESC PAUSE";
            _pages=GuidanceCardLayout.Pages(lesson.Title+". "+goal.Hint+" "+goal.Explanation+
                (confirmed?" CONFIRMED BY COMPLETED SIMULATION OUTCOME.":"")+
                (practice.Status==PracticeStatus.Failed?" FAILURE: "+practice.Failure:"")+" "+_binding+
                " Reading or deferring hints never completes an objective. Pause offers Retry Practice and Skip Practice / Menu.",layout);
        }
        _page=Math.Clamp(_page,0,Math.Max(0,_pages.Count-1));
        string next=practice.Status==PracticeStatus.Complete?"PRACTICE COMPLETE / ESC MENU OR RETRY":
            practice.Status==PracticeStatus.Failed?"FAILED / ESC RETRY":Status(confirmed,_reading[_section]);
        return new(true,goal.Title,_pages.Count>0?_pages[_page]:"",_binding,next,_session,key,
            _section,_page,_pages.Count,_reading[_section],confirmed) { SectionCount=_sectionCount };
    }
}
