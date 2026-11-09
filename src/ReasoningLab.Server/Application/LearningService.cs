using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ReasoningLab.Core;
using ReasoningLab.Server.Persistence;

namespace ReasoningLab.Server.Application;

public class RuleException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public class LearningService(LabDbContext db, TimeProvider time)
{
    private DateTimeOffset Now => time.GetUtcNow();

    // A transaction-scoped lock serializes changes for one learner across server instances.
    private async Task Lock(Guid learner) => await db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({learner.ToString()}, 0))");

    private IQueryable<LearningSession> Sessions => db.Sessions
        .Include(s => s.Items).ThenInclude(i => i.Question).ThenInclude(q => q.Skills)
        .Include(s => s.Items).ThenInclude(i => i.Attempt);

    public async Task<SessionView> Start(Guid learner, StartSession input)
    {
        if (input.Mode is not ("practice" or "diagnostic") || !await db.Pathways.AnyAsync(p => p.Id == input.Pathway))
            throw new RuleException(400, "Choose an available pathway and mode.");
        if (input.Mode == "diagnostic" && input.Pathway != "lsat")
            throw new RuleException(400, "This pathway does not yet have a diagnostic.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(learner);
        var session = await Sessions.Where(s => s.LearnerId == learner && s.PathwayId == input.Pathway && s.Mode == input.Mode && s.Completed == null).FirstOrDefaultAsync();
        if (session is null)
        {
            session = new() { LearnerId = learner, PathwayId = input.Pathway, Mode = input.Mode, Created = Now };
            if (input.Mode == "diagnostic")
            {
                var blueprint = await db.Blueprints.SingleAsync(b => b.Id == "lsat-diagnostic-v1");
                var rules = JsonSerializer.Deserialize<DiagnosticRules>(blueprint.RulesJson)!;
                session.BlueprintId = blueprint.Id;
                session.Deadline = Now.AddSeconds(rules.Seconds);
                for (var n = 0; n < rules.QuestionKeys.Length; n++)
                {
                    var key = rules.QuestionKeys[n];
                    var q = await db.Questions.Include(q => q.Skills).SingleAsync(q => q.Key == key && q.Version == 1);
                    session.Items.Add(new() { Question = q, Position = n });
                }
            }
            db.Sessions.Add(session);
        }
        var view = await View(session);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return view;
    }

    public async Task<SessionView> Get(Guid learner, Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(learner);
        var session = await Owned(learner, id);
        var view = await View(session);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return view;
    }

    private async Task<LearningSession> Owned(Guid learner, Guid id) =>
        await Sessions.SingleOrDefaultAsync(s => s.Id == id && s.LearnerId == learner)
        ?? throw new RuleException(404, "Session not found.");

    public async Task<SessionView> Answer(Guid learner, Guid id, SubmitAnswer answer)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(learner);
        var session = await Owned(learner, id);
        if (session.Completed is not null || session.Deadline <= Now)
        {
            var expired = await View(session);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return expired;
        }
        var item = session.Items.SingleOrDefault(i => i.Id == answer.QuestionId)
            ?? throw new RuleException(400, "Question was not assigned to this session.");
        if (answer.Choice < 0 || answer.Choice >= item.Question.Options.Length)
            throw new RuleException(400, "Choose one of the available answers.");
        var levelUp = false;
        if (item.Attempt is not null)
        {
            if (item.Attempt.Choice != answer.Choice) throw new RuleException(409, "This answer is already recorded.");
        }
        else
        {
            if (session.Items.Where(i => i.Attempt is null).Min(i => i.Position) != item.Position)
                throw new RuleException(409, "Answer the current question first.");
            item.Attempt = new() { SessionItemId = item.Id, Choice = answer.Choice, Correct = answer.Choice == item.Question.Answer, Created = Now };
            db.Attempts.Add(item.Attempt);
            if (session.Mode == "practice")
                levelUp = await RecordEvidence(session.LearnerId, item);
        }
        // Flush before selection so the next question sees the newly recorded attempt.
        await db.SaveChangesAsync();
        var view = await View(session);
        if (session.Mode == "practice")
            view = view with { Feedback = new(item.Attempt.Correct, item.Question.Answer, item.Question.Explanation, levelUp) };
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return view;
    }

    private async Task<bool> RecordEvidence(Guid learner, SessionItem item)
    {
        bool promoted = false;
        foreach (var mapping in item.Question.Skills)
        {
            var mastery = await db.Mastery.FindAsync(learner, mapping.SkillId);
            if (mastery is null) { mastery = new() { LearnerId = learner, SkillId = mapping.SkillId }; db.Mastery.Add(mastery); }
            var oldLevel = mastery.Level;
            (mastery.Level, mastery.Streak, mastery.ReviewDue) = LearningPolicy.Advance(
                mastery.Level, mastery.Streak, item.Attempt!.Correct, item.Question.Difficulty, item.IsReview, Now);
            mastery.Attempts++;
            if (item.Attempt.Correct) mastery.Correct++;
            promoted |= mastery.Level > oldLevel;
            db.Evidence.Add(new()
            {
                AttemptId = item.Attempt.Id,
                LearnerId = learner,
                SkillId = mapping.SkillId,
                Correct = item.Attempt.Correct,
                Difficulty = item.Question.Difficulty,
                PolicyVersion = LearningPolicy.Version
            });
        }
        if (!item.Attempt!.Correct)
        {
            var tag = await db.Misconceptions.FindAsync(learner, item.Question.Misconception);
            if (tag is null) { tag = new() { LearnerId = learner, Tag = item.Question.Misconception }; db.Misconceptions.Add(tag); }
            tag.Occurrences++;
        }
        return promoted;
    }

    private async Task<SessionView> View(LearningSession session)
    {
        if (session.Mode == "diagnostic" && session.Completed is null &&
            (session.Deadline <= Now || session.Items.All(i => i.Attempt is not null)))
        {
            session.Completed = Now;
            db.Results.Add(new() { SessionId = session.Id, Correct = session.Items.Count(i => i.Attempt?.Correct == true), Total = session.Items.Count, Completed = Now });
        }
        var pending = session.Items.OrderBy(i => i.Position).FirstOrDefault(i => i.Attempt is null);
        if (pending is null && session.Mode == "practice") pending = await SelectNext(session);
        var result = session.Completed is not null
            ? new AssessmentResult(session.Items.Count(i => i.Attempt?.Correct == true), session.Items.Count, "Original short diagnostic · raw score, not an official exam score") : null;
        return new(session.Id, session.PathwayId, session.Mode,
            pending is not null && session.Completed is null
                ? new(pending.Id, pending.Question.Prompt, pending.Question.Options, pending.Question.Difficulty, "Deductive reasoning", pending.IsReview) : null,
            session.Items.Count(i => i.Attempt is not null), session.Mode == "diagnostic" ? session.Items.Count : null,
            session.Deadline, Now, session.Completed is not null, pending is null && session.Mode == "practice", result);
    }

    private async Task<SessionItem?> SelectNext(LearningSession session)
    {
        var mastery = await db.Mastery.FindAsync(session.LearnerId, "deduction");
        var level = mastery?.Level ?? 1;
        var questions = await db.Questions.Include(q => q.Skills).Where(q => q.PathwayId == session.PathwayId
            && q.Skills.Any(s => s.SkillId == "deduction")).ToListAsync();
        questions = questions.GroupBy(q => q.Key).Select(g => g.MaxBy(q => q.Version)!).ToList();
        var history = await (from i in db.Items
                             join s in db.Sessions on i.SessionId equals s.Id
                             where s.LearnerId == session.LearnerId && i.Attempt != null
                             select new { i.Question.Key, i.Attempt!.Created, i.Attempt.Correct }).ToListAsync();
        var seen = history.Select(h => h.Key).ToHashSet();
        QuestionVersion? chosen = null;
        bool review = false;
        if (mastery?.ReviewDue <= Now)
        {
            var due = history.GroupBy(h => h.Key).Select(g => g.MaxBy(h => h.Created)!)
                .Where(h => h.Created.Add(h.Correct ? TimeSpan.FromDays(Math.Min(level, 7)) : TimeSpan.FromMinutes(10)) <= Now)
                .OrderBy(h => h.Correct).ThenBy(h => h.Created).FirstOrDefault();
            chosen = questions.FirstOrDefault(q => q.Key == due?.Key);
            review = chosen is not null;
        }
        chosen ??= questions.Where(q => !seen.Contains(q.Key)).OrderBy(q => Math.Abs(q.Difficulty - level)).ThenBy(q => q.Difficulty).ThenBy(q => q.Key).FirstOrDefault();
        if (chosen is null) return null;
        var item = new SessionItem { SessionId = session.Id, Question = chosen, QuestionVersionId = chosen.Id, Position = session.Items.Count, IsReview = review };
        session.Items.Add(item);
        db.Items.Add(item);
        return item;
    }

    public async Task<Dashboard> Dashboard(Guid learner)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(learner);
        foreach (var expired in await Sessions.Where(s => s.LearnerId == learner && s.Completed == null && s.Deadline <= Now).ToListAsync())
            await View(expired);
        await db.SaveChangesAsync();
        var skills = await (from m in db.Mastery
                            join s in db.Skills on m.SkillId equals s.Id
                            where m.LearnerId == learner
                            select new SkillProgress(s.Id, s.Name, m.Level, m.Correct, m.Attempts, m.ReviewDue)).ToArrayAsync();
        var attempts = await (from i in db.Items
                              join s in db.Sessions on i.SessionId equals s.Id
                              where s.LearnerId == learner && s.Mode == "practice" && i.Attempt != null
                              select new { s.PathwayId, i.Attempt!.Correct }).ToListAsync();
        var pathways = attempts.GroupBy(x => x.PathwayId).Select(g => new PathwayProgress(g.Key, g.Count(x => x.Correct), g.Count())).ToArray();
        var results = await (from r in db.Results
                             join s in db.Sessions on r.SessionId equals s.Id
                             where s.LearnerId == learner
                             orderby r.Completed descending
                             select new ResultSummary(r.SessionId, s.PathwayId, r.Correct, r.Total, r.Completed)).ToArrayAsync();
        var recommendations = skills.Length == 0 ? ["Begin with an everyday reasoning challenge."] : skills.Select(s =>
            s.ReviewDue <= Now ? $"Time to revisit {s.Name.ToLowerInvariant()}." : $"Continue {s.Name.ToLowerInvariant()} at level {s.Level}.").ToArray();
        var misconceptions = await db.Misconceptions.Where(m => m.LearnerId == learner).Select(m => m.Tag).ToArrayAsync();
        await tx.CommitAsync();
        return new(skills, pathways, results, recommendations, misconceptions, "Not estimated — full content coverage and validated exam-condition evidence are still needed.");
    }
}
