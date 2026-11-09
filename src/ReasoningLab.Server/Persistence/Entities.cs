namespace ReasoningLab.Server.Persistence;

public class Skill
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public class SkillPrerequisite
{
    public string SkillId { get; set; } = "";
    public string PrerequisiteId { get; set; } = "";
}
public class Pathway
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
public class PathwaySkill
{
    public string PathwayId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public int Position { get; set; }
}
public class ExamModule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public class ExamBlueprint
{
    public string Id { get; set; } = "";
    public string ModuleId { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string Status { get; set; } = "";
    public string RulesJson { get; set; } = "{}";
    public string Source { get; set; } = "";
    public bool MockEnabled { get; set; }
}
public class QuestionBank
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public class QuestionVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public int Version { get; set; } = 1;
    public string BankId { get; set; } = "";
    public string PathwayId { get; set; } = "";
    public int Difficulty { get; set; }
    public string Prompt { get; set; } = "";
    public string[] Options { get; set; } = [];
    public int Answer { get; set; }
    public string Explanation { get; set; } = "";
    public string Misconception { get; set; } = "";
    public List<QuestionSkill> Skills { get; set; } = [];
}
public class QuestionSkill
{
    public Guid QuestionVersionId { get; set; }
    public string SkillId { get; set; } = "";
}
public class Learner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Created { get; set; }
}
public class LearningSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerId { get; set; }
    public string PathwayId { get; set; } = "";
    public string Mode { get; set; } = "practice";
    public string? BlueprintId { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset? Completed { get; set; }
    public List<SessionItem> Items { get; set; } = [];
}
public class SessionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion Question { get; set; } = null!;
    public int Position { get; set; }
    public bool IsReview { get; set; }
    public Attempt? Attempt { get; set; }
}
public class Attempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionItemId { get; set; }
    public int Choice { get; set; }
    public bool Correct { get; set; }
    public DateTimeOffset Created { get; set; }
}
public class Mastery
{
    public Guid LearnerId { get; set; }
    public string SkillId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Streak { get; set; }
    public int Correct { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset ReviewDue { get; set; }
}
public class MasteryEvidence
{
    public Guid AttemptId { get; set; }
    public string SkillId { get; set; } = "";
    public Guid LearnerId { get; set; }
    public bool Correct { get; set; }
    public int Difficulty { get; set; }
    public string PolicyVersion { get; set; } = "";
}
public class Misconception
{
    public Guid LearnerId { get; set; }
    public string Tag { get; set; } = "";
    public int Occurrences { get; set; }
}
public class DiagnosticResult
{
    public Guid SessionId { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
    public DateTimeOffset Completed { get; set; }
}
