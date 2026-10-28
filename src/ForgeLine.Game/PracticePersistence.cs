namespace ForgeLine.Game;

internal static class PracticePersistence
{
    internal static void Validate(MatchSaveData save)
    {
        bool practice=PracticeLessons.TryGet(save.Configuration.CompositionKey,out var lesson);
        if(!practice){MatchPersistenceValidation.Require(save.PracticeProgress is null,"Practice progress is not valid in a skirmish save.");return;}
        var state=save.PracticeProgress;
        MatchPersistenceValidation.Require(save.SchemaVersion>=12&&state is not null,"Missing practice save progress or incompatible payload.");
        MatchPersistenceValidation.Require(state!.LessonKey==lesson!.Key&&state.DefinitionVersion==1&&state.Player.Value==1&&
            state.Tick.Value==save.SavedTick&&Enum.IsDefined(state.Status)&&state.CompletedAtTicks is not null&&
            state.CompletedAtTicks.Count==lesson.Objectives.Count&&state.CompletedAtTicks.All(t=>t is null||t>0&&t<=save.SavedTick)&&
            state.Failure is not null&&state.Failure.Length<=256&&
            (state.Status==PracticeStatus.Complete)==state.CompletedAtTicks.All(t=>t.HasValue),"Invalid practice progress.");
    }
}
