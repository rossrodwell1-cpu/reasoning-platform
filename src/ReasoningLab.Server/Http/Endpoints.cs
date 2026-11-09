using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ReasoningLab.Core;
using ReasoningLab.Server.Application;
using ReasoningLab.Server.Persistence;

namespace ReasoningLab.Server.Http;

public static class Endpoints
{
    public static void MapLabEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        app.MapPost("/api/profile", async (HttpContext context, LabDbContext db, IDataProtectionProvider dp, TimeProvider clock) =>
        {
            var protector = dp.CreateProtector("learner-v1");
            Guid id;
            try { id = Guid.Parse(protector.Unprotect(context.Request.Cookies["reasoning-profile"] ?? "")); }
            catch { id = Guid.Empty; }
            if (!await db.Learners.AnyAsync(l => l.Id == id))
            {
                var learner = new Learner { Created = clock.GetUtcNow() };
                db.Learners.Add(learner);
                await db.SaveChangesAsync();
                id = learner.Id;
                context.Response.Cookies.Append("reasoning-profile", protector.Protect(id.ToString()), new()
                { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(365), IsEssential = true });
            }
            return Results.Ok(new { id });
        });
        app.MapGet("/api/catalog", async (LabDbContext db) =>
        {
            var skills = await db.Skills.OrderBy(s => s.Name).ToListAsync();
            var prerequisites = await db.Prerequisites.ToListAsync();
            var pathways = await db.Pathways.ToListAsync();
            var mappings = await db.PathwaySkills.ToListAsync();
            var blueprints = await db.Blueprints.ToListAsync();
            return new Catalog(skills.Select(s => new SkillInfo(s.Id, s.Name, prerequisites.Where(p => p.SkillId == s.Id).Select(p => p.PrerequisiteId).ToArray())).ToArray(),
                pathways.Select(p => new PathwayInfo(p.Id, p.Name, p.Description, mappings.Where(m => m.PathwayId == p.Id).OrderBy(m => m.Position).Select(m => m.SkillId).ToArray(), p.Id == "lsat")).ToArray(),
                blueprints.Select(b => new BlueprintInfo(b.Id, b.ModuleId, b.EffectiveFrom, b.EffectiveUntil, b.Status, b.RulesJson, b.Source, b.MockEnabled)).ToArray());
        });
        app.MapGet("/api/blueprints/{module}", async (string module, DateOnly on, LabDbContext db) =>
        {
            var b = await db.Blueprints.Where(b => b.ModuleId == module && b.Status != "Original short diagnostic" && b.EffectiveFrom <= on && (b.EffectiveUntil == null || b.EffectiveUntil > on))
                .OrderByDescending(b => b.EffectiveFrom).FirstOrDefaultAsync();
            return b is null ? Results.NotFound() : Results.Ok(ToInfo(b));
        });
        app.MapPost("/api/sessions", async (StartSession input, HttpContext c, LabDbContext db, IDataProtectionProvider dp, LearningService service) =>
            await service.Start(await LearnerId(c, db, dp), input));
        app.MapGet("/api/sessions/{id:guid}", async (Guid id, HttpContext c, LabDbContext db, IDataProtectionProvider dp, LearningService service) =>
            await service.Get(await LearnerId(c, db, dp), id));
        app.MapPost("/api/sessions/{id:guid}/answers", async (Guid id, SubmitAnswer input, HttpContext c, LabDbContext db, IDataProtectionProvider dp, LearningService service) =>
            await service.Answer(await LearnerId(c, db, dp), id, input));
        app.MapGet("/api/dashboard", async (HttpContext c, LabDbContext db, IDataProtectionProvider dp, LearningService service) =>
            await service.Dashboard(await LearnerId(c, db, dp)));
        app.MapPost("/api/admin/questions", AddQuestion);
    }

    private static BlueprintInfo ToInfo(ExamBlueprint b) => new(b.Id, b.ModuleId, b.EffectiveFrom, b.EffectiveUntil, b.Status, b.RulesJson, b.Source, b.MockEnabled);

    private static async Task<IResult> AddQuestion(NewQuestion q, HttpContext c, LabDbContext db, IConfiguration config)
    {
        var expected = config["AdminToken"];
        var provided = c.Request.Headers["X-Admin-Token"].ToString();
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
            throw new RuleException(403, "Administrator token required.");
        if (string.IsNullOrWhiteSpace(q.Key) || q.Key.Length > 80 || string.IsNullOrWhiteSpace(q.Prompt) || q.Prompt.Length > 10000
            || q.Options is null || q.Options.Length is < 2 or > 8 || q.Options.Any(string.IsNullOrWhiteSpace)
            || q.Answer < 0 || q.Answer >= q.Options.Length || q.Difficulty < 1 || q.Difficulty > 10000
            || string.IsNullOrWhiteSpace(q.Explanation) || string.IsNullOrWhiteSpace(q.Misconception)
            || !await db.Pathways.AnyAsync(p => p.Id == q.Pathway) || !await db.Skills.AnyAsync(s => s.Id == q.Skill))
            throw new RuleException(400, "Provide a valid question, skill mapping, options, answer and explanation.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({q.Key}, 1))");
        var version = (await db.Questions.Where(v => v.Key == q.Key).MaxAsync(v => (int?)v.Version) ?? 0) + 1;
        var item = new QuestionVersion
        {
            Key = q.Key,
            Version = version,
            BankId = q.Pathway,
            PathwayId = q.Pathway,
            Difficulty = q.Difficulty,
            Prompt = q.Prompt,
            Options = q.Options,
            Answer = q.Answer,
            Explanation = q.Explanation,
            Misconception = q.Misconception,
            Skills = [new() { SkillId = q.Skill }]
        };
        db.Questions.Add(item);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/admin/questions/{item.Id}", new { item.Id, item.Version });
    }

    private static async Task<Guid> LearnerId(HttpContext c, LabDbContext db, IDataProtectionProvider dp)
    {
        Guid id;
        try { id = Guid.Parse(dp.CreateProtector("learner-v1").Unprotect(c.Request.Cookies["reasoning-profile"] ?? "")); }
        catch { throw new RuleException(401, "Start your learner profile first."); }
        if (!await db.Learners.AnyAsync(l => l.Id == id)) throw new RuleException(401, "Learner profile not found.");
        return id;
    }
}
