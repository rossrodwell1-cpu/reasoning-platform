namespace ReasoningLab.Core;

public record StartSession(string Pathway, string Mode = "practice");
public record SubmitAnswer(Guid QuestionId, int Choice);
// Id identifies the assignment, so a delayed retry cannot answer a later review of the same version.
public record QuestionCard(Guid Id, string Prompt, string[] Options, int Difficulty, string Skill, bool IsReview);
public record Feedback(bool Correct, int CorrectChoice, string Explanation, bool LevelUp);
public record SessionView(Guid Id, string Pathway, string Mode, QuestionCard? Question,
    int Answered, int? Total, DateTimeOffset? Deadline, DateTimeOffset ServerNow,
    bool Completed, bool Exhausted, AssessmentResult? Result, Feedback? Feedback = null);
public record AssessmentResult(int Correct, int Total, string Label);
public record SkillProgress(string Id, string Name, int Level, int Correct, int Attempts, DateTimeOffset ReviewDue);
public record PathwayProgress(string Id, int Correct, int Attempts);
public record ResultSummary(Guid SessionId, string Pathway, int Correct, int Total, DateTimeOffset Completed);
public record Dashboard(SkillProgress[] Skills, PathwayProgress[] Pathways, ResultSummary[] Results,
    string[] Recommendations, string[] Misconceptions, string Readiness);
public record SkillInfo(string Id, string Name, string[] Prerequisites);
public record PathwayInfo(string Id, string Name, string Description, string[] Skills, bool DiagnosticAvailable);
public record BlueprintInfo(string Id, string Module, DateOnly EffectiveFrom, DateOnly? EffectiveUntil,
    string Status, string RulesJson, string Source, bool MockEnabled);
public record Catalog(SkillInfo[] Skills, PathwayInfo[] Pathways, BlueprintInfo[] Blueprints);
public record NewQuestion(string Key, string Pathway, string Skill, int Difficulty, string Prompt,
    string[] Options, int Answer, string Explanation, string Misconception);

/// <summary>Explainable prototype policy; not a calibrated estimate of ability or an exam score.</summary>
public static class LearningPolicy
{
    public const string Version = "distinct-success-v1";
    public static (int Level, int Streak, DateTimeOffset ReviewDue) Advance(
        int level, int streak, bool correct, int difficulty, bool previouslySeen, DateTimeOffset now)
    {
        streak = correct && !previouslySeen && difficulty >= level ? streak + 1 : 0;
        if (streak >= 3) { level++; streak = 0; }
        return (level, streak, now.Add(correct ? TimeSpan.FromDays(Math.Min(level, 7)) : TimeSpan.FromMinutes(10)));
    }
}
